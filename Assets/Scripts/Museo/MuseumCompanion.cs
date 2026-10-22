using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The guide's figure walking the visit with the visitor. It keeps a comfortable distance behind, along the way the
    /// visitor actually walked, steps aside instead of blocking, keeps out of the line to the exhibit being read, waits at
    /// its own anchor by the hearth, and is never drawn against the visitor's eyes. It has no collider, never moves the
    /// visitor or the camera. The visitor can ask it a spoken question anywhere (<see cref="MuseumCompanionVoice"/>); the
    /// conversation about the visit stays with <see cref="MuseumGuide"/> at the guide anchor. The figure is the companion's own neutral figure (<see cref="GuideFigure"/>); nothing in it represents a person.
    /// </summary>
    public sealed class MuseumCompanion : MonoBehaviour
    {
        public const string BodyPiece = "Guia_Provisional_Cuerpo";
        public const string HeadPiece = "Guia_Provisional_Cabeza";
        private const float RetargetSeconds = .2f;

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

        public Transform MaskMount { get; private set; }
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
        public float FigureHeight => figureHeight;
        /// <summary>State of the walk, for diagnostics: points of the visitor's way kept, the one aimed at, current speed.</summary>
        public string WalkState => "trail " + trail.Count + ", target " + target + ", speed " + speed.ToString("F2") + ", zone " + Zone
            + ", last obstacle " + lastObstacle;
        private string lastObstacle = "";

        public void Configure(TMP_FontAsset fontAsset) => font = fontAsset;

        private void Awake()
        {
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
            Figure = GuideFigure.Build(transform, GuideFigureSettings.Companion, settings.FigureScale);
            figure = Figure.transform;
            figureRenderers = Figure.Renderers;
            figureHeight = Figure.Height;
            MaskMount = new GameObject("MaskMount").transform;
            MaskMount.SetParent(figure, false);
            // In front of the head, at its middle, facing the way the figure faces.
            MaskMount.localPosition = Figure.Head.localPosition + new Vector3(0, .115f, .11f) * (figureHeight / 1.62f);
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
            if (!trail.Record(floor)) rejoinPending = true;
            if (rejoinPending && Rejoin(floor, eyes)) rejoinPending = false;
            Move(floor);
            Face(floor);
            Gesture(eyes);
            float distance = Vector3.Distance(eyes, transform.position + Vector3.up * figureHeight * .7f);
            Show(distance > settings.HideWithin);
            UpdateRemark(exploring, eyes);
        }

        /// <summary>At the start of the visit the companion stands a little ahead and aside, facing the visitor.</summary>
        private void Arrive(Vector3 floor, Vector3 eyes)
        {
            var forward = Flat(head != null ? head.transform.forward : visitor.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            foreach (var offset in new[] { forward * 2.2f + right * .9f, forward * 2.2f - right * .9f, forward * 1.8f, -forward * settings.FollowDistance })
            {
                if (!Free(floor + offset, floor.y, out var spot) || !Clear(floor, spot)) continue;
                transform.position = spot;
                transform.rotation = Quaternion.LookRotation(Flat(floor - spot), Vector3.up);
                placed = true;
                trail.Clear();
                trail.Record(floor);
                Show(true);
                return;
            }
        }

        /// <summary>After the visitor is taken somewhere at once, the companion joins out of sight behind them.</summary>
        private bool Rejoin(Vector3 floor, Vector3 eyes)
        {
            var forward = Flat(head != null ? head.transform.forward : visitor.forward);
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

        private void Move(Vector3 floor)
        {
            var position = transform.position;
            Vector3 goal;
            var home = Zone == settings.HomeRoom ? HomeAnchor(floor.y) : null;
            if (home != null && Clear(position, home.Value)) goal = home.Value;
            else
            {
                if (Time.unscaledTime >= retargetAt)
                {
                    retargetAt = Time.unscaledTime + RetargetSeconds;
                    target = trail.FarthestReachable(position, Clear, 20);
                }
                float along = trail.LengthFrom(position, Mathf.Max(target, 0), floor);
                goal = target >= 0 ? trail[target] : trail.Count > 0 ? trail[0] : position;
                float wanted = settings.FollowDistance;
                if (along <= wanted)
                {
                    goal = Settle(position, floor);
                    speed = Mathf.MoveTowards(speed, 0, Time.deltaTime * 3);
                }
                else
                {
                    if (target > 0) trail.DropBefore(target);
                    if (target > 0) target = 0;
                    float top = along > settings.CatchUpBeyond ? settings.CatchUpSpeed : settings.WalkSpeed;
                    speed = Mathf.MoveTowards(speed, top, Time.deltaTime * 3);
                }
            }
            var step = goal - position;
            step.y = 0;
            float travel = Mathf.Min(step.magnitude, Mathf.Max(speed, settings.WalkSpeed * .5f) * Time.deltaTime);
            if (step.sqrMagnitude > 1e-6f)
            {
                position += step.normalized * travel;
                walkPhase += travel * 6;
            }
            position.y = Mathf.Lerp(position.y, goal.y, Time.deltaTime * 6);
            transform.position = position;
            // A slight step, not a bounce.
            figure.localPosition = Vector3.up * (Mathf.Abs(Mathf.Sin(walkPhase)) * .015f * Mathf.Clamp01(speed));
            Figure.Stride = travel / Mathf.Max(Time.deltaTime, 1e-4f) / settings.WalkSpeed;
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
            var moving = speed > .2f && trail.Count > 0;
            var look = moving && target >= 0 ? Flat(trail[target] - transform.position) : Flat(floor - transform.position);
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
                busy, Muted));
            if (remark != null) bubbleText.text = remark.Text;
            float wanted = remark != null && Visible ? 1 : 0;
            bubbleGroup.alpha = Mathf.MoveTowards(bubbleGroup.alpha, wanted, Time.unscaledDeltaTime * 4);
            bubble.gameObject.SetActive(bubbleGroup.alpha > 0);
            var toEyes = Flat(eyes - bubble.position);
            if (toEyes.sqrMagnitude > 1e-4f) bubble.rotation = Quaternion.LookRotation(-toEyes, Vector3.up);
        }

        private void Show(bool visible)
        {
            foreach (var renderer in figureRenderers) if (renderer != null) renderer.enabled = visible && placed;
            if (MaskMount != null) MaskMount.gameObject.SetActive(visible && placed);
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
