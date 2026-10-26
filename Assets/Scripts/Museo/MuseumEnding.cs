using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public enum EndingStage { Idle, Asking, Closing, Credits, Leaving }

    /// <summary>
    /// The end of the visit, as the owner asked on 21/10/2026: after a few seconds in the last room of the route (the
    /// overlook), or on coming close to its far end, or when the automatic tour finishes there, a small card in front of the
    /// visitor asks whether to end the visit. Ending turns the museum dark; out of the dark comes the fire of the title menu,
    /// and the credits published by the API roll up beside it. After them the title menu returns over the same fire and a
    /// new visit starts from the beginning. The question only comes once the credits are here: an empty roll is never shown.
    /// </summary>
    [RequireComponent(typeof(MuseumVisitFlow), typeof(MuseumInteraction))]
    public sealed class MuseumEnding : MonoBehaviour
    {
        [Tooltip("Seconds in the last room, free to look around, before the question comes.")]
        [SerializeField] private float dwellSeconds = 8;
        [Tooltip("Seconds in the last room while a record of its stand is open: reading is given more time.")]
        [SerializeField] private float readingDwellSeconds = 20;
        [Tooltip("Distance in metres to the far end of the last room at which the question comes at once.")]
        [SerializeField] private float edgeDistance = 1.2f;
        [SerializeField] private float closeSeconds = 1.6f;
        [SerializeField] private float holdSeconds = .6f;
        [SerializeField] private float revealSeconds = 2.2f;
        [SerializeField] private float leaveSeconds = 1.4f;
        [SerializeField] private float retrySeconds = 8;
        /// <summary>
        /// Distance of the question card in front of the eyes, on a desktop and on a headset. On a screen it stands closer
        /// and larger, so it reads in a browser window too (owner, Web test of 23/10: «está muy lejos y no se lee»).
        /// </summary>
        public const float CardDistance = .8f, CardDistanceHeadset = 1.5f;
        /// <summary>On a screen the card is drawn this much larger, but never wider than this share of the view.</summary>
        public const float CardScreenScale = 1.15f, CardMostOfView = .8f;
        private const float CardWidth = 980, CardHeight = 400;

        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private MuseumFarewell farewell;
        private MuseumAutoTour autoTour;
        private MuseumNarration narration;
        private MuseumEntryBackdrop entryBackdrop;
        private MuseumEntryBackdrop ownBackdrop;
        private Transform body;
        private DesktopVisitorController desktop;
        private Transform view;
        private Camera viewCamera;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private bool startKnown;
        private bool wasInside;
        private bool declined;
        private bool tourWasActive;
        private bool askSoon;
        private bool pausedNarration;
        private double retryAt;
        private float stageTime;
        private float musicFade = 1;
        private Canvas card;
        private CanvasGroup cardGroup;
        private Button yes, keep;
        private MuseumCreditsRoll roll;
        private MuseumMusic music;
        private AudioSource crackle;

        public MuseumCreditsClient Client { get; private set; }
        public EndingStage Stage { get; private set; }
        /// <summary>Seconds spent in the last room, counted only while the visitor is free to look around.</summary>
        public float Dwell { get; private set; }
        public float DwellSeconds { get => dwellSeconds; set => dwellSeconds = Mathf.Max(0, value); }
        public float EdgeDistance { get => edgeDistance; set => edgeDistance = Mathf.Max(0, value); }
        /// <summary>Seconds before credits that could not be read are asked for again.</summary>
        public float RetrySeconds { get => retrySeconds; set => retrySeconds = Mathf.Max(0, value); }
        /// <summary>How far the visitor stands from the far end of the last room; infinite outside it.</summary>
        public float DistanceToEnd { get; private set; } = float.PositiveInfinity;
        /// <summary>0 clear, 1 dark: the veil over the view while the museum gives way to the fire.</summary>
        public float Darkness { get; private set; }
        /// <summary>True from the moment the view is dark until the title menu: the visitor stands by the fire.</summary>
        public bool InSet => flow != null && flow.Phase == VisitPhase.Ending && (Stage == EndingStage.Credits || Stage == EndingStage.Leaving
            || Stage == EndingStage.Closing && stageTime >= closeSeconds);
        /// <summary>The fire of the title menu, or a set of its own when the menu shows something else.</summary>
        public MuseumEntryBackdrop Backdrop
        {
            get
            {
                if (entryBackdrop != null && entryBackdrop.Kind == EntryBackdropKind.Hearth) return entryBackdrop;
                if (ownBackdrop == null)
                {
                    ownBackdrop = new GameObject("EndingBackdrop").AddComponent<MuseumEntryBackdrop>();
                    ownBackdrop.transform.SetParent(transform, false);
                    ownBackdrop.Configure(EntryBackdropKind.Hearth);
                }
                return ownBackdrop;
            }
        }
        public Canvas Card => card;
        public Button YesButton => yes;
        public Button KeepButton => keep;
        public MuseumCreditsRoll Roll => roll;
        public event Action Changed;

        /// <summary>The visitor's body, where a new visit starts from, and the eyes the card and the roll face.</summary>
        public void Configure(Transform visitorBody, Transform eyes, Camera eyesCamera, MuseumEntryBackdrop backdrop)
        {
            Connect();
            body = visitorBody;
            desktop = visitorBody != null ? visitorBody.GetComponent<DesktopVisitorController>() : null;
            entryBackdrop = backdrop;
            ConfigureView(eyes, eyesCamera);
            if (body != null && !startKnown)
            {
                startPosition = body.position;
                startRotation = body.rotation;
                startKnown = true;
            }
        }

        /// <summary>A headset replaces the eyes the card and the roll face.</summary>
        public void ConfigureView(Transform eyes, Camera eyesCamera)
        {
            view = eyes;
            viewCamera = eyesCamera;
        }

        private void Awake() => Connect();

        private void Connect()
        {
            if (flow != null) return;
            flow = GetComponent<MuseumVisitFlow>();
            interaction = GetComponent<MuseumInteraction>();
            farewell = GetComponent<MuseumFarewell>();
            if (farewell == null) farewell = gameObject.AddComponent<MuseumFarewell>();
            Client = new MuseumCreditsClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            Client.Changed += Notify;
        }

        private string ApiBase()
        {
            var loader = GetComponent<TourLoader>();
            return loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);
        }

        private void Notify() => Changed?.Invoke();

        private void OnDestroy()
        {
            Client?.Dispose();
            if (card != null) Destroy(card.gameObject);
            if (roll != null) Destroy(roll.gameObject);
            if (ownBackdrop != null) Destroy(ownBackdrop.gameObject);
            if (music != null) music.Fade = 1;
        }

        private void Update()
        {
            Connect();
            Client.Poll();
            float elapsed = Time.unscaledDeltaTime;
            if (!MuseumExperienceConfiguration.Finite(elapsed) || elapsed < 0) elapsed = 0;
            switch (Stage)
            {
                case EndingStage.Idle: Watch(elapsed); break;
                case EndingStage.Asking: StepQuestion(elapsed); break;
                default: StepEnding(elapsed); break;
            }
            StepSound(elapsed);
        }

        /// <summary>Counts the time in the last room and decides when to ask.</summary>
        private void Watch(float elapsed)
        {
            farewell.Refresh();
            bool inside = farewell.AtEnd && flow.Phase == VisitPhase.Exploring;
            if (inside && !wasInside) { Dwell = 0; askSoon = false; }
            if (!inside && wasInside) { Dwell = 0; declined = false; askSoon = false; }
            wasInside = inside;
            autoTour ??= GetComponent<MuseumAutoTour>();
            bool tourActive = autoTour != null && autoTour.Active;
            // The automatic tour that ends in the last room asks at once, once its last narration was heard.
            if (inside && tourWasActive && !tourActive && autoTour.Status == MuseumInterfaceText.Get("auto_tour_finished")) askSoon = true;
            tourWasActive = tourActive;
            DistanceToEnd = inside ? FarEnd() : float.PositiveInfinity;
            if (!inside) return;
            // The credits are asked for on arrival so the roll starts without waiting; a failure is retried quietly.
            if (Client.State == MuseumCreditsState.Idle
                || (Client.State == MuseumCreditsState.Failed || Client.State == MuseumCreditsState.Incompatible) && Time.realtimeSinceStartupAsDouble >= retryAt)
            {
                retryAt = Time.realtimeSinceStartupAsDouble + retrySeconds;
                Client.Load();
            }
            if (declined) return;
            if (Free(true)) Dwell += elapsed;
            bool atEdge = DistanceToEnd <= edgeDistance && Free(false);
            float needed = interaction.State == InteractionState.Exploration ? dwellSeconds : Mathf.Max(dwellSeconds, readingDwellSeconds);
            if (Client.Ready && (Dwell >= needed || atEdge || askSoon && Free(true))) Ask();
        }

        /// <summary>
        /// The visitor is free to be asked: walking or reading by a stand, not paused, examining a piece, talking to the
        /// guide, holding a piece or carried by the automatic tour. Counting the time also waits for a narration or a voice.
        /// </summary>
        private bool Free(bool counting)
        {
            var state = interaction.State;
            if (state != InteractionState.Exploration && state != InteractionState.PointFocus && state != InteractionState.ElementSelected) return false;
            if (GetComponent<MuseumModelGrab>()?.Held == true || GetComponent<MuseumWayfinding>()?.MenuOpen == true) return false;
            // The automatic tour is never interrupted: it asks by itself when it ends.
            if (autoTour != null && autoTour.Active) return false;
            if (!counting) return true;
            narration ??= GetComponent<MuseumNarration>();
            if (narration != null && narration.State == NarrationState.Playing) return false;
            return GetComponent<MuseumGuideVoice>()?.Speaking != true;
        }

        /// <summary>
        /// Distance from the eyes to the far end of the last room: the end of its long side that lies away from the room the
        /// route comes from.
        /// </summary>
        private float FarEnd()
        {
            var room = farewell.Room;
            var eyes = view != null ? view : body;
            if (room == null || eyes == null) return float.PositiveInfinity;
            var local = room.transform.InverseTransformPoint(eyes.position);
            bool alongX = room.HalfSize.x >= room.HalfSize.y;
            float half = alongX ? room.HalfSize.x : room.HalfSize.y;
            float here = alongX ? local.x : local.z;
            float sign = 1;
            if (farewell.PreviousRoom != null)
            {
                var from = room.transform.InverseTransformPoint(farewell.PreviousRoom.transform.position);
                sign = (alongX ? from.x : from.z) > 0 ? -1 : 1;
            }
            return Mathf.Max(0, half - here * sign);
        }

        /// <summary>Opens the question now. Tests and the automatic tour call it directly.</summary>
        public bool Ask()
        {
            Connect();
            if (Stage != EndingStage.Idle || !Client.Ready || !flow.Ask()) return false;
            Stage = EndingStage.Asking;
            stageTime = 0;
            // Nothing speaks over the question.
            narration ??= GetComponent<MuseumNarration>();
            pausedNarration = narration != null && narration.State == NarrationState.Playing;
            if (pausedNarration) narration.TogglePause();
            GetComponent<MuseumGuideVoice>()?.StopSpeech();
            ShowCard();
            Changed?.Invoke();
            return true;
        }

        /// <summary>"Seguir recorriendo": the visit goes on and the question waits until the visitor leaves and comes back.</summary>
        public void KeepExploring()
        {
            if (Stage != EndingStage.Asking) return;
            HideCard();
            Stage = EndingStage.Idle;
            declined = true;
            Dwell = 0;
            askSoon = false;
            flow.KeepExploring();
            if (pausedNarration && narration != null && narration.State == NarrationState.Paused) narration.TogglePause();
            pausedNarration = false;
            Changed?.Invoke();
        }

        /// <summary>"Sí, finalizar": the museum fades out and the credits follow by the fire.</summary>
        public void Finish()
        {
            if (Stage != EndingStage.Asking && Stage != EndingStage.Idle) return;
            if (!Client.Ready || !flow.BeginEnding()) return;
            HideCard();
            autoTour ??= GetComponent<MuseumAutoTour>();
            autoTour?.Stop();
            Stage = EndingStage.Closing;
            stageTime = 0;
            Darkness = 0;
            Changed?.Invoke();
        }

        private void StepQuestion(float elapsed)
        {
            stageTime += elapsed;
            if (card != null)
            {
                cardGroup.alpha = Mathf.Clamp01(stageTime / .35f);
                KeepFocus();
            }
            var input = MuseumInput.Current;
            if (input.Back.WasPressedThisFrame() || input.Pause.WasPressedThisFrame()) { KeepExploring(); return; }
            var step = input.Navigate.ReadValue<Vector2>();
            bool move = input.Next.WasPressedThisFrame() || input.Previous.WasPressedThisFrame()
                || Mathf.Abs(step.x) > .5f && !navigationHeld;
            navigationHeld = Mathf.Abs(step.x) > .5f;
            if (move && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(EventSystem.current.currentSelectedGameObject == yes.gameObject ? keep.gameObject : yes.gameObject);
        }
        private bool navigationHeld;

        /// <summary>The keyboard always has one of the two choices in focus, so Enter answers.</summary>
        private void KeepFocus()
        {
            var events = EventSystem.current;
            if (events == null) return;
            var selected = events.currentSelectedGameObject;
            if (selected != yes.gameObject && selected != keep.gameObject) events.SetSelectedGameObject(yes.gameObject);
        }

        private void StepEnding(float elapsed)
        {
            stageTime += elapsed;
            if (Stage == EndingStage.Closing)
            {
                // The museum goes dark and quiet; the fire is already there behind the dark when it lifts.
                Darkness = Mathf.SmoothStep(0, 1, stageTime / closeSeconds);
                musicFade = 1 - Darkness;
                if (stageTime < closeSeconds + holdSeconds) return;
                Stage = EndingStage.Credits;
                stageTime = 0;
                StartRoll();
                Changed?.Invoke();
                return;
            }
            if (Stage == EndingStage.Credits)
            {
                Darkness = 1 - Mathf.SmoothStep(0, 1, stageTime / revealSeconds);
                if (roll != null && !roll.Done) return;
                Stage = EndingStage.Leaving;
                stageTime = 0;
                Changed?.Invoke();
                return;
            }
            // The roll dissolves and the title menu rises over the same fire.
            Darkness = 0;
            if (roll != null) roll.Presence = 1 - Mathf.SmoothStep(0, 1, stageTime / leaveSeconds);
            if (stageTime < leaveSeconds) return;
            Return();
        }

        /// <summary>Skips what is left of the roll. Tests call it to reach the end at once.</summary>
        public void Skip()
        {
            if (Stage == EndingStage.Credits) { Darkness = 0; Stage = EndingStage.Leaving; stageTime = 0; Changed?.Invoke(); }
        }

        private void StartRoll()
        {
            var set = Backdrop;
            if (roll != null) Destroy(roll.gameObject);
            if (set.Root == null || Client.Credits == null) { roll = null; return; }
            // The roll stands where the title menu column stands: to the left of the fire, read from where the eyes are.
            var stage = flow.Entry != null && flow.Entry.StageCamera != null && flow.Entry.StageCamera.enabled ? flow.Entry.StageCamera : null;
            var eyes = MuseumInput.Current.Spatial && view != null ? view : stage != null ? stage.transform : set.CameraPose;
            roll = MuseumCreditsRoll.Create(set.Root, set.CameraPose, eyes, stage != null ? stage : viewCamera, Client.Credits, Skip);
        }

        /// <summary>The title menu over the fire; a new visit begins where the first one did.</summary>
        private void Return()
        {
            if (roll != null) { Destroy(roll.gameObject); roll = null; }
            Stage = EndingStage.Idle;
            Darkness = 0;
            Dwell = 0;
            declined = askSoon = wasInside = pausedNarration = false;
            if (startKnown && body != null)
            {
                if (desktop != null && desktop.enabled) desktop.PlaceAt(startPosition, startRotation);
                else
                {
                    var xr = FindAnyObjectByType<MuseumXrEntry>();
                    if (xr != null && xr.InSet) xr.ReplaceMuseumPose(startPosition, startRotation);
                    else body.SetPositionAndRotation(startPosition, startRotation);
                }
            }
            flow.FinishEnding();
            Changed?.Invoke();
        }

        private void ShowCard()
        {
            if (card == null) BuildCard();
            var eyes = view != null ? view : body;
            var forward = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(eyes.up, Vector3.up);
            forward = forward.sqrMagnitude < .01f ? Vector3.forward : forward.normalized;
            float distance = MuseumInput.Current.Spatial ? CardDistanceHeadset : CardDistance;
            // Never behind a wall: the card stands before whatever is closer than its usual distance.
            foreach (var hit in Physics.RaycastAll(eyes.position, forward, distance + .2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (body == null || !hit.transform.IsChildOf(body)) distance = Mathf.Min(distance, Mathf.Max(.6f, hit.distance - .2f));
            var position = eyes.position + forward * distance + Vector3.down * .06f;
            card.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            float scale = 1;
            if (!MuseumInput.Current.Spatial && viewCamera != null)
            {
                float viewWidth = 2 * distance * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad) * viewCamera.aspect;
                scale = Mathf.Min(CardScreenScale, CardMostOfView * viewWidth / (CardWidth * MuseumWorldUi.Millimetre));
            }
            card.transform.localScale = Vector3.one * MuseumWorldUi.Millimetre * scale;
            card.worldCamera = viewCamera;
            cardGroup.alpha = 0;
            card.gameObject.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(yes.gameObject);
        }

        private void HideCard()
        {
            if (card == null) return;
            if (EventSystem.current != null && (EventSystem.current.currentSelectedGameObject == yes.gameObject
                || EventSystem.current.currentSelectedGameObject == keep.gameObject)) EventSystem.current.SetSelectedGameObject(null);
            card.gameObject.SetActive(false);
        }

        private void BuildCard()
        {
            card = MuseumWorldUi.Canvas("EndingQuestion", transform, new Vector2(CardWidth, CardHeight), viewCamera);
            card.sortingOrder = 20;
            cardGroup = card.GetComponent<CanvasGroup>();
            var surface = MuseumWorldUi.Surface("Card", card.transform, MuseumWorldUi.Panel);
            MuseumWorldUi.Fill(surface.rectTransform);
            var rule = MuseumWorldUi.Surface("AccentRule", surface.transform, MuseumWorldUi.Accent);
            rule.rectTransform.anchorMin = new Vector2(.5f, 1);
            rule.rectTransform.anchorMax = new Vector2(.5f, 1);
            rule.rectTransform.sizeDelta = new Vector2(120, 4);
            rule.rectTransform.anchoredPosition = new Vector2(0, -44);
            var title = MuseumWorldUi.Label("Title", surface.transform, MuseumInterfaceText.Get("ending_title"), 40, MuseumWorldUi.Text, true);
            title.alignment = TextAlignmentOptions.Center;
            Place(title.rectTransform, new Vector2(30, -128), new Vector2(-30, -66));
            var question = MuseumWorldUi.Label("Question", surface.transform, MuseumInterfaceText.Get("ending_question"), 32, MuseumWorldUi.Muted);
            question.alignment = TextAlignmentOptions.Center;
            Place(question.rectTransform, new Vector2(30, -196), new Vector2(-30, -140));
            yes = MuseumWorldUi.Choice("Finish", surface.transform, MuseumInterfaceText.Get("ending_finish"), 28, Finish, true);
            keep = MuseumWorldUi.Choice("Keep", surface.transform, MuseumInterfaceText.Get("ending_keep"), 28, KeepExploring, false);
            foreach (var (choice, left) in new[] { (yes, true), (keep, false) })
            {
                var rect = (RectTransform)choice.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, 0);
                rect.sizeDelta = new Vector2(330, 84);
                rect.anchoredPosition = new Vector2(left ? -180 : 180, 48);
            }
            var horizontal = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = keep, selectOnLeft = keep };
            yes.navigation = horizontal;
            keep.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = yes, selectOnRight = yes };
            card.gameObject.SetActive(false);
        }

        /// <summary>Stretches a rect across its parent between two offsets from the top.</summary>
        private static void Place(RectTransform rect, Vector2 bottomLeft, Vector2 topRight)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = bottomLeft;
            rect.offsetMax = topRight;
        }

        /// <summary>The museum's music fades with the light; the fire crackles alone during the credits.</summary>
        private void StepSound(float elapsed)
        {
            music ??= FindAnyObjectByType<MuseumMusic>();
            bool ending = flow.Phase == VisitPhase.Ending;
            if (!ending) musicFade = Mathf.MoveTowards(musicFade, 1, elapsed / 2.5f);
            if (music != null) music.Fade = musicFade;
            bool burning = ending && (Stage == EndingStage.Credits || Stage == EndingStage.Leaving);
            if (burning && crackle == null) crackle = MuseumFireCrackle.Create(Backdrop.Root, MuseumEntryBackdrop.Focus, MuseumInput.Current.Spatial);
            if (crackle == null) return;
            float volume = .55f * (GetComponent<MuseumNarration>()?.Volume ?? .8f);
            crackle.volume = Mathf.MoveTowards(crackle.volume, burning ? volume : 0, elapsed / (burning ? revealSeconds : 2.5f));
            if (!burning && crackle.volume <= 0) { Destroy(crackle.gameObject); crackle = null; }
        }
    }
}
