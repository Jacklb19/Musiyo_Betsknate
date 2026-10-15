using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Corrections applied while the delivered model is imported, read from <c>Delivery/mesh_repairs.json</c>.</summary>
    public sealed class MuseumMeshRepairs
    {
        public const string AssetPath = MuseumDeliveryManifest.Root + "/Delivery/mesh_repairs.json";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("two_sided_cards")] public CardRepair Cards;

        public sealed class CardRepair
        {
            [JsonProperty("material")] public string Material;
            [JsonProperty("objects")] public Dictionary<string, CardObject> Objects = new Dictionary<string, CardObject>();
        }

        public sealed class CardObject
        {
            [JsonProperty("delivered_triangles")] public int Delivered;
            [JsonProperty("coincident_triangles")] public int Coincident;
            /// <summary>Each face once, front and back.</summary>
            public int Repaired => 2 * (Delivered - Coincident);
        }

        public static MuseumMeshRepairs Load()
        {
            var repairs = JsonConvert.DeserializeObject<MuseumMeshRepairs>(File.ReadAllText(AssetPath));
            if (repairs == null || repairs.SchemaVersion != 1 || repairs.Cards == null || string.IsNullOrEmpty(repairs.Cards.Material))
                throw new InvalidOperationException("Invalid museum mesh repairs: " + AssetPath);
            return repairs;
        }

        /// <summary>Triangles an object carries after import.</summary>
        public int ExpectedTriangles(string name, int delivered)
            => Cards.Objects.TryGetValue(name, out var card) ? card.Repaired : delivered;

        /// <summary>A material whose every user carries explicit back faces draws one side only.</summary>
        public bool DrawsOneSide(string material) => material == Cards.Material;
    }

    /// <summary>
    /// Gives the listed thin cards of the museum model one consistent face per side: coincident faces are kept once and
    /// every face gets an explicit back face with the reversed normal, so lighting is right from both sides.
    /// </summary>
    public sealed class MuseumDeliveryMeshRepair : AssetPostprocessor
    {
        /// <summary>Raise when the repair changes, so every machine reimports the model.</summary>
        public override uint GetVersion() => 1;

        private void OnPostprocessModel(GameObject root)
        {
            if (assetPath != MuseumDeliveryManifest.ModelPath || !File.Exists(MuseumMeshRepairs.AssetPath)) return;
            var repairs = MuseumMeshRepairs.Load();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!repairs.Cards.Objects.TryGetValue(filter.name, out var card) || filter.sharedMesh == null) continue;
                var (delivered, coincident) = MakeTwoSided(filter.sharedMesh);
                if (delivered != card.Delivered || coincident != card.Coincident)
                    Debug.LogError(filter.name + ": the repair expected " + card.Delivered + " triangles with " + card.Coincident
                        + " coincident, the model has " + delivered + " with " + coincident + ".");
            }
        }

        /// <summary>Returns the delivered triangle count and how many of them repeated an earlier face.</summary>
        public static (int delivered, int coincident) MakeTwoSided(Mesh mesh)
        {
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var channels = new List<List<Vector4>>();
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>();
                mesh.GetUVs(channel, values);
                channels.Add(values.Count == positions.Length ? values : null);
            }
            var colors = mesh.colors32;
            var newPositions = new List<Vector3>(positions);
            var newNormals = new List<Vector3>(normals);
            var newTangents = new List<Vector4>(tangents);
            var newColors = new List<Color32>(colors);
            var newChannels = channels.Select(values => values != null ? new List<Vector4>(values) : null).ToList();
            var back = new Dictionary<int, int>();
            int Back(int index)
            {
                if (back.TryGetValue(index, out var copy)) return copy;
                copy = newPositions.Count;
                newPositions.Add(positions[index]);
                if (normals.Length > 0) newNormals.Add(-normals[index]);
                // The back face mirrors the surface frame, so the tangent's handedness turns with it.
                if (tangents.Length > 0) newTangents.Add(new Vector4(tangents[index].x, tangents[index].y, tangents[index].z, -tangents[index].w));
                if (colors.Length > 0) newColors.Add(colors[index]);
                for (int channel = 0; channel < newChannels.Count; channel++)
                    newChannels[channel]?.Add(channels[channel][index]);
                back[index] = copy;
                return copy;
            }

            int delivered = 0, coincident = 0;
            var submeshes = new List<int[]>();
            var seen = new HashSet<(Vector3Int, Vector3Int, Vector3Int)>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var source = mesh.GetTriangles(submesh);
                var result = new List<int>(source.Length * 2);
                for (int index = 0; index + 2 < source.Length; index += 3)
                {
                    delivered++;
                    int a = source[index], b = source[index + 1], c = source[index + 2];
                    if (!seen.Add(Key(positions[a], positions[b], positions[c]))) { coincident++; continue; }
                    result.Add(a); result.Add(b); result.Add(c);
                    result.Add(Back(a)); result.Add(Back(c)); result.Add(Back(b));
                }
                submeshes.Add(result.ToArray());
            }
            if (newPositions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(newPositions);
            if (normals.Length > 0) mesh.SetNormals(newNormals);
            if (tangents.Length > 0) mesh.SetTangents(newTangents);
            if (colors.Length > 0) mesh.SetColors(newColors);
            for (int channel = 0; channel < newChannels.Count; channel++)
                if (newChannels[channel] != null) mesh.SetUVs(channel, newChannels[channel]);
            for (int submesh = 0; submesh < submeshes.Count; submesh++) mesh.SetTriangles(submeshes[submesh], submesh, false);
            mesh.RecalculateBounds();
            return (delivered, coincident);
        }

        /// <summary>The same three corners in any order, to a tenth of a millimetre, are the same face.</summary>
        private static (Vector3Int, Vector3Int, Vector3Int) Key(Vector3 a, Vector3 b, Vector3 c)
        {
            var corners = new[] { Quantize(a), Quantize(b), Quantize(c) }
                .OrderBy(corner => corner.x).ThenBy(corner => corner.y).ThenBy(corner => corner.z).ToArray();
            return (corners[0], corners[1], corners[2]);
        }

        private static Vector3Int Quantize(Vector3 point)
            => new Vector3Int(Mathf.RoundToInt(point.x * 10000), Mathf.RoundToInt(point.y * 10000), Mathf.RoundToInt(point.z * 10000));
    }
}
