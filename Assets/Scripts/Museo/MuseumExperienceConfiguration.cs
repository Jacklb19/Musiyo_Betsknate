using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Local presentation and movement settings; contains no published content.</summary>
    [Preserve]
    public sealed class MuseumExperienceConfiguration
    {
        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("entry_fade_seconds")] public float EntryFadeSeconds;
        [JsonProperty("entry_menu_fade_share")] public float EntryMenuFadeShare;
        [JsonProperty("walk_speed")] public float WalkSpeed;
        [JsonProperty("sprint_speed")] public float SprintSpeed;
        [JsonProperty("acceleration")] public float Acceleration;
        [JsonProperty("deceleration")] public float Deceleration;
        [JsonProperty("keyboard_look_speed")] public float KeyboardLookSpeed;
        [JsonProperty("maximum_pitch")] public float MaximumPitch;
        [JsonProperty("default_sensitivity")] public float DefaultSensitivity;
        [JsonProperty("minimum_sensitivity")] public float MinimumSensitivity;
        [JsonProperty("maximum_sensitivity")] public float MaximumSensitivity;
        [JsonProperty("default_volume")] public float DefaultVolume;
        [JsonProperty("default_subtitles")] public bool DefaultSubtitles;
        [JsonProperty("menu_width")] public float MenuWidth;
        [JsonProperty("menu_height")] public float MenuHeight;
        [JsonProperty("menu_padding")] public int MenuPadding;
        [JsonProperty("menu_spacing")] public float MenuSpacing;
        [JsonProperty("button_height")] public float ButtonHeight;
        [JsonProperty("slider_step")] public float SliderStep;
        [JsonProperty("title_size")] public float TitleSize;
        [JsonProperty("body_size")] public float BodySize;
        [JsonProperty("backdrop_color")] public string BackdropColor;
        [JsonProperty("panel_color")] public string PanelColor;
        [JsonProperty("text_color")] public string TextColor;
        [JsonProperty("accent_color")] public string AccentColor;
        [JsonProperty("selected_color")] public string SelectedColor;
        private static MuseumExperienceConfiguration current;

        public static MuseumExperienceConfiguration Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>("MuseumExperienceConfiguration");
                if (resource == null) throw new InvalidOperationException("Missing museum experience configuration.");
                current = Parse(resource.text);
                return current;
            }
        }

        public static MuseumExperienceConfiguration Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumExperienceConfiguration>(json);
            if (result == null || result.SchemaVersion != 1 || !Positive(result.EntryFadeSeconds)
                || !Positive(result.EntryMenuFadeShare) || result.EntryMenuFadeShare >= 1
                || !Positive(result.WalkSpeed) || !Positive(result.SprintSpeed) || result.SprintSpeed < result.WalkSpeed
                || !Positive(result.Acceleration) || !Positive(result.Deceleration)
                || !Positive(result.KeyboardLookSpeed) || !Positive(result.MaximumPitch) || result.MaximumPitch >= 90
                || !Positive(result.MinimumSensitivity) || !Positive(result.MaximumSensitivity)
                || !Finite(result.DefaultSensitivity) || result.DefaultSensitivity < result.MinimumSensitivity
                || result.DefaultSensitivity > result.MaximumSensitivity || !Finite(result.DefaultVolume)
                || result.DefaultVolume < 0 || result.DefaultVolume > 1
                || !Positive(result.MenuWidth) || !Positive(result.MenuHeight) || result.MenuPadding < 0
                || !Positive(result.MenuSpacing) || !Positive(result.ButtonHeight)
                || !Positive(result.SliderStep) || result.SliderStep > 1
                || !Positive(result.TitleSize) || !Positive(result.BodySize))
                throw new InvalidOperationException("Invalid museum experience configuration.");
            ColorValue(result.BackdropColor); ColorValue(result.PanelColor);
            ColorValue(result.TextColor); ColorValue(result.AccentColor);
            ColorValue(result.SelectedColor);
            return result;
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0;
        public static Color ColorValue(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var color))
                throw new InvalidOperationException("Invalid museum interface color.");
            return color;
        }
    }
}
