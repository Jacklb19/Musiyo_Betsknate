using System;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A short tutorial on a first visit, learnt by doing (owner, 28/10/2026): in the arrival, once the companion's welcome
    /// has been read, the colibrí proposes one thing at a time in its own speech bubble (turn, move, open the menu, come up
    /// to a stand, take a piece, call it) and each one is done when the visitor does it, in any order. The texts name the
    /// real controls of the device in use: the Quest's controllers, the hands alone, or a keyboard and mouse. It never
    /// blocks the visit: it can be ignored, skipped by holding the back control or from the pause menu, and repeated from
    /// there. That it was finished or skipped is remembered in the visitor's preferences; one left halfway comes back. It opens no panel of its own: its bubble is a twin of
    /// the companion's, at the same place, while the companion's own remarks keep quiet.
    /// </summary>
    public sealed class MuseumFirstVisitTutorial : MonoBehaviour
    {
        /// <summary>
        /// True while the tutorial speaks, its last word included. Other in-world notices of the arrival (such as a room's
        /// name) can wait for it; <see cref="ActiveChanged"/> tells when it starts and ends.
        /// </summary>
        public static bool Active { get; private set; }
        public static event Action<bool> ActiveChanged;
        /// <summary>
        /// True only while the tutorial is about to start: not finished or skipped yet, the visitor exploring with the
        /// companion in the arrival, and its latest start not yet past. Anywhere else it is false, so a room's name held back
        /// for the tutorial is never held for the rest of the visit.
        /// </summary>
        public static bool Pending { get; private set; }

        private MuseumTutorialSettings settings;
        private MuseumTutorialProgress progress;
        private MuseumMenuHud menu;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumCompanion companion;
        private MuseumModelGrab grab;
        private MuseumNarration narration;
        private DesktopVisitorController visitor;
        private Camera head;
        private RectTransform bubble;
        private CanvasGroup bubbleGroup;
        private TMP_Text bubbleText;
        private Vector3 startFloor;
        private Vector3 lastGaze;
        private bool companionWasMuted;
        private float arrivalSince = float.NaN;
        private float welcomedAt = float.NaN;
        private float stepShownAt;
        private float doneUntil;
        private float endUntil;
        private float skipHeld;
        private string closing = "";
        private TutorialStepSettings shown;
        private MuseumRoomTitle roomTitle;
        private bool holdingTitle;

        /// <summary>The tutorial is running: proposing steps or saying its last word.</summary>
        public bool Running { get; private set; }
        public MuseumTutorialProgress Progress => progress;
        /// <summary>The text in the bubble now, empty when it rests.</summary>
        public string Text => bubbleText != null && bubbleGroup != null && bubbleGroup.alpha > 0 ? bubbleText.text : "";
        public bool Showing => bubbleGroup != null && bubbleGroup.alpha > .5f;
        public RectTransform Bubble => bubble;

        /// <summary>The tutorial of a visit, added once next to the visit's interaction.</summary>
        public static MuseumFirstVisitTutorial Ensure(MuseumMenuHud menu, MuseumInteraction interaction)
        {
            if (interaction == null) return null;
            var tutorial = interaction.GetComponent<MuseumFirstVisitTutorial>();
            if (tutorial == null) tutorial = interaction.gameObject.AddComponent<MuseumFirstVisitTutorial>();
            tutorial.menu = menu;
            tutorial.interaction = interaction;
            return tutorial;
        }

        private void Awake() => settings = MuseumTutorialSettings.Current;

        private void OnDisable()
        {
            if (Running) End(TutorialEnding.LeftHalfway);
            Pending = false;
            HoldRoomTitle(false);
        }

        /// <summary>The room's name waits while the tutorial is about to speak or speaking, then comes out right after it (owner, 30/10/2026).</summary>
        private void LateUpdate() => HoldRoomTitle(Active || Pending);

        private void HoldRoomTitle(bool hold)
        {
            if (hold == holdingTitle) return;
            if (roomTitle == null)
            {
                var hud = FindAnyObjectByType<MuseumHud>();
                if (hud != null) roomTitle = hud.RoomTitle;
            }
            if (roomTitle == null) return;
            if (hold) roomTitle.Hold(this);
            else roomTitle.Release(this);
            holdingTitle = hold;
        }

        private void Update()
        {
            if (!settings.Enabled || interaction == null) return;
            flow ??= interaction.GetComponent<MuseumVisitFlow>();
            if (flow == null) return;
            if (companion == null) companion = FindAnyObjectByType<MuseumCompanion>();
            if (companion == null || !companion.isActiveAndEnabled) { Pending = false; return; }
            bool exploring = flow.Phase == VisitPhase.Exploring;
            bool inArrival = exploring && companion.Placed && companion.Zone == settings.StartRoom;
            if (!inArrival) arrivalSince = float.NaN;
            else if (float.IsNaN(arrivalSince)) arrivalSince = Time.unscaledTime;
            var preferences = menu != null ? menu.Preferences : null;
            bool due = !Running && preferences != null && !preferences.TutorialDone && inArrival;
            // A second of grace past the latest start, in case the bubble could not be taken: it is never pending for long.
            Pending = due && Time.unscaledTime - arrivalSince <= settings.StartAtLatest + 1;
            if (!Running)
            {
                if (due && ReadyToStart()) Begin();
                return;
            }
            if (!exploring && flow.Phase != VisitPhase.Entering) { End(TutorialEnding.LeftHalfway); return; }
            Follow();
            Observe();
            CheckSkip();
            Present();
        }

        /// <summary>
        /// A first visit begins in the arrival once the companion is there and its welcome has been read, and at the latest
        /// a while after coming into the arrival.
        /// </summary>
        private bool ReadyToStart()
        {
            if (!companion.Placed || companion.Zone != settings.StartRoom || bubble == null && !TakeBubble()) return false;
            float now = Time.unscaledTime;
            var remarks = companion.Remarks;
            bool welcomed = remarks != null && remarks.Said.Contains(MuseumCompanionRemarks.WelcomeId) && remarks.Current == null;
            if (welcomed && float.IsNaN(welcomedAt)) welcomedAt = now;
            return welcomed && now - welcomedAt >= settings.StartAfterWelcome || now - arrivalSince >= settings.StartAtLatest;
        }

        /// <summary>
        /// Starts the tutorial from its first step, on a first visit or when asked again from the menu. It counts as done
        /// only once it ends or is skipped (owner, 30/10/2026): a tutorial left halfway starts again the next time the
        /// visitor comes into the arrival.
        /// </summary>
        public void Begin(bool fromMenu = false)
        {
            if (!settings.Enabled || companion == null && (companion = FindAnyObjectByType<MuseumCompanion>()) == null) return;
            if (bubble == null && !TakeBubble()) return;
            progress = new MuseumTutorialProgress(settings);
            if (fromMenu) progress.MarkDone(TutorialStep.Menu);
            visitor ??= FindAnyObjectByType<DesktopVisitorController>();
            grab ??= interaction.GetComponent<MuseumModelGrab>();
            startFloor = visitor != null ? visitor.transform.position : Vector3.zero;
            lastGaze = Gaze();
            companionWasMuted = companion.Muted;
            companion.Muted = true;
            shown = null;
            closing = "";
            doneUntil = endUntil = 0;
            skipHeld = 0;
            Running = true;
            Pending = false;
            SetActive(true);
        }

        /// <summary>Stops the tutorial now; it can be repeated from the pause menu.</summary>
        public void Skip()
        {
            if (!Running) return;
            End(TutorialEnding.Skipped);
        }

        /// <summary>
        /// Ends the tutorial. Finished or skipped, it is remembered now and says its last word a moment; left halfway, it
        /// goes quiet and is not remembered, so it starts again in the arrival.
        /// </summary>
        private void End(TutorialEnding ending)
        {
            if (MuseumTutorialProgress.Remembered(ending))
            {
                var preferences = menu != null ? menu.Preferences : null;
                if (preferences != null && !preferences.TutorialDone) { preferences.SetTutorialDone(true); preferences.Save(); }
                closing = MuseumInterfaceText.Get(ending == TutorialEnding.Finished ? "tutorial_end" : "tutorial_skipped");
                endUntil = Time.unscaledTime + settings.EndSeconds;
                progress = null;
                return;
            }
            Close();
        }

        /// <summary>The bubble goes, the companion speaks again and others hear that the tutorial is over.</summary>
        private void Close()
        {
            Running = false;
            closing = "";
            if (companion != null) companion.Muted = companionWasMuted;
            if (bubbleGroup != null) { bubbleGroup.alpha = 0; bubble.gameObject.SetActive(false); }
            SetActive(false);
        }

        private static void SetActive(bool value)
        {
            if (Active == value) return;
            Active = value;
            ActiveChanged?.Invoke(value);
        }

        /// <summary>A twin of the companion's speech bubble, at the same place and with the same look, for the tutorial's words.</summary>
        private bool TakeBubble()
        {
            var original = companion.transform.Find("Remark") as RectTransform;
            if (original == null) return false;
            var copy = Instantiate(original.gameObject, original.parent, false);
            copy.name = "TutorialRemark";
            bubble = (RectTransform)copy.transform;
            bubbleGroup = copy.GetComponent<CanvasGroup>();
            bubbleText = copy.GetComponentInChildren<TMP_Text>(true);
            // Only the tutorial's own lines are set here, so the smaller line of the skip hint may use markup.
            bubbleText.richText = true;
            bubbleGroup.alpha = 0;
            copy.SetActive(false);
            return bubbleGroup != null && bubbleText != null;
        }

        /// <summary>The tutorial's bubble stays where the companion's own would be and turns toward the visitor.</summary>
        private void Follow()
        {
            var original = companion.transform.Find("Remark");
            if (original != null) bubble.localPosition = original.localPosition;
            head = head != null && head.isActiveAndEnabled ? head : visitor != null ? visitor.GetComponentInChildren<Camera>() : null;
            if (head == null) return;
            var toEyes = head.transform.position - bubble.position;
            toEyes.y = 0;
            if (toEyes.sqrMagnitude > 1e-4f) bubble.rotation = Quaternion.LookRotation(-toEyes.normalized, Vector3.up);
        }

        private void Observe()
        {
            if (progress == null) return;
            var gaze = Gaze();
            float turned = Vector3.SignedAngle(lastGaze, gaze, Vector3.up);
            lastGaze = gaze;
            float moved = visitor != null ? CompanionTrail.Planar(visitor.transform.position - startFloor) : 0;
            var state = interaction.State;
            bool pointOpen = interaction.ActivePoint != null && state != InteractionState.Exploration && state != InteractionState.Paused
                && state != InteractionState.GuideQuestion;
            var input = MuseumInput.Current;
            bool called = companion.Voice != null && companion.Voice.Conversing || input.AskCompanion.WasPressedThisFrame();
            var completed = progress.Observe(new TutorialSignals(turned, moved, state == InteractionState.Paused, pointOpen,
                grab != null && grab.Held, called));
            if (shown != null && completed.Contains(MuseumTutorialSettings.Ids[shown.Id])) doneUntil = Time.unscaledTime + settings.DoneSeconds;
            if (progress.Finished && doneUntil <= Time.unscaledTime) End(TutorialEnding.Finished);
        }

        /// <summary>Holding the back control while walking skips the tutorial.</summary>
        private void CheckSkip()
        {
            if (progress == null || interaction.State != InteractionState.Exploration) { skipHeld = 0; return; }
            skipHeld = MuseumInput.Current.Back.IsPressed() ? skipHeld + Time.unscaledDeltaTime : 0;
            if (skipHeld >= settings.SkipHoldSeconds) { skipHeld = 0; Skip(); }
        }

        /// <summary>
        /// The current step shows a while, rests, and comes back now and then until it is done; a done step is acknowledged
        /// briefly. The bubble keeps quiet over a menu, a spoken answer, a narration and the companion's word at the hearth.
        /// </summary>
        private void Present()
        {
            float now = Time.unscaledTime;
            string text = "";
            if (progress == null)
            {
                if (now >= endUntil) { Close(); return; }
                text = closing;
            }
            else if (now < doneUntil) text = MuseumInterfaceText.Get("tutorial_step_done");
            else
            {
                var step = progress.Current;
                if (step != shown) { shown = step; stepShownAt = now; }
                float since = now - stepShownAt;
                bool resting = since > settings.ShowSeconds && (since - settings.ShowSeconds) % settings.RemindSeconds > settings.ShowSeconds * .3f;
                if (step != null && !resting) text = StepText(step);
            }
            narration ??= FindAnyObjectByType<MuseumNarration>();
            bool quiet = interaction.State == InteractionState.Paused || companion.Waiting || !companion.Visible
                || companion.Voice != null && companion.Voice.Conversing
                || narration != null && narration.State == NarrationState.Playing;
            if (!string.IsNullOrEmpty(text)) bubbleText.text = text;
            float wanted = !quiet && !string.IsNullOrEmpty(text) ? 1 : 0;
            bubbleGroup.alpha = Mathf.MoveTowards(bubbleGroup.alpha, wanted, Time.unscaledDeltaTime * 4);
            bubble.gameObject.SetActive(bubbleGroup.alpha > 0);
        }

        /// <summary>
        /// A step's words for the device in use, with the step count above and how to skip below: the hands alone get the
        /// words that send them to the controllers for a step that needs them.
        /// </summary>
        public string StepText(TutorialStepSettings step)
        {
            string key = step.TextKey;
            if (step.NeedsControllers && HandsOnly && MuseumInterfaceText.Current.Find(key + "_hands") != null) key += "_hands";
            string body = MuseumInterfaceText.Get(key);
            string count = MuseumInterfaceText.Format("tutorial_progress", progress.CurrentNumber, progress.Steps.Count);
            string hint = MuseumInterfaceText.Get("tutorial_skip_hint");
            return "<size=72%>" + count + "</size>\n" + body + "\n<size=68%>" + hint + "</size>";
        }

        /// <summary>On a headset with neither controller in hand: the hands alone.</summary>
        public static bool HandsOnly
        {
            get
            {
                var input = MuseumInput.Current;
                return input.Spatial && !input.LeftTracked.IsPressed() && !input.RightTracked.IsPressed();
            }
        }

        private Vector3 Gaze()
        {
            head = head != null && head.isActiveAndEnabled ? head : visitor != null ? visitor.GetComponentInChildren<Camera>() : null;
            var forward = head != null ? head.transform.forward : visitor != null ? visitor.transform.forward : Vector3.forward;
            forward.y = 0;
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        }
    }
}
