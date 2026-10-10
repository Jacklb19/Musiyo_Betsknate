using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Entry and pause menus share the same settings and keyboard navigation.</summary>
    public sealed class MuseumMenuHud : MonoBehaviour
    {
        private enum MenuPage { Home, Settings, Controls }
        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private TourLoader loader;
        private MuseumVisitorPreferences preferences;
        private MuseumExperienceConfiguration configuration;
        private GameObject overlay;
        private CanvasGroup overlayGroup;
        private CanvasGroup fade;
        private GameObject home;
        private GameObject settings;
        private GameObject controls;
        private TMP_Text title;
        private TMP_Text description;
        private Button begin;
        private Button resume;
        private Button mainMenu;
        private Button retry;
        private Button settingsButton;
        private Button settingsBack;
        private Button controlsBack;
        private Slider sensitivity;
        private Slider volume;
        private Button subtitles;
        private MenuPage page;
        private bool wasVisible;
        private bool wasEntry;
        private TourLoadState lastLoadState;
        private InputSystemUIInputModule navigationModule;
        private InputActionReference suspendedNavigation;

        [SerializeField] private MuseumMenuView viewPrefab;
        private MuseumMenuView view;
        private bool bound;
        private readonly System.Collections.Generic.List<System.Action> removeListeners = new();
        public MuseumMenuView View => view;

        public void Configure(MuseumVisitFlow visit, MuseumInteraction state, TourLoader tourLoader,
            MuseumVisitorPreferences visitorPreferences, MuseumMenuView prefab = null)
        {
            if (visit == null || state == null || tourLoader == null || visitorPreferences == null)
                throw new System.ArgumentException("Museum menu requires flow, interaction, loader and preferences.");
            Unbind();
            if (flow != null && flow.Menu == this) flow.Menu = null;
            flow = visit; interaction = state; loader = tourLoader; preferences = visitorPreferences;
            configuration = MuseumExperienceConfiguration.Current;
            if (view == null)
            {
                var existing = GetComponentsInChildren<MuseumMenuView>(true);
                if (existing.Length > 1) throw new System.InvalidOperationException("Multiple museum menu roots found.");
                if (existing.Length == 1) view = existing[0];
                else
                {
                    var source = prefab != null ? prefab : viewPrefab != null ? viewPrefab
                        : Resources.Load<MuseumMenuView>(MuseumMenuView.ResourceName);
                    if (source == null) throw new System.InvalidOperationException("Missing MuseumMenuRoot prefab. Restore Assets/_Musiyo/UI/Resources/MuseumMenuRoot.prefab or assign a menu prefab.");
                    source.ValidateReferences();
                    view = Instantiate(source, transform, false);
                    view.name = MuseumMenuView.ResourceName;
                }
            }
            view.ValidateReferences();
            overlay = view.Overlay.gameObject;
            overlayGroup = view.Overlay;
            fade = view.Transition;
            settings = view.Settings.gameObject;
            controls = view.Controls.gameObject;
            begin = view.Main.Primary;
            resume = view.Pause.Primary;
            mainMenu = view.Pause.MainMenu;
            sensitivity = view.Settings.Sensitivity;
            volume = view.Settings.Volume;
            subtitles = view.Settings.Subtitles;
            settingsBack = view.Settings.Back;
            controlsBack = view.Controls.Back;
            sensitivity.minValue = configuration.MinimumSensitivity;
            sensitivity.maxValue = configuration.MaximumSensitivity;
            volume.minValue = 0; volume.maxValue = 1;
            flow.Menu = this;
            if (isActiveAndEnabled) Bind();
        }

        private void Bind()
        {
            if (bound || view == null) return;
            bound = true;
            view.gameObject.SetActive(true);
            foreach (var label in view.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.RefreshText();
            foreach (var binding in view.GetComponentsInChildren<MuseumMenuThemeBinding>(true)) binding.Apply();
            Listen(begin, BeginVisit);
            Listen(resume, ResumeVisit);
            Listen(mainMenu, flow.ReturnToMenu);
            foreach (var homePage in new[] { view.Main, view.Pause })
            {
                Listen(homePage.Settings, OpenSettings);
                Listen(homePage.Controls, OpenControls);
                Listen(homePage.Retry, loader.Reload);
                Listen(homePage.Catalog, ReturnToCatalog);
            }
            Listen(subtitles, ToggleSubtitles);
            Listen(view.Settings.Reset, preferences.Reset);
            Listen(settingsBack, OpenHome);
            Listen(controlsBack, OpenHome);
            sensitivity.onValueChanged.AddListener(preferences.SetSensitivity);
            volume.onValueChanged.AddListener(preferences.SetVolume);
            flow.Changed += Refresh;
            interaction.Changed += Refresh;
            loader.Changed += Refresh;
            preferences.Changed += SyncSettings;
            SyncSettings(); Refresh();
        }

        private void Listen(Button button, UnityEngine.Events.UnityAction callback)
        {
            button.onClick.AddListener(callback);
            removeListeners.Add(() => { if (button != null) button.onClick.RemoveListener(callback); });
        }
        private void Unbind()
        {
            SuspendModuleNavigation(false);
            if (!bound) return;
            foreach (var remove in removeListeners) remove();
            removeListeners.Clear();
            sensitivity.onValueChanged.RemoveListener(preferences.SetSensitivity);
            volume.onValueChanged.RemoveListener(preferences.SetVolume);
            flow.Changed -= Refresh; interaction.Changed -= Refresh; loader.Changed -= Refresh;
            preferences.Changed -= SyncSettings;
            bound = false;
        }
        private void BeginVisit() { preferences.Save(); if (loader.State != TourLoadState.Loading) flow.BeginVisit(); }
        private void ResumeVisit() { preferences.Save(); interaction.SetPaused(false); }
        private void OpenSettings() => ShowPage(MenuPage.Settings);
        private void OpenControls() => ShowPage(MenuPage.Controls);
        private void OpenHome() => ShowPage(MenuPage.Home);
        private void ToggleSubtitles() => preferences.SetSubtitles(!preferences.Subtitles);
        private void ReturnToCatalog()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            interaction.GetComponent<MuseumWebBridge>()?.ReturnToCatalog();
#endif
        }
        private static bool CanReturnToCatalog
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }
        private void SyncSettings()
        {
            sensitivity.SetValueWithoutNotify(preferences.Sensitivity);
            volume.SetValueWithoutNotify(preferences.Volume);
            subtitles.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get(preferences.Subtitles ? "hide_subtitles" : "show_subtitles");
        }
        private void ShowPage(MenuPage selected)
        {
            preferences.Save();
            page = selected;
            Refresh();
            SelectFirst();
        }
        private void SelectFirst()
        {
            if (EventSystem.current == null) return;
            var selected = FirstSelectable();
            EventSystem.current.SetSelectedGameObject(selected);
            KeepFocusVisible(selected);
        }
        private GameObject FirstSelectable()
        {
            if (page == MenuPage.Settings) return sensitivity.gameObject;
            if (page == MenuPage.Controls) return controlsBack.gameObject;
            if (flow.Phase != VisitPhase.Menu) return resume.gameObject;
            if (begin.interactable) return begin.gameObject;
            return retry.gameObject.activeSelf ? retry.gameObject : settingsButton.gameObject;
        }
        private void Refresh()
        {
            if (!bound) return;
            bool entering = flow.Phase == VisitPhase.Entering;
            bool visible = flow.Phase == VisitPhase.Menu || !entering && interaction.State == InteractionState.Paused;
            overlay.SetActive(visible || entering);
            overlayGroup.interactable = overlayGroup.blocksRaycasts = visible;
            if (visible) overlayGroup.alpha = 1;
            fade.gameObject.SetActive(entering);
            UpdateTransition();
            if (!visible) page = MenuPage.Home;
            // Enter or Escape that closed the menu must not also activate a point or reopen the pause.
            if (wasVisible && !visible) flow.ConsumeInputThisFrame();
            bool entry = flow.Phase == VisitPhase.Menu;
            home = entry ? view.Main.gameObject : view.Pause.gameObject;
            var homeView = entry ? view.Main : view.Pause;
            retry = homeView.Retry;
            settingsButton = homeView.Settings;
            view.Main.gameObject.SetActive(page == MenuPage.Home && entry);
            view.Pause.gameObject.SetActive(page == MenuPage.Home && !entry);
            var currentPage = page == MenuPage.Settings ? view.Settings : page == MenuPage.Controls ? view.Controls : homeView;
            title = currentPage.Title;
            description = currentPage.Description;
            settings.SetActive(page == MenuPage.Settings);
            controls.SetActive(page == MenuPage.Controls);
            begin.gameObject.SetActive(entry);
            resume.gameObject.SetActive(!entry);
            mainMenu.gameObject.SetActive(!entry);
            foreach (var homePage in new[] { view.Main, view.Pause })
            {
                homePage.Retry.gameObject.SetActive(loader.State == TourLoadState.Unavailable);
                homePage.Catalog.gameObject.SetActive(CanReturnToCatalog);
            }
            begin.interactable = loader.State != TourLoadState.Loading;
            begin.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get(flow.HasStarted ? "resume_visit" : "explore_museum");
            title.text = page == MenuPage.Settings ? MuseumInterfaceText.Get("settings") : page == MenuPage.Controls ? MuseumInterfaceText.Get("controls")
                : entry ? interaction.Runtime.Contract?.tour?.name ?? MuseumInterfaceText.Get("museum_menu_title") : MuseumInterfaceText.Get("pause");
            description.text = page == MenuPage.Settings ? MuseumInterfaceText.Get("settings_description")
                : page == MenuPage.Controls ? MuseumInterfaceText.Get("controls_description")
                : !entry ? MuseumInterfaceText.Get("pause_menu_description")
                : loader.State == TourLoadState.Loading ? MuseumInterfaceText.Get("menu_loading")
                : loader.State == TourLoadState.Unavailable ? loader.Status
                : MuseumInterfaceText.Get("menu_description");
            // A page becoming active refreshes its neutral labels; apply preference-dependent text afterwards.
            SyncSettings();
            if (visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                var wayfinding = interaction.GetComponent<MuseumWayfinding>();
                if (wayfinding != null && wayfinding.MenuOpen) wayfinding.CloseMenu();
                // Loading picks a fallback; once the tour settles, "Explore" becomes the first choice again.
                bool loadSettled = loader.State != lastLoadState && page == MenuPage.Home;
                if (!wasVisible || entry != wasEntry || loadSettled || !SelectionInsideMenu()) SelectFirst();
            }
            lastLoadState = loader.State;
            SuspendModuleNavigation(visible);
            wasVisible = visible;
            wasEntry = entry;
        }
        private void Update()
        {
            if (!bound) return;
            if (flow.Phase == VisitPhase.Entering) UpdateTransition();
            var keyboard = Keyboard.current;
            if (!overlayGroup.interactable || keyboard == null) return;
            if (page != MenuPage.Home && (keyboard.backspaceKey.wasPressedThisFrame
                    || flow.Phase == VisitPhase.Menu && keyboard.escapeKey.wasPressedThisFrame)) { ShowPage(MenuPage.Home); return; }
            int step = keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame
                || keyboard.tabKey.wasPressedThisFrame && !keyboard.shiftKey.isPressed ? 1
                : keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame
                || keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed ? -1 : 0;
            // A mouse click on the backdrop clears the selection; any navigation key brings the focus back.
            if (!SelectionInsideMenu())
            {
                if (step != 0 || keyboard.enterKey.wasPressedThisFrame) SelectFirst();
                return;
            }
            if (step != 0) MoveSelection(step);
            int adjust = keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ? 1
                : keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame ? -1 : 0;
            var slider = EventSystem.current.currentSelectedGameObject.GetComponent<Slider>();
            if (adjust != 0 && slider != null)
                slider.value += adjust * configuration.SliderStep * (slider.maxValue - slider.minValue);
        }
        /// <summary>Keyboard order follows the visible page layout; it wraps at both ends.</summary>
        private void MoveSelection(int step)
        {
            var current = EventSystem.current.currentSelectedGameObject;
            var page = home.activeSelf ? home : settings.activeSelf ? settings : controls;
            var options = new System.Collections.Generic.List<Selectable>();
            foreach (var option in page.GetComponentsInChildren<Selectable>())
                if (option.IsInteractable() && option.gameObject.activeInHierarchy) options.Add(option);
            if (options.Count == 0) return;
            int index = options.FindIndex(option => option.gameObject == current);
            index = index < 0 ? 0 : (index + step + options.Count) % options.Count;
            EventSystem.current.SetSelectedGameObject(options[index].gameObject);
            KeepFocusVisible(options[index].gameObject);
        }
        /// <summary>The menu owns arrow keys while open, so the UI module's held-axis navigation cannot move twice.</summary>
        private void SuspendModuleNavigation(bool suspend)
        {
            if (suspend && navigationModule == null && EventSystem.current != null)
            {
                navigationModule = EventSystem.current.GetComponent<InputSystemUIInputModule>();
                if (navigationModule == null) return;
                suspendedNavigation = navigationModule.move;
                navigationModule.move = null;
            }
            else if (!suspend && navigationModule != null)
            {
                navigationModule.move = suspendedNavigation;
                navigationModule = null;
                suspendedNavigation = null;
            }
        }
        private bool SelectionInsideMenu()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(overlay.transform);
        }
        /// <summary>The menu dissolves into darkness, then the museum fades in; the camera never moves on its own.</summary>
        private void UpdateTransition()
        {
            if (flow.Phase != VisitPhase.Entering) { fade.alpha = 0; return; }
            float share = configuration.EntryMenuFadeShare;
            float progress = flow.TransitionProgress;
            overlayGroup.alpha = 1 - Mathf.SmoothStep(0, 1, progress / share);
            fade.alpha = progress < share ? Mathf.SmoothStep(0, 1, progress / share)
                : 1 - Mathf.SmoothStep(0, 1, (progress - share) / (1 - share));
        }
        /// <summary>Subpages consume Escape before exploration can resume.</summary>
        public bool Back()
        {
            if (page == MenuPage.Home) return false;
            ShowPage(MenuPage.Home);
            return true;
        }
        /// <summary>Scroll the authored page just enough to reveal keyboard focus.</summary>
        private void KeepFocusVisible(GameObject selected)
        {
            if (selected == null) return;
            var current = page == MenuPage.Settings ? view.Settings : page == MenuPage.Controls ? view.Controls
                : flow.Phase == VisitPhase.Menu ? view.Main : view.Pause;
            var scroll = current.Scroll;
            Canvas.ForceUpdateCanvases();
            var viewport = scroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, selected.transform);
            float delta = bounds.max.y > viewport.rect.yMax ? viewport.rect.yMax - bounds.max.y
                : bounds.min.y < viewport.rect.yMin ? viewport.rect.yMin - bounds.min.y : 0;
            if (Mathf.Abs(delta) > 0) scroll.content.anchoredPosition += new Vector2(0, delta);
        }
        private void OnEnable() => Bind();
        private void OnDisable()
        {
            Unbind();
            if (view != null) view.gameObject.SetActive(false);
            preferences?.Save();
        }
        private void OnDestroy()
        {
            Unbind();
            if (flow != null && flow.Menu == this) flow.Menu = null;
            if (view != null)
            {
                if (Application.isPlaying) Destroy(view.gameObject);
                else DestroyImmediate(view.gameObject);
            }
        }
    }
}
