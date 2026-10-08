using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Versioned interface copy, independent of published cultural content.</summary>
    public sealed class MuseumInterfaceText
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; private set; }
        [JsonProperty("language")] public string Language { get; private set; }
        [JsonProperty("suggested_visit_minutes")] public int SuggestedVisitMinutes { get; private set; }
        [JsonProperty("entries")] private Dictionary<string, string> entries;
        private static MuseumInterfaceText current;
        public static MuseumInterfaceText Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>("MuseumInterfaceText_es");
                if (resource == null) throw new InvalidOperationException("Missing museum interface text resource.");
                current = Parse(resource.text);
                return current;
            }
        }

        public static MuseumInterfaceText Parse(string json)
        {
            var catalog = JsonConvert.DeserializeObject<MuseumInterfaceText>(json);
            if (catalog == null || catalog.SchemaVersion != 1 || catalog.Language != "es"
                || catalog.SuggestedVisitMinutes <= 0 || catalog.entries == null || catalog.entries.Count == 0)
                throw new InvalidOperationException("Invalid museum interface text catalog.");
            foreach (var entry in catalog.entries)
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                    throw new InvalidOperationException("Interface text keys and values must be nonempty.");
            return catalog;
        }

        public string Read(string key)
        {
            if (!entries.TryGetValue(key, out var value))
                throw new InvalidOperationException("Missing museum interface text: " + key);
            return value;
        }

        public static string Get(string key) => Current.Read(key);
        public static string Format(string key, params object[] values) =>
            string.Format(CultureInfo.GetCultureInfo(Current.Language), Get(key), values);
    }
}
