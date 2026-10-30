using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The size a piece is shown at in the exhibition, set per record in Resources/MuseumPieceSizes.json. A piece not named
    /// there keeps the exhibition size every model is brought to (<see cref="ModelExaminationPose.ExhibitSize"/> on its
    /// longest side). A piece in <c>scanned_size</c> is shown at the size of its digitisation, whose glTF units are metres:
    /// the masks, which in real life are much bigger than that (owner, 28/10/2026: «que sean mas como en la vida real, que
    /// son mucho mas grandes»). A piece in <c>longest_side</c> is shown with its longest side at that many metres, for a
    /// measured size the digitisation does not carry. It sizes the pieces that float at their point; a piece standing on
    /// a pedestal's mount is fitted to it. Only the exhibition changes: examining a piece keeps its own framing, and a
    /// piece taken in the hand keeps its exhibition size.
    /// </summary>
    [Preserve]
    public sealed class MuseumPieceSizes
    {
        /// <summary>Longest side, in metres, a piece may be shown at; a digitisation beyond it keeps the exhibition size.</summary>
        public const float LargestSide = 2.5f;
        /// <summary>Shortest longest side, in metres, a piece may be shown at.</summary>
        public const float SmallestSide = .05f;
        [JsonProperty("schema_version", Required = Required.Always)] public int SchemaVersion;
        [JsonProperty("scanned_size")] public string[] ScannedSize = Array.Empty<string>();
        [JsonProperty("longest_side")] public Dictionary<string, float> LongestSide = new Dictionary<string, float>();
        private static MuseumPieceSizes current;

        /// <summary>Uses <paramref name="sizes"/> in place of the shipped file; null goes back to the file. For tests and captures.</summary>
        public static void Replace(MuseumPieceSizes sizes) => current = sizes;

        public static MuseumPieceSizes Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>("MuseumPieceSizes");
                current = resource != null ? Parse(resource.text) : new MuseumPieceSizes { SchemaVersion = 1 };
                return current;
            }
        }

        public static MuseumPieceSizes Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumPieceSizes>(json);
            if (result == null || result.SchemaVersion != 1) throw new InvalidOperationException("Invalid museum piece sizes.");
            result.ScannedSize ??= Array.Empty<string>();
            result.LongestSide ??= new Dictionary<string, float>();
            foreach (var entry in result.LongestSide)
                if (string.IsNullOrWhiteSpace(entry.Key) || !MuseumExperienceConfiguration.Finite(entry.Value)
                    || entry.Value < SmallestSide || entry.Value > LargestSide)
                    throw new InvalidOperationException("Invalid size for " + entry.Key + ".");
            foreach (var slug in result.ScannedSize)
                if (string.IsNullOrWhiteSpace(slug) || result.LongestSide.ContainsKey(slug))
                    throw new InvalidOperationException("A piece is either at its scanned size or at a measured one.");
            return result;
        }

        /// <summary>
        /// Scale over the exhibition size for the piece <paramref name="slug"/>, whose digitisation is
        /// <paramref name="naturalScale"/> times the exhibition size: 1 for a piece not named here, and also for a
        /// digitisation too small or too big to be a real size.
        /// </summary>
        public float ScaleFor(string slug, float naturalScale)
        {
            if (string.IsNullOrEmpty(slug)) return 1;
            if (LongestSide.TryGetValue(slug, out float metres)) return metres / ModelExaminationPose.ExhibitSize;
            if (Array.IndexOf(ScannedSize, slug) < 0 || !MuseumExperienceConfiguration.Finite(naturalScale)) return 1;
            float side = naturalScale * ModelExaminationPose.ExhibitSize;
            return side >= SmallestSide && side <= LargestSide ? naturalScale : 1;
        }
    }
}
