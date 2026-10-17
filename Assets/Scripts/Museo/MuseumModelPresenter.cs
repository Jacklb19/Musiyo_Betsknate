using System;
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
            public void Cancel()
            {
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
            attemptedKey = null;
        }

        private void OnTourChanged() { Release(); attemptedKey = null; }
        private void OnInteractionChanged()
        {
            var selected = interaction.SelectedElement;
            if (selected != null && interaction.State != InteractionState.Paused)
            {
                if (!selected.has_3d_model) { switchSlug = null; Release(); attemptedKey = null; }
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
                if (visitor != null && interaction.State != InteractionState.ModelExamination
                    && interaction.State != InteractionState.Paused
                    && (Vector3.Distance(visitor.position, session.Point.Anchor.transform.position) > 15
                        || currentRoom != null && session.VisitorRoom != null && currentRoom != session.VisitorRoom))
                { Release(); attemptedKey = null; SetState(ModelLoadState.Idle, MuseumInterfaceText.Get("model_released")); }
                else if (session.VisitorRoom == null && currentRoom != null) session.VisitorRoom = currentRoom;
            }
            if (interaction.State == InteractionState.ElementSelected && interaction.SelectedElement?.has_3d_model == true
                && session == null && attemptedKey == null && switchSlug == null && visitor != null
                && Vector3.Distance(visitor.position, interaction.ActivePoint.Anchor.transform.position)
                    <= interaction.ActivePoint.Anchor.ActivationRadius + 4)
                RequestModel(interaction.ActivePoint, interaction.SelectedElement.slug);
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
            rotation = parent.rotation;
            localScale = fit;
            return true;
        }

        private void UpdatePose()
        {
            if (session?.Pivot == null || !session.Ready) return;
            var pivot = session.Pivot.transform;
            bool examining = interaction.State == InteractionState.ModelExamination;
            if (studyLight != null) studyLight.enabled = examining;
            // Pause keeps the model at its last examination pose.
            if (interaction.State == InteractionState.Paused) return;
            // A piece in the visitor's hand, or on its way back, is placed by whoever holds it.
            if (PoseHeld && !examining) return;
            // A pedestal with a mount carries the model standing on it; elsewhere it floats at the point.
            var mount = examining ? null : session.Point.Anchor.GetComponentInChildren<PointExhibitMount>();
            var parent = examining ? viewCamera.transform : mount != null ? mount.transform : session.Point.Anchor.LookTarget;
            if (pivot.parent != parent) pivot.SetParent(parent, false);
            float reveal = Mathf.SmoothStep(.85f, 1, Mathf.Clamp01((Time.realtimeSinceStartup - session.ReadyAt) / RevealSeconds));
            var fit = mount != null ? mount.ScaleFor(session.Size, session.NaturalScale) : Vector3.one;
            pivot.localPosition = examining ? Vector3.forward * pose.Distance
                : mount != null ? Vector3.up * (session.Size.y * fit.y * reveal * .5f) : Vector3.zero;
            pivot.localRotation = examining ? pose.Rotation : Quaternion.identity;
            pivot.localScale = examining ? Vector3.one * session.ExaminationScale : fit * reveal;
            int examinationLayer = LayerMask.NameToLayer("ModelExamination");
            foreach (var item in session.Nodes)
                item.gameObject.layer = examining && examinationLayer >= 0 ? examinationLayer : 0;
        }

        private void RequestModel(TourPoint point, string slug)
        {
            if (!isActiveAndEnabled || point == null) return;
            string key = point.Anchor.Key + "/" + slug;
            if (attemptedKey == key) return;
            Release();
            attemptedKey = key;
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
                if (session != pending || this == null || !isActiveAndEnabled) return;
                pending.Expiration = DateTimeOffset.Parse(access.expires_at, System.Globalization.CultureInfo.InvariantCulture);
                pending.Nodes = pending.Pivot.GetComponentsInChildren<Transform>();
                pending.Renderers = content.GetComponentsInChildren<Renderer>();
                var bounds = pending.Renderers[0].bounds;
                foreach (var renderer in pending.Renderers) bounds.Encapsulate(renderer.bounds);
                pending.ExaminationScale = ModelExaminationPose.FrameScale(bounds.size, viewCamera != null ? viewCamera.fieldOfView : 70);
                pending.Size = bounds.size;
                pending.NaturalScale = 1 / content.transform.localScale.x;
                pending.ReadyAt = Time.realtimeSinceStartup;
                pending.Ready = retained = true;
                Progress = 1;
                Attribution = string.Join("\n", new[] { resource.alternative_text,
                    string.IsNullOrEmpty(resource.credit) ? null : MuseumInterfaceText.Format("narration_credit", resource.credit),
                    string.IsNullOrEmpty(resource.provenance) ? null : MuseumInterfaceText.Format("narration_provenance", resource.provenance) }.Where(value => !string.IsNullOrEmpty(value)));
                UpdatePose();
                timeline.MemoryAfter = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                timeline.Outcome = "ready";
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
                if (!retained)
                {
                    if (pending.Pivot != null) Destroy(pending.Pivot);
                    pending.Pivot = null;
                    pending.Dispose();
                    // Keep the target for an explicit retry, but never reuse a disposed token.
                    if (session == pending) session = null;
                }
            }
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
                        Progress = request.downloadProgress;
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
            Release();
            SetState(ModelLoadState.Unavailable, status);
        }
        public void Release()
        {
            var previous = session;
            session = null;
            previous?.Cancel();
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
