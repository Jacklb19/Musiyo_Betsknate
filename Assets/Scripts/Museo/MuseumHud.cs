using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumHud : MonoBehaviour
    {
        [SerializeField] private TourLoader loader;
        [SerializeField] private MuseumInteraction interaction;
        [SerializeField] private DesktopPointInput pointInput;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text focusText;
        [SerializeField] private TMP_Text panelText;
        [SerializeField] private GameObject panel;
        [SerializeField] private Transform elementList;
        [SerializeField] private Button elementTemplate;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button detailButton;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private DesktopVisitorController visitor;
        private UnityWebRequest detailRequest;
        private Coroutine reading;
        private string requestedSlug;
        private string detail;
        private MuseumModelPresenter model;
        private Button examineButton;
        private Button modelRetryButton;
        private TMP_Text modelText;
        private GameObject modelControls;
        private MuseumNarration narration;
        private TMP_Text narrationText;
        private TMP_Text subtitleText;
        private GameObject subtitleBackground;
        private GameObject narrationControls;
        private Button audioPauseButton;
        private Button audioMuteButton;
        private Button narrationRetryButton;
        private Slider volumeSlider;
        private Button subtitlesButton;
        private MuseumVisitorPreferences preferences;
        private MuseumVisitFlow visitFlow;
        private MuseumArrival arrival;
        private MuseumGuide guide;
        private MuseumFarewell farewell;
        private GameObject welcomeCard;
        private TMP_Text welcomeTitle;
        private TMP_Text welcomeBody;
        private TMP_Text stationText;
        private Button returnButton;
        private CanvasGroup panelGroup;
        private ScrollRect panelScroll;
        private string panelSubject;
        private string summary;
        private MuseumLecternDisplay lectern;
        private MuseumPointPresence presence;

        private void Awake()
        {
            if (interaction == null) return;
            preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current,
                PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, ""));
            visitFlow = interaction.GetComponent<MuseumVisitFlow>();
            if (visitFlow == null) visitFlow = interaction.gameObject.AddComponent<MuseumVisitFlow>();
            model = interaction.GetComponent<MuseumModelPresenter>();
            if (model == null) model = interaction.gameObject.AddComponent<MuseumModelPresenter>();
            if (visitor != null) model.Configure(visitor.GetComponentInChildren<Camera>(), visitor.transform);
            CreateModelInterface();
            narration = interaction.GetComponent<MuseumNarration>();
            if (narration == null) narration = interaction.gameObject.AddComponent<MuseumNarration>();
            presence = interaction.GetComponent<MuseumPointPresence>();
            presence.Configure(visitor != null ? visitor.transform : null);
            lectern = interaction.GetComponent<MuseumLecternDisplay>();
            if (lectern == null) lectern = interaction.gameObject.AddComponent<MuseumLecternDisplay>();
            lectern.Configure((RectTransform)panel.transform, visitor != null ? visitor.GetComponentInChildren<Camera>() : null);
            CreateNarrationInterface();
            var wayfinding = interaction.GetComponent<MuseumWayfinding>();
            if (wayfinding == null) wayfinding = interaction.gameObject.AddComponent<MuseumWayfinding>();
            wayfinding.Configure(visitor != null ? visitor.transform : null);
            gameObject.AddComponent<MuseumWayfindingHud>().Configure(wayfinding, interaction, detailButton, focusText);
            var visitorTransform = visitor != null ? visitor.transform : null;
            arrival = interaction.GetComponent<MuseumArrival>();
            if (arrival == null) arrival = interaction.gameObject.AddComponent<MuseumArrival>();
            arrival.Configure(visitorTransform);
            guide = interaction.GetComponent<MuseumGuide>();
            if (guide == null) guide = interaction.gameObject.AddComponent<MuseumGuide>();
            guide.Configure(visitorTransform);
            farewell = interaction.GetComponent<MuseumFarewell>();
            if (farewell == null) farewell = interaction.gameObject.AddComponent<MuseumFarewell>();
            farewell.Configure(visitorTransform);
            CreateStationInterface();
            gameObject.AddComponent<MuseumMenuHud>().Configure(visitFlow, interaction, loader, preferences,
                detailButton, panelText, sensitivitySlider);
            ApplyPreferences();
        }

        private void OnEnable()
        {
            loader.Changed += Refresh;
            interaction.Changed += Refresh;
            visitFlow.Changed += Refresh;
            preferences.Changed += ApplyPreferences;
            if (arrival != null) arrival.Changed += Refresh;
            if (guide != null) guide.Changed += Refresh;
            if (farewell != null) farewell.Changed += Refresh;
            if (model != null) model.Changed += Refresh;
            presence.Changed += OnPresenceChanged;
            if (narration != null)
            {
                narration.Changed += Refresh;
                narration.SubtitleChanged += RefreshSubtitle;
            }
            retryButton.onClick.AddListener(loader.Reload);
            closeButton.onClick.AddListener(interaction.Back);
            pauseButton.onClick.AddListener(TogglePause);
            detailButton.onClick.AddListener(OpenDetail);
            if (visitor != null && sensitivitySlider != null)
            {
                sensitivitySlider.SetValueWithoutNotify(preferences.Sensitivity);
                sensitivitySlider.onValueChanged.AddListener(preferences.SetSensitivity);
            }
            Refresh();
        }

        private void OnDisable()
        {
            loader.Changed -= Refresh;
            interaction.Changed -= Refresh;
            visitFlow.Changed -= Refresh;
            preferences.Changed -= ApplyPreferences;
            if (arrival != null) arrival.Changed -= Refresh;
            if (guide != null) guide.Changed -= Refresh;
            if (farewell != null) farewell.Changed -= Refresh;
            if (model != null) model.Changed -= Refresh;
            presence.Changed -= OnPresenceChanged;
            if (narration != null)
            {
                narration.Changed -= Refresh;
                narration.SubtitleChanged -= RefreshSubtitle;
            }
            retryButton.onClick.RemoveListener(loader.Reload);
            closeButton.onClick.RemoveListener(interaction.Back);
            pauseButton.onClick.RemoveListener(TogglePause);
            detailButton.onClick.RemoveListener(OpenDetail);
            if (visitor != null && sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(preferences.SetSensitivity);
            preferences.Save();
            CancelDetail();
        }

        private ScrollRect PanelScroll()
        {
            if (panelScroll == null) panel.TryGetComponent(out panelScroll);
            return panelScroll;
        }

        /// <summary>Page Up and Page Down scroll long panels; the mouse wheel stays free for model zoom.</summary>
        private void ScrollPanelWithKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !panel.activeSelf || PanelScroll() == null) return;
            int direction = keyboard.pageDownKey.wasPressedThisFrame ? -1 : keyboard.pageUpKey.wasPressedThisFrame ? 1 : 0;
            if (direction == 0) return;
            panelScroll.verticalNormalizedPosition = Mathf.Clamp01(panelScroll.verticalNormalizedPosition
                + direction * MuseumExperienceConfiguration.Current.PanelScrollStep);
        }

        private CanvasGroup PanelGroup()
        {
            if (panelGroup == null && !panel.TryGetComponent(out panelGroup)) panelGroup = panel.AddComponent<CanvasGroup>();
            return panelGroup;
        }

        private void TogglePause() => interaction.SetPaused(interaction.State != InteractionState.Paused);
        private void OpenDetail() => interaction.OpenDetail();
        private void OnPresenceChanged(bool away) => Refresh();

        private void Update()
        {
            var focus = pointInput.FocusedPoint;
            focusText.text = focus != null && focus.Content != null ? MuseumInterfaceText.Format("focus_point", focus.Content.name)
                : pointInput.GazeProgress > 0 ? MuseumInterfaceText.Format("gaze_progress", Mathf.RoundToInt(pointInput.GazeProgress * 100)) : ".";
            ScrollPanelWithKeyboard();
            if (panel.activeSelf && PanelGroup().alpha < 1)
                panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, 1,
                    Time.unscaledDeltaTime / MuseumExperienceConfiguration.Current.PanelFadeSeconds);
            if (model != null && model.State == ModelLoadState.Loading && modelText.gameObject.activeInHierarchy)
                modelText.text = model.Status + " " + Mathf.RoundToInt(model.Progress * 100) + "%";
        }

        private void Refresh()
        {
            statusText.text = loader.Status;
            retryButton.gameObject.SetActive(loader.State == TourLoadState.Unavailable);
            bool paused = interaction.State == InteractionState.Paused;
            RefreshModelInterface();
            RefreshNarrationInterface();
            RefreshStationInterface();
            pauseButton.GetComponentInChildren<TMP_Text>().text = paused ? MuseumInterfaceText.Get("resume") : MuseumInterfaceText.Get("pause");
            if (sensitivitySlider != null) sensitivitySlider.gameObject.SetActive(false);
            bool showPanel = !visitFlow.BlocksInput && !paused && interaction.State != InteractionState.Exploration;
            // A reading stand folds its screen while the visitor is away from the point and unfolds it on return.
            bool onStand = lectern.Find(interaction.ActivePoint) != null;
            if (onStand && presence.Away) showPanel = false;
            if (!showPanel) lectern.Hide();
            else
            {
                // The stand unfolds the panel itself; on the screen, Update fades it in.
                if (onStand) PanelGroup().alpha = 1;
                else if (!panel.activeSelf || lectern.InWorld) PanelGroup().alpha = 0;
                lectern.Show(interaction.ActivePoint);
            }
            // A new point, element or state starts reading from the top instead of a previous scroll offset.
            string subject = interaction.ActivePoint?.Anchor.Key + "/" + interaction.SelectedElement?.slug + "/" + interaction.State;
            if (subject != panelSubject && PanelScroll() != null) panelScroll.verticalNormalizedPosition = 1;
            panelSubject = subject;
            if (interaction.State == InteractionState.Exploration || paused)
            {
                CancelDetail();
                requestedSlug = null;
                detail = summary = null;
            }
            // A folding screen keeps what it was showing until it has closed.
            if (!showPanel && lectern.InWorld) return;
            foreach (Transform child in elementList)
                if (child.gameObject != elementTemplate.gameObject)
                { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            elementTemplate.gameObject.SetActive(false);
            bool choosing = interaction.State == InteractionState.PointFocus;
            elementList.gameObject.SetActive(choosing);
            detailButton.gameObject.SetActive(interaction.State == InteractionState.ElementSelected
                || interaction.State == InteractionState.ModelExamination);
            if (paused) { panelText.text = MuseumInterfaceText.Get("pause_description"); return; }
            if (interaction.State == InteractionState.GuideQuestion)
            {
                panelText.text = guide != null ? MuseumInterfaceText.Format("guide_panel", guide.Name, guide.Message) : "";
                return;
            }
            var point = interaction.ActivePoint;
            if (point == null) { panelText.text = ""; return; }
            if (choosing)
            {
                panelText.text = MuseumInterfaceText.Format("element_choice", point.Content.name);
                for (int index = 0; index < point.Content.elements.Length; index++)
                {
                    int selectedIndex = index;
                    var button = Instantiate(elementTemplate, elementList);
                    button.name = "ElementChoice";
                    button.gameObject.SetActive(true);
                    button.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Format("element_choice_item",
                        index + 1, point.Content.elements[index].title);
                    button.onClick.AddListener(() => interaction.SelectElement(selectedIndex));
                    // By a stand the visitor keeps walking, so the list takes no keyboard focus: its numbers choose.
                    if (index == 0 && interaction.HoldsVisitor && EventSystem.current != null)
                        EventSystem.current.SetSelectedGameObject(button.gameObject);
                }
                return;
            }
            var element = interaction.SelectedElement;
            if (element == null) return;
            // The element's own description is read as soon as it is chosen; F opens its complete text.
            if (requestedSlug != element.slug)
            {
                CancelDetail();
                requestedSlug = element.slug;
                detail = summary = null;
                reading = StartCoroutine(LoadDetail(element.slug));
            }
            if (interaction.State != InteractionState.Reading)
            {
                panelText.text = point.Content.name + "\n" + element.title
                    + (string.IsNullOrEmpty(summary) ? "" : "\n\n" + summary)
                    + (interaction.State == InteractionState.ModelExamination
                        ? MuseumInterfaceText.Get("examination_controls")
                        : MuseumInterfaceText.Get("detail_controls"));
                return;
            }
            panelText.text = detail ?? MuseumInterfaceText.Get("detail_loading");
        }

        private IEnumerator LoadDetail(string slug)
        {
            detailRequest = UnityWebRequest.Get(loader.ApiBase + "/elements/" + System.Uri.EscapeDataString(slug));
            detailRequest.timeout = 15;
            yield return detailRequest.SendWebRequest();
            bool success = detailRequest.result == UnityWebRequest.Result.Success;
            var json = success ? detailRequest.downloadHandler.text : null;
            detailRequest.Dispose();
            detailRequest = null;
            reading = null;
            if (!success || !ElementText.TryFormat(slug, json, out var formatted, out var description))
            { detail = MuseumInterfaceText.Get("detail_unavailable"); Refresh(); yield break; }
            detail = formatted;
            summary = description;
            Refresh();
        }

        private void CancelDetail()
        {
            if (reading != null) StopCoroutine(reading);
            reading = null;
            if (detailRequest == null) return;
            detailRequest.Abort();
            detailRequest.Dispose();
            detailRequest = null;
        }

        private void CreateModelInterface()
        {
            modelText = Instantiate(panelText, elementList.parent);
            modelText.name = "ModelStatus";
            modelText.text = "";
            modelText.GetComponent<LayoutElement>().minHeight = 0;
            examineButton = ModelButton(MuseumInterfaceText.Get("examine_model"), elementList.parent, () => model.Examine());
            modelRetryButton = ModelButton(MuseumInterfaceText.Get("retry_model"), elementList.parent, model.Retry);
            modelControls = new GameObject("ModelControls", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            modelControls.transform.SetParent(elementList.parent, false);
            // Seven examination buttons fit the panel as a grid instead of a column the viewport cuts off.
            var layout = modelControls.GetComponent<GridLayoutGroup>();
            int columns = MuseumExperienceConfiguration.Current.ModelControlColumns;
            layout.spacing = new Vector2(8, 8);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            layout.cellSize = new Vector2((((RectTransform)elementList).sizeDelta.x - layout.spacing.x * (columns - 1)) / columns, 52);
            ModelButton(MuseumInterfaceText.Get("rotate_left"), modelControls.transform, () => model.Rotate(new Vector2(-15, 0)));
            ModelButton(MuseumInterfaceText.Get("rotate_right"), modelControls.transform, () => model.Rotate(new Vector2(15, 0)));
            ModelButton(MuseumInterfaceText.Get("rotate_up"), modelControls.transform, () => model.Rotate(new Vector2(0, -15)));
            ModelButton(MuseumInterfaceText.Get("rotate_down"), modelControls.transform, () => model.Rotate(new Vector2(0, 15)));
            ModelButton(MuseumInterfaceText.Get("zoom_in"), modelControls.transform, () => model.Zoom(-.1f));
            ModelButton(MuseumInterfaceText.Get("zoom_out"), modelControls.transform, () => model.Zoom(.1f));
            ModelButton(MuseumInterfaceText.Get("reset_model"), modelControls.transform, model.ResetPose);
        }

        private Button ModelButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var button = Instantiate(detailButton, parent);
            button.name = "ModelAction";
            button.GetComponentInChildren<TMP_Text>().text = label;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0, 52);
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
            return button;
        }

        private void RefreshModelInterface()
        {
            if (model == null) return;
            bool selected = interaction.SelectedElement?.has_3d_model == true
                && interaction.State != InteractionState.Paused && interaction.State != InteractionState.Exploration;
            bool examining = interaction.State == InteractionState.ModelExamination;
            modelText.gameObject.SetActive(selected && interaction.State != InteractionState.Reading);
            modelText.text = model.Status + (string.IsNullOrEmpty(model.Attribution) ? "" : "\n" + model.Attribution);
            examineButton.gameObject.SetActive(selected && interaction.State == InteractionState.ElementSelected && model.CanExamine);
            modelRetryButton.gameObject.SetActive(selected && model.State == ModelLoadState.Unavailable);
            modelControls.SetActive(examining);
        }

        private void CreateNarrationInterface()
        {
            narrationText = Instantiate(panelText, elementList.parent);
            narrationText.name = "NarrationStatus";
            narrationText.GetComponent<LayoutElement>().minHeight = 0;
            narrationControls = new GameObject("NarrationControls", typeof(RectTransform), typeof(VerticalLayoutGroup));
            narrationControls.transform.SetParent(elementList.parent, false);
            var layout = narrationControls.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            audioPauseButton = ModelButton(MuseumInterfaceText.Get("pause_narration"), narrationControls.transform, narration.TogglePause);
            ModelButton(MuseumInterfaceText.Get("repeat_narration"), narrationControls.transform, narration.Repeat);
            audioMuteButton = ModelButton(MuseumInterfaceText.Get("mute_narration"), narrationControls.transform, narration.ToggleMute);
            narrationRetryButton = ModelButton(MuseumInterfaceText.Get("retry_narration"), elementList.parent, narration.Retry);
            subtitleText = Instantiate(focusText, transform);
            subtitleText.name = "NarrationSubtitles";
            subtitleText.fontSize = 24;
            subtitleText.overflowMode = TextOverflowModes.Truncate;
            subtitleText.raycastTarget = false;
            var rect = subtitleText.rectTransform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(.7f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, 132);
            rect.sizeDelta = new Vector2(-48, 160);
            subtitleText.alignment = TextAlignmentOptions.Bottom;
            subtitleBackground = new GameObject("SubtitleBackground", typeof(RectTransform), typeof(Image));
            subtitleBackground.transform.SetParent(transform, false);
            var backgroundRect = (RectTransform)subtitleBackground.transform;
            backgroundRect.anchorMin = rect.anchorMin;
            backgroundRect.anchorMax = rect.anchorMax;
            backgroundRect.pivot = rect.pivot;
            backgroundRect.anchoredPosition = rect.anchoredPosition;
            backgroundRect.sizeDelta = rect.sizeDelta;
            subtitleBackground.GetComponent<Image>().color = new Color(.06f, .12f, .11f, .96f);
            subtitleBackground.GetComponent<Image>().raycastTarget = false;
            subtitleBackground.transform.SetSiblingIndex(subtitleText.transform.GetSiblingIndex());
            volumeSlider = Instantiate(sensitivitySlider, elementList.parent);
            volumeSlider.name = "NarrationVolume";
            volumeSlider.GetComponentInChildren<TMP_Text>().text = MuseumInterfaceText.Get("narration_volume");
            volumeSlider.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            volumeSlider.minValue = 0;
            volumeSlider.maxValue = 1;
            volumeSlider.value = narration.Volume;
            volumeSlider.onValueChanged.RemoveAllListeners();
            volumeSlider.onValueChanged.AddListener(preferences.SetVolume);
            subtitlesButton = ModelButton(MuseumInterfaceText.Get("hide_subtitles"), elementList.parent, () => preferences.SetSubtitles(!preferences.Subtitles));
        }

        private void RefreshNarrationInterface()
        {
            if (narration == null) return;
            bool paused = interaction.State == InteractionState.Paused;
            bool selected = interaction.SelectedElement?.has_narration == true && !paused;
            bool silent = interaction.SelectedElement != null && !interaction.SelectedElement.has_narration && !paused
                && interaction.State != InteractionState.Reading;
            narrationText.gameObject.SetActive(selected || silent);
            narrationText.text = silent ? MuseumInterfaceText.Get("narration_not_published")
                : narration.Status + "\n" + narration.Transcription + (string.IsNullOrEmpty(narration.Attribution) ? "" : "\n" + narration.Attribution);
            narrationControls.SetActive(selected && narration.CanPlay);
            audioPauseButton.GetComponentInChildren<TMP_Text>().text = narration.State == NarrationState.Playing
                ? MuseumInterfaceText.Get("pause_narration_keyboard") : MuseumInterfaceText.Get("resume_narration_keyboard");
            audioMuteButton.GetComponentInChildren<TMP_Text>().text = narration.Muted ? MuseumInterfaceText.Get("unmute_narration") : MuseumInterfaceText.Get("mute_narration");
            narrationRetryButton.gameObject.SetActive(selected && narration.State == NarrationState.Unavailable);
            volumeSlider.gameObject.SetActive(false);
            subtitlesButton.gameObject.SetActive(false);
            RefreshSubtitle();
        }

        private void CreateStationInterface()
        {
            // Prompt line for the guide and the end of the suggested route, below the orientation legend.
            stationText = Instantiate(focusText, transform);
            stationText.name = "StationPrompt";
            stationText.fontSize = 24;
            stationText.alignment = TextAlignmentOptions.TopLeft;
            stationText.raycastTarget = false;
            var promptRect = stationText.rectTransform;
            promptRect.anchorMin = promptRect.anchorMax = new Vector2(0, 1);
            promptRect.pivot = new Vector2(0, 1);
            promptRect.anchoredPosition = new Vector2(24, -196);
            promptRect.sizeDelta = new Vector2(1200, 80);
            returnButton = ModelButton(MuseumInterfaceText.Get("return_to_catalog"), transform, farewell.ReturnToCatalog);
            returnButton.name = "ReturnToCatalog";
            var returnRect = returnButton.GetComponent<RectTransform>();
            returnRect.anchorMin = returnRect.anchorMax = new Vector2(0, 1);
            returnRect.pivot = new Vector2(0, 1);
            returnRect.anchoredPosition = new Vector2(24, -280);
            returnRect.sizeDelta = new Vector2(280, 52);

            welcomeCard = new GameObject("WelcomeCard", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            welcomeCard.transform.SetParent(transform, false);
            var cardRect = (RectTransform)welcomeCard.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(.5f, .5f);
            cardRect.pivot = new Vector2(.5f, .5f);
            cardRect.anchoredPosition = new Vector2(0, 40);
            cardRect.sizeDelta = new Vector2(820, 460);
            welcomeCard.GetComponent<Image>().color = new Color(.06f, .12f, .11f, .97f);
            var layout = welcomeCard.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 16;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            welcomeTitle = Instantiate(panelText, welcomeCard.transform);
            welcomeTitle.name = "WelcomeTitle";
            welcomeTitle.fontSize = 36;
            welcomeTitle.GetComponent<LayoutElement>().minHeight = 0;
            welcomeBody = Instantiate(panelText, welcomeCard.transform);
            welcomeBody.name = "WelcomeBody";
            welcomeBody.fontSize = 24;
            welcomeBody.GetComponent<LayoutElement>().minHeight = 0;
            ModelButton(MuseumInterfaceText.Get("start_visit"), welcomeCard.transform, arrival.Dismiss);
            welcomeCard.SetActive(false);
        }

        private void RefreshStationInterface()
        {
            if (stationText == null) return;
            bool exploring = !visitFlow.BlocksInput && interaction.State == InteractionState.Exploration;
            bool welcome = arrival != null && arrival.Visible && exploring;
            welcomeCard.SetActive(welcome);
            if (welcome)
            {
                welcomeTitle.text = arrival.Title;
                welcomeBody.text = arrival.Body;
            }
            string prompt = "";
            if (exploring && !welcome)
            {
                if (guide != null && !string.IsNullOrEmpty(guide.Prompt)) prompt = guide.Prompt;
                if (farewell != null && farewell.AtEnd) prompt = string.IsNullOrEmpty(prompt) ? farewell.Message : prompt + "\n" + farewell.Message;
            }
            // The examined model fills the view, so its keys stay readable even with the stand out of sight.
            if (!visitFlow.BlocksInput && interaction.State == InteractionState.ModelExamination && lectern.InWorld)
                prompt = MuseumInterfaceText.Get("examination_controls").Trim();
            stationText.text = prompt;
            stationText.gameObject.SetActive(!string.IsNullOrEmpty(prompt));
            returnButton.gameObject.SetActive(exploring && !welcome && farewell != null && farewell.AtEnd && farewell.CanReturn);
        }

        private void RefreshSubtitle()
        {
            subtitleText.text = narration.Subtitle;
            subtitleText.gameObject.SetActive(preferences.Subtitles && !string.IsNullOrEmpty(narration.Subtitle)
                && !visitFlow.BlocksInput && interaction.State != InteractionState.Paused);
            subtitleBackground.SetActive(subtitleText.gameObject.activeSelf);
            subtitlesButton.GetComponentInChildren<TMP_Text>().text = preferences.Subtitles ? MuseumInterfaceText.Get("hide_subtitles") : MuseumInterfaceText.Get("show_subtitles");
        }

        private void ApplyPreferences()
        {
            visitor?.SetSensitivity(preferences.Sensitivity);
            sensitivitySlider?.SetValueWithoutNotify(preferences.Sensitivity);
            volumeSlider?.SetValueWithoutNotify(preferences.Volume);
            narration?.SetVolume(preferences.Volume);
            RefreshSubtitle();
        }
    }
}
