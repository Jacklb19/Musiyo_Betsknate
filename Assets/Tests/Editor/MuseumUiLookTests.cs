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
    /// The look of the menus given at runtime: sprites drawn by the interface, the quiet sounds, the page motion, and the
    /// settings page with its headings, value texts and the choice to leave.
    /// </summary>
    public sealed class MuseumUiLookTests
    {
        [Test]
        public void RoundedSpriteIsSlicedAtItsRadiusAndClearOutsideItsCorners()
        {
            var sprite = MuseumUiSprites.Rounded(12);
            Assert.That(sprite.border, Is.EqualTo(new Vector4(12, 12, 12, 12)));
            Assert.That(sprite.texture.GetPixel(0, 0).a, Is.LessThan(.05f), "A corner pixel lies outside the rounding.");
            Assert.That(sprite.texture.GetPixel(13, 13).a, Is.GreaterThan(.95f), "The middle is solid.");
            Assert.That(MuseumUiSprites.Rounded(12), Is.SameAs(sprite), "The same sprite is shared.");
        }

        [Test]
        public void RadialVeilIsClearAtItsCentreAndDarkAtItsEdge()
        {
            var veil = MuseumUiSprites.Radial(.4f, 64);
            Assert.That(veil.texture.GetPixel(32, 32).a, Is.EqualTo(0).Within(.01f));
            Assert.That(veil.texture.GetPixel(0, 32).a, Is.GreaterThan(.95f));
            Assert.That(veil.texture.GetPixel(32, 63).a, Is.GreaterThan(.95f));
            Assert.That(MuseumUiSprites.Sample(new[] { (0f, 1f), (1f, 0f) }, .5f), Is.EqualTo(.5f).Within(.01f));
            Assert.That(MuseumUiSprites.Dot(16).texture.GetPixel(8, 8).a, Is.GreaterThan(.95f));
            Assert.That(MuseumUiSprites.Dot(16).texture.GetPixel(0, 0).a, Is.LessThan(.05f));
        }

        [Test]
        public void InterfaceSoundsAreShortQuietAndFinite()
        {
            foreach (MuseumUiSound kind in System.Enum.GetValues(typeof(MuseumUiSound)))
            {
                var samples = MuseumUiSounds.Samples(kind);
                Assert.That(samples.Length, Is.GreaterThan(0).And.LessThanOrEqualTo(MuseumUiSounds.Rate * .3f), kind.ToString());
                float peak = 0;
                foreach (var sample in samples)
                {
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False, kind.ToString());
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Assert.That(peak, Is.GreaterThan(.05f).And.LessThanOrEqualTo(1), kind + " is heard but never clips.");
                Assert.That(Mathf.Abs(samples[samples.Length - 1]), Is.LessThan(.02f), kind + " ends without a click.");
                var clip = MuseumUiSounds.Clip(kind);
                Assert.That(clip.samples, Is.EqualTo(samples.Length));
                Assert.That(MuseumUiSounds.Clip(kind), Is.SameAs(clip));
            }
            Assert.That(MuseumUiSounds.Level, Is.LessThan(.5f), "The interface never speaks louder than the content.");
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
                Root = new GameObject("LookTestRuntime");
                Loader = Root.AddComponent<TourLoader>();
                Interaction = Root.AddComponent<MuseumInteraction>();
                Flow = Root.AddComponent<MuseumVisitFlow>();
                Preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
                Canvas = new GameObject("LookTestCanvas", typeof(RectTransform), typeof(UnityEngine.Canvas));
                Events = new GameObject("LookTestEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events.GetComponent<EventSystem>(), null);
                EventSystem.current = Events.GetComponent<EventSystem>();
                Menu = Canvas.AddComponent<MuseumMenuHud>();
                Menu.Configure(Flow, Interaction, Loader, Preferences);
            }

            public MuseumTitleScreen Look()
            {
                var look = Canvas.AddComponent<MuseumTitleScreen>();
                look.Configure(Menu.View, Flow, null);
                return look;
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
        public void SettingsReadInSectionsWhoseHeadingsTakeNoFocus()
        {
            using var fixture = new MenuFixture();
            var content = fixture.Menu.View.Settings.Scroll.content;
            var headings = new System.Collections.Generic.List<string>();
            foreach (Transform child in content)
                if (child.name == "Heading") headings.Add(child.GetComponent<TMP_Text>().text);
            Assert.That(headings, Is.EqualTo(new[] { MuseumInterfaceText.Get("heading_sound"), MuseumInterfaceText.Get("heading_reading"),
                MuseumInterfaceText.Get("heading_control"), MuseumInterfaceText.Get("heading_comfort"), MuseumInterfaceText.Get("heading_sky") }));
            foreach (Transform child in content)
                if (child.name == "Heading") Assert.That(child.GetComponent<Selectable>(), Is.Null);
            // Reading order: the sound first, then the reading, then the control, and the reset at the end.
            int sound = content.Find("Heading").GetSiblingIndex();
            Assert.That(fixture.Menu.View.Settings.Volume.transform.GetSiblingIndex(), Is.EqualTo(sound + 1));
            Assert.That(fixture.Menu.MusicLevelSlider.transform.GetSiblingIndex(), Is.EqualTo(sound + 2));
            Assert.That(fixture.Menu.View.Settings.Subtitles.transform.GetSiblingIndex(), Is.LessThan(fixture.Menu.View.Settings.Sensitivity.transform.GetSiblingIndex()));
            Assert.That(fixture.Menu.View.Settings.Reset.transform.GetSiblingIndex(), Is.EqualTo(content.childCount - 1));
            fixture.Menu.View.Main.Settings.onClick.Invoke();
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(fixture.Menu.View.Settings.Volume.gameObject),
                "The settings open on their first option.");
            // Configuring again neither doubles the headings nor the values.
            fixture.Menu.Configure(fixture.Flow, fixture.Interaction, fixture.Loader, fixture.Preferences);
            int count = 0;
            foreach (Transform child in content) if (child.name == "Heading") count++;
            Assert.That(count, Is.EqualTo(5));
            Assert.That(fixture.Menu.View.Settings.Subtitles.transform.Find("Value"), Is.Not.Null);
            Assert.That(fixture.Menu.View.Settings.Subtitles.GetComponentsInChildren<TMP_Text>(true).Length, Is.EqualTo(2));
        }

        [Test]
        public void OptionsSayTheirChoiceBesideThemAndTheMenuOffersToLeave()
        {
            using var fixture = new MenuFixture();
            var menu = fixture.Menu;
            Assert.That(menu.SubtitlesValue.text, Is.EqualTo(MuseumInterfaceText.Get(fixture.Preferences.Subtitles ? "option_on" : "option_off")));
            menu.View.Settings.Subtitles.onClick.Invoke();
            Assert.That(menu.SubtitlesValue.text, Is.EqualTo(MuseumInterfaceText.Get(fixture.Preferences.Subtitles ? "option_on" : "option_off")));
            Assert.That(menu.View.Settings.Subtitles.GetComponentInChildren<TMP_Text>().text, Is.EqualTo(MuseumInterfaceText.Get("subtitles_option")));
            Assert.That(MuseumMenuHud.CanQuit, Is.True, "Outside a Web page the museum can be left.");
            Assert.That(menu.QuitMainButton.gameObject.activeSelf, Is.True, "The title menu offers to leave.");
            Assert.That(menu.QuitButton.gameObject.activeSelf, Is.False, "The pause's choice waits for the pause.");
            Assert.That(menu.QuitMainButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo(MuseumInterfaceText.Get("quit_museum")));
            fixture.Flow.BeginVisit();
            fixture.Flow.EnterImmediately();
            fixture.Interaction.SetPaused(true);
            Assert.That(menu.QuitButton.gameObject.activeSelf, Is.True);
            Assert.That(menu.QuitMainButton.gameObject.activeSelf, Is.False);
            // The pause keeps every choice it had, and the way out comes last.
            var pause = menu.View.Pause;
            Assert.That(menu.QuitButton.transform.GetSiblingIndex(), Is.GreaterThan(pause.MainMenu.transform.GetSiblingIndex()));
            Assert.That(pause.Primary.gameObject.activeSelf && menu.AutoTourButton.gameObject.activeSelf == (fixture.Interaction.GetComponent<MuseumAutoTour>() != null));
        }

        [Test]
        public void InAHeadsetTheCardIsWiderAndStandsToTheLeftOfTheGaze()
        {
            using var fixture = new MenuFixture();
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1024, 900);
            fixture.Look();
            var layout = fixture.Menu.View.GetComponent<MuseumMenuLayout>();
            layout.Spatial = true;
            layout.Apply();
            var centred = ((RectTransform)fixture.Menu.View.Settings.transform).rect.width;
            layout.SpatialColumn = new Vector2(840, -440);
            layout.Apply();
            Assert.That(layout.Compact, Is.True, "A headset keeps the card the owner liked.");
            foreach (var page in new[] { fixture.Menu.View.Pause, fixture.Menu.View.Settings, fixture.Menu.View.Controls })
            {
                var rect = (RectTransform)page.transform;
                Assert.That(rect.pivot.x, Is.EqualTo(.5f));
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(-440).Within(.01f), page.name + " stands to the left.");
                Assert.That(rect.rect.width, Is.EqualTo(840).Within(.01f), page.name + " takes the headset's width.");
            }
            Assert.That(840, Is.GreaterThan(centred), "The headset's card is wider than the one centred before.");
            var main = fixture.Menu.View.Main.transform;
            var frame = (RectTransform)(main.parent != fixture.Menu.View.Overlay.transform ? main.parent : main);
            Assert.That(frame.anchoredPosition.x, Is.EqualTo(-440).Within(.01f));
            // Without a headset the column on the left of a screen is unchanged.
            layout.Spatial = false;
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1600, 900);
            layout.Apply();
            Assert.That(((RectTransform)fixture.Menu.View.Settings.transform).pivot.x, Is.EqualTo(0));
        }

        [Test]
        public void TheLookGivesEveryPageAColumnOnAScreenAndACardWhereItIsNarrow()
        {
            using var fixture = new MenuFixture();
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1600, 900);
            var look = fixture.Look();
            var layout = fixture.Menu.View.GetComponent<MuseumMenuLayout>();
            layout.Apply();
            Assert.That(layout.Compact, Is.False);
            foreach (var page in new[] { fixture.Menu.View.Pause, fixture.Menu.View.Settings, fixture.Menu.View.Controls })
            {
                var rect = (RectTransform)page.transform;
                Assert.That(rect.pivot.x, Is.EqualTo(0), page.name + " stands on the left.");
                Assert.That(rect.anchoredPosition.x, Is.GreaterThan(0));
                Assert.That(page.GetComponent<Image>().sprite, Is.SameAs(MuseumUiSprites.Rounded(MuseumTitleScreen.CardRadius)));
            }
            typeof(MuseumTitleScreen).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(look, null);
            Assert.That(look.Shaded, Is.True, "A menu on a wide screen stands on the dark gathering on the left.");
            Assert.That(fixture.Menu.View.Settings.GetComponent<Image>().color.a, Is.EqualTo(0), "On the shade a page has no card.");
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(900, 700);
            layout.Apply();
            Assert.That(layout.Compact, Is.True);
            typeof(MuseumTitleScreen).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(look, null);
            Assert.That(look.Shaded, Is.False);
            Assert.That(fixture.Menu.View.Settings.GetComponent<Image>().color.a, Is.GreaterThan(.5f), "A narrow window keeps the card.");
            Assert.That(((RectTransform)fixture.Menu.View.Settings.transform).pivot.x, Is.EqualTo(.5f));
            // Every slider shows its value and carries a fill; every choice takes the focus under the pointer.
            var volume = fixture.Menu.View.Settings.Volume;
            Assert.That(volume.fillRect, Is.Not.Null);
            Assert.That(look.SliderValueText(volume), Is.EqualTo(Mathf.RoundToInt(volume.value * 100) + " %"));
            volume.value = .5f;
            Assert.That(look.SliderValueText(volume), Is.EqualTo("50 %"));
            look.FormatSlider(volume, amount => "x" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(look.SliderValueText(volume), Is.EqualTo("x0.5"));
            Assert.That(fixture.Menu.View.Pause.Primary.GetComponent<MuseumMenuChoice>(), Is.Not.Null);
            // Nothing authored repaints a choice or the rule when a page opens again.
            foreach (var binding in fixture.Menu.View.Pause.GetComponentsInChildren<MuseumMenuThemeBinding>(true))
                Assert.That(binding.GetComponentInParent<Selectable>(), Is.Null, binding.name + " keeps a binding inside a choice.");
            Assert.That(fixture.Menu.View.Pause.transform.Find("Viewport/Content/PresentationRule").GetComponent<MuseumMenuThemeBinding>(), Is.Null);
            foreach (var choice in new[] { fixture.Menu.View.Pause.Settings, fixture.Menu.View.Pause.Primary })
                Assert.That(choice.GetComponentInChildren<TMP_Text>(true).fontSize, Is.EqualTo(MuseumTitleScreen.ChoiceSize));
            Assert.That(fixture.Menu.View.Main.Scroll.content.Find("LoadingLine"), Is.Not.Null);
            // A page longer than the screen shows a thin line at its edge that scrolls it; the keyboard never stops on it.
            var settings = fixture.Menu.View.Settings;
            Assert.That(settings.Scroll.verticalScrollbar, Is.Not.Null);
            Assert.That(settings.Scroll.verticalScrollbar.transform.IsChildOf(settings.transform), Is.True);
            Assert.That(settings.Scroll.verticalScrollbar.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(settings.Scroll.verticalScrollbarVisibility, Is.EqualTo(ScrollRect.ScrollbarVisibility.AutoHide));
            Assert.That(settings.GetComponent<LayoutElement>().preferredHeight, Is.GreaterThanOrEqualTo(1000));
            fixture.Menu.View.Main.Settings.onClick.Invoke();
            var move = typeof(MuseumMenuHud).GetMethod("MoveSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int step = 0; step < 24; step++)
            {
                move.Invoke(fixture.Menu, new object[] { 1 });
                Assert.That(EventSystem.current.currentSelectedGameObject.GetComponent<Scrollbar>(), Is.Null);
            }
        }

        [Test]
        public void APageThatOpensBreathesInAndEndsExactlyAsAuthored()
        {
            using var fixture = new MenuFixture();
            var motion = fixture.Menu.Motion;
            Assert.That(motion, Is.Not.Null);
            var settings = fixture.Menu.View.Settings;
            fixture.Menu.View.Main.Settings.onClick.Invoke();
            var group = settings.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null);
            Assert.That(group.alpha, Is.EqualTo(0).Within(.001f), "A page starts invisible on its first frame.");
            Assert.That(settings.transform.localScale.x, Is.EqualTo(MuseumMenuMotion.StartScale).Within(.001f));
            motion.Complete();
            Assert.That(group.alpha, Is.EqualTo(1));
            Assert.That(settings.transform.localScale, Is.EqualTo(Vector3.one));
            foreach (var choice in settings.GetComponentsInChildren<Selectable>(false))
                Assert.That(choice.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1), choice.name);
            // Opening the same page again does not play it again; another page does.
            motion.Show(settings);
            Assert.That(group.alpha, Is.EqualTo(1));
            fixture.Menu.Back();
            Assert.That(fixture.Menu.View.Main.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0).Within(.001f));
            motion.Complete();
        }

        [Test]
        public void AMenuThatOpensComesIntoViewInsteadOfSnapping()
        {
            using var fixture = new MenuFixture();
            fixture.Flow.BeginVisit();
            fixture.Flow.EnterImmediately();
            Assert.That(fixture.Menu.View.Overlay.gameObject.activeSelf, Is.False);
            fixture.Interaction.SetPaused(true);
            Assert.That(fixture.Menu.View.Overlay.alpha, Is.EqualTo(0).Within(.001f), "The pause starts clear and rises in Update.");
            Assert.That(fixture.Menu.View.Overlay.interactable, Is.True);
            Assert.That(MuseumMenuHud.OpenSeconds, Is.LessThan(MuseumMenuHud.AfterEndingFadeSeconds));
        }
    }
}
