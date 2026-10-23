using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    public enum ModelLoadState { Idle, Loading, Ready, Unavailable }

    /// <summary>
    /// Brings the published pieces into the museum. From the start of the visit each point's first piece (the masks, the
    /// Castle) stands on its pedestal, loaded one after another within a small share of each frame (owner, 22/10/2026):
    /// these are the resident pieces. The piece of the record being read is the one on show; it is the resident piece
    /// of its point when it is that one, and otherwise it is brought on demand and stands in the resident's place
    /// meanwhile. Pieces without a natural size turn their face toward the visitor at their point, and toward their
    /// reading stand otherwise.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(TourLoader))]
    public sealed class MuseumModelPresenter : MonoBehaviour
    {
        private sealed class LoadSession
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public TourPoint Point;
            public string Slug;
            public UnityWebRequest Request;
            public GltfImport Import;
            public GameObject Pivot;
            public Transform[] Nodes;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public ModelLoadTimeline Timeline;
            public float ExaminationScale = 1;
            /// <summary>Scale that brings the normalized model back to the size it was built at.</summary>
            public float NaturalScale = 1;
            /// <summary>Size of the normalized model before any display scale.</summary>
            public Vector3 Size;
            public float ReadyAt;
            public RoomAnchor VisitorRoom;
            public bool Ready;
            public float Deadline;
            public DateTimeOffset Expiration;
            /// <summary>Resource variant and file hash the access was granted for; a renewal must grant the same file.</summary>
            public string ResourceId, Sha256;
            /// <summary>When to ask the service again for access, before it runs out while the piece is on show.</summary>
            public DateTimeOffset RenewAt;
            public bool Renewing;
            /// <summary>Stands on its pedestal for the whole visit, whether or not it is the piece being read.</summary>
            public bool Resident;
            /// <summary>Description of the piece for those who cannot see it, from its resource.</summary>
            public string Alternative;
            /// <summary>Compass angle the piece's face is turned to, once known.</summary>
            public float Yaw;
            public bool YawSet;
            private bool cancelled;
            public void Cancel()
            {
                if (cancelled) return;
                cancelled = true;
                Cancellation.Cancel();
                Request?.Abort();
                if (Pivot != null)
                {
                    Pivot.SetActive(false);
                    if (Ready) { Destroy(Pivot); Pivot = null; }
                }
                // Import jobs must finish before their native allocations are disposed.
                if (Ready) Dispose();
            }
            public void Dispose() { Import?.Dispose(); Import = null; Cancellation.Dispose(); }
        }

        private MuseumInteraction interaction;
        private TourLoader loader;
        private Camera viewCamera;
        private Transform visitor;
        private LoadSession session;
        private string attemptedKey;
        private float nextScan;
        private readonly ModelExaminationPose pose = new ModelExaminationPose();
        private Light studyLight;
        private RoomAnchor[] roomAnchors;
        private string switchSlug;
        private float switchAt;
        private ModelLoadBudget budget;
        private ModelByteCache cache;
        private ModelLoadTimeline observed;
        private float observeUntil;
        private readonly Dictionary<string, LoadSession> residents = new Dictionary<string, LoadSession>();
        private readonly List<(TourPoint point, string slug)> preloadQueue = new List<(TourPoint, string)>();
        private LoadSession preloading;
        private RoomAnchor preloadedRoom;
        private bool preloadPlanned, preloadReported;
        private PointLectern[] stands;
        /// <summary>Seconds a piece takes to turn its face toward the visitor.</summary>
        public const float FacingSeconds = .8f;
        /// <summary>Beyond the point's radius, how far away the visitor is still looked at, in metres.</summary>
        public const float FacingReach = 2.5f;
        /// <summary>
        /// When the pieces of the points are brought: "visit" all at the start of the visit, "room" those of the room the
        /// visitor enters, "off" only the piece being read. The museum sets it from its configuration.
        /// </summary>
        public string PreloadMode { get; set; } = "off";
        /// <summary>Resident pieces standing ready on their pedestals.</summary>
        public int ResidentCount => residents.Values.Count(resident => resident.Ready);
        /// <summary>Whether the resident piece of a point is ready, and which one it is.</summary>
        public bool TryResident(TourPoint point, out string slug, out Transform model)
        {
            slug = null; model = null;
            if (point == null || !residents.TryGetValue(point.Anchor.Key, out var resident) || !resident.Ready || resident.Pivot == null) return false;
            slug = resident.Slug;
            model = resident.Pivot.transform;
            return true;
        }
        /// <summary>Loads of the resident pieces, measured on this device: bytes, triangles, memory and longest frame.</summary>
        public IReadOnlyList<ModelLoadTimeline> ResidentTimelines => residents.Values.Select(resident => resident.Timeline).ToList();
        /// <summary>Changing piece within the same point waits this long before asking for the model, so quick changes never pile up requests.</summary>
        public const float SwitchSettleSeconds = .25f;
        /// <summary>A model that appears grows in from slightly smaller over this time.</summary>
        public const float RevealSeconds = .25f;
        /// <summary>Requests sent for models, for diagnostics and tests.</summary>
        public int RequestCount { get; private set; }
        /// <summary>Slug of the model on show or on its way, if any.</summary>
        public string ModelSlug => session?.Slug;
        public ModelLoadState State { get; private set; }
        public float Progress { get; private set; }
        public string Status { get; private set; } = "";
        public string Attribution { get; private set; } = "";
        public bool CanExamine => State == ModelLoadState.Ready && session != null
            && session.Point == interaction.ActivePoint && session.Slug == interaction.SelectedElement?.slug;
        public Transform Model => session?.Pivot != null && session.Ready ? session.Pivot.transform : null;
        /// <summary>Renderers of the piece on show, for reaching and holding it; empty while nothing is shown.</summary>
        public Renderer[] Renderers => Model != null ? session.Renderers : Array.Empty<Renderer>();
        /// <summary>Point whose piece is on show or on its way.</summary>
        public TourPoint ModelPoint => session?.Point;
        /// <summary>While true, someone else holds the piece: its exhibition pose is not applied until it is let go.</summary>
        public bool PoseHeld { get; set; }
        /// <summary>Stages of the latest load, measured on this device.</summary>
        public ModelLoadTimeline LastTimeline { get; private set; }
        public ModelExaminationPose Pose => pose;
        public event Action Changed;
        public string Profile => Application.platform == RuntimePlatform.Android ? "quest" : "web";

        public void Configure(Camera camera, Transform visitorTransform)
        { viewCamera = camera; visitor = visitorTransform; CreateStudyLight(); }

        private void Awake()
        {
            interaction = GetComponent<MuseumInteraction>();
            loader = GetComponent<TourLoader>();
            roomAnchors = interaction.Runtime.GetComponentsInChildren<RoomAnchor>();
            var settings = MuseumExperienceConfiguration.Current;
            budget = gameObject.AddComponent<ModelLoadBudget>();
            budget.BudgetSeconds = settings.ModelLoadFrameBudgetMs / 1000f;
            cache = new ModelByteCache(settings.ModelCacheEntries, settings.ModelCacheBytes);
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            if (visitor == null) visitor = GetComponentInChildren<DesktopVisitorController>()?.transform;
            CreateStudyLight();
        }
        private void CreateStudyLight()
        {
            if (studyLight != null || viewCamera == null) return;
            var lightObject = new GameObject("ModelStudyLight");
            lightObject.transform.SetParent(viewCamera.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(30, -25, 0);
            studyLight = lightObject.AddComponent<Light>();
            studyLight.type = LightType.Directional;
            studyLight.intensity = .8f;
            studyLight.shadows = LightShadows.None;
            int layer = LayerMask.NameToLayer("ModelExamination");
            studyLight.cullingMask = layer >= 0 ? 1 << layer : 0;
            studyLight.enabled = false;
        }
        private void OnDestroy()
        {
            if (studyLight != null) Destroy(studyLight.gameObject);
            cache?.Clear();
        }
        private void OnEnable()
        {
            interaction.Changed += OnInteractionChanged;
            interaction.Runtime.Changed += OnTourChanged;
            OnInteractionChanged();
        }
        private void OnDisable()
        {
            interaction.Changed -= OnInteractionChanged;
            interaction.Runtime.Changed -= OnTourChanged;
            Release();
            DropResidents();
            attemptedKey = null;
        }

        private void OnTourChanged()
        {
            Release();
            DropResidents();
            attemptedKey = null;
            preloadPlanned = preloadReported = false;
            preloadedRoom = null;
        }
        private void OnInteractionChanged()
        {
            // Once the visitor leaves the point's records, a piece brought for one of them gives the pedestal back to the
            // point's resident piece.
            if (interaction.State == InteractionState.Exploration && session != null && !session.Resident && session.Point != null
                && residents.ContainsKey(session.Point.Anchor.Key)) { Release(); attemptedKey = null; }
            var selected = interaction.SelectedElement;
            if (selected != null && interaction.State != InteractionState.Paused)
            {
                if (!selected.has_3d_model)
                {
                    // A point's own record, without a model, leaves the piece of that point on its pedestal; the arrows
                    // beside it step on to the next one. Arriving on that record, the point's first piece is brought.
                    if (session == null || session.Point != interaction.ActivePoint)
                    {
                        switchSlug = null; Release(); attemptedKey = null;
                        var piece = FirstPiece(interaction.ActivePoint);
                        if (piece != null) RequestModel(interaction.ActivePoint, piece.slug);
                    }
                }
                else if (session != null && session.Point == interaction.ActivePoint && session.Slug != selected.slug
                    || switchSlug != null && switchSlug != selected.slug)
                {
                    // Another piece of the same point: drop the one on show at once, ask for the new one once the choice settles.
                    Release();
                    attemptedKey = null;
                    switchSlug = selected.slug;
                    switchAt = Time.realtimeSinceStartup + SwitchSettleSeconds;
                    SetState(ModelLoadState.Loading, MuseumInterfaceText.Get("model_loading"));
                }
                else if (switchSlug == null) RequestModel(interaction.ActivePoint, selected.slug);
            }
            UpdatePose();
        }

        public bool Examine()
        {
            if (!CanExamine || viewCamera == null) return false;
            pose.Reset();
            return interaction.OpenModel();
        }
        public void Rotate(Vector2 degrees) { pose.Rotate(degrees); UpdatePose(); }
        public void Zoom(float delta) { pose.Zoom(delta); UpdatePose(); }
        public void ResetPose() { pose.Reset(); UpdatePose(); }
        public void Retry()
        {
            var point = session?.Point ?? interaction.ActivePoint;
            var slug = session?.Slug ?? interaction.SelectedElement?.slug;
            if (point == null || slug == null) return;
            attemptedKey = null;
            RequestModel(point, slug);
        }

        private void Update()
        {
            // The longest frame from the request until shortly after the piece appears, including its first draws.
            if (observed != null)
            {
                observed.ObserveFrame(Time.unscaledDeltaTime);
                if (observed.Outcome != "pending" && Time.realtimeSinceStartup >= observeUntil)
                {
                    Debug.Log(observed.ToString());
                    observed = null;
                }
            }
            var currentRoom = visitor != null ? roomAnchors.FirstOrDefault(room => room.Contains(visitor.position)) : null;
            Preload(currentRoom);
            // The frames while a resident piece loads are measured, as those of the piece on show are.
            preloading?.Timeline.ObserveFrame(Time.unscaledDeltaTime);
            KeepResidents();
            foreach (var resident in residents.Values) if (resident != session) Face(resident, Time.unscaledDeltaTime, false);
            if (session != null && interaction.State != InteractionState.ModelExamination) Face(session, Time.unscaledDeltaTime, true);
            if (switchSlug != null && Time.realtimeSinceStartup >= switchAt)
            {
                var settled = switchSlug;
                switchSlug = null;
                if (interaction.SelectedElement?.slug == settled && interaction.State == InteractionState.ElementSelected)
                    RequestModel(interaction.ActivePoint, settled);
                else if (State == ModelLoadState.Loading && session == null) SetState(ModelLoadState.Idle, "");
            }
            // The appearance runs over a few frames; one frame past its end sets the final size.
            if (session != null && session.Ready && Time.realtimeSinceStartup < session.ReadyAt + RevealSeconds + .1f) UpdatePose();
            if (session != null)
            {
                if (State == ModelLoadState.Loading && Time.realtimeSinceStartup > session.Deadline)
                { Fail(MuseumInterfaceText.Get("model_timeout")); return; }
                if (session.Ready && DateTimeOffset.UtcNow >= session.Expiration)
                { Fail(MuseumInterfaceText.Get("model_expired")); return; }
                // Access is granted for minutes; a piece the visitor stays with is granted again before it runs out.
                if (session.Ready && !session.Renewing && DateTimeOffset.UtcNow >= session.RenewAt) Renew(session);
                if (visitor != null && interaction.State != InteractionState.ModelExamination
                    && interaction.State != InteractionState.Paused
                    && (Vector3.Distance(visitor.position, session.Point.Anchor.transform.position) > 15
                        || currentRoom != null && session.VisitorRoom != null && currentRoom != session.VisitorRoom))
                { Release(); attemptedKey = null; SetState(ModelLoadState.Idle, MuseumInterfaceText.Get("model_released")); }
                else if (session.VisitorRoom == null && currentRoom != null) session.VisitorRoom = currentRoom;
            }
            // The piece of the selected record, or on a point's own record the point's first piece, comes back on return.
            var wanted = interaction.State != InteractionState.ElementSelected || interaction.SelectedElement == null ? null
                : interaction.SelectedElement.has_3d_model ? interaction.SelectedElement : FirstPiece(interaction.ActivePoint);
            if (wanted != null && session == null && attemptedKey == null && switchSlug == null && visitor != null
                && Vector3.Distance(visitor.position, interaction.ActivePoint.Anchor.transform.position)
                    <= interaction.ActivePoint.Anchor.ActivationRadius + 4)
                RequestModel(interaction.ActivePoint, wanted.slug);
            if (interaction.State == InteractionState.ModelExamination)
            {
                var input = MuseumInput.Current;
                var direction = input.Rotate.ReadValue<Vector2>();
                Rotate(new Vector2(direction.x, -direction.y) * (80 * Time.unscaledDeltaTime));
                Zoom(-input.Zoom.ReadValue<float>() * Time.unscaledDeltaTime * .4f);
                if (input.ResetPose.WasPressedThisFrame()) ResetPose();
                if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                {
                    if (input.Click.IsPressed())
                    {
                        var delta = input.Drag.ReadValue<Vector2>();
                        Rotate(new Vector2(delta.x, -delta.y) * .3f);
                    }
                    Zoom(-input.ScrollWheel.ReadValue<Vector2>().y / 120 * .08f);
                }
                UpdatePose();
            }
            if (interaction.State != InteractionState.Exploration || visitor == null || Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + .5f;
            var nearby = interaction.Runtime.OrderedPoints.Where(point => point.HasContent
                && Vector3.Distance(visitor.position, point.Anchor.transform.position) <= point.Anchor.ActivationRadius + 4
                && point.GetComponentInParent<RoomAnchor>().Contains(visitor.position))
                .OrderBy(point => (point.Anchor.transform.position - visitor.position).sqrMagnitude);
            foreach (var point in nearby)
            {
                var element = point.Content.elements.FirstOrDefault(candidate => candidate.has_3d_model);
                if (element == null) continue;
                RequestModel(point, element.slug);
                break;
            }
        }

        /// <summary>First record of a point that has a model, the piece its pedestal shows while the point's own record is read.</summary>
        private static ElementSummaryContractV1 FirstPiece(TourPoint point)
            => point != null && point.HasContent ? point.Content.elements.FirstOrDefault(candidate => candidate.has_3d_model) : null;

        /// <summary>Applies the exhibition or examination pose again, for instance once a held piece is back in place.</summary>
        public void RefreshPose() => UpdatePose();

        /// <summary>
        /// Where the piece on show stands in the exhibition: always computed from its pedestal or point and its own size,
        /// never from where a visitor has just moved it.
        /// </summary>
        public bool TryExhibitPose(out Vector3 position, out Quaternion rotation, out Vector3 localScale)
        {
            position = Vector3.zero; rotation = Quaternion.identity; localScale = Vector3.one;
            if (session?.Pivot == null || !session.Ready) return false;
            var mount = session.Point.Anchor.GetComponentInChildren<PointExhibitMount>();
            var parent = mount != null ? mount.transform : session.Point.Anchor.LookTarget;
            var fit = mount != null ? mount.ScaleFor(session.Size, session.NaturalScale) : Vector3.one;
            position = parent.TransformPoint(mount != null ? Vector3.up * (session.Size.y * fit.y * .5f) : Vector3.zero);
            rotation = Turns(session, mount) ? Quaternion.Euler(0, session.Yaw, 0) : parent.rotation;
            localScale = fit;
            return true;
        }

        /// <summary>A piece turns to the visitor unless it stands at its natural size, like the Castle built into the courtyard.</summary>
        private static bool Turns(LoadSession piece, PointExhibitMount mount) => piece.YawSet && (mount == null || !mount.KeepNaturalScale);

        /// <summary>
        /// Turns a piece's face toward the visitor at its point, and toward the reading spot of its stand otherwise. The
        /// face of a published piece is its +Z side: glTF puts the front of an asset toward +Z and glTFast keeps that axis
        /// when it converts to Unity (it mirrors X only). Turning -Z toward the visitor showed them the back of the masks
        /// (owner's headset test of 23/10/2026).
        /// </summary>
        private void Face(LoadSession piece, float elapsed, bool active)
        {
            if (piece?.Pivot == null || !piece.Ready || piece.Point == null) return;
            var anchor = piece.Point.Anchor;
            var centre = anchor.LookTarget.position;
            var eye = viewCamera != null ? viewCamera.transform.position : visitor != null ? visitor.position : centre;
            Vector3 toward;
            if ((viewCamera != null || visitor != null) && anchor.DistanceTo(eye) <= anchor.ActivationRadius + FacingReach) toward = eye - centre;
            else
            {
                stands ??= FindObjectsByType<PointLectern>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                var stand = stands.FirstOrDefault(candidate => candidate != null && candidate.Point == anchor);
                if (stand == null) return;
                toward = stand.ViewingSpot(1.5f) - centre;
            }
            toward.y = 0;
            if (toward.sqrMagnitude < .0001f) return;
            float target = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            piece.Yaw = piece.YawSet ? Mathf.LerpAngle(piece.Yaw, target, 1 - Mathf.Exp(-Mathf.Max(0, elapsed) / FacingSeconds)) : target;
            piece.YawSet = true;
            Place(piece, active);
        }

        private void UpdatePose() => Place(session, true);

        /// <summary>
        /// Places a piece: in the visitor's view while it is examined, otherwise standing on its pedestal's mount or
        /// floating at its point, turned as <see cref="Face"/> decided. Only the piece being read can be examined or held.
        /// </summary>
        private void Place(LoadSession piece, bool active)
        {
            if (piece?.Pivot == null || !piece.Ready) return;
            var pivot = piece.Pivot.transform;
            bool examining = active && interaction.State == InteractionState.ModelExamination;
            if (active && studyLight != null) studyLight.enabled = examining;
            // Pause keeps the model at its last examination pose.
            if (active && interaction.State == InteractionState.Paused) return;
            // A piece in the visitor's hand, or on its way back, is placed by whoever holds it.
            if (active && PoseHeld && !examining) return;
            // A pedestal with a mount carries the model standing on it; elsewhere it floats at the point.
            var mount = examining ? null : piece.Point.Anchor.GetComponentInChildren<PointExhibitMount>();
            var parent = examining ? viewCamera.transform : mount != null ? mount.transform : piece.Point.Anchor.LookTarget;
            if (pivot.parent != parent) pivot.SetParent(parent, false);
            float reveal = Mathf.SmoothStep(.85f, 1, Mathf.Clamp01((Time.realtimeSinceStartup - piece.ReadyAt) / RevealSeconds));
            var fit = mount != null ? mount.ScaleFor(piece.Size, piece.NaturalScale) : Vector3.one;
            pivot.localPosition = examining ? Vector3.forward * pose.Distance
                : mount != null ? Vector3.up * (piece.Size.y * fit.y * reveal * .5f) : Vector3.zero;
            pivot.localRotation = examining ? pose.Rotation : Quaternion.identity;
            if (!examining && Turns(piece, mount)) pivot.rotation = Quaternion.Euler(0, piece.Yaw, 0);
            pivot.localScale = examining ? Vector3.one * piece.ExaminationScale : fit * reveal;
            int layer = examining ? LayerMask.NameToLayer("ModelExamination") : 0;
            if (layer < 0) layer = 0;
            if (piece.Nodes != null && piece.Nodes.Length > 0 && piece.Nodes[0] != null && piece.Nodes[0].gameObject.layer == layer
                && piece.Nodes[piece.Nodes.Length - 1] != null && piece.Nodes[piece.Nodes.Length - 1].gameObject.layer == layer) return;
            foreach (var item in piece.Nodes) item.gameObject.layer = layer;
        }

        /// <summary>
        /// Plans and starts the loads of the resident pieces, one at a time: all of them at the start of the visit, or
        /// those of each room as the visitor enters it.
        /// </summary>
        private void Preload(RoomAnchor currentRoom)
        {
            if (PreloadMode != "visit" && PreloadMode != "room" || interaction.Runtime.Contract == null || !isActiveAndEnabled) return;
            if (!preloadPlanned || PreloadMode == "room" && currentRoom != null && currentRoom != preloadedRoom)
            {
                preloadPlanned = true;
                preloadedRoom = currentRoom;
                foreach (var point in interaction.Runtime.OrderedPoints)
                {
                    var piece = FirstPiece(point);
                    if (piece == null || residents.ContainsKey(point.Anchor.Key) || preloadQueue.Any(item => item.point == point)) continue;
                    if (PreloadMode == "room" && (currentRoom == null || point.GetComponentInParent<RoomAnchor>() != currentRoom)) continue;
                    preloadQueue.Add((point, piece.slug));
                }
                // A piece built into the room at its natural size, like the Castle, comes first: it is what the visitor
                // sees on entering the room, and it is light.
                var first = preloadQueue.Where(item => item.point != null && item.point.Anchor.GetComponentInChildren<PointExhibitMount>() is PointExhibitMount mount
                    && mount.KeepNaturalScale).ToList();
                preloadQueue.RemoveAll(item => first.Contains(item));
                preloadQueue.InsertRange(0, first);
            }
            if (preloading != null) return;
            while (preloadQueue.Count > 0)
            {
                var (point, slug) = preloadQueue[0];
                preloadQueue.RemoveAt(0);
                if (point == null || residents.ContainsKey(point.Anchor.Key)) continue;
                // The piece of the point being read is brought for the visitor right now; it is not loaded twice.
                if (session != null && session.Point == point && session.Slug == slug) continue;
                var pending = new LoadSession { Point = point, Slug = slug, Resident = true, Deadline = Time.realtimeSinceStartup + 90,
                    Timeline = new ModelLoadTimeline(slug, Profile) };
                pending.Timeline.MemoryBefore = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                residents[point.Anchor.Key] = pending;
                preloading = pending;
                RequestCount++;
                Load(pending);
                return;
            }
            if (!preloadReported && residents.Count > 0 && residents.Values.All(resident => resident.Ready))
            {
                preloadReported = true;
                Debug.Log(ResidentSummary());
            }
        }

        /// <summary>One line on the resident pieces for the device log: how many, their bytes, triangles and memory.</summary>
        public string ResidentSummary()
        {
            var ready = residents.Values.Where(resident => resident.Ready).ToList();
            return System.FormattableString.Invariant($"Resident pieces on {Profile}: {ready.Count} ({string.Join(", ", ready.Select(resident => resident.Slug))}), ")
                + System.FormattableString.Invariant($"{ready.Sum(resident => resident.Timeline.Bytes) / 1048576.0:F1} MB downloaded, {ready.Sum(resident => resident.Timeline.Triangles)} triangles, ")
                + System.FormattableString.Invariant($"memory +{ready.Sum(resident => Math.Max(0, resident.Timeline.MemoryAfter - resident.Timeline.MemoryBefore)) / 1048576.0:F1} MB, ")
                + System.FormattableString.Invariant($"longest load frame {ready.Max(resident => resident.Timeline.WorstFrameMilliseconds):F1} ms.");
        }

        /// <summary>Asks again for access to the resident pieces before it runs out, and takes away those that ran out.</summary>
        private void KeepResidents()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var resident in residents.Values.ToList())
            {
                if (resident == session || !resident.Ready) continue;
                if (now >= resident.Expiration) DropResident(resident);
                else if (!resident.Renewing && now >= resident.RenewAt) Renew(resident);
            }
        }

        /// <summary>Whether a load or a renewal still concerns a piece in use.</summary>
        private bool Live(LoadSession piece)
            => piece != null && (session == piece || piece.Resident && residents.TryGetValue(piece.Point.Anchor.Key, out var resident) && resident == piece);

        private void DropResident(LoadSession resident)
        {
            if (resident == null) return;
            if (residents.TryGetValue(resident.Point.Anchor.Key, out var current) && current == resident) residents.Remove(resident.Point.Anchor.Key);
            if (preloading == resident) preloading = null;
            if (session == resident) { session = null; attemptedKey = null; }
            resident.Cancel();
        }

        private void DropResidents()
        {
            foreach (var resident in residents.Values.ToList()) DropResident(resident);
            residents.Clear();
            preloadQueue.Clear();
            preloading = null;
        }

        private void RequestModel(TourPoint point, string slug)
        {
            if (!isActiveAndEnabled || point == null) return;
            string key = point.Anchor.Key + "/" + slug;
            if (attemptedKey == key) return;
            Release();
            attemptedKey = key;
            if (residents.TryGetValue(point.Anchor.Key, out var resident))
            {
                if (resident.Slug == slug)
                {
                    // The point's resident piece is already on its pedestal, or on its way: it becomes the piece on show.
                    session = resident;
                    resident.VisitorRoom = visitor != null ? roomAnchors.FirstOrDefault(room => room.Contains(visitor.position)) : null;
                    if (resident.Pivot != null) resident.Pivot.SetActive(true);
                    if (!resident.Ready) { SetState(ModelLoadState.Loading, MuseumInterfaceText.Get("model_loading")); return; }
                    Progress = 1;
                    Attribution = resident.Alternative ?? "";
                    UpdatePose();
                    SetState(ModelLoadState.Ready, MuseumInterfaceText.Get("model_ready"));
                    return;
                }
                // Another piece of the point is brought; the resident one steps aside meanwhile.
                if (resident.Pivot != null) resident.Pivot.SetActive(false);
            }
            var pending = new LoadSession { Point = point, Slug = slug, Deadline = Time.realtimeSinceStartup + 60,
                VisitorRoom = visitor != null ? roomAnchors.FirstOrDefault(room => room.Contains(visitor.position)) : null,
                Timeline = new ModelLoadTimeline(slug, Profile) };
            FinishObservation();
            observed = LastTimeline = pending.Timeline;
            observed.MemoryBefore = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            session = pending;
            RequestCount++;
            SetState(ModelLoadState.Loading, MuseumInterfaceText.Get("model_loading"));
            Load(pending);
        }

        private async void Load(LoadSession pending)
        {
            bool retained = false;
            var timeline = pending.Timeline;
            try
            {
                string api = loader.ApiBase;
                string elementJson;
                timeline.Begin("element");
                using (var request = UnityWebRequest.Get(api + "/elements/" + Uri.EscapeDataString(pending.Slug)))
                {
                    await Send(pending, request, 15);
                    elementJson = request.downloadHandler.text;
                }
                if (!ElementContractV1.TryParse(elementJson, out var element) || element.slug != pending.Slug
                    || !ModelResourcePolicy.TrySelect(element, Profile, out var resource, out var id))
                    throw new InvalidOperationException();
                ResourceAccessContractV1 access;
                Uri url;
                // Access is granted anew for every load, including one the cache can serve: revocation and expiry apply.
                timeline.Begin("access");
                using (var request = new UnityWebRequest(api + "/resources/" + Uri.EscapeDataString(id) + "/access?schema_version=1", "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    await Send(pending, request, 15);
                    if (!ModelResourcePolicy.TryAccess(api, request.downloadHandler.text, Profile, DateTimeOffset.UtcNow, out access, out url))
                        throw new InvalidOperationException();
                }
                string cacheKey = Profile + "/" + id;
                if (cache.TryGet(cacheKey, access.sha256, out var data)) timeline.FromCache = true;
                else
                {
                    timeline.Begin("download");
                    using (var request = UnityWebRequest.Get(url))
                    {
                        await Send(pending, request, 60, true);
                        data = request.downloadHandler.data;
                    }
                    timeline.Begin("verify");
                    if (!await Verify(data, Profile, access.sha256)) throw new InvalidOperationException();
                    pending.Cancellation.Token.ThrowIfCancellationRequested();
                    cache.Put(cacheKey, access.sha256, data);
                }
                timeline.Bytes = data.LongLength;
                var logger = new CollectingLogger();
                pending.Import = new GltfImport(deferAgent: budget, logger: logger);
                timeline.Begin("import");
                if (!await pending.Import.Load(data, url, new ImportSettings { GenerateMipMaps = true }, pending.Cancellation.Token))
                    throw new InvalidOperationException();
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                timeline.Begin("textures");
                timeline.Textures = pending.Import.TextureCount;
                for (int index = 0; index < pending.Import.TextureCount; index++)
                {
                    var texture = pending.Import.GetTexture(index);
                    if (texture != null && (texture.width > 2048 || texture.height > 2048)) throw new InvalidOperationException();
                }
                timeline.Begin("instantiate");
                pending.Pivot = new GameObject("RemoteModelPivot");
                var content = new GameObject("RemoteModelContent");
                content.transform.SetParent(pending.Pivot.transform, false);
                var instantiator = new GameObjectInstantiator(pending.Import, content.transform,
                    settings: new InstantiationSettings { Mask = ComponentType.Mesh });
                if (!await pending.Import.InstantiateMainSceneAsync(instantiator, pending.Cancellation.Token)
                    || (logger.Items?.Any(item => item.Type == LogType.Error) ?? false)
                    || !WithinTriangleBudget(content, Profile, out long triangles) || !ModelExaminationPose.Normalize(content.transform))
                    throw new InvalidOperationException();
                timeline.Triangles = triangles;
                timeline.End();
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                if (this == null || !isActiveAndEnabled || !Live(pending)) return;
                pending.Expiration = DateTimeOffset.Parse(access.expires_at, System.Globalization.CultureInfo.InvariantCulture);
                pending.ResourceId = id;
                pending.Sha256 = access.sha256;
                pending.RenewAt = ModelResourcePolicy.RenewAt(DateTimeOffset.UtcNow, pending.Expiration);
                pending.Nodes = pending.Pivot.GetComponentsInChildren<Transform>();
                pending.Renderers = content.GetComponentsInChildren<Renderer>();
                var bounds = pending.Renderers[0].bounds;
                foreach (var renderer in pending.Renderers) bounds.Encapsulate(renderer.bounds);
                pending.ExaminationScale = ModelExaminationPose.FrameScale(bounds.size, viewCamera != null ? viewCamera.fieldOfView : 70);
                pending.Size = bounds.size;
                pending.NaturalScale = 1 / content.transform.localScale.x;
                pending.ReadyAt = Time.realtimeSinceStartup;
                pending.Ready = retained = true;
                pending.Alternative = resource.alternative_text;
                timeline.MemoryAfter = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                timeline.Outcome = "ready";
                if (session != pending)
                {
                    // A resident piece stands on its pedestal; the piece of another record of its point may be on show.
                    var shown = session != null && session.Point == pending.Point && session.Slug != pending.Slug;
                    pending.Pivot.SetActive(!shown);
                    Face(pending, 0, false);
                    Place(pending, false);
                    Debug.Log(timeline.ToString());
                    return;
                }
                Progress = 1;
                // The resource's credits and provenance notes are the closing credits' (owner, 22/10/2026).
                Attribution = pending.Alternative ?? "";
                Face(pending, 0, true);
                UpdatePose();
                // Keep watching frames for a moment: the first draws upload textures and may compile shaders.
                observeUntil = Time.realtimeSinceStartup + 1;
                SetState(ModelLoadState.Ready, MuseumInterfaceText.Get("model_ready"));
            }
            catch (OperationCanceledException) { timeline.End(); timeline.Outcome = "cancelled"; }
            catch (Exception)
            {
                timeline.End();
                timeline.Outcome = "failed";
                if (this != null && session == pending && !pending.Cancellation.IsCancellationRequested)
                    SetState(ModelLoadState.Unavailable, MuseumInterfaceText.Get("model_unavailable"));
            }
            finally
            {
                if (preloading == pending) preloading = null;
                if (!retained)
                {
                    if (pending.Pivot != null) Destroy(pending.Pivot);
                    pending.Pivot = null;
                    pending.Dispose();
                    // Keep the target for an explicit retry, but never reuse a disposed token.
                    if (session == pending) session = null;
                    if (pending.Resident && residents.TryGetValue(pending.Point.Anchor.Key, out var resident) && resident == pending)
                        residents.Remove(pending.Point.Anchor.Key);
                }
            }
        }

        /// <summary>
        /// Asks the service again for access to the piece on show. The same file granted again keeps it in place; a service
        /// that answers but no longer grants it takes it away now, as revocation must; an unreachable service is asked again
        /// shortly while the current access lasts.
        /// </summary>
        private async void Renew(LoadSession current)
        {
            current.Renewing = true;
            try
            {
                string api = loader.ApiBase;
                using (var request = new UnityWebRequest(api + "/resources/" + Uri.EscapeDataString(current.ResourceId) + "/access?schema_version=1", "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = 15;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (this == null || !Live(current)) { request.Abort(); return; }
                        await Task.Yield();
                    }
                    if (this == null || !Live(current)) return;
                    var now = DateTimeOffset.UtcNow;
                    if (request.result == UnityWebRequest.Result.Success
                        && ModelResourcePolicy.TryAccess(api, request.downloadHandler.text, Profile, now, out var access, out _)
                        && string.Equals(access.sha256 ?? "", current.Sha256 ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Expiration = DateTimeOffset.Parse(access.expires_at, System.Globalization.CultureInfo.InvariantCulture);
                        current.RenewAt = ModelResourcePolicy.RenewAt(now, current.Expiration);
                        return;
                    }
                    if (request.result == UnityWebRequest.Result.Success
                        || request.result == UnityWebRequest.Result.ProtocolError && request.responseCode < 500)
                    {
                        // A piece no longer granted leaves, as revocation must: the one on show and, if resident, its pedestal.
                        if (session == current) Fail(MuseumInterfaceText.Get("model_expired"));
                        else DropResident(current);
                        return;
                    }
                    current.RenewAt = now.AddSeconds(ModelResourcePolicy.RenewRetrySeconds);
                }
            }
            finally { current.Renewing = false; }
        }

        /// <summary>
        /// Hash and structure checks of a downloaded file. They run on a worker thread where the platform has one, so a
        /// large file does not hold up the frame; the browser build has no worker threads and checks in place.
        /// </summary>
        private static Task<bool> Verify(byte[] data, string profile, string sha256)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult(ModelResourcePolicy.ValidateGlb(data, profile, sha256));
#else
            return Task.Run(() => ModelResourcePolicy.ValidateGlb(data, profile, sha256));
#endif
        }

        /// <summary>Logs a load that ends before its observation window closed, so no measurement is lost.</summary>
        private void FinishObservation()
        {
            if (observed == null) return;
            if (observed.Outcome == "pending") observed.Outcome = "superseded";
            Debug.Log(observed.ToString());
            observed = null;
        }

        private async Task Send(LoadSession pending, UnityWebRequest request, int timeout, bool model = false)
        {
            request.timeout = timeout;
            pending.Request = request;
            var operation = request.SendWebRequest();
            try
            {
                while (!operation.isDone)
                {
                    pending.Cancellation.Token.ThrowIfCancellationRequested();
                    if (model)
                    {
                        if (request.downloadedBytes > (ulong)ModelResourcePolicy.ByteLimit(Profile))
                        { request.Abort(); throw new InvalidOperationException(); }
                        if (session == pending) Progress = request.downloadProgress;
                    }
                    await Task.Yield();
                }
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException();
            }
            finally { pending.Request = null; }
        }

        private static bool WithinTriangleBudget(GameObject content, string profile, out long triangles)
        {
            triangles = 0;
            foreach (var filter in content.GetComponentsInChildren<MeshFilter>()) triangles += CountTriangles(filter.sharedMesh);
            foreach (var skin in content.GetComponentsInChildren<SkinnedMeshRenderer>()) triangles += CountTriangles(skin.sharedMesh);
            return triangles > 0 && triangles <= ModelResourcePolicy.TriangleLimit(profile);
        }
        private static long CountTriangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (int index = 0; index < mesh.subMeshCount; index++)
                if (mesh.GetTopology(index) == MeshTopology.Triangles) count += mesh.GetIndexCount(index) / 3;
            return count;
        }
        private void Fail(string status)
        {
            var failing = session;
            Release();
            // A resident piece that failed or ran out leaves its pedestal too.
            if (failing != null && failing.Resident) DropResident(failing);
            SetState(ModelLoadState.Unavailable, status);
        }
        public void Release()
        {
            var previous = session;
            session = null;
            if (previous != null && Live(previous))
            {
                // A resident piece stays on its pedestal when it stops being the piece on show.
                if (previous.Pivot != null) previous.Pivot.SetActive(true);
                Place(previous, false);
            }
            else
            {
                previous?.Cancel();
                // The point's resident piece comes back to its pedestal.
                if (previous?.Point != null && residents.TryGetValue(previous.Point.Anchor.Key, out var resident) && resident.Pivot != null)
                {
                    resident.Pivot.SetActive(true);
                    Place(resident, false);
                }
            }
            PoseHeld = false;
            if (studyLight != null) studyLight.enabled = false;
            interaction?.CancelModel();
            Progress = 0;
            Attribution = "";
            SetState(ModelLoadState.Idle, "");
        }
        private void SetState(ModelLoadState state, string status)
        { State = state; Status = status; Changed?.Invoke(); }
    }
}
