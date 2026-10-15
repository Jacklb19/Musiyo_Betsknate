using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumPreferencesTests
    {
        [Test]
        public void PreferencesSurviveSerializationAndResetToConfiguredDefaults()
        {
            var configuration = MuseumExperienceConfiguration.Current;
            var preferences = MuseumVisitorPreferences.Load(configuration, null);
            preferences.SetSensitivity(.3f);
            preferences.SetVolume(.4f);
            preferences.SetSubtitles(false);
            var loaded = MuseumVisitorPreferences.Load(configuration, preferences.Serialize());
            Assert.That(loaded.Sensitivity, Is.EqualTo(.3f));
            Assert.That(loaded.Volume, Is.EqualTo(.4f));
            Assert.That(loaded.Subtitles, Is.False);
            loaded.Reset();
            Assert.That(loaded.Sensitivity, Is.EqualTo(configuration.DefaultSensitivity));
            Assert.That(loaded.Volume, Is.EqualTo(configuration.DefaultVolume));
            Assert.That(loaded.Subtitles, Is.EqualTo(configuration.DefaultSubtitles));
        }

        [TestCase("invalid")]
        [TestCase("{\"schema_version\":4,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false}")]
        [TestCase("{\"schema_version\":1,\"sensitivity\":-3,\"volume\":0.5,\"subtitles\":false}")]
        [TestCase("{\"schema_version\":1,\"sensitivity\":0.3,\"volume\":9,\"subtitles\":false}")]
        [TestCase("{\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false}")]
        public void CorruptOrUnsupportedPreferencesFallBackToConfiguredDefaults(string saved)
        {
            var settings = MuseumExperienceConfiguration.Current;
            var preferences = MuseumVisitorPreferences.Load(settings, saved);
            Assert.That(preferences.Sensitivity, Is.EqualTo(settings.DefaultSensitivity));
            Assert.That(preferences.Volume, Is.EqualTo(settings.DefaultVolume));
            Assert.That(preferences.Subtitles, Is.EqualTo(settings.DefaultSubtitles));
        }

        [Test]
        public void TurningChoiceIsSavedAndEarlierPreferencesKeepTheirValues()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var earlier = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":1,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false}");
            Assert.That(earlier.Sensitivity, Is.EqualTo(.3f));
            Assert.That(earlier.Turn(MuseumTurnMode.Continuous), Is.EqualTo(MuseumTurnMode.Continuous),
                "A visitor who never chose follows the shipped default.");
            earlier.SetTurnMode(MuseumTurnMode.Snap);
            earlier.SetTurnSpeed(105);
            var loaded = MuseumVisitorPreferences.Load(settings, earlier.Serialize());
            Assert.That(loaded.Turn(MuseumTurnMode.Continuous), Is.EqualTo(MuseumTurnMode.Snap));
            Assert.That(loaded.TurnSpeed, Is.EqualTo(105));
            loaded.Reset();
            Assert.That(loaded.TurnMode, Is.Null);
            Assert.That(loaded.TurnSpeed, Is.EqualTo(0));
            var odd = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":2,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":true,\"turn_mode\":\"spin\",\"turn_speed\":9000}");
            Assert.That(odd.TurnMode, Is.Null);
            Assert.That(odd.TurnSpeed, Is.EqualTo(0));
            Assert.That(odd.Sensitivity, Is.EqualTo(.3f), "An unknown turning value does not discard the other preferences.");
        }

        [Test]
        public void NonFiniteChangesCannotPoisonAudioOrLookSettings()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var preferences = MuseumVisitorPreferences.Load(settings, null);
            preferences.SetVolume(float.NaN);
            preferences.SetSensitivity(float.PositiveInfinity);
            preferences.SetMusicLevel(float.NaN);
            Assert.That(preferences.Volume, Is.EqualTo(settings.DefaultVolume));
            Assert.That(preferences.Sensitivity, Is.EqualTo(settings.DefaultSensitivity));
            Assert.That(preferences.MusicLevel, Is.EqualTo(1));
        }

        [Test]
        public void MusicChoiceMigratesWithoutChangingNarrationAndSurvivesReload()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var preferences = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":2,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false,\"turn_mode\":\"snap\",\"turn_speed\":105}");
            Assert.That(preferences.MusicLevel, Is.EqualTo(1));
            preferences.SetMusicLevel(0);
            var loaded = MuseumVisitorPreferences.Load(settings, preferences.Serialize());
            Assert.That(loaded.MusicLevel, Is.Zero);
            Assert.That(loaded.Volume, Is.EqualTo(.5f));
            Assert.That(loaded.TurnMode, Is.EqualTo("snap"));
            loaded.Reset();
            Assert.That(loaded.MusicLevel, Is.EqualTo(1));
            var invalid = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":3,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false,\"music_level\":9}");
            Assert.That(invalid.MusicLevel, Is.EqualTo(1));
            Assert.That(invalid.Volume, Is.EqualTo(.5f));
        }

        [TestCase("schema_version", 4)]
        [TestCase("point_open_seconds", -1)]
        [TestCase("model_load_frame_budget_ms", 0)]
        [TestCase("grab_return_seconds", 0)]
        [TestCase("reading_follow_line", 1)]
        [TestCase("auto_tour_speed", 9)]
        [TestCase("auto_tour_stuck_seconds", 0)]
        [TestCase("entry_glide_seconds", 0)]
        [TestCase("entry_glide_max_distance", -1)]
        [TestCase("entry_backdrop_color", "see-through")]
        [TestCase("entry_fade_seconds", 0)]
        [TestCase("entry_menu_fade_share", 1)]
        [TestCase("maximum_pitch", 90)]
        [TestCase("deceleration", -1)]
        [TestCase("selected_color", "invalid-color")]
        [TestCase("slider_step", 0)]
        [TestCase("panel_scroll_step", 2)]
        [TestCase("model_control_columns", 0)]
        [TestCase("lectern_float_distance", 0)]
        [TestCase("lectern_follow_seconds", -1)]
        [TestCase("pointer_color", "not-a-color")]
        [TestCase("muted_text_color", "invalid")]
        [TestCase("secondary_accent_color", "invalid")]
        public void InvalidExperienceConfigurationFailsWithADiagnostic(string property, object value)
        {
            var json = Resources.Load<TextAsset>("MuseumExperienceConfiguration").text;
            var pattern = "\"" + property + "\"\\s*:\\s*(\"[^\"]*\"|[^,}\\s]+)";
            Assert.That(Regex.IsMatch(json, pattern), Is.True, "The configuration field must exist before it is invalidated.");
            var replacement = "\"" + property + "\": " + (value is string ? "\"" + value + "\"" : Convert.ToString(value, CultureInfo.InvariantCulture));
            var invalid = Regex.Replace(json, pattern, _ => replacement);
            Assert.Throws<InvalidOperationException>(() => MuseumExperienceConfiguration.Parse(invalid));
        }
    }
}
