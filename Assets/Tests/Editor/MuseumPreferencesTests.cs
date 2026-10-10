using System;
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

        [TestCase("\"schema_version\": 1", "\"schema_version\": 2")]
        [TestCase("\"entry_fade_seconds\": 1.1", "\"entry_fade_seconds\": 0")]
        [TestCase("\"entry_menu_fade_share\": 0.4", "\"entry_menu_fade_share\": 1")]
        [TestCase("\"maximum_pitch\": 80.0", "\"maximum_pitch\": 90")]
        [TestCase("\"deceleration\": 12.0", "\"deceleration\": -1")]
        [TestCase("#9C5B1F", "invalid-color")]
        [TestCase("\"slider_step\": 0.05", "\"slider_step\": 0")]
        [TestCase("\"panel_scroll_step\": 0.25", "\"panel_scroll_step\": 2")]
        [TestCase("\"model_control_columns\": 2", "\"model_control_columns\": 0")]
        [TestCase("\"lectern_float_distance\": 0.35", "\"lectern_float_distance\": 0")]
        [TestCase("\"lectern_follow_seconds\": 0.18", "\"lectern_follow_seconds\": -1")]
        [TestCase("#E3A83B40", "not-a-color")]
        public void InvalidExperienceConfigurationFailsWithADiagnostic(string before, string after)
        {
            var json = Resources.Load<TextAsset>("MuseumExperienceConfiguration").text;
            Assert.Throws<InvalidOperationException>(() => MuseumExperienceConfiguration.Parse(json.Replace(before, after)));
        }
    }
}
