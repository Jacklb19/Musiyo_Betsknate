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
        [SerializeField] private MuseumMenuView menuPrefab;
        private MuseumWelcomeView welcomeView;
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
        private MuseumInput input;
        private GameObject carousel;
        private TMP_Text carouselLabel;
        private Button carouselPrevious;
        private Button carouselNext;
        private readonly System.Collections.Generic.List<(TMP_Text caption, string textKey)> keyedCaptions = new();

        private void Awake()
        {
            if (interaction == null) return;
            MuseumAtmosphere.Current.Apply();
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
            CreateCarouselInterface();
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
            gameObject.AddComponent<MuseumGuideView>().Configure(guide, interaction, wayfinding, panelText, elementList.parent, ModelButton);
            farewell = interaction.GetComponent<MuseumFarewell>();
            if (farewell == null) farewell = interaction.gameObject.AddComponent<MuseumFarewell>();
            farewell.Configure(visitorTransform);
            var entry = interaction.GetComponent<MuseumEntryView>();
            if (entry == null) entry = interaction.gameObject.AddComponent<MuseumEntryView>();
            var backdrop = interaction.GetComponent<MuseumEntryBackdrop>();
            if (backdrop == null) backdrop = interaction.gameObject.AddComponent<MuseumEntryBackdrop>();
            var settings = MuseumExperienceConfiguration.Current;
            backdrop.Configure(MuseumEntryBackdrop.Parse(settings.EntryBackdrop), () => loader.ApiBase, settings.EntryMaskElement);
            entry.Configure(visitor != null ? visitor.GetComponentInChildren<Camera>() : null, visitorTransform,
                interaction.GetComponentInChildren<MuseumEntryViewpoint>(true), backdrop);
            var menu = GetComponent<MuseumMenuHud>();
            if (menu == null) menu = gameObject.AddComponent<MuseumMenuHud>();
            menu.Configure(visitFlow, interaction, loader, preferences, menuPrefab);
            welcomeView = menu.View.Welcome;
            CreateStationInterface();
            var header = statusText.transform.parent as RectTransform;
            var controlBar = header != null && header.parent != null ? header.parent.Find("Controls") as RectTransform : null;
            gameObject.AddComponent<MuseumHudPresence>().Configure(visitFlow, interaction, loader, header, controlBar);
            ApplyPreferences();
        }

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android delivery uses the same visit and menu views with a spatial host.
            StartCoroutine(AttachHeadset());
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator AttachHeadset()
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!UnityEngine.XR.XRSettings.isDeviceActive && Time.realtimeSinceStartup < deadline) yield return null;
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                Debug.LogError("Quest requires an active OpenXR headset. Spatial presentation was not started.");
                yield break;
            }
            var host = gameObject.AddComponent<MuseumXrHost>();
            host.Configure(visitor, interaction, GetComponent<MuseumMenuHud>(), GetComponent<Canvas>());
        }
#endif

        private void OnEnable()
        {
            loader.Changed += Refresh;
            interaction.Changed += Refresh;
            visitFlow.Changed += Refresh;
            preferences.Changed += ApplyPreferences;
            input = MuseumInput.Current;
            input.BindingsChanged += RefreshKeys;
            if (arrival != null) arrival.Changed += Refresh;
            if (guide != null) guide.Changed += Refresh;
            if (farewell != null) farewell.Changed += Refresh;
            if (interaction.TryGetComponent<MuseumWayfinding>(out var orientation)) orientation.Changed += Refresh;
            if (model != null) model.Changed += Refresh;
            presence.Changed += OnPresenceChanged;
            if (narration != null)
            {
                narration.Changed += Refresh;
                narration.SubtitleChanged += RefreshSubtitle;
            }
            welcomeView.Dismiss.onClick.AddListener(arrival.Dismiss);
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
            if (input != null) input.BindingsChanged -= RefreshKeys;
            if (arrival != null) arrival.Changed -= Refresh;
            if (guide != null) guide.Changed -= Refresh;
            if (farewell != null) farewell.Changed -= Refresh;
            if (interaction.TryGetComponent<MuseumWayfinding>(out var orientation)) orientation.Changed -= Refresh;
            if (model != null) model.Changed -= Refresh;
            presence.Changed -= OnPresenceChanged;
            if (narration != null)
            {
                narration.Changed -= Refresh;
                narration.SubtitleChanged -= RefreshSubtitle;
            }
            welcomeView.Dismiss.onClick.RemoveListener(arrival.Dismiss);
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

        /// <summary>Page keys and the right controller stick scroll long panels; the mouse wheel stays free for model zoom.</summary>
        private void ScrollPointPanel()
        {
            var scroll = MuseumInput.Current.ScrollPanel;
            if (!scroll.WasPressedThisFrame() || !panel.activeSelf || PanelScroll() == null) return;
            int direction = System.Math.Sign(scroll.ReadValue<float>());
            if (direction == 0) return;
            panelScroll.verticalNormalizedPosition = Mathf.Clamp01(panelScroll.verticalNormalizedPosition
                + direction * MuseumExperienceConfiguration.Current.PanelScrollStep);
        }

        private CanvasGroup PanelGroup()
        {
            if (panelGroup == null && !panel.TryGetComponent(out panelGroup)) panelGroup = panel.AddComponent<CanvasGroup>();
            return panelGroup;
        }

        /// <summary>A reassigned key changes every caption and prompt that names it.</summary>
        public void RefreshDeviceLabels() => RefreshKeys();

        private void RefreshKeys()
        {
            foreach (var (caption, textKey) in keyedCaptions)
                if (caption != null) caption.text = MuseumInterfaceText.Get(textKey);
            Refresh();
        }

        private void TogglePause() => interaction.SetPaused(interaction.State != InteractionState.Paused);
        private void OpenDetail() => interaction.OpenDetail();
        private void OnPresenceChanged(bool away) => Refresh();

        private void Update()
        {
            var focus = pointInput.FocusedPoint;
            focusText.text = focus != null && focus.Content != null ? MuseumInterfaceText.Format("focus_point", focus.Content.name)
                : pointInput.GazeProgress > 0 ? MuseumInterfaceText.Format("gaze_progress", Mathf.RoundToInt(pointInput.GazeProgress * 100)) : ".";
            ScrollPointPanel();
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
            RefreshCarousel();
            RefreshNarrationInterface();
            RefreshStationInterface();
            pauseButton.GetComponentInChildren<TMP_Text>().text = paused ? MuseumInterfaceText.Get("resume") : MuseumInterfaceText.Get("pause");
            if (sensitivitySlider != null) sensitivitySlider.gameObject.SetActive(false);
            bool showPanel = !visitFlow.BlocksInput && !paused && interaction.State != InteractionState.Exploration;
            // The conversation with the guide unfolds at the guide's anchor; exhibit details at their point.
            var shownPoint = interaction.State == InteractionState.GuideQuestion ? interaction.Runtime.GuidePoint : interaction.ActivePoint;
            // A reading stand folds its screen while the visitor is away from the point and unfolds it on return.
            bool onStand = lectern.Find(shownPoint) != null;
            if (onStand && presence.Away) showPanel = false;
            if (!showPanel) lectern.Hide();
            else
            {
                // The stand unfolds the panel itself; on the screen, Update fades it in.
                if (onStand) PanelGroup().alpha = 1;
                else if (!panel.activeSelf || lectern.InWorld) PanelGroup().alpha = 0;
                lectern.Show(shownPoint);
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
            examineButton = ModelButton("examine_model", elementList.parent, () => model.Examine());
            modelRetryButton = ModelButton("retry_model", elementList.parent, model.Retry);
            modelControls = new GameObject("ModelControls", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            modelControls.transform.SetParent(elementList.parent, false);
            // Seven examination buttons fit the panel as a grid instead of a column the viewport cuts off.
            var layout = modelControls.GetComponent<GridLayoutGroup>();
            int columns = MuseumExperienceConfiguration.Current.ModelControlColumns;
            layout.spacing = new Vector2(8, 8);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            layout.cellSize = new Vector2((((RectTransform)elementList).sizeDelta.x - layout.spacing.x * (columns - 1)) / columns, 52);
            ModelButton("rotate_left", modelControls.transform, () => model.Rotate(new Vector2(-15, 0)));
            ModelButton("rotate_right", modelControls.transform, () => model.Rotate(new Vector2(15, 0)));
            ModelButton("rotate_up", modelControls.transform, () => model.Rotate(new Vector2(0, -15)));
            ModelButton("rotate_down", modelControls.transform, () => model.Rotate(new Vector2(0, 15)));
            ModelButton("zoom_in", modelControls.transform, () => model.Zoom(-.1f));
            ModelButton("zoom_out", modelControls.transform, () => model.Zoom(.1f));
            ModelButton("reset_model", modelControls.transform, model.ResetPose);
        }

        /// <summary>Buttons are created from interface text keys so their captions follow the keys the visitor assigned.</summary>
        private Button ModelButton(string textKey, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var button = Instantiate(detailButton, parent);
            button.name = "ModelAction";
            var caption = button.GetComponentInChildren<TMP_Text>();
            caption.text = MuseumInterfaceText.Get(textKey);
            keyedCaptions.Add((caption, textKey));
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

        /// <summary>
        /// A point with several pieces that have a model shows one at a time: Previous and Next choose among them with
        /// the same selection as the element list, and the counter names the piece on show.
        /// </summary>
        private void CreateCarouselInterface()
        {
            carousel = new GameObject("PieceCarousel", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            carousel.transform.SetParent(elementList.parent, false);
            carousel.transform.SetSiblingIndex(panelText.transform.GetSiblingIndex() + 1);
            var row = carousel.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            carousel.GetComponent<LayoutElement>().preferredHeight = 64;
            carouselPrevious = ModelButton("carousel_previous", carousel.transform, () => StepCarousel(-1));
            carouselPrevious.name = "CarouselPrevious";
            carouselLabel = Instantiate(panelText, carousel.transform);
            carouselLabel.name = "CarouselPosition";
            carouselLabel.richText = false;
            carouselLabel.alignment = TextAlignmentOptions.Center;
            carouselLabel.fontSize = panelText.fontSize * .85f;
            if (carouselLabel.TryGetComponent<LayoutElement>(out var labelLayout)) { labelLayout.minHeight = 0; labelLayout.flexibleWidth = 1; }
            else carouselLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            carouselNext = ModelButton("carousel_next", carousel.transform, () => StepCarousel(1));
            carouselNext.name = "CarouselNext";
            foreach (var button in new[] { carouselPrevious, carouselNext })
            {
                var layout = button.GetComponent<LayoutElement>();
                // Wide enough for "Siguiente" on one line; the counter takes what is left.
                layout.minWidth = layout.preferredWidth = 170;
                layout.flexibleWidth = 0;
            }
            carousel.SetActive(false);
        }

        /// <summary>Indices, in the point's element list, of the pieces that have a model.</summary>
        private System.Collections.Generic.List<int> CarouselPieces()
        {
            var pieces = new System.Collections.Generic.List<int>();
            var point = interaction.ActivePoint;
            if (point == null || !point.HasContent) return pieces;
            for (int index = 0; index < point.Content.elements.Length; index++)
                if (point.Content.elements[index].has_3d_model) pieces.Add(index);
            return pieces;
        }

        /// <summary>Moves to the previous or next piece, wrapping around; from the common record it starts at either end.</summary>
        public bool StepCarousel(int direction)
        {
            var pieces = CarouselPieces();
            if (pieces.Count < 2 || interaction.State != InteractionState.ElementSelected) return false;
            var elements = interaction.ActivePoint.Content.elements;
            int current = pieces.FindIndex(index => elements[index] == interaction.SelectedElement);
            int next = current < 0 ? (direction > 0 ? 0 : pieces.Count - 1) : (current + direction + pieces.Count) % pieces.Count;
            return interaction.SelectElement(pieces[next]);
        }

        private void RefreshCarousel()
        {
            if (carousel == null) return;
            var pieces = CarouselPieces();
            bool shown = pieces.Count >= 2 && interaction.State == InteractionState.ElementSelected;
            carousel.SetActive(shown);
            if (!shown) return;
            var elements = interaction.ActivePoint.Content.elements;
            int current = pieces.FindIndex(index => elements[index] == interaction.SelectedElement);
            carouselLabel.text = current < 0 ? MuseumInterfaceText.Format("carousel_overview", pieces.Count)
                : MuseumInterfaceText.Format("carousel_position", current + 1, pieces.Count, elements[pieces[current]].title);
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
            audioPauseButton = ModelButton("pause_narration", narrationControls.transform, narration.TogglePause);
            ModelButton("repeat_narration", narrationControls.transform, narration.Repeat);
            audioMuteButton = ModelButton("mute_narration", narrationControls.transform, narration.ToggleMute);
            narrationRetryButton = ModelButton("retry_narration", elementList.parent, narration.Retry);
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
            subtitlesButton = ModelButton("hide_subtitles", elementList.parent, () => preferences.SetSubtitles(!preferences.Subtitles));
        }

        private void RefreshNarrationInterface()
        {
            if (narration == null) return;
            bool paused = interaction.State == InteractionState.Paused;
            // Pieces of one point share the narration of its common record when they have none of their own.
            var heard = MuseumNarration.NarrationFor(interaction.ActivePoint, interaction.SelectedElement);
            bool selected = heard?.has_narration == true && !paused;
            bool silent = heard != null && !heard.has_narration && !paused
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
            returnButton = ModelButton("return_to_catalog", transform, farewell.ReturnToCatalog);
            returnButton.name = "ReturnToCatalog";
            var returnRect = returnButton.GetComponent<RectTransform>();
            returnRect.anchorMin = returnRect.anchorMax = new Vector2(0, 1);
            returnRect.pivot = new Vector2(0, 1);
            returnRect.anchoredPosition = new Vector2(24, -280);
            returnRect.sizeDelta = new Vector2(280, 52);

            welcomeCard = welcomeView.gameObject;
            welcomeTitle = welcomeView.Title;
            welcomeBody = welcomeView.Body;
            // The route and key list are longer than one line of the card.
            welcomeBody.textWrappingMode = TextWrappingModes.Normal;
            welcomeBody.overflowMode = TextOverflowModes.Overflow;
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
            // The orientation menu covers the same corner; its own title says where the visitor is.
            if (interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true) prompt = "";
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
