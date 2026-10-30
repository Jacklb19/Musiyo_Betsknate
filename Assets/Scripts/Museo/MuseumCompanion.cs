using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The guide's figure going through the visit with the visitor. It keeps itself in sight (owner, 28/10/2026): with the
    /// visitor standing, once it has been out of sight a moment it flies to a free spot in front of them and to one side of
    /// their gaze, at about the height of their eyes; while they walk it goes a little ahead and aside, never on their way;
    /// after a jump it reappears in sight. It never chases a glance, never passes through the visitor or a wall, and where
    /// no clear way leads into sight it follows the way the visitor actually walked. It steps aside instead of blocking,
    /// keeps out of the line to the exhibit being read, waits at
    /// its own anchor by the hearth, and is never drawn against the visitor's eyes. It has no collider, never moves the
    /// visitor or the camera. The visitor can ask it a spoken question anywhere (<see cref="MuseumCompanionVoice"/>); the
    /// conversation about the visit stays with <see cref="MuseumGuide"/> at the guide anchor. While the visitor is in the
    /// hearth, it waits at the door («Aquí te espero») and joins them again, calmly, once they come out. Its speed changes
    /// slowly, so it never darts after the visitor. The figure is the companion's own neutral figure (<see cref="GuideFigure"/>); nothing in it represents a person.
    /// </summary>
    public sealed class MuseumCompanion : MonoBehaviour
    {
        public const string BodyPiece = "Guia_Provisional_Cuerpo";
        public const string HeadPiece = "Guia_Provisional_Cabeza";
        private const float RetargetSeconds = .2f;
        /// <summary>Speed changes slowly: the companion eases in and out of its movements instead of darting.</summary>
        private const float Acceleration = 1.2f, Deceleration = 2f;
        /// <summary>How long the companion's word at the door stays.</summary>
        private const float WaitNoticeSeconds = 6;

        [SerializeField] private TMP_FontAsset font;
        private MuseumCompanionSettings settings;
        private MuseumCompanionRemarks remarks;
        private readonly CompanionTrail trail = new CompanionTrail();
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private Transform visitor;
        private Camera head;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumNarration narration;
        private TourRuntime runtime;
        private RoomAnchor[] rooms = new RoomAnchor[0];
        private Transform figure;
        private Renderer[] figureRenderers = new Renderer[0];
        private readonly List<Renderer> hiddenOriginals = new List<Renderer>();
        private RectTransform bubble;
        private TMP_Text bubbleText;
        private CanvasGroup bubbleGroup;
        private int target = -1;
        private float retargetAt;
        private bool placed;
        private bool rejoinPending;
        private float walkPhase;
        private float figureHeight = 1.5f;
        private float speed;
        /// <summary>Height of the bird's middle above the companion's floor spot, before the lift toward the visitor's eyes.</summary>
        private float floatCentre = 1.35f;
        private float lift;
        private float stepBob;
        private Vector3 heading;
        private Vector3 lastFloor;
        private bool hasLastFloor;
        private Vector3 visitorVelocity;
        private bool walking;
        private float stillFor;
        private Vector3? viewSpot;
        private Vector3? leadSpot;
        private float viewSearchAt;
        private float leadAt;
        private float sightAt;
        private bool seen = true;

        /// <summary>
        /// Where a worn mask would go. The guides wear no mask (D5, 22/10/2026), so there is none and the scene's mask
        /// component is removed when the visit starts.
        /// </summary>
        public Transform MaskMount => null;
        /// <summary>The companion's figure; its look comes from <c>Resources/MuseumGuideFigures.json</c>.</summary>
        public GuideFigure Figure { get; private set; }
        public bool Placed => placed;
        public bool Visible => figure != null && figure.gameObject.activeSelf && figureRenderers.Any(renderer => renderer.enabled);
        /// <summary>The visitor asked for quiet: no remarks are made.</summary>
        public bool Muted { get; set; }
        /// <summary>True while the companion's own voice plays: the spoken answer to a visitor's question.</summary>
        public bool Speaking => voice != null && voice.Speaking;
        /// <summary>Spoken questions to the companion, anywhere in the museum.</summary>
        public MuseumCompanionVoice Voice => voice;
        private MuseumCompanionVoice voice;
        public string RemarkText => remarks?.Current?.Text ?? "";
        public MuseumCompanionRemarks Remarks => remarks;
        public string Zone { get; private set; }
        /// <summary>The visitor is in the room where the companion waits outside (the hearth): it stays at the door.</summary>
        public bool Waiting { get; private set; }
        /// <summary>Where the companion waits by the door while the visitor is inside.</summary>
        public Vector3 WaitSpot { get; private set; }
        private float noticeUntil;
        private string notice = "";
        public float FigureHeight => figureHeight;
        /// <summary>State of the walk, for diagnostics: points of the visitor's way kept, the one aimed at, current speed.</summary>
        public string WalkState => "trail " + trail.Count + ", target " + target + ", speed " + speed.ToString("F2") + ", zone " + Zone
            + ", mode " + Mode + ", seen " + InSight + ", out of sight " + OutOfSightFor.ToString("F1") + ", last obstacle " + lastObstacle;
        private string lastObstacle = "";
        /// <summary>What the companion is doing: lead (ahead while the visitor walks), view (moving into sight), settle, trail, home.</summary>
        public string Mode { get; private set; } = "";
        /// <summary>The visitor can see the companion: within the width of their view and with no wall between.</summary>
        public bool InSight => seen;
        /// <summary>Seconds the companion has been out of the visitor's sight.</summary>
        public float OutOfSightFor { get; private set; }
        /// <summary>The visitor is walking (or being carried), as the companion judges from their smoothed speed.</summary>
        public bool VisitorWalking => walking;
        /// <summary>Metres the bird rises or sinks from its own flying height toward the visitor's eyes.</summary>
        public float Lift => lift;
        /// <summary>World position of the middle of the bird.</summary>
        public Vector3 BirdCentre => transform.position + Vector3.up * (floatCentre + lift);

        public void Configure(TMP_FontAsset fontAsset) => font = fontAsset;

        private void Awake()
        {
            if (TryGetComponent<MuseumCompanionMask>(out var mask)) Destroy(mask);
            settings = MuseumCompanionSettings.Current;
            remarks = new MuseumCompanionRemarks(settings);
        }

        private void Start()
        {
            if (!settings.Enabled) { enabled = false; return; }
            var desktop = FindAnyObjectByType<DesktopVisitorController>(FindObjectsInactive.Include);
            visitor = desktop != null ? desktop.transform : null;
            interaction = FindAnyObjectByType<MuseumInteraction>();
            flow = interaction != null ? interaction.GetComponent<MuseumVisitFlow>() : null;
            runtime = interaction != null ? interaction.Runtime : null;
            rooms = FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None);
            BuildFigure();
            BuildBubble();
            voice = GetComponent<MuseumCompanionVoice>();
            if (voice == null) voice = gameObject.AddComponent<MuseumCompanionVoice>();
            if (GetComponent<MuseumCompanionVoicePanel>() == null) gameObject.AddComponent<MuseumCompanionVoicePanel>();
            Show(false);
        }

        private void OnDisable()
        {
            foreach (var original in hiddenOriginals) if (original != null) original.enabled = true;
            hiddenOriginals.Clear();
        }

        /// <summary>
        /// Builds the companion's own figure (<see cref="GuideFigure"/>) and hides the seated placeholder of the museum model.
        /// Nothing of the museum's meshes is reused: on the headset, static batching merges them, and a copy of the merged
        /// mesh showed the whole room as a grey sheet 8 km wide (Quest 3, 22/10/2026).
        /// </summary>
        private void BuildFigure()
        {
            HidePlaceholder(hiddenOriginals);
            // Its look and height come from the guides' figures alone (MuseumGuideFigures.json).
            Figure = GuideFigure.Build(transform, GuideFigureSettings.Companion);
            figure = Figure.transform;
            figureRenderers = Figure.Renderers;
            figureHeight = Figure.Height;
            var definition = GuideFigureSettings.Current.For(GuideFigureSettings.Companion);
            floatCentre = definition != null && Figure.Floats ? definition.FloatHeight : figureHeight * .7f;
        }

        /// <summary>Hides the provisional guide pieces of the museum model; the guides' own figures stand in for them.</summary>
        public static void HidePlaceholder(List<Renderer> hidden)
        {
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if ((renderer.name == BodyPiece || renderer.name == HeadPiece) && renderer.enabled)
                {
                    renderer.enabled = false;
                    hidden.Add(renderer);
                }
        }

        /// <summary>A small panel above the figure for its words; it turns toward the visitor and never takes input.</summary>
        private void BuildBubble()
        {
            var canvasObject = new GameObject("Remark", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            bubble = (RectTransform)canvasObject.transform;
            bubble.SetParent(transform, false);
            bubble.sizeDelta = new Vector2(620, 200);
            bubble.localScale = Vector3.one * .001f;
            bubble.localPosition = Vector3.up * (figureHeight + .32f);
            bubbleGroup = canvasObject.GetComponent<CanvasGroup>();
            bubbleGroup.interactable = false;
            bubbleGroup.blocksRaycasts = false;
            var experience = MuseumExperienceConfiguration.Current;
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(bubble, false);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, 0);
            panelRect.pivot = new Vector2(.5f, 0);
            panel.GetComponent<Image>().color = MuseumExperienceConfiguration.ColorValue(experience.PanelColor);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 20, 20);
            layout.childControlWidth = layout.childControlHeight = true;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(panelRect, false);
            bubbleText = textObject.GetComponent<TextMeshProUGUI>();
            if (font != null) bubbleText.font = font;
            bubbleText.fontSize = 30;
            bubbleText.color = MuseumExperienceConfiguration.ColorValue(experience.TextColor);
            bubbleText.richText = false;
            bubbleText.textWrappingMode = TextWrappingModes.Normal;
            bubbleText.alignment = TextAlignmentOptions.Center;
            textObject.GetComponent<LayoutElement>().preferredWidth = 560;
            bubbleGroup.alpha = 0;
        }

        private void Update()
        {
            if (visitor == null || interaction == null || flow == null) return;
            if (head == null || !head.isActiveAndEnabled) head = visitor.GetComponentInChildren<Camera>();
            bool exploring = flow.Phase == VisitPhase.Exploring;
            if (!exploring && !placed) { Show(false); return; }
            var eyes = head != null ? head.transform.position : visitor.position + Vector3.up * 1.65f;
            var floor = new Vector3(eyes.x, visitor.position.y, eyes.z);
            Zone = rooms.FirstOrDefault(room => room != null && room.Contains(floor))?.Key;
            if (!placed) { Arrive(floor, eyes); return; }
            bool inside = !string.IsNullOrEmpty(settings.WaitOutside) && Zone == settings.WaitOutside && InWaitArea(floor);
            if (inside && !Waiting) BeginWaiting(floor);
            else if (!inside && Waiting) EndWaiting(floor);
            // A jump (a teleport, sitting down or standing up) starts the way afresh: the old target is gone with it.
            if (!Waiting && !trail.Record(floor)) { rejoinPending = true; target = -1; viewSpot = leadSpot = null; hasLastFloor = false; }
            TrackVisitor(floor);
            if (!Waiting && rejoinPending && Rejoin(floor, eyes)) rejoinPending = false;
            Watch(floor);
            if (Waiting) WaitAtDoor();
            else Move(floor);
            Face(floor);
            Hover(eyes, floor);
            Gesture(eyes);
            float distance = Vector3.Distance(eyes, BirdCentre);
            Show(distance > settings.HideWithin);
            UpdateRemark(exploring, eyes);
        }

        /// <summary>
        /// The box round the fire and its seats where the companion does not go, from the pieces named in the settings that
        /// stand in the waiting room; null where the settings name none or none is found (then the whole room counts).
        /// Found again every few seconds while missing, since the museum may still be loading.
        /// </summary>
        public Bounds? WaitArea
        {
            get
            {
                if (waitBox.HasValue || settings.WaitBoxPieces.Count == 0 || Time.unscaledTime < waitBoxSearchAt) return waitBox;
                waitBoxSearchAt = Time.unscaledTime + 3;
                var room = rooms.FirstOrDefault(item => item != null && item.Key == settings.WaitOutside);
                Bounds? found = null;
                foreach (var piece in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!settings.WaitBoxPieces.Any(name => piece.name.StartsWith(name)) || room != null && !room.Contains(piece.bounds.center)) continue;
                    if (found.HasValue) { var box = found.Value; box.Encapsulate(piece.bounds); found = box; }
                    else found = piece.bounds;
                }
                if (found.HasValue)
                {
                    var box = found.Value;
                    box.Expand(new Vector3(settings.WaitBoxMargin * 2, 0, settings.WaitBoxMargin * 2));
                    waitBox = box;
                }
                return waitBox;
            }
        }
        private Bounds? waitBox;
        private float waitBoxSearchAt;

        /// <summary>A floor spot within the box round the fire, or within the waiting room where there is no box.</summary>
        public bool InWaitArea(Vector3 spot)
        {
            var box = WaitArea;
            if (box.HasValue)
                return spot.x >= box.Value.min.x && spot.x <= box.Value.max.x && spot.z >= box.Value.min.z && spot.z <= box.Value.max.z;
            var room = rooms.FirstOrDefault(item => item != null && item.Key == settings.WaitOutside);
            return room != null && room.Contains(spot);
        }

        /// <summary>
        /// The visitor stepped into the box round the fire: the companion stays at its edge where they came in, the newest
        /// point of their way outside the box, stepped back from it, and says it will wait there.
        /// </summary>
        private void BeginWaiting(Vector3 floor)
        {
            Waiting = true;
            var room = rooms.FirstOrDefault(item => item != null && item.Key == settings.WaitOutside);
            var box = WaitArea;
            var door = transform.position;
            for (int index = trail.Count - 1; index >= 0; index--)
                if (!InWaitArea(trail[index])) { door = trail[index]; break; }
            var middle = box.HasValue ? box.Value.center : room != null ? room.transform.position : floor;
            var away = Flat(door - middle);
            if (away == Vector3.zero) away = Flat(transform.position - floor);
            WaitSpot = door;
            foreach (float back in new[] { 1.2f, .8f, .4f })
                if (Free(door + away * back, door.y, out var spot)) { WaitSpot = spot; break; }
            // Outside already, it never walks through a wall to reach its place: it waits where it stands.
            bool outside = !InWaitArea(transform.position);
            if (outside && !Clear(transform.position, WaitSpot)) WaitSpot = transform.position;
            notice = MuseumInterfaceText.Get("companion_wait_here");
            noticeUntil = Time.unscaledTime + WaitNoticeSeconds;
        }

        /// <summary>Back out of the hearth, the companion joins the visitor again, calmly, from where they are now.</summary>
        private void EndWaiting(Vector3 floor)
        {
            Waiting = false;
            trail.Clear();
            trail.Record(transform.position);
            trail.Record(floor);
            target = -1;
            noticeUntil = 0;
        }

        /// <summary>Walks to its place by the door at an ordinary pace and stays there, facing the visitor.</summary>
        private void WaitAtDoor()
        {
            var position = transform.position;
            var step = WaitSpot - position;
            step.y = 0;
            float wanted = step.magnitude > .05f ? settings.WalkSpeed : 0;
            speed = Mathf.MoveTowards(speed, wanted, Time.deltaTime * (wanted > speed ? Acceleration : Deceleration));
            float travel = Mathf.Min(step.magnitude, speed * Time.deltaTime);
            if (step.sqrMagnitude > 1e-6f) position += step.normalized * travel;
            position.y = Mathf.Lerp(position.y, WaitSpot.y, Time.deltaTime * 6);
            transform.position = position;
            stepBob = 0;
            Figure.Stride = travel / Mathf.Max(Time.deltaTime, 1e-4f) / settings.WalkSpeed;
        }

        /// <summary>At the start of the visit the companion is in sight, a little ahead and aside, facing the visitor.</summary>
        private void Arrive(Vector3 floor, Vector3 eyes)
        {
            var forward = Gaze();
            var right = Vector3.Cross(Vector3.up, forward);
            var inSight = settings.InView.Enabled ? FindViewSpot(floor, floor, false) : null;
            foreach (var offset in new[] { forward * 2.2f + right * .9f, forward * 2.2f - right * .9f, forward * 1.8f, -forward * settings.FollowDistance })
            {
                Vector3 spot;
                if (inSight.HasValue) spot = inSight.Value;
                else if (!Free(floor + offset, floor.y, out spot) || !Clear(floor, spot)) continue;
                transform.position = spot;
                transform.rotation = Quaternion.LookRotation(Flat(floor - spot), Vector3.up);
                placed = true;
                trail.Clear();
                trail.Record(floor);
                lastFloor = floor;
                hasLastFloor = true;
                Show(true);
                return;
            }
        }

        /// <summary>
        /// After the visitor is taken somewhere at once, the companion reappears in sight, in front and aside; where no such
        /// spot is free, out of sight behind them, as before.
        /// </summary>
        private bool Rejoin(Vector3 floor, Vector3 eyes)
        {
            if (settings.InView.Enabled && Reappear(floor)) return true;
            var forward = Gaze();
            var right = Vector3.Cross(Vector3.up, forward);
            foreach (var offset in new[] { -forward * settings.FollowDistance, -forward * settings.FollowDistance + right, -forward * settings.FollowDistance - right })
                if (Free(floor + offset, floor.y, out var spot) && Clear(floor, spot) && !InView(spot, eyes))
                {
                    transform.position = spot;
                    target = -1;
                    return true;
                }
            return false;
        }

        /// <summary>Puts the companion at once on a spot in sight; false where none is free.</summary>
        private bool Reappear(Vector3 floor)
        {
            var spot = FindViewSpot(floor, floor, false);
            if (!spot.HasValue) return false;
            transform.position = spot.Value;
            transform.rotation = Quaternion.LookRotation(Flat(floor - spot.Value) == Vector3.zero ? Vector3.forward : Flat(floor - spot.Value), Vector3.up);
            target = -1;
            viewSpot = leadSpot = null;
            speed = 0;
            seen = true;
            OutOfSightFor = 0;
            return true;
        }

        /// <summary>The visitor's speed over the floor, smoothed, and whether they walk or stand, with some hysteresis.</summary>
        private void TrackVisitor(Vector3 floor)
        {
            float elapsed = Mathf.Max(Time.deltaTime, 1e-4f);
            if (hasLastFloor)
            {
                var velocity = (floor - lastFloor) / elapsed;
                velocity.y = 0;
                visitorVelocity = Vector3.Lerp(visitorVelocity, velocity, 1 - Mathf.Exp(-elapsed * 6));
            }
            else visitorVelocity = Vector3.zero;
            lastFloor = floor;
            hasLastFloor = true;
            var view = settings.InView;
            float pace = visitorVelocity.magnitude;
            if (pace > view.WalkThreshold) { walking = true; stillFor = 0; }
            else if (pace < view.StandSpeed)
            {
                stillFor += elapsed;
                if (stillFor >= view.StandSeconds) walking = false;
            }
        }

        /// <summary>Whether the visitor can see the companion now, checked a few times a second, and for how long they cannot.</summary>
        private void Watch(Vector3 floor)
        {
            if (Time.unscaledTime >= sightAt)
            {
                sightAt = Time.unscaledTime + .1f;
                seen = Sees(transform.position, floor, settings.InView.SightMargin);
            }
            OutOfSightFor = seen || Waiting ? 0 : OutOfSightFor + Time.deltaTime;
        }

        /// <summary>
        /// A spot within the width of the visitor's view (with <paramref name="margin"/> degrees more) and with no wall
        /// between it and them.
        /// </summary>
        private bool Sees(Vector3 spot, Vector3 floor, float margin)
        {
            if (head == null) return true;
            var toSpot = Flat(spot - floor);
            if (toSpot == Vector3.zero) return true;
            float half = Mathf.Atan(Mathf.Tan(head.fieldOfView * .5f * Mathf.Deg2Rad) * Mathf.Max(head.aspect, .5f)) * Mathf.Rad2Deg;
            return Vector3.Angle(Gaze(), toSpot) <= half + margin && Clear(floor, spot);
        }

        private void Move(Vector3 floor)
        {
            var position = transform.position;
            Vector3 goal;
            float least = settings.WalkSpeed * .5f;
            var home = Zone == settings.HomeRoom ? HomeAnchor(floor.y) : null;
            if (home != null && Clear(position, home.Value)) { goal = home.Value; Mode = "home"; }
            else if (settings.InView.Enabled && InViewGoal(position, floor, out goal)) least = .3f;
            else goal = FollowTrail(position, floor);
            var step = goal - position;
            step.y = 0;
            // Into sight it slows down as it arrives instead of stopping short.
            float pace = Mathf.Max(speed, least);
            if (Mode == "view") pace = Mathf.Min(pace, Mathf.Max(.25f, step.magnitude * 1.5f));
            float travel = Mathf.Min(step.magnitude, pace * Time.deltaTime);
            if (step.sqrMagnitude > 1e-6f)
            {
                heading = step.normalized;
                position += heading * travel;
                walkPhase += travel * 6;
            }
            position.y = Mathf.Lerp(position.y, goal.y, Time.deltaTime * 6);
            transform.position = position;
            // A slight step, not a bounce.
            stepBob = Mathf.Abs(Mathf.Sin(walkPhase)) * .015f * Mathf.Clamp01(speed);
            Figure.Stride = travel / Mathf.Max(Time.deltaTime, 1e-4f) / settings.WalkSpeed;
        }

        /// <summary>Follows the way the visitor walked until it is close enough, then settles out of the way.</summary>
        private Vector3 FollowTrail(Vector3 position, Vector3 floor)
        {
            Mode = "trail";
            if (Time.unscaledTime >= retargetAt)
            {
                retargetAt = Time.unscaledTime + RetargetSeconds;
                target = trail.FarthestReachable(position, Clear, 20);
            }
            if (target >= trail.Count) target = trail.Count - 1;
            float along = trail.LengthFrom(position, Mathf.Max(target, 0), floor);
            var goal = target >= 0 ? trail[target] : trail.Count > 0 ? trail[0] : position;
            if (along <= settings.FollowDistance)
            {
                Mode = "settle";
                goal = Settle(position, floor);
                Accelerate(0);
            }
            else
            {
                if (target > 0) trail.DropBefore(target);
                if (target > 0) target = 0;
                Accelerate(along > settings.CatchUpBeyond ? settings.CatchUpSpeed : settings.WalkSpeed);
            }
            return goal;
        }

        private void Accelerate(float top) =>
            speed = Mathf.MoveTowards(speed, top, Time.deltaTime * (top > speed ? Acceleration : Deceleration));

        /// <summary>
        /// Where the companion goes to stay in sight, or false to follow the visitor's way instead. Walking, a spot ahead and
        /// aside of their way. Standing, it stays where it is while it is seen; out of sight a moment (or far off), it flies
        /// to a spot in front and to one side of the gaze. A spot is only taken with a clear way to it, round the visitor
        /// rather than through them.
        /// </summary>
        private bool InViewGoal(Vector3 position, Vector3 floor, out Vector3 goal)
        {
            var view = settings.InView;
            goal = position;
            float now = Time.unscaledTime;
            float distance = CompanionTrail.Planar(position - floor);
            float pace = visitorVelocity.magnitude;
            if (walking)
            {
                viewSpot = null;
                if (now >= leadAt) { leadAt = now + RetargetSeconds; leadSpot = LeadSpot(floor, position); }
                if (!leadSpot.HasValue) return false;
                var next = Route(position, leadSpot.Value, floor);
                if (!Clear(position, next)) return false;
                goal = next;
                Mode = "lead";
                float remaining = CompanionTrail.Planar(leadSpot.Value - position);
                Accelerate(remaining > .2f ? Mathf.Clamp(pace + remaining, settings.WalkSpeed * .5f, settings.CatchUpSpeed * 1.25f) : pace);
                return true;
            }
            leadSpot = null;
            if (viewSpot.HasValue && CompanionTrail.Planar(viewSpot.Value - position) < .05f) viewSpot = null;
            bool wanted = OutOfSightFor >= view.OutOfSightSeconds || distance > view.Distance * 1.8f;
            if (!viewSpot.HasValue && wanted && now >= viewSearchAt)
            {
                viewSearchAt = now + .25f;
                viewSpot = FindViewSpot(floor, position, true);
                // Out of sight with no clear way back for long, it reappears in sight as after a jump.
                if (!viewSpot.HasValue && OutOfSightFor >= view.ReappearAfter && Reappear(floor))
                {
                    goal = transform.position;
                    Mode = "reappear";
                    return true;
                }
            }
            if (viewSpot.HasValue)
            {
                var next = Route(position, viewSpot.Value, floor);
                if (Clear(position, next))
                {
                    goal = next;
                    Mode = "view";
                    Accelerate(view.MoveSpeed);
                    return true;
                }
                viewSpot = null;
            }
            // Far along the way with no spot in sight to fly to, it follows the way the visitor walked.
            if (distance > settings.FollowDistance + 1) return false;
            Mode = "settle";
            goal = Settle(position, floor);
            Accelerate(0);
            return true;
        }

        /// <summary>
        /// A free spot in sight: at the distance of the settings, to one side of the gaze (the side the companion is on
        /// first), with a clear line from the visitor, never between them and the exhibit being read; with
        /// <paramref name="needWay"/>, only one the companion can reach from <paramref name="from"/> without a wall.
        /// </summary>
        private Vector3? FindViewSpot(Vector3 floor, Vector3 from, bool needWay)
        {
            var view = settings.InView;
            var forward = Gaze();
            var right = Vector3.Cross(Vector3.up, forward);
            float current = Vector3.Dot(from - floor, right) >= 0 ? 1 : -1;
            foreach (var (distance, angle, side) in ViewCandidates(view, current))
            {
                var candidate = floor + Quaternion.AngleAxis(side * angle, Vector3.up) * forward * distance;
                if (!Free(candidate, floor.y, out var spot) || !Clear(floor, spot) || Blocking(floor, spot)) continue;
                if (needWay && !Clear(from, Route(from, spot, floor))) continue;
                return spot;
            }
            return null;
        }

        /// <summary>
        /// The spots in sight tried in order: the set distance and angle first, on the side given first, then a little
        /// nearer or farther and a little nearer the gaze or wider.
        /// </summary>
        public static IEnumerable<(float distance, float angle, float side)> ViewCandidates(CompanionInViewSettings view, float firstSide)
        {
            float first = firstSide >= 0 ? 1 : -1;
            foreach (float distance in new[] { view.Distance, view.Distance * .8f, view.Distance * 1.2f })
                foreach (float angle in new[] { view.Angle, view.Angle - 8, view.Angle + 8 })
                    foreach (float side in new[] { first, -first })
                        yield return (distance, Mathf.Clamp(angle, 5, 80), side);
        }

        /// <summary>While the visitor walks: ahead along their way and aside of it, on the companion's side first.</summary>
        private Vector3? LeadSpot(Vector3 floor, Vector3 position)
        {
            var view = settings.InView;
            var along = Flat(visitorVelocity);
            if (along == Vector3.zero) return null;
            var right = Vector3.Cross(Vector3.up, along);
            float current = Vector3.Dot(position - floor, right) >= 0 ? 1 : -1;
            foreach (float side in new[] { current, -current })
                foreach (float reach in new[] { 1, .7f })
                {
                    var candidate = floor + along * view.LeadAhead * reach + right * side * view.LeadAside;
                    if (Free(candidate, floor.y, out var spot) && Clear(floor, spot) && !Blocking(floor, spot)) return spot;
                }
            return null;
        }

        /// <summary>
        /// The next point toward a goal: the goal itself, or where the straight way would pass too near the visitor, a free
        /// point round them on the goal's side.
        /// </summary>
        private Vector3 Route(Vector3 from, Vector3 goal, Vector3 floor)
        {
            float clearance = settings.InView.PassClearance;
            if (!PassesNear(from, goal, floor, clearance)) return goal;
            var across = Vector3.Cross(Vector3.up, Flat(goal - from));
            float side = Vector3.Dot(goal - floor, across) >= 0 ? 1 : -1;
            foreach (float turn in new[] { side, -side })
                if (Free(floor + across * turn * (clearance + .35f), floor.y, out var round)) return round;
            return goal;
        }

        /// <summary>True when the straight way between two floor spots passes within <paramref name="clearance"/> of a third.</summary>
        public static bool PassesNear(Vector3 from, Vector3 to, Vector3 visitor, float clearance)
        {
            if (CompanionTrail.Planar(from - visitor) < clearance) return false;
            var way = to - from;
            way.y = 0;
            float length = way.sqrMagnitude;
            if (length < 1e-6f) return false;
            var offset = visitor - from;
            offset.y = 0;
            float share = Mathf.Clamp01(Vector3.Dot(offset, way) / length);
            return CompanionTrail.Planar(from + way * share - visitor) < clearance;
        }

        /// <summary>True when a spot stands between the visitor and the exhibit being read.</summary>
        private bool Blocking(Vector3 floor, Vector3 spot)
        {
            var point = interaction.ActivePoint != null && interaction.State != InteractionState.Exploration ? interaction.ActivePoint.Anchor : null;
            return point != null && Blocks(floor, spot, point.LookTarget.position);
        }

        /// <summary>The bird flies at about the visitor's eyes, a little below them, within limits from its own height.</summary>
        private void Hover(Vector3 eyes, Vector3 floor)
        {
            var view = settings.InView;
            float wanted = view.Enabled && Figure != null && Figure.Floats
                ? Mathf.Clamp(eyes.y - transform.position.y - view.EyeDrop - floatCentre, view.LiftMin, view.LiftMax) : 0;
            lift = Mathf.MoveTowards(lift, wanted, view.LiftSpeed * Time.deltaTime);
            figure.localPosition = Vector3.up * (lift + stepBob);
            if (bubble != null) bubble.localPosition = Vector3.up * (figureHeight + .32f + lift);
        }

        /// <summary>The visitor's gaze over the floor; their body's facing when they look straight up or down.</summary>
        private Vector3 Gaze()
        {
            var forward = Flat(head != null ? head.transform.forward : visitor.forward);
            return forward != Vector3.zero ? forward : Flat(visitor.forward) != Vector3.zero ? Flat(visitor.forward) : Vector3.forward;
        }

        /// <summary>
        /// Where the companion stands once it has caught up: where it is, unless that is in the visitor's way or between
        /// the visitor and the exhibit being read; then a free spot a little to the side.
        /// </summary>
        private Vector3 Settle(Vector3 position, Vector3 floor)
        {
            var away = Flat(position - floor);
            float distance = CompanionTrail.Planar(position - floor);
            bool crowding = distance < settings.PersonalSpace;
            var point = interaction.ActivePoint != null && interaction.State != InteractionState.Exploration ? interaction.ActivePoint.Anchor : null;
            bool blocking = point != null && Blocks(floor, position, point.LookTarget.position);
            if (!crowding && !blocking) return position;
            var toVisitor = away.sqrMagnitude > 1e-4f ? -away : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, toVisitor);
            foreach (var candidate in new[]
            {
                floor + away * settings.FollowDistance + side * settings.SideOffset, floor + away * settings.FollowDistance - side * settings.SideOffset,
                floor + side * settings.FollowDistance, floor - side * settings.FollowDistance, floor + away * settings.FollowDistance,
            })
            {
                if (!Free(candidate, floor.y, out var spot) || !Clear(position, spot) || !Clear(floor, spot)) continue;
                if (point != null && Blocks(floor, spot, point.LookTarget.position)) continue;
                return spot;
            }
            return position;
        }

        /// <summary>True when a spot stands between the visitor and an exhibit, within a narrow cone toward it.</summary>
        public static bool Blocks(Vector3 visitorFloor, Vector3 spot, Vector3 exhibit)
        {
            var toExhibit = Flat(exhibit - visitorFloor);
            var toSpot = spot - visitorFloor;
            toSpot.y = 0;
            float along = Vector3.Dot(toSpot, toExhibit);
            float exhibitDistance = CompanionTrail.Planar(exhibit - visitorFloor);
            if (along <= 0 || along >= exhibitDistance) return false;
            float across = (toSpot - toExhibit * along).magnitude;
            return across < .45f + along * Mathf.Tan(20 * Mathf.Deg2Rad);
        }

        /// <summary>The floor spot of the published guide anchor, the companion's own place by the hearth.</summary>
        private Vector3? HomeAnchor(float floorY)
        {
            var anchor = runtime != null ? runtime.FindAnchor(runtime.Guide?.key) : null;
            if (anchor == null) return null;
            var spot = anchor.transform.position;
            return Free(new Vector3(spot.x, floorY, spot.z), floorY, out var floor) ? floor : (Vector3?)null;
        }

        private void Face(Vector3 floor)
        {
            // Moving, it looks where it goes; once there, at the visitor.
            var moving = speed > .2f && heading != Vector3.zero && !Waiting;
            var look = moving ? heading : Flat(floor - transform.position);
            if (look.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look, Vector3.up),
                settings.TurnSpeed * Time.deltaTime);
        }

        /// <summary>While the visitor asks it something, the figure looks at them, listens, thinks and speaks.</summary>
        private void Gesture(Vector3 eyes)
        {
            if (Figure == null) return;
            bool talking = voice != null && voice.Conversing;
            Figure.LookAt = talking ? eyes : (Vector3?)null;
            Figure.Gesture = !talking ? GuideGesture.Idle
                : voice.Stage == CompanionVoiceStage.Recording || voice.Stage == CompanionVoiceStage.Review ? GuideGesture.Listen
                : voice.Stage == CompanionVoiceStage.Transcribing || voice.Stage == CompanionVoiceStage.Thinking ? GuideGesture.Think
                : voice.Speaking ? GuideGesture.Speak : GuideGesture.Idle;
        }

        private void UpdateRemark(bool exploring, Vector3 eyes)
        {
            bool busy = (voice != null && voice.Conversing) || interaction.State != InteractionState.Exploration
                || (narration ??= FindAnyObjectByType<MuseumNarration>()) != null && narration.State == NarrationState.Playing;
            var remark = remarks.Tick(new CompanionMoment(Time.unscaledTimeAsDouble, Zone, exploring && interaction.State != InteractionState.Paused,
                busy || Waiting, Muted));
            bool waitingWord = Waiting && Time.unscaledTime < noticeUntil;
            if (waitingWord) bubbleText.text = notice;
            else if (remark != null) bubbleText.text = remark.Text;
            float wanted = (remark != null || waitingWord) && Visible ? 1 : 0;
            bubbleGroup.alpha = Mathf.MoveTowards(bubbleGroup.alpha, wanted, Time.unscaledDeltaTime * 4);
            bubble.gameObject.SetActive(bubbleGroup.alpha > 0);
            var toEyes = Flat(eyes - bubble.position);
            if (toEyes.sqrMagnitude > 1e-4f) bubble.rotation = Quaternion.LookRotation(-toEyes, Vector3.up);
        }

        private void Show(bool visible)
        {
            foreach (var renderer in figureRenderers) if (renderer != null) renderer.enabled = visible && placed;
            if (!visible && bubble != null && !placed) bubble.gameObject.SetActive(false);
        }

        /// <summary>A free standing spot at about the floor height: floor below, no wall, rim or furniture in the body's room.</summary>
        public bool Free(Vector3 candidate, float floorY, out Vector3 spot)
        {
            spot = candidate;
            if (!Physics.Raycast(candidate + Vector3.up * 1.2f, Vector3.down, out var floorHit, 2.5f, ~0, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(floorHit.point.y - floorY) > .3f || Belongs(floorHit.collider)) return false;
            spot = floorHit.point;
            int count = Physics.OverlapCapsuleNonAlloc(spot + Vector3.up * .45f, spot + Vector3.up * 1.4f, .22f, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++) if (!Belongs(overlap[index])) return false;
            return true;
        }
        private readonly Collider[] overlap = new Collider[16];

        /// <summary>A straight walk between two floor spots meets nothing at knee or chest height for a body of 0.2 m radius.</summary>
        public bool Clear(Vector3 from, Vector3 to)
        {
            var offset = to - from;
            offset.y = 0;
            float length = offset.magnitude;
            if (length < .01f) return true;
            var direction = offset / length;
            foreach (float height in new[] { .5f, 1.2f })
            {
                int count = Physics.SphereCastNonAlloc(from + Vector3.up * height, .2f, direction, hits, length, ~0, QueryTriggerInteraction.Ignore);
                for (int index = 0; index < count; index++)
                    if (!Belongs(hits[index].collider) && hits[index].distance > 0)
                    {
                        lastObstacle = hits[index].collider.name;
                        return false;
                    }
            }
            return true;
        }

        /// <summary>The visitor's own body is no obstacle to walking toward the visitor.</summary>
        private bool Belongs(Collider collider) => collider != null && visitor != null && collider.transform.IsChildOf(visitor);

        private bool InView(Vector3 spot, Vector3 eyes)
        {
            if (head == null) return false;
            var direction = (spot + Vector3.up * figureHeight * .6f - eyes).normalized;
            return Vector3.Dot(direction, head.transform.forward) > Mathf.Cos(Mathf.Deg2Rad * (head.fieldOfView * .5f + 15));
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0;
            return vector.sqrMagnitude > 1e-6f ? vector.normalized : Vector3.zero;
        }
    }
}
