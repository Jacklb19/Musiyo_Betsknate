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
        [TestCase("{\"schema_version\":2,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false}")]
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
        public void NonFiniteChangesCannotPoisonAudioOrLookSettings()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var preferences = MuseumVisitorPreferences.Load(settings, null);
            preferences.SetVolume(float.NaN);
            preferences.SetSensitivity(float.PositiveInfinity);
            Assert.That(preferences.Volume, Is.EqualTo(settings.DefaultVolume));
            Assert.That(preferences.Sensitivity, Is.EqualTo(settings.DefaultSensitivity));
        }

        [TestCase("schema_version", 5)]
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
