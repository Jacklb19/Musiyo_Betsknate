using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Comfort and sound choices: each voice under the general volume, the sizes of the subtitles and of the reading, and
    /// on a headset the vignette while moving and the height of the eyes. They are saved with the other preferences,
    /// survive earlier versions, and the settings page offers each one where it applies.
    /// </summary>
    public sealed class MuseumComfortTests
    {
        [Test]
        public void NewChoicesTakeTheirDefaultsFromEarlierPreferencesAndSurviveReload()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var earlier = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":3,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":false,\"music_level\":0.4,\"turn_mode\":\"snap\",\"turn_speed\":105}");
            Assert.That(earlier.MusicLevel, Is.EqualTo(.4f));
            Assert.That(earlier.NarrationLevel, Is.EqualTo(1));
            Assert.That(earlier.GuideLevel, Is.EqualTo(1));
            Assert.That(earlier.SubtitleScale, Is.EqualTo(1));
            Assert.That(earlier.ReadingScale, Is.EqualTo(1));
            Assert.That(earlier.VignetteChoice, Is.EqualTo(MuseumVignette.Soft), "A visitor who never chose keeps the soft vignette.");
            Assert.That(earlier.HeightOffset, Is.Zero);
            earlier.SetNarrationLevel(.7f);
            earlier.SetGuideLevel(.2f);
            earlier.SetSubtitleScale(1.25f);
            earlier.SetReadingScale(1.3f);
            earlier.SetVignette(MuseumVignette.Strong);
            earlier.SetHeightOffset(.1f);
            var loaded = MuseumVisitorPreferences.Load(settings, earlier.Serialize());
            Assert.That(loaded.NarrationLevel, Is.EqualTo(.7f));
            Assert.That(loaded.GuideLevel, Is.EqualTo(.2f));
            Assert.That(loaded.SubtitleScale, Is.EqualTo(1.25f));
            Assert.That(loaded.ReadingScale, Is.EqualTo(1.3f));
            Assert.That(loaded.VignetteChoice, Is.EqualTo(MuseumVignette.Strong));
            Assert.That(loaded.HeightOffset, Is.EqualTo(.1f).Within(.0001f));
            Assert.That(loaded.TurnMode, Is.EqualTo("snap"), "The earlier choices stay.");
            loaded.Reset();
            Assert.That(loaded.NarrationLevel, Is.EqualTo(1));
            Assert.That(loaded.GuideLevel, Is.EqualTo(1));
            Assert.That(loaded.SubtitleScale, Is.EqualTo(1));
            Assert.That(loaded.ReadingScale, Is.EqualTo(1));
            Assert.That(loaded.VignetteChoice, Is.EqualTo(MuseumVignette.Soft));
            Assert.That(loaded.HeightOffset, Is.Zero);
        }

        [Test]
        public void OddChoicesFallBackWithoutDiscardingTheRest()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var odd = MuseumVisitorPreferences.Load(settings,
                "{\"schema_version\":4,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":true,\"narration_level\":7,\"guide_level\":-1,"
                + "\"subtitle_scale\":3,\"reading_scale\":0.2,\"vignette\":\"blur\",\"height_offset\":2}");
            Assert.That(odd.Sensitivity, Is.EqualTo(.3f));
            Assert.That(odd.NarrationLevel, Is.EqualTo(1));
            Assert.That(odd.GuideLevel, Is.EqualTo(1));
            Assert.That(odd.SubtitleScale, Is.EqualTo(1));
            Assert.That(odd.ReadingScale, Is.EqualTo(1));
            Assert.That(odd.VignetteChoice, Is.EqualTo(MuseumVignette.Soft));
            Assert.That(odd.HeightOffset, Is.Zero);
            odd.SetHeightOffset(5);
            Assert.That(odd.HeightOffset, Is.EqualTo(MuseumComfort.MaxHeightOffset));
            odd.SetSubtitleScale(2);
            Assert.That(odd.SubtitleScale, Is.EqualTo(1), "Only an offered size is kept.");
            odd.SetNarrationLevel(float.NaN);
            Assert.That(odd.NarrationLevel, Is.EqualTo(1));
        }

        [Test]
        public void SizesAndVignettesCycleThroughTheirOfferedValues()
        {
            Assert.That(MuseumComfort.NextScale(MuseumComfort.SubtitleScales, 1), Is.EqualTo(1.25f));
            Assert.That(MuseumComfort.NextScale(MuseumComfort.SubtitleScales, 1.25f), Is.EqualTo(.85f));
            Assert.That(MuseumComfort.NextScale(MuseumComfort.ReadingScales, 1.3f), Is.EqualTo(1));
            Assert.That(MuseumComfort.NextScale(MuseumComfort.ReadingScales, 9), Is.EqualTo(1), "An unknown size starts the cycle again.");
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.SubtitleScales, .85f), Is.EqualTo("size_small"));
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.SubtitleScales, 1), Is.EqualTo("size_normal"));
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.SubtitleScales, 1.25f), Is.EqualTo("size_large"));
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.ReadingScales, 1.15f), Is.EqualTo("size_large"));
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.ReadingScales, 1.3f), Is.EqualTo("size_larger"));
            Assert.That(MuseumComfort.ScaleKey(MuseumComfort.ReadingScales, 7), Is.EqualTo("size_normal"));
            Assert.That(MuseumComfort.NextVignette(MuseumVignette.Off), Is.EqualTo(MuseumVignette.Soft));
            Assert.That(MuseumComfort.NextVignette(MuseumVignette.Strong), Is.EqualTo(MuseumVignette.Off));
            Assert.That(MuseumComfort.Intensity(MuseumVignette.Off), Is.Zero);
            Assert.That(MuseumComfort.Intensity(MuseumVignette.Soft), Is.LessThan(MuseumComfort.Intensity(MuseumVignette.Strong)));
            Assert.That(MuseumComfort.ParseVignette(MuseumComfort.Name(MuseumVignette.Strong)), Is.EqualTo(MuseumVignette.Strong));
            Assert.That(MuseumComfort.Centimetres(.05f), Is.EqualTo(5));
            Assert.That(MuseumInterfaceText.Format("height_value", -10), Does.Contain("-10"));
        }

        [Test]
        public void TheVignetteClosesWhileMovingAndNeverDrawsWhenSwitchedOff()
        {
            var head = new GameObject("ComfortTestHead");
            var vignette = head.AddComponent<MuseumComfortVignette>();
            try
            {
                vignette.Configure(head.transform);
                Assert.That(vignette.Drawn, Is.False);
                vignette.Intensity = MuseumComfort.Intensity(MuseumVignette.Soft);
                vignette.Set(true);
                vignette.Step(.05f);
                Assert.That(vignette.Level, Is.GreaterThan(0).And.LessThan(1), "It closes over a few frames, not at once.");
                Assert.That(vignette.Drawn, Is.True);
                vignette.Step(1);
                Assert.That(vignette.Level, Is.EqualTo(1));
                vignette.Set(false);
                vignette.Step(.1f);
                Assert.That(vignette.Level, Is.LessThan(1).And.GreaterThan(0), "It opens more slowly than it closes.");
                vignette.Step(1);
                Assert.That(vignette.Drawn, Is.False);
                vignette.Intensity = 0;
                vignette.Set(true);
                vignette.Step(1);
                Assert.That(vignette.Drawn, Is.False, "Switched off, no veil is ever drawn.");
                var canvas = head.GetComponentInChildren<Canvas>(true);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace), "A headset only sees what stands in the world.");
                Assert.That(canvas.transform.localPosition.z, Is.GreaterThan(MuseumXrEntry.VeilDistance));
            }
            finally { Object.DestroyImmediate(head); }
        }

        private sealed class MenuFixture : System.IDisposable
        {
            public readonly GameObject Root, Canvas, Events;
            public readonly MuseumMenuHud Menu;
            public readonly MuseumVisitFlow Flow;
            public readonly MuseumInteraction Interaction;
            public readonly TourLoader Loader;
            public readonly MuseumVisitorPreferences Preferences;
            private readonly bool hadStored;
            private readonly string stored;

            public MenuFixture()
            {
                hadStored = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
                stored = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
                MuseumInput.Replace(MuseumInput.CreateDefault());
                Root = new GameObject("ComfortTestRuntime");
                Loader = Root.AddComponent<TourLoader>();
                Interaction = Root.AddComponent<MuseumInteraction>();
                Flow = Root.AddComponent<MuseumVisitFlow>();
                Preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
                Canvas = new GameObject("ComfortTestCanvas", typeof(RectTransform), typeof(UnityEngine.Canvas));
                Events = new GameObject("ComfortTestEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events.GetComponent<EventSystem>(), null);
                EventSystem.current = Events.GetComponent<EventSystem>();
                Menu = Canvas.AddComponent<MuseumMenuHud>();
                Menu.Configure(Flow, Interaction, Loader, Preferences);
            }

            public void Dispose()
            {
                typeof(MuseumMenuHud).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Menu, null);
                Object.DestroyImmediate(Canvas);
                Object.DestroyImmediate(Root);
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events.GetComponent<EventSystem>(), null);
                Object.DestroyImmediate(Events);
                MuseumInput.Replace(null);
                if (hadStored) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, stored);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void EachVoiceHasItsOwnLevelUnderTheGeneralVolume()
        {
            using var fixture = new MenuFixture();
            var music = fixture.Root.AddComponent<MuseumMusic>();
            fixture.Menu.Configure(fixture.Flow, fixture.Interaction, fixture.Loader, fixture.Preferences);
            float general = fixture.Preferences.Volume;
            fixture.Menu.NarrationLevelSlider.value = .3f;
            Assert.That(fixture.Preferences.NarrationLevel, Is.EqualTo(.3f));
            Assert.That(fixture.Preferences.Volume, Is.EqualTo(general));
            Assert.That(fixture.Preferences.MusicLevel, Is.EqualTo(1));
            fixture.Menu.GuideLevelSlider.value = 0;
            Assert.That(fixture.Preferences.GuideLevel, Is.Zero);
            Assert.That(fixture.Preferences.NarrationLevel, Is.EqualTo(.3f));
            Assert.That(MuseumMusic.TargetVolume(general, .32f, 1, false, 1), Is.EqualTo(general * .32f).Within(.0001f),
                "The music follows the general volume, not the narration's.");
            fixture.Menu.View.Settings.Reset.onClick.Invoke();
            Assert.That(fixture.Menu.NarrationLevelSlider.value, Is.EqualTo(1));
            Assert.That(fixture.Menu.GuideLevelSlider.value, Is.EqualTo(1));
            // The sound section lists the general volume, the music, the narration and the guide, in that order.
            var content = fixture.Menu.View.Settings.Scroll.content;
            int volume = fixture.Menu.View.Settings.Volume.transform.GetSiblingIndex();
            Assert.That(fixture.Menu.MusicLevelSlider.transform.GetSiblingIndex(), Is.EqualTo(volume + 1));
            Assert.That(fixture.Menu.NarrationLevelSlider.transform.GetSiblingIndex(), Is.EqualTo(volume + 2));
            Assert.That(fixture.Menu.GuideLevelSlider.transform.GetSiblingIndex(), Is.EqualTo(volume + 3));
            Assert.That(content.GetChild(volume + 4).name, Is.EqualTo("Heading"));
            var voice = fixture.Root.AddComponent<MuseumGuideVoice>();
            voice.SetVolume(.25f);
            Assert.That(voice.Volume, Is.EqualTo(.25f));
            voice.SetVolume(float.PositiveInfinity);
            Assert.That(voice.Volume, Is.EqualTo(.25f));
            Object.DestroyImmediate(music);
        }

        [Test]
        public void SizesCycleFromTheirOptionsAndSayTheirWord()
        {
            using var fixture = new MenuFixture();
            var menu = fixture.Menu;
            Assert.That(menu.SubtitleSizeValue.text, Is.EqualTo(MuseumInterfaceText.Get("size_normal")));
            menu.SubtitleSizeButton.onClick.Invoke();
            Assert.That(fixture.Preferences.SubtitleScale, Is.EqualTo(1.25f));
            Assert.That(menu.SubtitleSizeValue.text, Is.EqualTo(MuseumInterfaceText.Get("size_large")));
            menu.SubtitleSizeButton.onClick.Invoke();
            Assert.That(menu.SubtitleSizeValue.text, Is.EqualTo(MuseumInterfaceText.Get("size_small")));
            menu.ReadingSizeButton.onClick.Invoke();
            Assert.That(fixture.Preferences.ReadingScale, Is.EqualTo(1.15f));
            Assert.That(menu.ReadingSizeValue.text, Is.EqualTo(MuseumInterfaceText.Get("size_large")));
            menu.ReadingSizeButton.onClick.Invoke();
            Assert.That(menu.ReadingSizeValue.text, Is.EqualTo(MuseumInterfaceText.Get("size_larger")));
            // The reading options follow the subtitles switch, under the reading heading.
            int subtitles = menu.View.Settings.Subtitles.transform.GetSiblingIndex();
            Assert.That(menu.SubtitleSizeButton.transform.GetSiblingIndex(), Is.EqualTo(subtitles + 1));
            Assert.That(menu.ReadingSizeButton.transform.GetSiblingIndex(), Is.EqualTo(subtitles + 2));
        }

        [Test]
        public void HeadsetComfortIsOfferedOnlyOnAHeadsetAndTheHeightMovesInSteps()
        {
            using var fixture = new MenuFixture();
            var menu = fixture.Menu;
            Assert.That(menu.VignetteButton.gameObject.activeSelf, Is.False, "A desktop has no vignette.");
            Assert.That(menu.HeightOffsetSlider.gameObject.activeSelf, Is.False);
            Assert.That(menu.SubtitleSizeButton.gameObject.activeSelf, Is.True);
            MuseumInput.Current.SetSpatial(true);
            menu.SetSpatial(true);
            Assert.That(menu.VignetteButton.gameObject.activeSelf, Is.True);
            Assert.That(menu.HeightOffsetSlider.gameObject.activeSelf, Is.True);
            Assert.That(menu.SubtitleSizeButton.gameObject.activeSelf, Is.False, "A headset shows no screen subtitles.");
            Assert.That(menu.ReadingSizeButton.gameObject.activeSelf, Is.True, "The stand's text is read on a headset too.");
            Assert.That(menu.VignetteValue.text, Is.EqualTo(MuseumInterfaceText.Get("vignette_soft")));
            menu.VignetteButton.onClick.Invoke();
            Assert.That(fixture.Preferences.VignetteChoice, Is.EqualTo(MuseumVignette.Strong));
            Assert.That(menu.VignetteValue.text, Is.EqualTo(MuseumInterfaceText.Get("vignette_strong")));
            menu.VignetteButton.onClick.Invoke();
            Assert.That(fixture.Preferences.VignetteChoice, Is.EqualTo(MuseumVignette.Off));
            menu.HeightOffsetSlider.value = .07f;
            Assert.That(fixture.Preferences.HeightOffset, Is.EqualTo(.05f).Within(.0001f), "The height stops at whole steps.");
            Assert.That(menu.HeightOffsetSlider.value, Is.EqualTo(.05f).Within(.0001f));
            menu.HeightOffsetSlider.value = -1;
            Assert.That(fixture.Preferences.HeightOffset, Is.EqualTo(-MuseumComfort.MaxHeightOffset).Within(.0001f));
            // The comfort section comes after the control, before the reset.
            var content = menu.View.Settings.Scroll.content;
            Assert.That(menu.VignetteButton.transform.GetSiblingIndex(), Is.GreaterThan(menu.TurnSpeedButton.transform.GetSiblingIndex()));
            Assert.That(menu.HeightOffsetSlider.transform.GetSiblingIndex(), Is.EqualTo(menu.VignetteButton.transform.GetSiblingIndex() + 1));
            Assert.That(content.GetChild(menu.VignetteButton.transform.GetSiblingIndex() - 1).name, Is.EqualTo("Heading"));
            Assert.That(menu.View.Settings.Reset.transform.GetSiblingIndex(), Is.EqualTo(content.childCount - 1));
            menu.SetSpatial(false);
            Assert.That(menu.VignetteButton.gameObject.activeSelf, Is.False);
            Assert.That(menu.HeightOffsetSlider.gameObject.activeSelf, Is.False);
            // On the look, the height says centimetres instead of a share.
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1600, 900);
            var look = fixture.Canvas.AddComponent<MuseumTitleScreen>();
            look.Configure(menu.View, fixture.Flow, null);
            Assert.That(look.SliderValueText(menu.HeightOffsetSlider), Is.EqualTo(MuseumInterfaceText.Format("height_value", -25)));
        }
    }
}
