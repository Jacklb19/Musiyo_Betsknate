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
        public void DisabledPresenterUnsubscribesAndRestoresModuleNavigation()
        {
            Assert.That(module.move, Is.Null);
            menu.enabled = false;
            Callback("OnDisable");
            Assert.That(module.move, Is.Not.Null);
            bool before = preferences.Subtitles;
            menu.View.Settings.Subtitles.onClick.Invoke();
            Assert.That(preferences.Subtitles, Is.EqualTo(before));
            menu.enabled = true;
            Callback("OnEnable");
            Assert.That(module.move, Is.Null);
            menu.View.Main.Controls.onClick.Invoke();
            Assert.That(menu.View.Controls.gameObject.activeSelf, Is.True);
            Assert.That(menu.Back(), Is.True);
            Assert.That(menu.View.Main.gameObject.activeSelf, Is.True);
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
        public void DestroyedPresenterRestoresNavigationAndReleasesFlowReference()
        {
            Callback("OnDestroy");
            UnityEngine.Object.DestroyImmediate(menu);
            Assert.That(flow.Menu, Is.Null);
            Assert.That(module.move, Is.Not.Null);
            Assert.That(canvas.GetComponentsInChildren<MuseumMenuView>(true), Is.Empty);
        }

        // EditMode does not dispatch ordinary MonoBehaviour callbacks; PlayMode exercises actual enable/disable/destruction.
        private void Callback(string name)
            => typeof(MuseumMenuHud).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
    }
}
