using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The guide who waits by the hearth for the closing conversation: a large ball of yarn in the sayo's colours (the
    /// owner's choice of 22/10/2026: no human figure) floating gently over one end of the museum's own north bench by the
    /// fire, so the guide's screen unfolds beside it. From across the room an invitation floats near it, «Acércate a
    /// conversar», and the ball turns toward the visitor. Sitting down on a bench of the hearth (<see cref="MuseumSeating"/>)
    /// opens the conversation; the first time it begins the interview with the published questions. While the visitor
    /// sits there the conversation stays open, whichever bench they chose. The ball turns slowly while an answer is on its
    /// way and its wool swells softly while its voice is heard; it never glows, flickers or fades.
    /// </summary>
    [RequireComponent(typeof(MuseumGuide))]
    public sealed class MuseumHearthInterviewer : MonoBehaviour
    {
        /// <summary>Within this distance of the guide's place the invitation shows and the ball turns to the visitor.</summary>
        public const float InviteWithin = 11;
        /// <summary>The museum's own seats by the fire: wooden benches under woven blankets (delivered with the hearth room).</summary>
        public static readonly string[] HearthSeats = { "Fogon_Manta_Norte", "Fogon_Manta_Este", "Fogon_Manta_Oeste" };
        private MuseumGuide guide;
        private MuseumVisitFlow flow;
        private MuseumGuideVoice voice;
        private MuseumHearthQuiz quiz;
        private MuseumSeating seating;
        private TourRuntime runtime;
        private Transform visitor;
        private Camera head;
        private Transform stand;
        private Canvas invitation;
        private CanvasGroup invitationGroup;
        private PointAnchor builtFor;
        private bool quizOffered;

        public GuideFigure Figure { get; private set; }
        /// <summary>The first conversation of the visit begins with the questions about the visit (the closing interview).</summary>
        public bool BeginsInterview { get; set; } = true;
        /// <summary>The invitation is on show.</summary>
        public bool Inviting => invitation != null && invitation.gameObject.activeSelf && invitationGroup.alpha > .5f;
        public string InvitationText { get; private set; } = "";
        /// <summary>Where the guide's place is, on the floor.</summary>
        public Vector3 Place => stand != null ? stand.position : transform.position;
        /// <summary>The visitor sits on a bench of the hearth.</summary>
        public bool VisitorSeated => seating != null && seating.Seated && seating.Current.Hearth;

        private void Start()
        {
            guide = GetComponent<MuseumGuide>();
            flow = GetComponent<MuseumVisitFlow>();
            voice = GetComponent<MuseumGuideVoice>();
            quiz = GetComponent<MuseumHearthQuiz>();
            runtime = GetComponent<TourRuntime>();
            seating = GetComponent<MuseumSeating>();
            if (seating != null)
            {
                seating.SatDown += OnSatDown;
                seating.HearthFocus = () => Figure != null ? Figure.Head.position : (Vector3?)null;
            }
            // Seated in the hearth, the conversation stays open whichever bench the visitor chose.
            guide.KeepsInRange = () => VisitorSeated;
            var desktop = FindAnyObjectByType<DesktopVisitorController>(FindObjectsInactive.Include);
            visitor = desktop != null ? desktop.transform : null;
        }

        private void OnDestroy()
        {
            if (seating != null) seating.SatDown -= OnSatDown;
            if (stand != null) Destroy(stand.gameObject);
        }

        /// <summary>The guide's place: its mat or seated placeholder in the museum model, else a step beyond the anchor.</summary>
        public static Vector3 PlaceFor(PointAnchor anchor)
        {
            var pieces = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var mat = pieces.FirstOrDefault(piece => piece.name == "Fogon_Estera_Guia");
            var seated = pieces.FirstOrDefault(piece => piece.name == MuseumCompanion.BodyPiece);
            Vector3 spot;
            if (mat != null) spot = mat.bounds.center;
            else if (seated != null) spot = seated.bounds.center;
            else spot = anchor.transform.position + anchor.transform.forward * 1.1f;
            // The floor where the visitor stands to talk: the lowest surface under the guide's anchor, since stools,
            // markers or the visitor may stand there and under the mat stands the museum's bench, which is no floor.
            float floor = anchor.transform.position.y;
            var hits = Physics.RaycastAll(anchor.transform.position + Vector3.up * .5f, Vector3.down, 1.1f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore).Where(hit => hit.collider.GetComponentInParent<DesktopVisitorController>() == null).ToArray();
            if (hits.Length > 0) floor = hits.Min(hit => hit.point.y);
            return new Vector3(spot.x, floor, spot.z);
        }

        private void Build(PointAnchor anchor)
        {
            if (stand != null) Destroy(stand.gameObject);
            builtFor = anchor;
            MuseumCompanion.HidePlaceholder(new List<Renderer>());
            var place = PlaceFor(anchor);
            stand = new GameObject("HearthInterviewer").transform;
            stand.position = place;
            var seat = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(piece => HearthSeats.Contains(piece.name))
                .OrderBy(piece => (Flat(piece.bounds.center) - Flat(place)).sqrMagnitude).FirstOrDefault();
            if (seat != null && (Flat(seat.bounds.center) - Flat(place)).magnitude < 2.5f)
            {
                // Over one end of the museum's bench: the guide's screen unfolds at its middle, in front of it.
                var bounds = seat.bounds;
                var along = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                float reach = Mathf.Max(bounds.size.x, bounds.size.z) * .5f;
                stand.position = new Vector3(bounds.center.x, place.y, bounds.center.z) + along * Mathf.Clamp(reach - .35f, 0, .75f);
            }
            var front = Flat(anchor.transform.position - stand.position);
            stand.rotation = Quaternion.LookRotation(front.sqrMagnitude > .01f ? front.normalized : -anchor.transform.forward, Vector3.up);
            Figure = GuideFigure.Build(stand, GuideFigureSettings.Interviewer);
            BuildInvitation();
            Debug.Log("[MuseumHearthInterviewer] ball at " + stand.position.ToString("F2") + (seat != null ? " by " + seat.name : ""));
        }

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);

        private void BuildInvitation()
        {
            invitation = MuseumWorldUi.Canvas("Invitation", stand, new Vector2(620, 150), head);
            invitationGroup = invitation.GetComponent<CanvasGroup>();
            invitationGroup.interactable = false;
            invitationGroup.blocksRaycasts = false;
            var surface = MuseumWorldUi.Surface("Panel", invitation.transform, MuseumWorldUi.Panel);
            MuseumWorldUi.Fill(surface.rectTransform);
            var rule = MuseumWorldUi.Surface("Rule", surface.transform, MuseumWorldUi.Accent);
            rule.rectTransform.anchorMin = Vector2.zero;
            rule.rectTransform.anchorMax = new Vector2(1, 0);
            rule.rectTransform.pivot = new Vector2(.5f, 0);
            rule.rectTransform.sizeDelta = new Vector2(0, 6);
            InvitationText = MuseumInterfaceText.Get("interviewer_invitation");
            var label = MuseumWorldUi.Label("Text", surface.transform, InvitationText, 58, MuseumWorldUi.Text, true);
            label.alignment = TextAlignmentOptions.Center;
            MuseumWorldUi.Fill(label.rectTransform, new Vector2(20, 12));
            invitationGroup.alpha = 0;
            invitation.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (guide == null || runtime == null) return;
            var anchor = runtime.FindAnchor(runtime.Guide?.key);
            if (anchor == null || !guide.Present)
            {
                if (stand != null) { Destroy(stand.gameObject); stand = null; Figure = null; builtFor = null; }
                return;
            }
            if (builtFor != anchor || stand == null) Build(anchor);
            if (visitor == null) return;
            if (head == null || !head.isActiveAndEnabled)
            {
                head = visitor.GetComponentsInChildren<Camera>().FirstOrDefault(camera => camera.isActiveAndEnabled);
                if (head == null) head = Camera.main;
                if (invitation != null) invitation.worldCamera = head;
            }
            var eyes = head != null ? head.transform.position : visitor.position + Vector3.up * 1.65f;
            var offset = eyes - stand.position;
            offset.y = 0;
            bool exploring = flow == null || flow.Phase == VisitPhase.Exploring;
            bool near = exploring && offset.magnitude <= InviteWithin;
            bool open = guide.Open;
            Pose(eyes, near, open);
            // The invitation: from across the room until the visitor sits down or the conversation opens.
            bool invite = near && !open && !VisitorSeated;
            float alpha = Mathf.MoveTowards(invitationGroup.alpha, invite ? 1 : 0, Time.unscaledDeltaTime * 3);
            invitationGroup.alpha = alpha;
            invitation.gameObject.SetActive(alpha > 0);
            if (alpha <= 0) return;
            // A little in front of the ball, toward the visitor, above it.
            invitation.transform.localPosition = Vector3.forward * .6f + Vector3.up * (Figure.Height + .35f);
            invitation.transform.rotation = MuseumWorldUi.Facing(invitation.transform.position, eyes);
        }

        /// <summary>Sitting down in the hearth opens the conversation; the first time, the interview begins.</summary>
        private void OnSatDown(SeatSpot seat)
        {
            if (seat == null || !seat.Hearth || guide == null || !guide.Present) return;
            if (!guide.Ask()) return;
            if (quiz != null && !quizOffered && BeginsInterview) StartQuizWhenReady();
        }

        private void StartQuizWhenReady()
        {
            if (quiz.Available) { quizOffered = true; quiz.Begin(); return; }
            quiz.Changed -= OnQuizChanged;
            quiz.Changed += OnQuizChanged;
        }

        private void OnQuizChanged()
        {
            if (quizOffered || !BeginsInterview || !guide.Open || !quiz.Available || quiz.Active || quiz.Done) return;
            quizOffered = true;
            quiz.Changed -= OnQuizChanged;
            quiz.Begin();
        }

        private void Pose(Vector3 eyes, bool near, bool open)
        {
            if (Figure == null) return;
            Figure.LookAt = near || open ? eyes : (Vector3?)null;
            bool thinking = guide.Client.Busy || quiz != null && quiz.Client.AnswerState == GuideQuizAnswerState.Sending
                || voice != null && voice.Stage == GuideVoiceStage.Transcribing;
            bool listening = voice != null && voice.Stage == GuideVoiceStage.Recording || open;
            Figure.Gesture = voice != null && voice.Speaking ? GuideGesture.Speak
                : thinking ? GuideGesture.Think
                : listening ? GuideGesture.Listen
                : GuideGesture.Idle;
        }
    }
}
