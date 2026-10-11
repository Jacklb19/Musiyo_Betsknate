using System;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumMenuPrefabTests
    {
        private GameObject root;
        private GameObject canvas;
        private GameObject events;
        private MuseumMenuHud menu;
        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private TourLoader loader;
        private MuseumVisitorPreferences preferences;
        private InputSystemUIInputModule module;
        private bool hadStoredPreferences;
        private string storedPreferences;

        [SetUp]
        public void SetUp()
        {
            hadStoredPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            storedPreferences = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            MuseumInput.Replace(MuseumInput.CreateDefault());
            root = new GameObject("MenuTestRuntime");
            loader = root.AddComponent<TourLoader>();
            interaction = root.AddComponent<MuseumInteraction>();
            flow = root.AddComponent<MuseumVisitFlow>();
            preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
            canvas = new GameObject("MenuTestCanvas", typeof(RectTransform), typeof(Canvas));
            events = new GameObject("MenuTestEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
            module = events.GetComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(events.GetComponent<EventSystem>(), null);
            EventSystem.current = events.GetComponent<EventSystem>();
            menu = canvas.AddComponent<MuseumMenuHud>();
            menu.Configure(flow, interaction, loader, preferences);
        }

        [TearDown]
        public void TearDown()
        {
            if (menu != null) Callback("OnDestroy");
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (events != null) typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(events.GetComponent<EventSystem>(), null);
            if (events != null) UnityEngine.Object.DestroyImmediate(events);
            MuseumInput.Replace(null);
            if (hadStoredPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, storedPreferences);
            else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
            PlayerPrefs.Save();
        }

        [Test]
        public void ShippedPrefabHasValidNestedPagesAndNoExtraInputInfrastructure()
        {
            var prefab = Resources.Load<MuseumMenuView>(MuseumMenuView.ResourceName);
            Assert.That(prefab, Is.Not.Null);
            Assert.DoesNotThrow(prefab.ValidateReferences);
            foreach (var page in new[] { prefab.Main, prefab.Pause, prefab.Settings, prefab.Controls })
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(page), Is.Not.Null, "Each page must remain a reusable nested prefab.");
            Assert.That(prefab.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<EventSystem>(true), Is.Empty);
        }

        [Test]
        public void ReconfigureAndReopenPreserveOneViewAndOnePreferenceCallback()
        {
            int changes = 0;
            preferences.Changed += () => changes++;
            for (int i = 0; i < 3; i++)
            {
                menu.Configure(flow, interaction, loader, preferences);
                menu.enabled = false;
                Callback("OnDisable");
                menu.enabled = true;
                Callback("OnEnable");
            }
            Assert.That(canvas.GetComponentsInChildren<MuseumMenuView>(true).Length, Is.EqualTo(1));
            menu.View.Main.Settings.onClick.Invoke();
            menu.View.Settings.Subtitles.onClick.Invoke();
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(preferences.Subtitles, Is.EqualTo(!MuseumExperienceConfiguration.Current.DefaultSubtitles));
            Assert.That(canvas.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void OpenMenuKeepsWalkingSilentAndMovesItsOwnFocus()
        {
            var input = MuseumInput.Current;
            Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Menu));
            Assert.That(module.actionsAsset, Is.SameAs(input.Asset), "The interface module reads the one runtime copy of the actions.");
            Assert.That(module.move, Is.Null, "The menu moves its own focus, so the module must not move it a second time.");
            Assert.That(input.Navigate.enabled, Is.True);
            Assert.That(input.Move.enabled, Is.False);
            Assert.That(input.Interact.enabled, Is.False);
        }

        [Test]
        public void DisabledPresenterUnsubscribesAndReturnsWhenEnabled()
        {
            menu.enabled = false;
            Callback("OnDisable");
            bool before = preferences.Subtitles;
            menu.View.Settings.Subtitles.onClick.Invoke();
            Assert.That(preferences.Subtitles, Is.EqualTo(before));
            menu.enabled = true;
            Callback("OnEnable");
            menu.View.Main.Controls.onClick.Invoke();
            Assert.That(menu.View.Controls.gameObject.activeSelf, Is.True);
            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.View.Main.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ControlsPageListsEveryRebindableKeyWithItsCurrentAssignment()
        {
            var input = MuseumInput.Current;
            var rows = menu.View.Controls.Bindings.GetComponentsInChildren<MuseumRebindRow>(true);
            int rebindable = 0;
            foreach (var binding in input.Bindings.All) if (binding.Rebindable) rebindable++;
            Assert.That(rows.Length, Is.EqualTo(rebindable));
            var guide = System.Array.Find(rows, row => row.Binding.Id == "guide");
            Assert.That(guide.Action.text, Is.EqualTo(MuseumInterfaceText.Get("input_guide")));
            Assert.That(guide.Key.text, Is.EqualTo("G"));
            Assert.That(input.Bindings.TryRebind(input.Bindings.Find("guide"), "<Keyboard>/t", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(guide.Key.text, Is.EqualTo("T"), "A reassigned key reaches its row.");
            Assert.That(MuseumInterfaceText.Format("guide_prompt", "Guía"), Does.Contain("T:"), "Prompts name the key in force.");
            menu.Configure(flow, interaction, loader, preferences);
            Assert.That(menu.View.Controls.Bindings.GetComponentsInChildren<MuseumRebindRow>(true).Length, Is.EqualTo(rebindable),
                "Configuring again reuses the rows.");
            menu.View.Controls.RestoreBindings.onClick.Invoke();
            Assert.That(guide.Key.text, Is.EqualTo("G"));
            Assert.That(menu.View.Controls.BindingStatus.text, Is.EqualTo(MuseumInterfaceText.Get("rebind_restored")));
        }

        [Test]
        public void LoadingFallbackSettlesToExploreAndKeepsRetryAndPauseActions()
        {
            var change = typeof(TourLoader).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);
            change.Invoke(loader, new object[] { TourLoadState.Loading, "Loading fixture" });
            Assert.That(menu.View.Main.Primary.interactable, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Settings.gameObject));
            menu.View.Main.Primary.onClick.Invoke();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
            change.Invoke(loader, new object[] { TourLoadState.Unavailable, "Unavailable fixture" });
            Assert.That(menu.View.Main.Retry.gameObject.activeSelf, Is.True);
            change.Invoke(loader, new object[] { TourLoadState.Ready, "Ready fixture" });
            Assert.That(menu.View.Main.Primary.interactable, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Primary.gameObject));
            menu.View.Main.Primary.onClick.Invoke();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Entering));
            flow.Advance(MuseumExperienceConfiguration.Current.EntryFadeSeconds);
            interaction.SetPaused(true);
            Assert.That(menu.View.Pause.gameObject.activeSelf, Is.True);
            menu.View.Pause.MainMenu.onClick.Invoke();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
            Assert.That(menu.View.Main.Primary.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo(MuseumInterfaceText.Get("resume_visit")));
        }

        [Test]
        public void AGlidingEntryShowsTheMuseumBehindTheMenuAndNeverDarkensIt()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var visitor = new GameObject("MenuTestVisitor");
            var eyes = new GameObject("MenuTestCamera", typeof(Camera));
            eyes.transform.SetParent(visitor.transform, false);
            eyes.transform.localPosition = Vector3.up * 1.65f;
            var anchor = new GameObject("MenuTestViewpoint", typeof(MuseumEntryViewpoint));
            anchor.transform.position = new Vector3(-2, 2.4f, -3);
            try
            {
                var entry = root.AddComponent<MuseumEntryView>();
                entry.HeadsetActive = () => false;
                entry.Configure(eyes.GetComponent<Camera>(), visitor.transform, anchor.GetComponent<MuseumEntryViewpoint>());
                menu.Configure(flow, interaction, loader, preferences);
                var backdrop = menu.View.Overlay.GetComponent<Graphic>();
                Assert.That(backdrop.color, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(settings.EntryBackdropColor)));
                Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(menu.View.Main.transform), Is.True,
                    "The staged entry keeps keyboard focus on its own page.");
                flow.BeginVisit();
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Glide));
                Assert.That(menu.View.Transition.gameObject.activeSelf, Is.False);
                Assert.That(MuseumInput.Current.Context, Is.EqualTo(MuseumInputContext.Menu), "Nothing walks while the camera glides.");
                flow.Advance(flow.TransitionSeconds * settings.EntryMenuFadeShare);
                Callback("Update");
                Assert.That(menu.View.Overlay.alpha, Is.EqualTo(0).Within(.001f));
                Assert.That(menu.View.Overlay.interactable, Is.False);
                Assert.That(menu.View.Transition.alpha, Is.EqualTo(0));
                flow.Advance(flow.TransitionSeconds);
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
                interaction.SetPaused(true);
                Assert.That(backdrop.color, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(settings.BackdropColor)),
                    "A pause covers the visit with the denser backdrop.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(visitor);
                UnityEngine.Object.DestroyImmediate(anchor);
            }
        }

        [Test]
        public void AuthoredLayoutAndUnboundStyleSurviveRefreshAndReconfigure()
        {
            var page = menu.View.Main;
            var layout = page.GetComponent<LayoutElement>();
            layout.preferredWidth = 711;
            layout.preferredHeight = 777;
            var binding = page.Title.GetComponent<MuseumMenuThemeBinding>();
            binding.FontToken = MuseumMenuFontToken.None;
            binding.ColorToken = MuseumMenuColorToken.None;
            page.Title.fontSize = 31;
            page.Title.color = Color.cyan;
            menu.Configure(flow, interaction, loader, preferences);
            menu.View.Main.Settings.onClick.Invoke();
            menu.Back();
            Assert.That(layout.preferredWidth, Is.EqualTo(711));
            Assert.That(layout.preferredHeight, Is.EqualTo(777));
            Assert.That(page.Title.fontSize, Is.EqualTo(31));
            Assert.That(page.Title.color, Is.EqualTo(Color.cyan));
        }

        [Test]
        public void ReopeningSettingsKeepsPreferenceDependentSubtitleLabel()
        {
            preferences.SetSubtitles(false);
            menu.View.Main.Settings.onClick.Invoke();
            menu.Back();
            menu.View.Main.Settings.onClick.Invoke();
            Assert.That(menu.View.Settings.Subtitles.GetComponentInChildren<TMPro.TMP_Text>().text,
                Is.EqualTo(MuseumInterfaceText.Get("show_subtitles")));
        }

        [Test]
        public void KeyboardOrderUsesVisibleHierarchyAndWraps()
        {
            var move = typeof(MuseumMenuHud).GetMethod("MoveSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            var first = menu.View.Main.Primary;
            menu.View.Main.Settings.transform.SetAsLastSibling();
            EventSystem.current.SetSelectedGameObject(first.gameObject);
            move.Invoke(menu, new object[] { -1 });
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Settings.gameObject));
            move.Invoke(menu, new object[] { 1 });
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(first.gameObject));
        }

        [Test]
        public void ConstrainedCanvasShrinksTheAuthoredCardAndScrollsKeyboardFocusIntoView()
        {
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(480, 320);
            menu.Configure(flow, interaction, loader, preferences);
            Canvas.ForceUpdateCanvases();
            var page = menu.View.Main;
            Assert.That(((RectTransform)page.transform).rect.width, Is.LessThanOrEqualTo(480));
            Assert.That(((RectTransform)page.transform).rect.height, Is.LessThanOrEqualTo(320));
            var move = typeof(MuseumMenuHud).GetMethod("MoveSelection", BindingFlags.Instance | BindingFlags.NonPublic);
            move.Invoke(menu, new object[] { -1 });
            var selected = EventSystem.current.currentSelectedGameObject.transform;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(page.Scroll.viewport, selected);
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(page.Scroll.viewport.rect.yMin - .1f));
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(page.Scroll.viewport.rect.yMax + .1f));
        }

        [Test]
        public void MissingRequiredReferenceReportsThePageAndField()
        {
            menu.View.Controls.Back = null;
            var error = Assert.Throws<InvalidOperationException>(menu.View.ValidateReferences);
            Assert.That(error.Message, Does.Contain("Controls").And.Contain("Back"));
        }

        [Test]
        public void DestroyedPresenterReleasesFlowReferenceAndItsView()
        {
            Callback("OnDestroy");
            UnityEngine.Object.DestroyImmediate(menu);
            Assert.That(flow.Menu, Is.Null);
            Assert.That(canvas.GetComponentsInChildren<MuseumMenuView>(true), Is.Empty);
        }

        // EditMode does not dispatch ordinary MonoBehaviour callbacks; PlayMode exercises actual enable/disable/destruction.
        private void Callback(string name)
            => typeof(MuseumMenuHud).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
    }
}
