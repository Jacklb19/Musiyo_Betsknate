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

        public void Configure(MuseumVisitFlow visit, MuseumInteraction state, TourLoader tourLoader,
            MuseumVisitorPreferences visitorPreferences, Button buttonTemplate, TMP_Text textTemplate, Slider sliderTemplate)
        {
            flow = visit; interaction = state; loader = tourLoader; preferences = visitorPreferences;
            configuration = MuseumExperienceConfiguration.Current;
            flow.Menu = this;
            overlay = new GameObject("MuseumMenu", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            overlayGroup = overlay.GetComponent<CanvasGroup>();
            overlay.transform.SetParent(transform, false);
            Stretch((RectTransform)overlay.transform);
            overlay.GetComponent<Image>().color = MuseumExperienceConfiguration.ColorValue(configuration.BackdropColor);
            var card = new GameObject("MenuCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            card.transform.SetParent(overlay.transform, false);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(configuration.MenuWidth, configuration.MenuHeight);
            card.GetComponent<Image>().color = MuseumExperienceConfiguration.ColorValue(configuration.PanelColor);
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(configuration.MenuPadding, configuration.MenuPadding, configuration.MenuPadding, configuration.MenuPadding);
            layout.spacing = configuration.MenuSpacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            title = Text(textTemplate, card.transform, "MenuTitle", configuration.TitleSize, configuration.TitleSize * 3);
            description = Text(textTemplate, card.transform, "MenuDescription", configuration.BodySize, configuration.BodySize * 4);
            home = Page("Home", card.transform);
            begin = Action(buttonTemplate, home.transform, "explore_museum", () => { preferences.Save(); flow.BeginVisit(); });
            resume = Action(buttonTemplate, home.transform, "resume_visit", () => { preferences.Save(); interaction.SetPaused(false); });
            settingsButton = Action(buttonTemplate, home.transform, "settings", () => ShowPage(MenuPage.Settings));
            Action(buttonTemplate, home.transform, "controls", () => ShowPage(MenuPage.Controls));
            mainMenu = Action(buttonTemplate, home.transform, "main_menu", flow.ReturnToMenu);
            retry = Action(buttonTemplate, home.transform, "retry_tour", loader.Reload);
#if UNITY_WEBGL && !UNITY_EDITOR
            Action(buttonTemplate, home.transform, "return_to_catalog", () => interaction.GetComponent<MuseumWebBridge>()?.ReturnToCatalog());
#endif
            settings = Page("Settings", card.transform);
            sensitivity = SettingSlider(sliderTemplate, settings.transform, "mouse_sensitivity", configuration.MinimumSensitivity, configuration.MaximumSensitivity);
            sensitivity.onValueChanged.AddListener(preferences.SetSensitivity);
            volume = SettingSlider(sliderTemplate, settings.transform, "narration_volume", 0, 1);
            volume.onValueChanged.AddListener(preferences.SetVolume);
            subtitles = Action(buttonTemplate, settings.transform, "hide_subtitles", () => preferences.SetSubtitles(!preferences.Subtitles));
            Action(buttonTemplate, settings.transform, "reset_settings", preferences.Reset);
            settingsBack = Action(buttonTemplate, settings.transform, "back_to_menu", () => ShowPage(MenuPage.Home));
            controls = Page("Controls", card.transform);
            var help = Text(textTemplate, controls.transform, "ControlInstructions", configuration.BodySize, configuration.BodySize * 11);
            help.text = MuseumInterfaceText.Get("menu_controls");
            controlsBack = Action(buttonTemplate, controls.transform, "back_to_menu", () => ShowPage(MenuPage.Home));
            var transition = new GameObject("EntryFade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            transition.transform.SetParent(transform, false);
            Stretch((RectTransform)transition.transform);
            transition.GetComponent<Image>().color = MuseumExperienceConfiguration.ColorValue(configuration.PanelColor);
            fade = transition.GetComponent<CanvasGroup>();
            fade.alpha = 0;
            flow.Changed += Refresh;
            interaction.Changed += Refresh;
            loader.Changed += Refresh;
            preferences.Changed += SyncSettings;
            SyncSettings();
            Refresh();
        }

        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

        private GameObject Page(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            result.transform.SetParent(parent, false);
            var layout = result.GetComponent<VerticalLayoutGroup>();
            layout.spacing = configuration.MenuSpacing;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return result;
        }
        private TMP_Text Text(TMP_Text template, Transform parent, string name, float size, float height)
        {
            var label = Instantiate(template, parent);
            label.name = name;
            label.gameObject.SetActive(true);
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Truncate;
            label.color = MuseumExperienceConfiguration.ColorValue(configuration.TextColor);
            if (!label.TryGetComponent<LayoutElement>(out var element)) element = label.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 0;
            element.preferredHeight = height;
            return label;
        }
        private Button Action(Button template, Transform parent, string key, UnityEngine.Events.UnityAction action)
        {
            var button = Instantiate(template, parent);
            button.name = key;
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get(key);
            button.GetComponentInChildren<TMP_Text>().fontSize = configuration.BodySize;
            button.GetComponentInChildren<TMP_Text>().color = MuseumExperienceConfiguration.ColorValue(configuration.TextColor);
            var colors = button.colors;
            colors.normalColor = MuseumExperienceConfiguration.ColorValue(configuration.PanelColor);
            colors.highlightedColor = colors.selectedColor = MuseumExperienceConfiguration.ColorValue(configuration.SelectedColor);
            button.colors = colors;
            if (!button.TryGetComponent<LayoutElement>(out var element)) element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = configuration.ButtonHeight;
            element.minHeight = configuration.ButtonHeight;
            return button;
        }
        private Slider SettingSlider(Slider template, Transform parent, string key, float minimum, float maximum)
        {
            var slider = Instantiate(template, parent);
            slider.name = key;
            slider.gameObject.SetActive(true);
            slider.onValueChanged.RemoveAllListeners();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            var sliderColors = slider.colors;
            sliderColors.selectedColor = MuseumExperienceConfiguration.ColorValue(configuration.SelectedColor);
            slider.colors = sliderColors;
            slider.minValue = minimum;
            slider.maxValue = maximum;
            slider.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get(key);
            slider.GetComponentInChildren<TMP_Text>().color = MuseumExperienceConfiguration.ColorValue(configuration.TextColor);
            if (!slider.TryGetComponent<LayoutElement>(out var element)) element = slider.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = configuration.ButtonHeight * 1.5f;
            element.minHeight = element.preferredHeight;
            return slider;
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
            EventSystem.current.SetSelectedGameObject(FirstSelectable());
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
            if (flow == null) return;
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
            home.SetActive(page == MenuPage.Home);
            settings.SetActive(page == MenuPage.Settings);
            controls.SetActive(page == MenuPage.Controls);
            bool entry = flow.Phase == VisitPhase.Menu;
            begin.gameObject.SetActive(entry);
            resume.gameObject.SetActive(!entry);
            mainMenu.gameObject.SetActive(!entry);
            retry.gameObject.SetActive(loader.State == TourLoadState.Unavailable);
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
            if (flow == null) return;
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
        private void OnDisable()
        {
            SuspendModuleNavigation(false);
            if (preferences != null) preferences.Save();
        }
        private void OnDestroy()
        {
            if (flow != null) flow.Changed -= Refresh;
            if (interaction != null) interaction.Changed -= Refresh;
            if (loader != null) loader.Changed -= Refresh;
            if (preferences != null) preferences.Changed -= SyncSettings;
        }
    }
}
