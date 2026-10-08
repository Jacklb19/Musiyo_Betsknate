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
            public float ExaminationScale = 1;
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
        public ModelLoadState State { get; private set; }
        public float Progress { get; private set; }
        public string Status { get; private set; } = "";
        public string Attribution { get; private set; } = "";
        public bool CanExamine => State == ModelLoadState.Ready && session != null
            && session.Point == interaction.ActivePoint && session.Slug == interaction.SelectedElement?.slug;
        public Transform Model => session?.Pivot != null ? session.Pivot.transform : null;
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
        private void OnDestroy() { if (studyLight != null) Destroy(studyLight.gameObject); }
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
                if (!selected.has_3d_model) { Release(); attemptedKey = null; }
                else RequestModel(interaction.ActivePoint, selected.slug);
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
            var currentRoom = visitor != null ? roomAnchors.FirstOrDefault(room => room.Contains(visitor.position)) : null;
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
                && session == null && attemptedKey == null && visitor != null
                && Vector3.Distance(visitor.position, interaction.ActivePoint.Anchor.transform.position)
                    <= interaction.ActivePoint.Anchor.ActivationRadius + 4)
                RequestModel(interaction.ActivePoint, interaction.SelectedElement.slug);
            if (interaction.State == InteractionState.ModelExamination)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    var direction = new Vector2((keyboard.rightArrowKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed ? 1 : 0),
                        (keyboard.downArrowKey.isPressed ? 1 : 0) - (keyboard.upArrowKey.isPressed ? 1 : 0));
                    Rotate(direction * (80 * Time.unscaledDeltaTime));
                    if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed) Zoom(-Time.unscaledDeltaTime * .4f);
                    if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed) Zoom(Time.unscaledDeltaTime * .4f);
                    if (keyboard.homeKey.wasPressedThisFrame) ResetPose();
                }
                var mouse = Mouse.current;
                if (mouse != null && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                {
                    if (mouse.leftButton.isPressed)
                    {
                        var delta = mouse.delta.ReadValue();
                        Rotate(new Vector2(delta.x, -delta.y) * .3f);
                    }
                    Zoom(-mouse.scroll.ReadValue().y / 120 * .08f);
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

        private void UpdatePose()
        {
            if (session?.Pivot == null || !session.Ready) return;
            var pivot = session.Pivot.transform;
            bool examining = interaction.State == InteractionState.ModelExamination;
            if (studyLight != null) studyLight.enabled = examining;
            // Pause keeps the model at its last examination pose.
            if (interaction.State == InteractionState.Paused) return;
            var parent = examining ? viewCamera.transform : session.Point.Anchor.LookTarget;
            if (pivot.parent != parent) pivot.SetParent(parent, false);
            pivot.localPosition = examining ? Vector3.forward * pose.Distance : Vector3.zero;
            pivot.localRotation = examining ? pose.Rotation : Quaternion.identity;
            pivot.localScale = Vector3.one * (examining ? session.ExaminationScale : 1);
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
                VisitorRoom = visitor != null ? roomAnchors.FirstOrDefault(room => room.Contains(visitor.position)) : null };
            session = pending;
            SetState(ModelLoadState.Loading, MuseumInterfaceText.Get("model_loading"));
            Load(pending);
        }

        private async void Load(LoadSession pending)
        {
            bool retained = false;
            try
            {
                string api = loader.ApiBase;
                string elementJson;
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
                using (var request = new UnityWebRequest(api + "/resources/" + Uri.EscapeDataString(id) + "/access?schema_version=1", "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    await Send(pending, request, 15);
                    if (!ModelResourcePolicy.TryAccess(api, request.downloadHandler.text, Profile, DateTimeOffset.UtcNow, out access, out url))
                        throw new InvalidOperationException();
                }
                byte[] data;
                using (var request = UnityWebRequest.Get(url))
                {
                    await Send(pending, request, 60, true);
                    data = request.downloadHandler.data;
                }
                if (!ModelResourcePolicy.ValidateGlb(data, Profile, access.sha256)) throw new InvalidOperationException();
                var logger = new CollectingLogger();
                pending.Import = new GltfImport(logger: logger);
                if (!await pending.Import.Load(data, url, new ImportSettings { GenerateMipMaps = true }, pending.Cancellation.Token))
                    throw new InvalidOperationException();
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                for (int index = 0; index < pending.Import.TextureCount; index++)
                {
                    var texture = pending.Import.GetTexture(index);
                    if (texture != null && (texture.width > 2048 || texture.height > 2048)) throw new InvalidOperationException();
                }
                pending.Pivot = new GameObject("RemoteModelPivot");
                var content = new GameObject("RemoteModelContent");
                content.transform.SetParent(pending.Pivot.transform, false);
                var instantiator = new GameObjectInstantiator(pending.Import, content.transform,
                    settings: new InstantiationSettings { Mask = ComponentType.Mesh });
                if (!await pending.Import.InstantiateMainSceneAsync(instantiator, pending.Cancellation.Token)
                    || (logger.Items?.Any(item => item.Type == LogType.Error) ?? false)
                    || !WithinTriangleBudget(content, Profile) || !ModelExaminationPose.Normalize(content.transform))
                    throw new InvalidOperationException();
                pending.Cancellation.Token.ThrowIfCancellationRequested();
                if (session != pending || this == null || !isActiveAndEnabled) return;
                pending.Expiration = DateTimeOffset.Parse(access.expires_at, System.Globalization.CultureInfo.InvariantCulture);
                pending.Nodes = pending.Pivot.GetComponentsInChildren<Transform>();
                var bounds = content.GetComponentsInChildren<Renderer>()[0].bounds;
                foreach (var renderer in content.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                pending.ExaminationScale = ModelExaminationPose.FrameScale(bounds.size, viewCamera != null ? viewCamera.fieldOfView : 70);
                pending.Ready = retained = true;
                Progress = 1;
                Attribution = string.Join("\n", new[] { resource.alternative_text,
                    string.IsNullOrEmpty(resource.credit) ? null : MuseumInterfaceText.Format("narration_credit", resource.credit),
                    string.IsNullOrEmpty(resource.provenance) ? null : MuseumInterfaceText.Format("narration_provenance", resource.provenance) }.Where(value => !string.IsNullOrEmpty(value)));
                UpdatePose();
                SetState(ModelLoadState.Ready, MuseumInterfaceText.Get("model_ready"));
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
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

        private static bool WithinTriangleBudget(GameObject content, string profile)
        {
            long triangles = 0;
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
