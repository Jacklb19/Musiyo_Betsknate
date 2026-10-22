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
        /// <summary>Shipped size of the subtitles, before the visitor's choice.</summary>
        public const float SubtitleSize = 24;
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
        /// <summary>The whole record without the narration's transcription, for the screen by a stand.</summary>
        private string record;
        private MuseumLecternDisplay lectern;
        private MuseumPointPresence presence;
        private MuseumInput input;
        private MuseumPieceCues pieceCues;
        private Button narrationToggle;
        private MuseumServiceNotice serviceNotice;
        private Button serviceRetryButton;
        private MuseumNarrationReading narrationReading;
        private Button followNarrationButton;
        private MuseumModelGrab grab;
        private MuseumAutoTour autoTour;
        private readonly System.Collections.Generic.List<(TMP_Text caption, string textKey)> keyedCaptions = new();
        private RectTransform focusPill;
        private RectTransform stationPill;
        private string shownFocus;

        private void Awake()
        {
            if (interaction == null) return;
            MuseumAtmosphere.Current.Apply();
            // The museum's own hearth burns with the same living fire as the entry.
            MuseumFire.ReplaceMuseumPlaceholder();
            preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current,
                PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, ""));
            visitFlow = interaction.GetComponent<MuseumVisitFlow>();
            if (visitFlow == null) visitFlow = interaction.gameObject.AddComponent<MuseumVisitFlow>();
            model = interaction.GetComponent<MuseumModelPresenter>();
            if (model == null) model = interaction.gameObject.AddComponent<MuseumModelPresenter>();
            if (visitor != null) model.Configure(visitor.GetComponentInChildren<Camera>(), visitor.transform);
            // The texts made from the panel text below share its larger reading size.
            panelText.fontSize = MuseumLecternDisplay.TextSize;
            CreateModelInterface();
            grab = interaction.GetComponent<MuseumModelGrab>();
            if (grab == null) grab = interaction.gameObject.AddComponent<MuseumModelGrab>();
            grab.Configure(visitor != null ? visitor.GetComponentInChildren<Camera>() : null, visitor);
            autoTour = interaction.GetComponent<MuseumAutoTour>();
            if (autoTour == null) autoTour = interaction.gameObject.AddComponent<MuseumAutoTour>();
            if (visitor != null) autoTour.Configure(visitor.GetComponent<CharacterController>(), visitor.GetComponentInChildren<Camera>()?.transform);
            narration = interaction.GetComponent<MuseumNarration>();
            if (narration == null) narration = interaction.gameObject.AddComponent<MuseumNarration>();
            presence = interaction.GetComponent<MuseumPointPresence>();
            presence.Configure(visitor != null ? visitor.transform : null);
            lectern = interaction.GetComponent<MuseumLecternDisplay>();
            if (lectern == null) lectern = interaction.gameObject.AddComponent<MuseumLecternDisplay>();
            lectern.Configure((RectTransform)panel.transform, visitor != null ? visitor.GetComponentInChildren<Camera>() : null);
            CreateNarrationInterface();
            CreateCarouselInterface();
            serviceNotice = interaction.GetComponent<MuseumServiceNotice>();
            if (serviceNotice == null) serviceNotice = interaction.gameObject.AddComponent<MuseumServiceNotice>();
            serviceNotice.Configure(visitor != null ? visitor.transform : null);
            serviceRetryButton = ModelButton("retry_service", elementList.parent, loader.Reload);
            serviceRetryButton.name = "RetryService";
            serviceRetryButton.gameObject.SetActive(false);
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
            var quiz = interaction.GetComponent<MuseumHearthQuiz>();
            if (quiz == null) quiz = interaction.gameObject.AddComponent<MuseumHearthQuiz>();
            guide.Attach(quiz);
            var voice = interaction.GetComponent<MuseumGuideVoice>();
            if (voice == null) voice = interaction.gameObject.AddComponent<MuseumGuideVoice>();
            guide.Attach(voice);
            gameObject.AddComponent<MuseumGuideView>().Configure(guide, interaction, wayfinding, panelText, elementList.parent, ModelButton, quiz, voice);
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
            // The end of the route asks whether to finish, and the credits roll by the same fire as the title menu.
            var ending = interaction.GetComponent<MuseumEnding>();
            if (ending == null) ending = interaction.gameObject.AddComponent<MuseumEnding>();
            var eyes = visitor != null ? visitor.GetComponentInChildren<Camera>() : null;
            ending.Configure(visitorTransform, eyes != null ? eyes.transform : visitorTransform, eyes, backdrop);
            var menu = GetComponent<MuseumMenuHud>();
            if (menu == null) menu = gameObject.AddComponent<MuseumMenuHud>();
            menu.Configure(visitFlow, interaction, loader, preferences, menuPrefab);
            welcomeView = menu.View.Welcome;
            // Over the hearth set the entry menu reads as a title screen.
            gameObject.AddComponent<MuseumTitleScreen>().Configure(menu.View, visitFlow, backdrop);
            CreateStationInterface();
            var header = statusText.transform.parent as RectTransform;
            var controlBar = header != null && header.parent != null ? header.parent.Find("Controls") as RectTransform : null;
            gameObject.AddComponent<MuseumHudPresence>().Configure(visitFlow, interaction, loader, header, controlBar);
            CreateSideColumn();
            // Menus, point screens and prompts share the typography of the Web.
            MuseumTypography.Apply(transform);
            ApplyPreferences();
            Skin();
        }

        /// <summary>The screen of the visit takes the palette and the rounded surfaces of the menus.</summary>
        private void Skin()
        {
            if (panel.TryGetComponent<Image>(out var screen)) MuseumHudSkin.Surface(screen);
            foreach (var button in panel.GetComponentsInChildren<Button>(true)) MuseumHudSkin.Choice(button, button == detailButton);
            MuseumHudSkin.Choice(elementTemplate);
            MuseumHudSkin.Choice(returnButton, true);
            MuseumHudSkin.Choice(serviceRetryButton);
            if (panelText != null) panelText.color = MuseumHudSkin.Text;
            if (subtitleBackground != null) MuseumHudSkin.Pill(subtitleBackground.GetComponent<Image>(), .86f);
            if (subtitleText != null) subtitleText.color = MuseumHudSkin.Text;
            focusText.color = MuseumHudSkin.Text;
            focusPill = MuseumHudSkin.PillBehind(focusText);
            stationText.color = MuseumHudSkin.Text;
            stationPill = MuseumHudSkin.PillBehind(stationText);
            shownFocus = null;
        }

        /// <summary>
        /// The point screen reads in two columns: the text scrolls on the left, and the element list, the narration and the
        /// model controls stand in a column on the right, so the screen is wide and low like a museum label.
        /// </summary>
        private void CreateSideColumn()
        {
            var content = elementList.parent as RectTransform;
            var viewport = content != null ? content.parent as RectTransform : null;
            if (viewport == null) return;
            float width = MuseumLecternDisplay.SideColumnWidth;
            var side = new GameObject("SideColumn", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            side.SetParent(panel.transform, false);
            side.anchorMin = new Vector2(1, 0);
            side.anchorMax = Vector2.one;
            side.pivot = Vector2.one;
            side.offsetMin = new Vector2(viewport.offsetMax.x - width, viewport.offsetMin.y);
            side.offsetMax = new Vector2(viewport.offsetMax.x, viewport.offsetMax.y);
            var layout = side.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            viewport.offsetMax = new Vector2(viewport.offsetMax.x - width - MuseumLecternDisplay.ColumnGap, viewport.offsetMax.y);
            // Reading stays in the scrolling column; everything that is chosen or pressed moves to the side, in order.
            var moved = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in content)
                if (child.GetComponent<TMP_Text>() == null) moved.Add(child);
            foreach (var child in moved) child.SetParent(side, false);
            if (modelControls != null && modelControls.TryGetComponent<GridLayoutGroup>(out var grid))
                grid.cellSize = new Vector2((width - grid.spacing.x * (grid.constraintCount - 1)) / grid.constraintCount, grid.cellSize.y);
            lectern.SideColumn = side;
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
            serviceNotice.Changed += Refresh;
            autoTour.Changed += Refresh;
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
            serviceNotice.Changed -= Refresh;
            autoTour.Changed -= Refresh;
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
            // While a piece is in the hand the stick turns the piece.
            if (grab != null && grab.Held && MuseumInput.Current.Spatial) return;
            int direction = System.Math.Sign(scroll.ReadValue<float>());
            if (direction == 0) return;
            // Scrolling by hand takes the panel away from the narration until the visitor asks to follow it again.
            narrationReading?.Suspend();
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

        /// <summary>Point whose reading stand carries the service notice, or null.</summary>
        private TourPoint NoticePoint => serviceNotice.Point != null && lectern.Find(serviceNotice.Point) != null ? serviceNotice.Point : null;

        private void TogglePause() => interaction.SetPaused(interaction.State != InteractionState.Paused);
        private void OpenDetail() => interaction.OpenDetail();
        private void OnPresenceChanged(bool away) => Refresh();

        private void Update()
        {
            var focus = pointInput.FocusedPoint;
            focusText.text = grab != null && grab.Held ? MuseumInterfaceText.Get("grab_holding")
                : grab != null && grab.Hovered ? MuseumInterfaceText.Get("grab_hint")
                : focus != null && focus.Content != null ? MuseumInterfaceText.Format("focus_point", focus.Content.name)
                : pointInput.GazeProgress > 0 ? MuseumInterfaceText.Format("gaze_progress", Mathf.RoundToInt(pointInput.GazeProgress * 100)) : ".";
            if (focusText.text != shownFocus) { shownFocus = focusText.text; MuseumHudSkin.Fit(focusPill, focusText); }
            ScrollPointPanel();
            if (panel.activeSelf && PanelGroup().alpha < 1)
                panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, 1,
                    Time.unscaledDeltaTime / MuseumExperienceConfiguration.Current.PanelFadeSeconds);
            if (model != null && model.State == ModelLoadState.Loading && modelText.gameObject.activeInHierarchy)
                modelText.text = model.Status + " " + Mathf.RoundToInt(model.Progress * 100) + "%";
        }

        private void Refresh()
        {
            // By a stand the point opens on its first record at once: the arrows beside the piece choose among the rest,
            // so the screen never shows a list to pick from.
            if (interaction.State == InteractionState.PointFocus && interaction.DetailInWorld && interaction.SelectElement(0)) return;
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
            // Without the museum service, the reading stand of the point the visitor stands at says so instead of staying
            // silent. Points without a stand show no screen for it: a notice floating in the open turned after the visitor.
            var noticePoint = NoticePoint;
            bool notice = !visitFlow.BlocksInput && !paused && interaction.State == InteractionState.Exploration
                && noticePoint != null;
            serviceRetryButton.gameObject.SetActive(notice && loader.State == TourLoadState.Unavailable);
            showPanel |= notice;
            // The conversation with the guide unfolds at the guide's anchor; exhibit details at their point.
            var shownPoint = interaction.State == InteractionState.GuideQuestion ? interaction.Runtime.GuidePoint
                : notice ? noticePoint : interaction.ActivePoint;
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
                detail = summary = record = null;
            }
            // A folding screen keeps what it was showing until it has closed.
            if (!showPanel && lectern.InWorld) return;
            foreach (Transform child in elementList)
                if (child.gameObject != elementTemplate.gameObject)
                { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            elementTemplate.gameObject.SetActive(false);
            bool choosing = interaction.State == InteractionState.PointFocus;
            elementList.gameObject.SetActive(choosing);
            // By a stand the screen folds by itself when the visitor walks on, so it needs no button to close.
            closeButton.gameObject.SetActive(!interaction.DetailInWorld || interaction.State == InteractionState.GuideQuestion);
            // By a stand the screen already holds the whole record, so nothing asks to open it.
            detailButton.gameObject.SetActive(!interaction.DetailInWorld && (interaction.State == InteractionState.ElementSelected
                || interaction.State == InteractionState.ModelExamination));
            // Only a formatted record carries markup, and in it the service text is shown literally; nothing else is parsed.
            panelText.richText = false;
            if (paused) { panelText.text = MuseumInterfaceText.Get("pause_description"); return; }
            if (notice)
            {
                panelText.text = MuseumInterfaceText.Get(loader.State == TourLoadState.Loading ? "service_loading_point" : "service_unavailable_point");
                return;
            }
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
                detail = summary = record = null;
                reading = StartCoroutine(LoadDetail(element.slug));
            }
            if (interaction.DetailInWorld && interaction.State != InteractionState.Reading)
            {
                // The screen by the stand is a label: where the record stands among its point's, its name and all its text.
                int count = point.Content.elements.Length;
                int position = System.Array.IndexOf(point.Content.elements, element);
                // The record begins with the piece's name; the narration's transcription follows below it on its own.
                panelText.richText = true;
                panelText.text = ElementText.Plain(point.Content.name
                    + (count > 1 ? " · " + MuseumInterfaceText.Format("piece_position", position + 1, count) : "")) + "\n"
                    + (record ?? ElementText.Plain(element.title) + "\n\n" + (detail ?? ElementText.Plain(MuseumInterfaceText.Get("detail_loading")))).TrimEnd();
                return;
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
            panelText.richText = detail != null;
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
            ElementText.TryFormat(slug, json, false, out record, out _);
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
        /// A point with several records shows one at a time: arrows beside the piece on its pedestal step through them, and
        /// the screen says which one of how many is on show.
        /// </summary>
        private void CreateCarouselInterface()
        {
            pieceCues = interaction.gameObject.AddComponent<MuseumPieceCues>();
            pieceCues.Configure(interaction, model, lectern, visitor != null ? visitor.GetComponentInChildren<Camera>() : null, StepCarousel);
        }

        /// <summary>Moves to the previous or next record of the point, wrapping around.</summary>
        public bool StepCarousel(int direction)
        {
            var point = interaction.ActivePoint;
            if (point == null || !point.HasContent || point.Content.elements.Length < 2
                || interaction.State != InteractionState.ElementSelected) return false;
            var elements = point.Content.elements;
            int current = System.Array.IndexOf(elements, interaction.SelectedElement);
            int next = current < 0 ? (direction > 0 ? 0 : elements.Length - 1) : (current + direction + elements.Length) % elements.Length;
            return interaction.SelectElement(next);
        }

        private void RefreshCarousel()
        {
            if (pieceCues == null) return;
            var point = interaction.ActivePoint;
            bool onStand = lectern.Find(point) != null;
            pieceCues.Shown = point != null && point.HasContent && point.Content.elements.Length >= 2
                && interaction.State == InteractionState.ElementSelected && !visitFlow.BlocksInput && !(onStand && presence.Away);
        }

        private void RefreshModelInterface()
        {
            if (model == null) return;
            bool selected = interaction.SelectedElement?.has_3d_model == true
                && interaction.State != InteractionState.Paused && interaction.State != InteractionState.Exploration;
            bool examining = interaction.State == InteractionState.ModelExamination;
            modelText.gameObject.SetActive(selected && interaction.State != InteractionState.Reading);
            modelText.text = model.Status + (string.IsNullOrEmpty(model.Attribution) ? "" : "\n" + model.Attribution);
            // By a stand the piece is examined by taking it in the hand, so the screen offers no examination.
            examineButton.gameObject.SetActive(selected && interaction.State == InteractionState.ElementSelected && model.CanExamine
                && !interaction.DetailInWorld);
            modelRetryButton.gameObject.SetActive(selected && model.State == ModelLoadState.Unavailable);
            modelControls.SetActive(examining && !interaction.DetailInWorld);
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
            followNarrationButton = ModelButton("follow_narration", narrationControls.transform, () => narrationReading.Resume());
            followNarrationButton.name = "FollowNarration";
            narrationRetryButton = ModelButton("retry_narration", elementList.parent, narration.Retry);
            // By a stand a single quiet control pauses, resumes or repeats the narration that started by itself.
            narrationToggle = ModelButton("pause_narration_keyboard", elementList.parent, ToggleNarration);
            narrationToggle.name = "NarrationToggle";
            subtitleText = Instantiate(focusText, transform);
            subtitleText.name = "NarrationSubtitles";
            subtitleText.fontSize = SubtitleSize;
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
            narrationReading = gameObject.AddComponent<MuseumNarrationReading>();
            narrationReading.Configure(narration, PanelScroll(), narrationText, followNarrationButton);
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
            if (silent) narrationText.text = MuseumInterfaceText.Get("narration_not_published");
            else narrationReading.Show(narration.Status, narration.Transcription, narration.Attribution);
            bool inWorld = interaction.DetailInWorld;
            narrationControls.SetActive(selected && narration.CanPlay && !inWorld);
            narrationToggle.gameObject.SetActive(selected && narration.CanPlay && inWorld);
            SetCaption(narrationToggle, narration.State == NarrationState.Playing ? "pause_narration_keyboard"
                : narration.State == NarrationState.Paused ? "resume_narration_keyboard" : "repeat_narration");
            audioPauseButton.GetComponentInChildren<TMP_Text>().text = narration.State == NarrationState.Playing
                ? MuseumInterfaceText.Get("pause_narration_keyboard") : MuseumInterfaceText.Get("resume_narration_keyboard");
            audioMuteButton.GetComponentInChildren<TMP_Text>().text = narration.Muted ? MuseumInterfaceText.Get("unmute_narration") : MuseumInterfaceText.Get("mute_narration");
            narrationRetryButton.gameObject.SetActive(selected && narration.State == NarrationState.Unavailable);
            volumeSlider.gameObject.SetActive(false);
            subtitlesButton.gameObject.SetActive(false);
            RefreshSubtitle();
        }

        private void ToggleNarration()
        {
            narration.TogglePause();
            narrationReading?.Resume();
        }

        /// <summary>Gives a control made by <see cref="ModelButton"/> another caption that still follows the visitor's keys.</summary>
        private void SetCaption(Button button, string textKey)
        {
            var caption = button.GetComponentInChildren<TMP_Text>();
            int index = keyedCaptions.FindIndex(entry => entry.caption == caption);
            if (index >= 0) keyedCaptions[index] = (caption, textKey);
            caption.text = MuseumInterfaceText.Get(textKey);
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
            // Without the service, a short line says so; the point screens and the menus offer to retry.
            if (exploring && !welcome && loader.State == TourLoadState.Unavailable && NoticePoint == null)
                prompt = string.IsNullOrEmpty(prompt) ? MuseumInterfaceText.Get("service_unavailable_hint")
                    : MuseumInterfaceText.Get("service_unavailable_hint") + "\n" + prompt;
            // The automatic tour says what it is doing and how to take control back.
            if (!visitFlow.BlocksInput && autoTour != null && !string.IsNullOrEmpty(autoTour.Status) && !welcome)
                prompt = string.IsNullOrEmpty(prompt) ? autoTour.Status : autoTour.Status + "\n" + prompt;
            // The examined model fills the view, so its keys stay readable even with the stand out of sight.
            if (!visitFlow.BlocksInput && interaction.State == InteractionState.ModelExamination && lectern.InWorld)
                prompt = MuseumInterfaceText.Get("examination_controls").Trim();
            // The orientation menu covers the same corner; its own title says where the visitor is.
            if (interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true) prompt = "";
            stationText.text = prompt;
            stationText.gameObject.SetActive(!string.IsNullOrEmpty(prompt));
            MuseumHudSkin.Fit(stationPill, stationText);
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
            // The general volume stands over every voice; each one keeps its own share under it.
            narration?.SetVolume(preferences.Volume * preferences.NarrationLevel);
            interaction.GetComponent<MuseumGuideVoice>()?.SetVolume(preferences.Volume * preferences.GuideLevel);
            var music = FindAnyObjectByType<MuseumMusic>();
            if (music != null) { music.SetMasterVolume(preferences.Volume); music.SetMusicLevel(preferences.MusicLevel); }
            if (Application.isPlaying) MuseumUiSounds.Current.SetVolume(preferences.Volume);
            // Sizes: the subtitles on the screen and the reading on a stand follow the visitor's choice.
            if (subtitleText != null) subtitleText.fontSize = SubtitleSize * preferences.SubtitleScale;
            float reading = MuseumLecternDisplay.TextSize * preferences.ReadingScale;
            if (panelText != null) panelText.fontSize = reading;
            if (narrationText != null) narrationText.fontSize = reading;
            if (modelText != null) modelText.fontSize = reading;
            RefreshSubtitle();
        }
    }
}
