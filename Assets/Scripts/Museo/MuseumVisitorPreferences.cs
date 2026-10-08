using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Versioned local preferences. Never stores content or temporary access URLs.</summary>
    [Preserve]
    public sealed class MuseumVisitorPreferences
    {
        public const string StorageKey = "musiyo.visitor-preferences.v1";
        [JsonProperty("schema_version", Required = Required.Always)] private int schemaVersion = 1;
        [JsonProperty("sensitivity", Required = Required.Always)] public float Sensitivity { get; private set; }
        [JsonProperty("volume", Required = Required.Always)] public float Volume { get; private set; }
        [JsonProperty("subtitles", Required = Required.Always)] public bool Subtitles { get; private set; }
        [JsonIgnore] private MuseumExperienceConfiguration configuration;
        public event Action Changed;

        public static MuseumVisitorPreferences Load(MuseumExperienceConfiguration settings, string saved)
        {
            MuseumVisitorPreferences result = null;
            if (!string.IsNullOrEmpty(saved))
            {
                try { result = JsonConvert.DeserializeObject<MuseumVisitorPreferences>(saved); }
                catch (JsonException) { }
            }
            if (result == null || result.schemaVersion != 1 || !MuseumExperienceConfiguration.Finite(result.Sensitivity)
                || result.Sensitivity < settings.MinimumSensitivity || result.Sensitivity > settings.MaximumSensitivity
                || !MuseumExperienceConfiguration.Finite(result.Volume) || result.Volume < 0 || result.Volume > 1)
            {
                result = new MuseumVisitorPreferences { Sensitivity = settings.DefaultSensitivity,
                    Volume = settings.DefaultVolume, Subtitles = settings.DefaultSubtitles };
            }
            result.configuration = settings;
            return result;
        }

        public void SetSensitivity(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            Sensitivity = Mathf.Clamp(value, configuration.MinimumSensitivity, configuration.MaximumSensitivity);
            Changed?.Invoke();
        }
        public void SetVolume(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            Volume = Mathf.Clamp01(value);
            Changed?.Invoke();
        }
        public void SetSubtitles(bool value) { Subtitles = value; Changed?.Invoke(); }
        public void Reset()
        {
            Sensitivity = configuration.DefaultSensitivity;
            Volume = configuration.DefaultVolume;
            Subtitles = configuration.DefaultSubtitles;
            Changed?.Invoke();
        }
        public string Serialize() => JsonConvert.SerializeObject(this);
        public void Save()
        {
            PlayerPrefs.SetString(StorageKey, Serialize());
            PlayerPrefs.Save();
        }
    }
}
