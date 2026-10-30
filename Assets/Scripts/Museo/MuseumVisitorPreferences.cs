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
        private const int Version = 4;
        [JsonProperty("schema_version", Required = Required.Always)] private int schemaVersion = Version;
        [JsonProperty("sensitivity", Required = Required.Always)] public float Sensitivity { get; private set; }
        /// <summary>The general volume: every sound of the visit is heard under it.</summary>
        [JsonProperty("volume", Required = Required.Always)] public float Volume { get; private set; }
        [JsonProperty("music_level")] public float MusicLevel { get; private set; } = 1;
        /// <summary>Share of the general volume for the narrations of the records.</summary>
        [JsonProperty("narration_level")] public float NarrationLevel { get; private set; } = 1;
        /// <summary>Share of the general volume for the guide's spoken answers.</summary>
        [JsonProperty("guide_level")] public float GuideLevel { get; private set; } = 1;
        [JsonProperty("subtitles", Required = Required.Always)] public bool Subtitles { get; private set; }
        /// <summary>Size of the subtitles relative to their shipped size.</summary>
        [JsonProperty("subtitle_scale")] public float SubtitleScale { get; private set; } = 1;
        /// <summary>Size of the text of a record on its stand relative to its shipped size.</summary>
        [JsonProperty("reading_scale")] public float ReadingScale { get; private set; } = 1;
        /// <summary>Headset turning chosen by the visitor, or null to follow the shipped default.</summary>
        [JsonProperty("turn_mode")] public string TurnMode { get; private set; }
        /// <summary>Continuous turning speed chosen by the visitor, or 0 to follow the shipped default.</summary>
        [JsonProperty("turn_speed")] public float TurnSpeed { get; private set; }
        /// <summary>The vignette that narrows the view while moving on a headset: off, soft or strong.</summary>
        [JsonProperty("vignette")] public string Vignette { get; private set; } = MuseumComfort.Name(MuseumVignette.Soft);
        /// <summary>Metres added to the height of the eyes on a headset, for a seated visitor or a tall one.</summary>
        [JsonProperty("height_offset")] public float HeightOffset { get; private set; }
        /// <summary>The first-visit tutorial has run (or was skipped); it starts again only when asked from the menu.</summary>
        [JsonProperty("tutorial_done")] public bool TutorialDone { get; private set; }
        /// <summary>Small labels by the controls of the Quest controllers, during the tutorial and when looked at.</summary>
        [JsonProperty("controller_labels")] public bool ControllerLabels { get; private set; } = true;
        /// <summary>Speed the narrations are heard at: 1 or 1.5 (owner, 28/10/2026). Older saves without it hear 1.</summary>
        [JsonProperty("narration_speed")] public float NarrationSpeed { get; private set; } = MuseumNarrationSpeed.Normal;
        /// <summary>The museum's sky: day as delivered, or night (owner, 28/10/2026).</summary>
        [JsonProperty("sky")] public string Sky { get; private set; } = DaySky;
        public const string DaySky = "day", NightSky = "night";
        public bool Night => Sky == NightSky;
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
            // Earlier versions carry over their values and take the shipped defaults for what they did not know.
            if (result == null || result.schemaVersion < 1 || result.schemaVersion > Version
                || !MuseumExperienceConfiguration.Finite(result.Sensitivity)
                || result.Sensitivity < settings.MinimumSensitivity || result.Sensitivity > settings.MaximumSensitivity
                || !MuseumExperienceConfiguration.Finite(result.Volume) || result.Volume < 0 || result.Volume > 1)
            {
                result = new MuseumVisitorPreferences { Sensitivity = settings.DefaultSensitivity,
                    Volume = settings.DefaultVolume, Subtitles = settings.DefaultSubtitles };
            }
            if (!Unit(result.MusicLevel)) result.MusicLevel = 1;
            if (!Unit(result.NarrationLevel)) result.NarrationLevel = 1;
            if (!Unit(result.GuideLevel)) result.GuideLevel = 1;
            if (!MuseumComfort.IsScale(MuseumComfort.SubtitleScales, result.SubtitleScale)) result.SubtitleScale = 1;
            if (!MuseumComfort.IsScale(MuseumComfort.ReadingScales, result.ReadingScale)) result.ReadingScale = 1;
            if (MuseumTurning.ParseMode(result.TurnMode) == null) result.TurnMode = null;
            if (!MuseumExperienceConfiguration.Finite(result.TurnSpeed) || result.TurnSpeed < MuseumTurning.MinimumSpeed
                || result.TurnSpeed > MuseumTurning.MaximumSpeed) result.TurnSpeed = 0;
            if (MuseumComfort.ParseVignette(result.Vignette) == null) result.Vignette = MuseumComfort.Name(MuseumVignette.Soft);
            if (!MuseumExperienceConfiguration.Finite(result.HeightOffset) || Mathf.Abs(result.HeightOffset) > MuseumComfort.MaxHeightOffset)
                result.HeightOffset = 0;
            if (!MuseumNarrationSpeed.IsSpeed(result.NarrationSpeed)) result.NarrationSpeed = MuseumNarrationSpeed.Normal;
            if (result.Sky != NightSky) result.Sky = DaySky;
            result.schemaVersion = Version;
            result.configuration = settings;
            return result;
        }

        private static bool Unit(float value) => MuseumExperienceConfiguration.Finite(value) && value >= 0 && value <= 1;

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
        public void SetNarrationLevel(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            NarrationLevel = Mathf.Clamp01(value);
            Changed?.Invoke();
        }
        public void SetGuideLevel(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            GuideLevel = Mathf.Clamp01(value);
            Changed?.Invoke();
        }
        public void SetSubtitleScale(float value)
        {
            if (!MuseumComfort.IsScale(MuseumComfort.SubtitleScales, value)) return;
            SubtitleScale = value;
            Changed?.Invoke();
        }
        public void SetReadingScale(float value)
        {
            if (!MuseumComfort.IsScale(MuseumComfort.ReadingScales, value)) return;
            ReadingScale = value;
            Changed?.Invoke();
        }
        public void SetTurnMode(MuseumTurnMode mode) { TurnMode = MuseumTurning.Name(mode); Changed?.Invoke(); }
        public void SetTurnSpeed(float degreesPerSecond)
        {
            if (!MuseumExperienceConfiguration.Finite(degreesPerSecond)) return;
            TurnSpeed = Mathf.Clamp(degreesPerSecond, MuseumTurning.MinimumSpeed, MuseumTurning.MaximumSpeed);
            Changed?.Invoke();
        }
        public void SetVignette(MuseumVignette vignette) { Vignette = MuseumComfort.Name(vignette); Changed?.Invoke(); }
        public void SetNight(bool night) { Sky = night ? NightSky : DaySky; Changed?.Invoke(); }
        public void SetHeightOffset(float metres)
        {
            if (!MuseumExperienceConfiguration.Finite(metres)) return;
            HeightOffset = Mathf.Clamp(metres, -MuseumComfort.MaxHeightOffset, MuseumComfort.MaxHeightOffset);
            Changed?.Invoke();
        }
        public void SetTutorialDone(bool value) { TutorialDone = value; Changed?.Invoke(); }
        public void SetControllerLabels(bool value) { ControllerLabels = value; Changed?.Invoke(); }
        public void SetNarrationSpeed(float value)
        {
            if (!MuseumNarrationSpeed.IsSpeed(value)) return;
            NarrationSpeed = value;
            Changed?.Invoke();
        }
        /// <summary>Turning the visitor chose, or the shipped default.</summary>
        public MuseumTurnMode Turn(MuseumTurnMode fallback) => MuseumTurning.ParseMode(TurnMode) ?? fallback;
        /// <summary>The vignette the visitor chose; soft until they choose.</summary>
        public MuseumVignette VignetteChoice => MuseumComfort.ParseVignette(Vignette) ?? MuseumVignette.Soft;
        public void Reset()
        {
            Sensitivity = configuration.DefaultSensitivity;
            Volume = configuration.DefaultVolume;
            MusicLevel = NarrationLevel = GuideLevel = 1;
            Subtitles = configuration.DefaultSubtitles;
            SubtitleScale = ReadingScale = 1;
            TurnMode = null;
            TurnSpeed = 0;
            Vignette = MuseumComfort.Name(MuseumVignette.Soft);
            HeightOffset = 0;
            ControllerLabels = true;
            NarrationSpeed = MuseumNarrationSpeed.Normal;
            Sky = DaySky;
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
