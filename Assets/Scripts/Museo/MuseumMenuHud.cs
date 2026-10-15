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
        private MuseumInput input;
        private readonly System.Collections.Generic.List<MuseumRebindRow> rebindRows = new();
        private Vector2Int heldNavigation;
        private bool navigationArmed;

        [SerializeField] private MuseumMenuView viewPrefab;
        private MuseumMenuView view;
        private bool bound;
        private bool spatial;
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
            MuseumInputDirector.Ensure(interaction);
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
            Listen(view.Controls.RestoreBindings, RestoreBindings);
            sensitivity.onValueChanged.AddListener(preferences.SetSensitivity);
            volume.onValueChanged.AddListener(preferences.SetVolume);
            flow.Changed += Refresh;
            interaction.Changed += Refresh;
            loader.Changed += Refresh;
            preferences.Changed += SyncSettings;
            input = MuseumInput.Current;
            input.BindingsChanged += RefreshKeys;
            BuildRebindRows();
            SyncSettings(); Refresh();
        }

        /// <summary>One row per key the visitor may change, in the order of the binding list; the row layout belongs to its prefab.</summary>
        private void BuildRebindRows()
        {
            var page = view.Controls;
            foreach (var binding in input.Bindings.All)
            {
                if (!binding.Rebindable) continue;
                var row = rebindRows.Find(existing => existing != null && existing.Binding != null && existing.Binding.Id == binding.Id);
                if (row == null)
                {
                    row = Instantiate(page.RowPrefab, page.Bindings, false);
                    row.name = "Rebind_" + binding.Id;
                    foreach (var theme in row.GetComponentsInChildren<MuseumMenuThemeBinding>(true)) theme.Apply();
                    rebindRows.Add(row);
                }
                row.Configure(input, binding, ReportBinding);
            }
            page.BindingStatus.text = "";
        }

        /// <summary>The spatial host keeps controller guidance visible and hides keyboard-only adjustments.</summary>
        public void SetSpatial(bool value)
        {
            spatial = value;
            foreach (var label in view.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.RefreshText();
            view.GetComponent<MuseumMenuLayout>().Spatial = value;
            sensitivity.gameObject.SetActive(!value);
            foreach (var label in view.Settings.GetComponentsInChildren<MuseumInterfaceLabel>(true))
                if (label.TextKey == "mouse_sensitivity") label.gameObject.SetActive(!value);
            view.Controls.Bindings.gameObject.SetActive(!value);
            view.Controls.RestoreBindings.gameObject.SetActive(!value);
            view.Controls.BindingStatus.gameObject.SetActive(!value);
            Refresh();
        }

        private void ReportBinding(string message) => view.Controls.BindingStatus.text = message;
        private void RestoreBindings()
        {
            input.Bindings.RestoreDefaults();
            ReportBinding(MuseumInterfaceText.Get("rebind_restored"));
        }
        /// <summary>A reassigned key changes the rows and every menu text that names it.</summary>
        private void RefreshKeys()
        {
            foreach (var row in rebindRows) if (row != null) row.Refresh();
            foreach (var label in view.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.RefreshText();
            Refresh();
        }

        private void Listen(Button button, UnityEngine.Events.UnityAction callback)
        {
            button.onClick.AddListener(callback);
            removeListeners.Add(() => { if (button != null) button.onClick.RemoveListener(callback); });
        }
        private void Unbind()
        {
            if (!bound) return;
            foreach (var row in rebindRows) if (row != null) row.Cancel();
            if (input != null) input.BindingsChanged -= RefreshKeys;
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
            foreach (var row in rebindRows) if (row != null) row.Cancel();
            view.Controls.BindingStatus.text = "";
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
            if (page == MenuPage.Settings) return spatial ? volume.gameObject : sensitivity.gameObject;
            if (page == MenuPage.Controls)
                return !spatial && rebindRows.Count > 0 && rebindRows[0] != null ? rebindRows[0].gameObject : controlsBack.gameObject;
            if (flow.Phase != VisitPhase.Menu) return resume.gameObject;
            if (begin.interactable) return begin.gameObject;
            return retry.gameObject.activeSelf ? retry.gameObject : settingsButton.gameObject;
        }
        private void Refresh()
        {
            if (!bound) return;
            Canvas.ForceUpdateCanvases();
            view.GetComponent<MuseumMenuLayout>()?.Apply();
            bool entering = flow.Phase == VisitPhase.Entering;
            bool visible = flow.Phase == VisitPhase.Menu || !entering && interaction.State == InteractionState.Paused;
            overlay.SetActive(visible || entering);
            overlayGroup.interactable = overlayGroup.blocksRaycasts = visible;
            if (visible) overlayGroup.alpha = 1;
            // Over the staged entry the backdrop lets the museum show through; a pause keeps the denser one.
            if (overlay.TryGetComponent<MuseumMenuThemeBinding>(out var theme) && theme.ColorToken == MuseumMenuColorToken.Backdrop
                && overlay.TryGetComponent<Graphic>(out var backdrop))
                backdrop.color = MuseumExperienceConfiguration.ColorValue(flow.Phase != VisitPhase.Exploring && flow.Entry != null
                    && flow.Entry.Available ? configuration.EntryBackdropColor : configuration.BackdropColor);
            fade.gameObject.SetActive(entering && (flow.Transition == EntryTransition.Fade || FromBackdrop));
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
            description.text = page == MenuPage.Settings ? MuseumInterfaceText.Get(spatial ? "xr_settings_description" : "settings_description")
                : page == MenuPage.Controls ? MuseumInterfaceText.Get(spatial ? "xr_controls_description" : "controls_description")
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
            wasVisible = visible;
            wasEntry = entry;
            // Buttons shown or hidden above change how tall the page needs to be.
            view.GetComponent<MuseumMenuLayout>()?.Apply();
        }
        private void Update()
        {
            if (!bound) return;
            if (flow.Phase == VisitPhase.Entering) UpdateTransition();
            // The key being chosen for an action, and the one that just was, never also operates the menu.
            if (!overlayGroup.interactable || input.Capturing || input.CaptureEndedThisFrame) { navigationArmed = false; return; }
            if (page != MenuPage.Home && (input.Back.WasPressedThisFrame()
                    || flow.Phase == VisitPhase.Menu && input.Pause.WasPressedThisFrame())) { ShowPage(MenuPage.Home); return; }
            var pressed = PressedNavigation();
            // The reverse combination also contains the forward key, so it is read first.
            int step = input.Previous.WasPressedThisFrame() ? -1 : input.Next.WasPressedThisFrame() ? 1 : -pressed.y;
            // A mouse click on the backdrop clears the selection; any navigation key brings the focus back.
            if (!SelectionInsideMenu())
            {
                if (step != 0 || input.Confirm.WasPressedThisFrame()) SelectFirst();
                return;
            }
            if (step != 0) MoveSelection(step);
            var slider = EventSystem.current.currentSelectedGameObject.GetComponent<Slider>();
            if (pressed.x != 0 && slider != null)
                slider.value += pressed.x * configuration.SliderStep * (slider.maxValue - slider.minValue);
        }
        /// <summary>Directions pressed this frame. Keys already held when the menu opened, such as a walking key at the moment of pausing, do not count.</summary>
        private Vector2Int PressedNavigation()
        {
            var value = input.Navigate.ReadValue<Vector2>();
            var held = new Vector2Int(value.x > .5f ? 1 : value.x < -.5f ? -1 : 0, value.y > .5f ? 1 : value.y < -.5f ? -1 : 0);
            var pressed = new Vector2Int(held.x != heldNavigation.x ? held.x : 0, held.y != heldNavigation.y ? held.y : 0);
            heldNavigation = held;
            if (navigationArmed) return pressed;
            navigationArmed = held == Vector2Int.zero;
            return Vector2Int.zero;
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
        private bool SelectionInsideMenu()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(overlay.transform);
        }
        /// <summary>The menu dissolves first. A glide then shows the way in; a fade goes dark before the museum appears.</summary>
        private void UpdateTransition()
        {
            if (flow.Phase != VisitPhase.Entering) { fade.alpha = 0; return; }
            float share = configuration.EntryMenuFadeShare;
            float progress = flow.TransitionProgress;
            overlayGroup.alpha = 1 - Mathf.SmoothStep(0, 1, progress / share);
            // From a scenic backdrop the screen goes dark, then opens on the museum as the glide begins.
            if (flow.Transition == EntryTransition.Glide && FromBackdrop)
            {
                fade.alpha = progress < share ? Mathf.SmoothStep(0, 1, progress / share)
                    : 1 - Mathf.SmoothStep(0, 1, (progress - share) / ((1 - share) * .35f));
                return;
            }
            if (flow.Transition == EntryTransition.Glide) { fade.alpha = 0; return; }
            fade.alpha = progress < share ? Mathf.SmoothStep(0, 1, progress / share)
                : 1 - Mathf.SmoothStep(0, 1, (progress - share) / (1 - share));
        }
        private bool FromBackdrop => flow.Entry != null && flow.Entry.BackdropActive;

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
            if (!selected.transform.IsChildOf(scroll.content)) return;
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
