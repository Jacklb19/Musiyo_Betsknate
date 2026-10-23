using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The guide who waits by the hearth for the closing conversation: the woven figure (D5) seated on the museum's own
    /// bench by the fire nearest the guide's mat (a plank bench where there is none), toward one end so the guide's screen
    /// unfolds beside it, with a cabuya jigra at its side and an empty totuma on the bench (research A5.1–A5.2), turned
    /// toward the spot where visitors stand to talk; never before served food. From
    /// across the room an invitation floats above it, «Acércate a conversar», and it turns its head to the visitor;
    /// walking up to it opens the conversation by itself, as a reading stand unfolds its screen, so nobody can pass it by
    /// unknowingly. During the conversation it looks at the visitor, thinks while an answer is on its way and nods while
    /// its voice is heard. It moves nothing but its head.
    /// </summary>
    [RequireComponent(typeof(MuseumGuide))]
    public sealed class MuseumHearthInterviewer : MonoBehaviour
    {
        /// <summary>Within this distance of the guide's place the invitation shows and the figure turns to the visitor.</summary>
        public const float InviteWithin = 11;
        public const string WoodSource = "Props/Banco_Tripode";
        private const float BenchLength = 1.2f, BenchDepth = .32f, BenchHeight = .43f;
        private MuseumGuide guide;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumGuideVoice voice;
        private MuseumHearthQuiz quiz;
        private TourRuntime runtime;
        private Transform visitor;
        private Camera head;
        private Transform stand;
        private Canvas invitation;
        private CanvasGroup invitationGroup;
        private PointAnchor builtFor;
        private bool openedThisApproach;
        private bool quizOffered;
        private readonly List<Material> materials = new List<Material>();

        public GuideFigure Figure { get; private set; }
        /// <summary>The bench the guide sits on: the museum's own seat by the fire, or a plank bench where there is none.</summary>
        public Transform Bench { get; private set; }
        /// <summary>The guide's place, holding its figure, totuma and jigra.</summary>
        public Transform Stand => stand;
        /// <summary>The first conversation of the visit begins with the questions about the visit (the closing interview).</summary>
        public bool BeginsInterview { get; set; } = true;
        /// <summary>The invitation is on show.</summary>
        public bool Inviting => invitation != null && invitation.gameObject.activeSelf && invitationGroup.alpha > .5f;
        public string InvitationText { get; private set; } = "";
        /// <summary>Where the guide's place is, on the floor.</summary>
        public Vector3 Place => stand != null ? stand.position : transform.position;

        private void Start()
        {
            guide = GetComponent<MuseumGuide>();
            interaction = GetComponent<MuseumInteraction>();
            flow = GetComponent<MuseumVisitFlow>();
            voice = GetComponent<MuseumGuideVoice>();
            quiz = GetComponent<MuseumHearthQuiz>();
            runtime = GetComponent<TourRuntime>();
            var desktop = FindAnyObjectByType<DesktopVisitorController>(FindObjectsInactive.Include);
            visitor = desktop != null ? desktop.transform : null;
        }

        private void OnDestroy()
        {
            if (stand != null) Destroy(stand.gameObject);
            foreach (var material in materials) if (material != null) Destroy(material);
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

        /// <summary>The museum's own seats by the fire: wooden benches under woven blankets (delivered with the hearth room).</summary>
        public static readonly string[] HearthSeats = { "Fogon_Manta_Norte", "Fogon_Manta_Este", "Fogon_Manta_Oeste" };

        private void Build(PointAnchor anchor)
        {
            if (stand != null) Destroy(stand.gameObject);
            builtFor = anchor;
            MuseumCompanion.HidePlaceholder(new List<Renderer>());
            var place = PlaceFor(anchor);
            stand = new GameObject("HearthInterviewer").transform;
            var seat = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(piece => HearthSeats.Contains(piece.name))
                .OrderBy(piece => (Flat(piece.bounds.center) - Flat(place)).sqrMagnitude).FirstOrDefault();
            bool museumSeat = seat != null && (Flat(seat.bounds.center) - Flat(place)).magnitude < 2.5f
                && seat.bounds.max.y - place.y > .3f && seat.bounds.max.y - place.y < .65f;
            Debug.Log("[MuseumHearthInterviewer] place " + place.ToString("F2") + (seat == null ? ", no museum seat"
                : ", seat " + seat.name + " at " + seat.bounds.center.ToString("F2") + " top " + seat.bounds.max.y.ToString("F2"))
                + (museumSeat ? ": seated on it" : ": plank bench"));
            if (museumSeat)
            {
                // On the museum's bench, toward one end: the guide's screen unfolds at the middle, in front of it.
                var bounds = seat.bounds;
                var along = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                float reach = Mathf.Max(bounds.size.x, bounds.size.z) * .5f;
                var centre = new Vector3(bounds.center.x, place.y, bounds.center.z);
                var front = Vector3.Cross(Vector3.up, along);
                if (Vector3.Dot(anchor.transform.position - centre, front) < 0) front = -front;
                stand.position = centre + along * Mathf.Clamp(reach - .35f, 0, .75f);
                stand.rotation = Quaternion.LookRotation(front, Vector3.up);
                Figure = GuideFigure.Build(stand, GuideFigureSettings.Interviewer);
                float top = bounds.max.y - place.y;
                Figure.transform.localPosition = new Vector3(0, top - GuideFigure.SeatHeight, 0);
                Bench = seat.transform;
                AddBelongings(stand, top, .5f, -.45f);
            }
            else
            {
                stand.position = place;
                var toVisitor = anchor.transform.position - place;
                toVisitor.y = 0;
                stand.rotation = Quaternion.LookRotation(toVisitor.sqrMagnitude > .01f ? toVisitor.normalized : -anchor.transform.forward, Vector3.up);
                BuildBench();
                Figure = GuideFigure.Build(stand, GuideFigureSettings.Interviewer);
                Figure.transform.localPosition = new Vector3(-.3f, BenchHeight - GuideFigure.SeatHeight, .02f);
                AddBelongings(stand, BenchHeight, .02f, -.95f);
            }
            BuildInvitation();
        }

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);

        /// <summary>
        /// A plank bench on two boards, of the museum stools' wood, where the hearth room has no seat of its own near the
        /// guide's place.
        /// </summary>
        private void BuildBench()
        {
            var bench = new GameObject("Bench").transform;
            bench.SetParent(stand, false);
            Bench = bench;
            var woodSource = Resources.Load<GameObject>(WoodSource);
            var woodRenderer = woodSource != null ? woodSource.GetComponentInChildren<Renderer>() : null;
            var wood = woodRenderer != null ? woodRenderer.sharedMaterial : null;
            if (wood == null) wood = Tone("BenchWood", "#6B4A30");
            void Board(string name, Vector3 centre, Vector3 size)
            {
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = name;
                Destroy(board.GetComponent<Collider>());
                board.transform.SetParent(bench, false);
                board.transform.localPosition = centre;
                board.transform.localScale = size;
                board.GetComponent<Renderer>().sharedMaterial = wood;
            }
            Board("Seat", new Vector3(0, BenchHeight - .025f, 0), new Vector3(BenchLength, .05f, BenchDepth));
            Board("LeftBoard", new Vector3(-BenchLength * .4f, (BenchHeight - .05f) * .5f, 0), new Vector3(.05f, BenchHeight - .05f, BenchDepth * .9f));
            Board("RightBoard", new Vector3(BenchLength * .4f, (BenchHeight - .05f) * .5f, 0), new Vector3(.05f, BenchHeight - .05f, BenchDepth * .9f));
            var solid = bench.gameObject.AddComponent<BoxCollider>();
            solid.center = new Vector3(0, BenchHeight * .5f, 0);
            solid.size = new Vector3(BenchLength, BenchHeight, BenchDepth);
        }

        /// <summary>
        /// The guide's belongings: an empty totuma on the bench beside it, whose dark inside shows it holds nothing, and a
        /// cabuya jigra, a woven bag, on the floor at its other side. Offsets run sideways from the guide's place.
        /// </summary>
        private void AddBelongings(Transform holder, float seatTop, float totumaOffset, float jigraOffset)
        {
            var gourd = Tone("Totuma", "#7A5232");
            var hollow = Tone("TotumaInside", "#3A2617");
            var totuma = new GameObject("Totuma", typeof(MeshFilter), typeof(MeshRenderer));
            totuma.transform.SetParent(holder, false);
            totuma.transform.localPosition = new Vector3(totumaOffset, seatTop, 0);
            totuma.GetComponent<MeshFilter>().sharedMesh = GuideFigure.Lathe(new[] { new[] { (0f, 0f), (.012f, .045f), (.04f, .07f), (.065f, .078f) } },
                new[] { 1f }, Vector3.zero);
            totuma.GetComponent<MeshRenderer>().sharedMaterial = gourd;
            var inside = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            inside.name = "Inside";
            Destroy(inside.GetComponent<Collider>());
            inside.transform.SetParent(totuma.transform, false);
            inside.transform.localPosition = new Vector3(0, .055f, 0);
            inside.transform.localScale = new Vector3(.15f, .002f, .15f);
            inside.GetComponent<Renderer>().sharedMaterial = hollow;

            var fibre = Tone("Jigra", "#BFA67A");
            var jigra = new GameObject("Jigra", typeof(MeshFilter), typeof(MeshRenderer));
            jigra.transform.SetParent(holder, false);
            jigra.transform.localPosition = new Vector3(jigraOffset, 0, .3f);
            jigra.GetComponent<MeshFilter>().sharedMesh = GuideFigure.Lathe(new[] { new[] { (0f, 0f), (.02f, .11f), (.16f, .14f), (.28f, .12f), (.34f, .08f), (.36f, 0f) } },
                new[] { .7f }, Vector3.zero);
            jigra.GetComponent<MeshRenderer>().sharedMaterial = fibre;
        }

        private Material Tone(string name, string color)
        {
            var authored = Resources.Load<Material>(GuideFigure.MaterialResource);
            var material = authored != null ? new Material(authored) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            var value = MuseumExperienceConfiguration.ColorValue(color);
            material.SetColor("_BaseColor", value);
            material.color = value;
            materials.Add(material);
            return material;
        }

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

        private float InvitationHeight => Figure.Height + .3f;

        private void Update()
        {
            if (guide == null || runtime == null) return;
            var anchor = runtime.FindAnchor(runtime.Guide?.key);
            if (anchor == null || !guide.Present)
            {
                if (stand != null) { Destroy(stand.gameObject); stand = null; Figure = null; Bench = null; builtFor = null; }
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
            OpenOnApproach();
            Pose(eyes, near, open);
            // The invitation: from across the room until the conversation opens.
            bool invite = near && !open && !guide.InRange;
            float alpha = Mathf.MoveTowards(invitationGroup.alpha, invite ? 1 : 0, Time.unscaledDeltaTime * 3);
            invitationGroup.alpha = alpha;
            invitation.gameObject.SetActive(alpha > 0);
            if (alpha <= 0) return;
            // A little in front of the guide, toward the visitor, so the cobs hanging over the fire never hide it.
            invitation.transform.localPosition = Figure.transform.localPosition + Vector3.forward * .7f + Vector3.up * InvitationHeight;
            invitation.transform.rotation = MuseumWorldUi.Facing(invitation.transform.position, eyes);
        }

        /// <summary>Reaching the guide opens the conversation once per approach; walking away and back opens it again.</summary>
        private void OpenOnApproach()
        {
            if (!guide.InRange) { openedThisApproach = false; return; }
            if (openedThisApproach || interaction.State != InteractionState.Exploration) return;
            openedThisApproach = true;
            if (!guide.Ask()) return;
            // The conversation about the visit begins at once the first time; later the visitor chooses to begin it again.
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
            bool listening = voice != null && voice.Stage == GuideVoiceStage.Recording;
            Figure.Gesture = voice != null && voice.Speaking ? GuideGesture.Speak
                : listening ? GuideGesture.Listen
                : thinking ? GuideGesture.Think
                : GuideGesture.Idle;
        }
    }
}
