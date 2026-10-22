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
        /// <summary>Seconds over which the title menu rises after the closing credits.</summary>
        public const float AfterEndingFadeSeconds = 1.4f;
        /// <summary>Seconds over which a menu that opens comes into view.</summary>
        public const float OpenSeconds = .28f;
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
        private Slider musicLevel;
        private Button subtitles;
        private Button turnMode;
        private Button turnSpeed;
        private Button autoTour;
        private Button orientation;
        private Button quitMain;
        private Button quitPause;
        private TMP_Text subtitlesValue;
        private TMP_Text turnModeValue;
        private TMP_Text turnSpeedValue;
        private TMP_Text headingSound;
        private TMP_Text headingReading;
        private TMP_Text headingControl;
        private TMP_Text headingComfort;
        private Slider narrationLevel;
        private Slider guideLevel;
        private Slider heightOffset;
        private Button subtitleSize;
        private Button readingSize;
        private Button vignette;
        private TMP_Text subtitleSizeValue;
        private TMP_Text readingSizeValue;
        private TMP_Text vignetteValue;
        private MuseumMenuMotion motion;
        private float openSeconds = OpenSeconds;
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
        public MuseumVisitorPreferences Preferences => preferences;
        public Slider MusicLevelSlider => musicLevel;
        /// <summary>Headset-only turning choices, added beside the subtitles option of the settings page.</summary>
        public Button TurnModeButton => turnMode;
        public Button TurnSpeedButton => turnSpeed;
        /// <summary>Starts the automatic tour from the pause menu, or stops it while it runs.</summary>
        public Button AutoTourButton => autoTour;
        /// <summary>Opens the optional orientation from the pause menu; the visit screen has no permanent button for it.</summary>
        public Button OrientationButton => orientation;
        /// <summary>Leaves the application, from the title menu and from the pause; a Web visit returns to the catalog instead.</summary>
        public Button QuitButton => quitPause;
        public Button QuitMainButton => quitMain;
        /// <summary>The page motion, so a test can wait for a reveal or end it.</summary>
        public MuseumMenuMotion Motion => motion;
        /// <summary>True while the museum's content is on its way and the entry cannot begin yet.</summary>
        public bool Loading => loader != null && loader.State == TourLoadState.Loading;
        /// <summary>Text beside an option that shows its current choice.</summary>
        public TMP_Text SubtitlesValue => subtitlesValue;
        public TMP_Text SubtitleSizeValue => subtitleSizeValue;
        public TMP_Text ReadingSizeValue => readingSizeValue;
        public TMP_Text VignetteValue => vignetteValue;
        /// <summary>Volumes of each voice under the general one, and the comfort choices of a headset.</summary>
        public Slider NarrationLevelSlider => narrationLevel;
        public Slider GuideLevelSlider => guideLevel;
        public Slider HeightOffsetSlider => heightOffset;
        public Button SubtitleSizeButton => subtitleSize;
        public Button ReadingSizeButton => readingSize;
        public Button VignetteButton => vignette;
        public TMP_Text TurnModeValue => turnModeValue;
        public TMP_Text TurnSpeedValue => turnSpeedValue;
        /// <summary>True where the application can be left from the menu: everywhere but a Web page.</summary>
        public static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;

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
            if (musicLevel == null)
            {
                musicLevel = Instantiate(volume, volume.transform.parent, false);
                musicLevel.name = "MusicVolume";
                var musicLabel = musicLevel.GetComponentInChildren<MuseumInterfaceLabel>(true);
                musicLabel.TextKey = "music_volume";
                musicLabel.RefreshText();
                musicLevel.onValueChanged = new Slider.SliderEvent();
                musicLevel.minValue = 0; musicLevel.maxValue = 1;
                musicLevel.transform.SetSiblingIndex(volume.transform.GetSiblingIndex() + 1);
            }
            narrationLevel ??= AddSlider(volume, "NarrationLevel", "narration_level", 0, 1);
            guideLevel ??= AddSlider(volume, "GuideLevel", "guide_level", 0, 1);
            heightOffset ??= AddSlider(volume, "HeightOffset", "height_offset", -MuseumComfort.MaxHeightOffset, MuseumComfort.MaxHeightOffset);
            subtitles = view.Settings.Subtitles;
            settingsBack = view.Settings.Back;
            controlsBack = view.Controls.Back;
            // The subtitles read as an option with its state beside it, like every other choice of the settings.
            if (subtitlesValue == null)
            {
                foreach (var label in subtitles.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.TextKey = "subtitles_option";
                subtitlesValue = AddValue(subtitles);
            }
            subtitleSize ??= AddOption(subtitles, "SubtitleSize", "subtitle_size");
            subtitleSizeValue ??= AddValue(subtitleSize);
            readingSize ??= AddOption(subtitleSize, "ReadingSize", "reading_size");
            readingSizeValue ??= AddValue(readingSize);
            vignette ??= AddOption(subtitles, "Vignette", "vignette");
            vignetteValue ??= AddValue(vignette);
            turnMode ??= AddOption(subtitles, "TurnMode", "turn_mode");
            turnModeValue ??= AddValue(turnMode);
            turnSpeed ??= AddOption(turnMode, "TurnSpeed", "turn_speed_option");
            turnSpeedValue ??= AddValue(turnSpeed);
            autoTour ??= AddOption(view.Pause.Primary, "AutoTour", "auto_tour_start");
            orientation ??= AddOption(autoTour, "Orientation", "wayfinding_open");
            quitPause ??= AddOption(mainMenu, "Quit", "quit_museum");
            quitMain ??= AddOption(view.Main.Controls, "Quit", "quit_museum");
            headingSound ??= AddHeading(volume, "heading_sound");
            headingReading ??= AddHeading(subtitles, "heading_reading");
            headingControl ??= AddHeading(sensitivity, "heading_control");
            headingComfort ??= AddHeading(vignette, "heading_comfort");
            OrderSettings();
            turnMode.gameObject.SetActive(spatial);
            turnSpeed.gameObject.SetActive(spatial);
            ShowComfort(spatial);
            motion = view.GetComponent<MuseumMenuMotion>();
            if (motion == null) motion = view.gameObject.AddComponent<MuseumMenuMotion>();
            motion.Configure(view);
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
            Listen(quitPause, Quit);
            Listen(quitMain, Quit);
            foreach (var homePage in new[] { view.Main, view.Pause })
            {
                Listen(homePage.Settings, OpenSettings);
                Listen(homePage.Controls, OpenControls);
                Listen(homePage.Retry, loader.Reload);
                Listen(homePage.Catalog, ReturnToCatalog);
            }
            Listen(subtitles, ToggleSubtitles);
            Listen(turnMode, ToggleTurnMode);
            Listen(turnSpeed, NextTurnSpeed);
            Listen(autoTour, ToggleAutoTour);
            Listen(orientation, OpenOrientation);
            Listen(view.Settings.Reset, preferences.Reset);
            Listen(settingsBack, OpenHome, MuseumUiSound.Back);
            Listen(controlsBack, OpenHome, MuseumUiSound.Back);
            Listen(view.Controls.RestoreBindings, RestoreBindings);
            Listen(subtitleSize, NextSubtitleSize, MuseumUiSound.Step);
            Listen(readingSize, NextReadingSize, MuseumUiSound.Step);
            Listen(vignette, NextVignette, MuseumUiSound.Step);
            sensitivity.onValueChanged.AddListener(preferences.SetSensitivity);
            volume.onValueChanged.AddListener(preferences.SetVolume);
            musicLevel.onValueChanged.AddListener(preferences.SetMusicLevel);
            narrationLevel.onValueChanged.AddListener(preferences.SetNarrationLevel);
            guideLevel.onValueChanged.AddListener(preferences.SetGuideLevel);
            heightOffset.onValueChanged.AddListener(SetHeightOffset);
            flow.Changed += Refresh;
            interaction.Changed += Refresh;
            loader.Changed += Refresh;
            preferences.Changed += SyncSettings;
            input = MuseumInput.Current;
            input.BindingsChanged += RefreshKeys;
            BuildRebindRows();
            SyncSettings(); Refresh();
        }

        /// <summary>
        /// A further option made from an authored button of the same page, placed right after it, so it shares its look,
        /// layout and keyboard order. Its caption comes from the interface catalog.
        /// </summary>
        private static Button AddOption(Button template, string name, string textKey)
        {
            var option = Instantiate(template, template.transform.parent, false);
            option.name = name;
            option.onClick = new Button.ButtonClickedEvent();
            option.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            foreach (var label in option.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.TextKey = textKey;
            option.GetComponentInChildren<TMP_Text>(true).text = MuseumInterfaceText.Get(textKey);
            return option;
        }

        /// <summary>Removes a component now in the Editor and at the end of the frame in play, as Unity requires.</summary>
        internal static void Discard(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
        /// <summary>A further slider made from an authored one of the same page, with its own caption and range.</summary>
        private static Slider AddSlider(Slider template, string name, string textKey, float minimum, float maximum)
        {
            var slider = Instantiate(template, template.transform.parent, false);
            slider.name = name;
            var label = slider.GetComponentInChildren<MuseumInterfaceLabel>(true);
            label.TextKey = textKey;
            label.RefreshText();
            slider.onValueChanged = new Slider.SliderEvent();
            slider.minValue = minimum; slider.maxValue = maximum;
            slider.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            return slider;
        }

        /// <summary>The height moves in whole steps, so a slider stops at centimetres a visitor can read.</summary>
        private void SetHeightOffset(float metres)
        {
            float stepped = Mathf.Round(metres / MuseumComfort.HeightStep) * MuseumComfort.HeightStep;
            preferences.SetHeightOffset(stepped);
        }
        private void NextSubtitleSize() => preferences.SetSubtitleScale(MuseumComfort.NextScale(MuseumComfort.SubtitleScales, preferences.SubtitleScale));
        private void NextReadingSize() => preferences.SetReadingScale(MuseumComfort.NextScale(MuseumComfort.ReadingScales, preferences.ReadingScale));
        private void NextVignette() => preferences.SetVignette(MuseumComfort.NextVignette(preferences.VignetteChoice));

        /// <summary>The comfort of a headset is offered only on a headset; the subtitles of a screen only on a screen.</summary>
        private void ShowComfort(bool value)
        {
            headingComfort.gameObject.SetActive(value);
            vignette.gameObject.SetActive(value);
            heightOffset.gameObject.SetActive(value);
            subtitleSize.gameObject.SetActive(!value);
        }

        /// <summary>A text on the right of an option that says its current choice; the look gives it its colour and size.</summary>
        private static TMP_Text AddValue(Button option)
        {
            var caption = option.GetComponentInChildren<TMP_Text>(true);
            var value = Instantiate(caption, caption.transform.parent, false);
            value.name = "Value";
            foreach (var label in value.GetComponents<MuseumInterfaceLabel>()) Discard(label);
            foreach (var binding in value.GetComponents<MuseumMenuThemeBinding>()) Discard(binding);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.raycastTarget = false;
            value.text = "";
            var rect = value.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return value;
        }

        /// <summary>A small heading over a group of options; it takes no focus, so the keyboard passes over it.</summary>
        private static TMP_Text AddHeading(Selectable before, string textKey)
        {
            var description = before.transform.parent.Find("Description");
            var template = description != null ? description.GetComponent<TMP_Text>() : before.GetComponentInChildren<TMP_Text>(true);
            var heading = Instantiate(template, before.transform.parent, false);
            heading.name = "Heading";
            foreach (var binding in heading.GetComponents<MuseumMenuThemeBinding>()) Discard(binding);
            var label = heading.GetComponent<MuseumInterfaceLabel>();
            if (label == null) label = heading.gameObject.AddComponent<MuseumInterfaceLabel>();
            label.TextKey = textKey;
            heading.text = MuseumInterfaceText.Get(textKey);
            heading.alignment = TextAlignmentOptions.BottomLeft;
            heading.raycastTarget = false;
            heading.margin = new Vector4(6, 14, 6, 2);
            var size = heading.GetComponent<LayoutElement>();
            if (size == null) size = heading.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = 40;
            size.flexibleHeight = 0;
            heading.transform.SetSiblingIndex(before.transform.GetSiblingIndex());
            return heading;
        }

        /// <summary>The settings in reading order: sound, reading, control, and the reset last.</summary>
        private void OrderSettings()
        {
            var order = new Component[] { headingSound, volume, musicLevel, narrationLevel, guideLevel, headingReading, subtitles,
                subtitleSize, readingSize, headingControl, sensitivity, turnMode, turnSpeed, headingComfort, vignette, heightOffset,
                view.Settings.Reset };
            int index = view.Settings.Description.transform.GetSiblingIndex() + 1;
            foreach (var item in order)
                if (item != null && item.transform.parent == view.Settings.Description.transform.parent) item.transform.SetSiblingIndex(index++);
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
            turnMode.gameObject.SetActive(value);
            ShowComfort(value);
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

        private void Listen(Button button, UnityEngine.Events.UnityAction callback, MuseumUiSound sound = MuseumUiSound.Confirm)
        {
            if (button == null) return;
            UnityEngine.Events.UnityAction heard = () => { Sound(sound); callback(); };
            button.onClick.AddListener(heard);
            removeListeners.Add(() => { if (button != null) button.onClick.RemoveListener(heard); });
        }
        private static void Sound(MuseumUiSound sound) { if (Application.isPlaying) MuseumUiSounds.Current.Play(sound); }
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
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
            musicLevel.onValueChanged.RemoveListener(preferences.SetMusicLevel);
            narrationLevel.onValueChanged.RemoveListener(preferences.SetNarrationLevel);
            guideLevel.onValueChanged.RemoveListener(preferences.SetGuideLevel);
            heightOffset.onValueChanged.RemoveListener(SetHeightOffset);
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
        private void ToggleTurnMode()
        {
            var shipped = MuseumXrConfiguration.Current.DefaultTurn;
            preferences.SetTurnMode(preferences.Turn(shipped) == MuseumTurnMode.Continuous ? MuseumTurnMode.Snap : MuseumTurnMode.Continuous);
        }
        private void NextTurnSpeed() => preferences.SetTurnSpeed(MuseumXrConfiguration.Current.NextTurnSpeed(preferences.TurnSpeed));
        private void ToggleAutoTour()
        {
            var tour = interaction.GetComponent<MuseumAutoTour>();
            if (tour == null) return;
            if (tour.Active) { tour.Stop(); Refresh(); return; }
            ResumeVisit();
            tour.Begin();
        }
        private void OpenOrientation()
        {
            var wayfinding = interaction.GetComponent<MuseumWayfinding>();
            if (wayfinding == null) return;
            ResumeVisit();
            if (!wayfinding.MenuOpen) wayfinding.ToggleMenu();
        }
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
            musicLevel.SetValueWithoutNotify(preferences.MusicLevel);
            narrationLevel.SetValueWithoutNotify(preferences.NarrationLevel);
            guideLevel.SetValueWithoutNotify(preferences.GuideLevel);
            heightOffset.SetValueWithoutNotify(preferences.HeightOffset);
            subtitleSizeValue.text = MuseumInterfaceText.Get(MuseumComfort.ScaleKey(MuseumComfort.SubtitleScales, preferences.SubtitleScale));
            readingSizeValue.text = MuseumInterfaceText.Get(MuseumComfort.ScaleKey(MuseumComfort.ReadingScales, preferences.ReadingScale));
            vignetteValue.text = MuseumInterfaceText.Get(MuseumComfort.TextKey(preferences.VignetteChoice));
            var music = FindAnyObjectByType<MuseumMusic>();
            if (music != null) { music.SetMusicLevel(preferences.MusicLevel); music.SetMasterVolume(preferences.Volume); }
            subtitlesValue.text = MuseumInterfaceText.Get(preferences.Subtitles ? "option_on" : "option_off");
            var xr = MuseumXrConfiguration.Current;
            var turning = preferences.Turn(xr.DefaultTurn);
            turnModeValue.text = MuseumInterfaceText.Get(turning == MuseumTurnMode.Continuous ? "turn_continuous" : "turn_snap");
            turnSpeedValue.text = MuseumInterfaceText.Format("turn_speed_value", xr.TurnSpeed(preferences.TurnSpeed));
            // The speed only applies to continuous turning.
            turnSpeed.gameObject.SetActive(spatial && turning == MuseumTurnMode.Continuous);
            var tour = interaction != null ? interaction.GetComponent<MuseumAutoTour>() : null;
            autoTour.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get(tour != null && tour.Active ? "auto_tour_stop" : "auto_tour_start");
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
            if (page == MenuPage.Settings)
            {
                foreach (var option in view.Settings.Scroll.content.GetComponentsInChildren<Selectable>(false))
                    if (option.IsInteractable() && option.gameObject.activeInHierarchy) return option.gameObject;
                return settingsBack.gameObject;
            }
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
            // A menu that opens comes into view over a moment; after the credits the title menu rises slowly over the fire.
            if (visible && !wasVisible)
            {
                overlayGroup.alpha = 0;
                openSeconds = flow.AfterEnding && flow.Phase == VisitPhase.Menu ? AfterEndingFadeSeconds : OpenSeconds;
                Sound(MuseumUiSound.Open);
            }
            else if (wasVisible && !visible) Sound(MuseumUiSound.Close);
            // Over the staged entry the backdrop lets the museum show through; a pause keeps a light veil, and the dark
            // behind the column does the rest. In a headset the pages float on their own, without a veil in the world.
            if (overlay.TryGetComponent<MuseumMenuThemeBinding>(out var theme) && theme.ColorToken == MuseumMenuColorToken.Backdrop
                && overlay.TryGetComponent<Graphic>(out var backdrop))
            {
                var veil = MuseumExperienceConfiguration.ColorValue(flow.Phase != VisitPhase.Exploring && flow.Entry != null
                    && flow.Entry.Available ? configuration.EntryBackdropColor : configuration.BackdropColor);
                if (spatial) veil.a = 0;
                backdrop.color = veil;
            }
            fade.gameObject.SetActive(entering && (flow.Transition == EntryTransition.Fade || FromBackdrop) || flow.Phase == VisitPhase.Ending);
            UpdateTransition();
            if (!visible) page = MenuPage.Home;
            // Enter or Escape that closed the menu must not also activate a point or reopen the pause.
            if (wasVisible && !visible) flow.ConsumeInputThisFrame();
            // While the way in dissolves the menu, the entry page stays the one dissolving; the pause page never flashes in.
            bool entry = flow.Phase != VisitPhase.Exploring;
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
            autoTour.gameObject.SetActive(!entry && interaction.GetComponent<MuseumAutoTour>() != null);
            orientation.gameObject.SetActive(!entry && interaction.GetComponent<MuseumWayfinding>() != null);
            mainMenu.gameObject.SetActive(!entry);
            quitPause.gameObject.SetActive(!entry && CanQuit);
            quitMain.gameObject.SetActive(entry && CanQuit);
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
            if (motion != null) { if (visible) motion.Show(currentPage); else motion.Hide(); }
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
            if (flow.Phase == VisitPhase.Entering || flow.Phase == VisitPhase.Ending) UpdateTransition();
            if (overlayGroup.interactable && overlayGroup.alpha < 1)
                overlayGroup.alpha = Mathf.MoveTowards(overlayGroup.alpha, 1, Time.unscaledDeltaTime / Mathf.Max(.01f, openSeconds));
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
            {
                slider.value += pressed.x * configuration.SliderStep * (slider.maxValue - slider.minValue);
                Sound(MuseumUiSound.Step);
            }
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
                if (option.IsInteractable() && option.gameObject.activeInHierarchy && !(option is Scrollbar)) options.Add(option);
            if (options.Count == 0) return;
            int index = options.FindIndex(option => option.gameObject == current);
            index = index < 0 ? 0 : (index + step + options.Count) % options.Count;
            EventSystem.current.SetSelectedGameObject(options[index].gameObject);
            KeepFocusVisible(options[index].gameObject);
            Sound(MuseumUiSound.Focus);
        }
        private bool SelectionInsideMenu()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.activeInHierarchy && selected.transform.IsChildOf(overlay.transform);
        }
        /// <summary>The menu dissolves first. A glide then shows the way in; a fade goes dark before the museum appears.</summary>
        private void UpdateTransition()
        {
            // At the end of the visit the screen goes dark over the museum and opens again on the fire.
            if (flow.Phase == VisitPhase.Ending) { fade.alpha = flow.GetComponent<MuseumEnding>()?.Darkness ?? 1; return; }
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
                if (Application.isPlaying) Discard(view.gameObject);
                else DestroyImmediate(view.gameObject);
            }
        }
    }
}
