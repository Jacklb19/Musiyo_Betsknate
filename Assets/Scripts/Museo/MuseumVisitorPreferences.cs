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
        private const int Version = 3;
        [JsonProperty("schema_version", Required = Required.Always)] private int schemaVersion = Version;
        [JsonProperty("sensitivity", Required = Required.Always)] public float Sensitivity { get; private set; }
        [JsonProperty("volume", Required = Required.Always)] public float Volume { get; private set; }
        [JsonProperty("music_level")] public float MusicLevel { get; private set; } = 1;
        [JsonProperty("subtitles", Required = Required.Always)] public bool Subtitles { get; private set; }
        /// <summary>Headset turning chosen by the visitor, or null to follow the shipped default.</summary>
        [JsonProperty("turn_mode")] public string TurnMode { get; private set; }
        /// <summary>Continuous turning speed chosen by the visitor, or 0 to follow the shipped default.</summary>
        [JsonProperty("turn_speed")] public float TurnSpeed { get; private set; }
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
            // Earlier versions carry over their values and use full relative music level.
            if (result == null || result.schemaVersion < 1 || result.schemaVersion > Version
                || !MuseumExperienceConfiguration.Finite(result.Sensitivity)
                || result.Sensitivity < settings.MinimumSensitivity || result.Sensitivity > settings.MaximumSensitivity
                || !MuseumExperienceConfiguration.Finite(result.Volume) || result.Volume < 0 || result.Volume > 1)
            {
                result = new MuseumVisitorPreferences { Sensitivity = settings.DefaultSensitivity,
                    Volume = settings.DefaultVolume, Subtitles = settings.DefaultSubtitles };
            }
            if (!MuseumExperienceConfiguration.Finite(result.MusicLevel) || result.MusicLevel < 0 || result.MusicLevel > 1)
                result.MusicLevel = 1;
            if (MuseumTurning.ParseMode(result.TurnMode) == null) result.TurnMode = null;
            if (!MuseumExperienceConfiguration.Finite(result.TurnSpeed) || result.TurnSpeed < MuseumTurning.MinimumSpeed
                || result.TurnSpeed > MuseumTurning.MaximumSpeed) result.TurnSpeed = 0;
            result.schemaVersion = Version;
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
        public void SetMusicLevel(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            MusicLevel = Mathf.Clamp01(value);
            Changed?.Invoke();
        }
        public void SetTurnMode(MuseumTurnMode mode) { TurnMode = MuseumTurning.Name(mode); Changed?.Invoke(); }
        public void SetTurnSpeed(float degreesPerSecond)
        {
            if (!MuseumExperienceConfiguration.Finite(degreesPerSecond)) return;
            TurnSpeed = Mathf.Clamp(degreesPerSecond, MuseumTurning.MinimumSpeed, MuseumTurning.MaximumSpeed);
            Changed?.Invoke();
        }
        /// <summary>Turning the visitor chose, or the shipped default.</summary>
        public MuseumTurnMode Turn(MuseumTurnMode fallback) => MuseumTurning.ParseMode(TurnMode) ?? fallback;
        public void Reset()
        {
            Sensitivity = configuration.DefaultSensitivity;
            Volume = configuration.DefaultVolume;
            MusicLevel = 1;
            Subtitles = configuration.DefaultSubtitles;
            TurnMode = null;
            TurnSpeed = 0;
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
