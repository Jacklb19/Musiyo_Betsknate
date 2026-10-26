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
        [JsonProperty("entry_glide_seconds")] public float EntryGlideSeconds;
        [JsonProperty("entry_glide_max_distance")] public float EntryGlideMaxDistance;
        [JsonProperty("panel_fade_seconds")] public float PanelFadeSeconds;
        [JsonProperty("panel_scroll_step")] public float PanelScrollStep;
        /// <summary>Speed of a point screen's text with the stick pushed all the way, in interface units per second.</summary>
        [JsonProperty("panel_scroll_speed")] public float PanelScrollSpeed;
        [JsonProperty("model_control_columns")] public int ModelControlColumns;
        [JsonProperty("walk_speed")] public float WalkSpeed;
        [JsonProperty("sprint_speed")] public float SprintSpeed;
        [JsonProperty("acceleration")] public float Acceleration;
        [JsonProperty("deceleration")] public float Deceleration;
        [JsonProperty("keyboard_look_speed")] public float KeyboardLookSpeed;
        [JsonProperty("maximum_pitch")] public float MaximumPitch;
        [JsonProperty("point_leave_margin")] public float PointLeaveMargin;
        [JsonProperty("lectern_screen_width")] public float LecternScreenWidth;
        [JsonProperty("lectern_unfold_seconds")] public float LecternUnfoldSeconds;
        [JsonProperty("lectern_viewing_distance")] public float LecternViewingDistance;
        [JsonProperty("lectern_float_height")] public float LecternFloatHeight;
        [JsonProperty("lectern_float_distance")] public float LecternFloatDistance;
        [JsonProperty("lectern_follow_seconds")] public float LecternFollowSeconds;
        /// <summary>
        /// Reading board of a stand, in metres: across, and up its slope from the lower edge of the stand's own board. It
        /// is larger than the delivered board so a page of the record reads comfortably on it (owner, 23/10/2026).
        /// </summary>
        [JsonProperty("lectern_board_width")] public float LecternBoardWidth;
        [JsonProperty("lectern_board_height")] public float LecternBoardHeight;
        /// <summary>
        /// On a computer or browser screen the same page is seen much smaller than in a headset, so the reading board grows
        /// by this factor there and the visitor taken to a stand stands this close (owner, 25/10/2026: "la ficha se ve
        /// pequeña y lejos"). A headset keeps the board and the distance above. Missing or zero: 1, and the headset's distance.
        /// </summary>
        [JsonProperty("lectern_screen_board_scale")] public float LecternScreenBoardScale;
        [JsonProperty("lectern_screen_viewing_distance")] public float LecternScreenViewingDistance;
        /// <summary>Margin between the edges of the reading board and the page that lies on it, in metres.</summary>
        [JsonProperty("lectern_board_margin")] public float LecternBoardMargin;
        /// <summary>Gap between a stand's reading board and the page lying on it, in metres.</summary>
        [JsonProperty("lectern_screen_gap")] public float LecternScreenGap;
        /// <summary>Finish of the reading stands: the wood of the stand and the face of its board.</summary>
        [JsonProperty("lectern_wood_color")] public string LecternWoodColor;
        [JsonProperty("lectern_board_color")] public string LecternBoardColor;

        /// <summary>Size of the reading board, in metres, in a headset or on a screen.</summary>
        public Vector2 LecternBoard(bool spatial)
            => new Vector2(LecternBoardWidth, LecternBoardHeight) * (spatial || LecternScreenBoardScale <= 0 ? 1 : LecternScreenBoardScale);

        /// <summary>How far in front of a stand a visitor taken there stands, in metres, in a headset or on a screen.</summary>
        public float ViewingDistance(bool spatial)
            => spatial || LecternScreenViewingDistance <= 0 ? LecternViewingDistance : LecternScreenViewingDistance;
        [JsonProperty("default_sensitivity")] public float DefaultSensitivity;
        [JsonProperty("minimum_sensitivity")] public float MinimumSensitivity;
        [JsonProperty("maximum_sensitivity")] public float MaximumSensitivity;
        [JsonProperty("default_volume")] public float DefaultVolume;
        [JsonProperty("default_subtitles")] public bool DefaultSubtitles;
        [JsonProperty("slider_step")] public float SliderStep;
        [JsonProperty("rebind_timeout_seconds")] public float RebindTimeoutSeconds;
        [JsonProperty("title_size")] public float TitleSize;
        [JsonProperty("body_size")] public float BodySize;
        [JsonProperty("backdrop_color")] public string BackdropColor;
        [JsonProperty("entry_backdrop_color")] public string EntryBackdropColor;
        [JsonProperty("panel_color")] public string PanelColor;
        [JsonProperty("text_color")] public string TextColor;
        [JsonProperty("accent_color")] public string AccentColor;
        [JsonProperty("selected_color")] public string SelectedColor;
        [JsonProperty("muted_text_color")] public string MutedTextColor = "#C2CCBE";
        [JsonProperty("secondary_accent_color")] public string SecondaryAccentColor = "#B86850";
        [JsonProperty("pointer_color")] public string PointerColor;
        /// <summary>Scenic set behind the entry menu: museum, dark, hearth or mask. See <see cref="MuseumEntryBackdrop"/>.</summary>
        [JsonProperty("entry_backdrop")] public string EntryBackdrop = "museum";
        /// <summary>Record whose published model the mask backdrop shows, if the record allows reuse.</summary>
        [JsonProperty("entry_mask_element")] public string EntryMaskElement = "";
        /// <summary>Seconds inside a point's radius before its screen opens; short, so walking past an edge does not flicker.</summary>
        [JsonProperty("point_open_seconds")] public float PointOpenSeconds;
        /// <summary>Waits, in seconds, between automatic attempts to reach the museum service after it failed.</summary>
        [JsonProperty("service_retry_seconds")] public float[] ServiceRetrySeconds;
        /// <summary>Milliseconds per frame a published model may spend loading, so a piece never stalls the view.</summary>
        [JsonProperty("model_load_frame_budget_ms")] public float ModelLoadFrameBudgetMs;
        /// <summary>Downloaded pieces kept in memory, so switching between two pieces does not download them again.</summary>
        [JsonProperty("model_cache_entries")] public int ModelCacheEntries;
        [JsonProperty("model_cache_bytes")] public int ModelCacheBytes;
        /// <summary>
        /// When the pieces of the points are brought: "visit" all at the start of the visit, "room" those of each room as
        /// the visitor enters it (the fallback if the headset cannot hold its frame rate with all of them), "off" only
        /// the piece of the record being read.
        /// </summary>
        [JsonProperty("model_preload")] public string ModelPreload;
        /// <summary>Seconds a released piece takes to glide back to its exhibition pose.</summary>
        [JsonProperty("grab_return_seconds")] public float GrabReturnSeconds;
        /// <summary>On a desktop, how far in front of the eyes a held piece floats, in metres.</summary>
        [JsonProperty("grab_desktop_distance")] public float GrabDesktopDistance;
        /// <summary>On a desktop, how far away a piece can be taken from, in metres.</summary>
        [JsonProperty("grab_desktop_reach")] public float GrabDesktopReach;
        /// <summary>Mouse rotation of a held piece, in degrees per unit of mouse movement.</summary>
        [JsonProperty("grab_desktop_rotation")] public float GrabDesktopRotation;
        /// <summary>Height of the viewport, from its top, where the fragment being heard is kept.</summary>
        [JsonProperty("reading_follow_line")] public float ReadingFollowLine;
        /// <summary>Seconds the panel takes to reach the fragment being heard.</summary>
        [JsonProperty("reading_follow_seconds")] public float ReadingFollowSeconds;
        [JsonProperty("auto_tour_speed")] public float AutoTourSpeed;
        [JsonProperty("auto_tour_acceleration")] public float AutoTourAcceleration;
        /// <summary>Shortest time the automatic tour stays at a point, also when it has no narration.</summary>
        [JsonProperty("auto_tour_stop_seconds")] public float AutoTourStopSeconds;
        /// <summary>A walk that makes no progress for this long gives up on its current stop.</summary>
        [JsonProperty("auto_tour_stuck_seconds")] public float AutoTourStuckSeconds;
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
            if (result == null || result.SchemaVersion != 5 || !Positive(result.EntryFadeSeconds)
                || !Positive(result.PanelFadeSeconds) || !Positive(result.PanelScrollStep) || result.PanelScrollStep > 1
                || !Positive(result.PanelScrollSpeed) || result.PanelScrollSpeed > 5000
                || result.ModelControlColumns < 1 || !Positive(result.EntryMenuFadeShare) || result.EntryMenuFadeShare >= 1
                || !Positive(result.EntryGlideSeconds) || !Positive(result.EntryGlideMaxDistance)
                || !Positive(result.WalkSpeed) || !Positive(result.SprintSpeed) || result.SprintSpeed < result.WalkSpeed
                || !Positive(result.Acceleration) || !Positive(result.Deceleration)
                || !Positive(result.PointLeaveMargin) || !Positive(result.LecternScreenWidth)
                || !Positive(result.LecternUnfoldSeconds) || !Positive(result.LecternViewingDistance)
                || !Positive(result.LecternFloatHeight) || !Positive(result.LecternFloatDistance)
                || !Positive(result.LecternFollowSeconds)
                || !Positive(result.LecternBoardWidth) || result.LecternBoardWidth > 2 || !Positive(result.LecternBoardHeight)
                || result.LecternBoardHeight > 2 || !Finite(result.LecternBoardMargin) || result.LecternBoardMargin < 0
                || result.LecternBoardMargin * 4 >= Mathf.Min(result.LecternBoardWidth, result.LecternBoardHeight)
                || !Finite(result.LecternScreenGap) || result.LecternScreenGap < 0 || result.LecternScreenGap > .2f
                || !Finite(result.LecternScreenBoardScale) || result.LecternScreenBoardScale < 0 || result.LecternScreenBoardScale > 1.6f
                || !Finite(result.LecternScreenViewingDistance) || result.LecternScreenViewingDistance < 0
                || !Positive(result.KeyboardLookSpeed) || !Positive(result.MaximumPitch) || result.MaximumPitch >= 90
                || !Positive(result.MinimumSensitivity) || !Positive(result.MaximumSensitivity)
                || !Finite(result.DefaultSensitivity) || result.DefaultSensitivity < result.MinimumSensitivity
                || result.DefaultSensitivity > result.MaximumSensitivity || !Finite(result.DefaultVolume)
                || result.DefaultVolume < 0 || result.DefaultVolume > 1
                || !Positive(result.SliderStep) || result.SliderStep > 1 || !Positive(result.RebindTimeoutSeconds)
                || !Positive(result.TitleSize) || !Positive(result.BodySize))
                throw new InvalidOperationException("Invalid museum experience configuration.");
            ColorValue(result.BackdropColor); ColorValue(result.EntryBackdropColor); ColorValue(result.PanelColor);
            ColorValue(result.TextColor); ColorValue(result.AccentColor);
            ColorValue(result.SelectedColor); ColorValue(result.PointerColor);
            ColorValue(result.MutedTextColor); ColorValue(result.SecondaryAccentColor);
            ColorValue(result.LecternWoodColor); ColorValue(result.LecternBoardColor);
            MuseumEntryBackdrop.Parse(result.EntryBackdrop);
            if (!Finite(result.PointOpenSeconds) || result.PointOpenSeconds < 0 || result.PointOpenSeconds > 3
                || result.ServiceRetrySeconds == null || result.ServiceRetrySeconds.Length == 0
                || Array.Exists(result.ServiceRetrySeconds, wait => !Finite(wait) || wait < 1 || wait > 600)
                || !Positive(result.ModelLoadFrameBudgetMs) || result.ModelLoadFrameBudgetMs > 20
                || result.ModelCacheEntries < 0 || result.ModelCacheEntries > 4 || result.ModelCacheBytes < 0
                || result.ModelPreload != "visit" && result.ModelPreload != "room" && result.ModelPreload != "off"
                || !Positive(result.GrabReturnSeconds) || result.GrabReturnSeconds > 3
                || !Positive(result.GrabDesktopDistance) || !Positive(result.GrabDesktopReach)
                || result.GrabDesktopReach < result.GrabDesktopDistance || !Positive(result.GrabDesktopRotation)
                || !Positive(result.ReadingFollowLine) || result.ReadingFollowLine >= 1 || !Positive(result.ReadingFollowSeconds)
                || !Positive(result.AutoTourSpeed) || result.AutoTourSpeed > result.WalkSpeed
                || !Positive(result.AutoTourAcceleration) || !Positive(result.AutoTourStopSeconds)
                || !Positive(result.AutoTourStuckSeconds))
                throw new InvalidOperationException("Invalid museum interaction settings.");
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
