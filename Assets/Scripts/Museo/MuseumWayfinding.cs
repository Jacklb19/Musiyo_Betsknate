using System;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Museum
{
    [RequireComponent(typeof(TourRuntime), typeof(MuseumInteraction))]
    public sealed class MuseumWayfinding : MonoBehaviour
    {
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform visitor;
        private readonly List<RoomContractV1> rooms = new List<RoomContractV1>();
        private readonly Dictionary<string, RoomAnchor> anchors = new Dictionary<string, RoomAnchor>();
        private NavMeshData navigation;
        private int agentType;
        private NavMeshDataInstance navigationInstance;
        private LineRenderer route;
        private NavMeshPath path;
        private Vector3 lastPosition = Vector3.positiveInfinity;
        private float nextRefresh;
        private bool suggested;
        public bool MenuOpen { get; private set; }
        public string CurrentRoomKey { get; private set; }
        public string TargetRoomKey { get; private set; }
        public string CurrentRoomName => rooms.FirstOrDefault(room => room.key == CurrentRoomKey)?.name ?? MuseumInterfaceText.Get("wayfinding_between_rooms");
        public string Status { get; private set; } = "";
        public IReadOnlyList<RoomContractV1> Rooms => rooms;
        public bool NavigationReady => navigation != null;
        public int CornerCount => route != null ? route.positionCount : 0;
        public event Action Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;
        private void Awake()
        {
            runtime = GetComponent<TourRuntime>();
            interaction = GetComponent<MuseumInteraction>();
            path = new NavMeshPath();
        }
        private void OnEnable() { runtime.Changed += Bind; Bind(); }
        private void OnDisable()
        {
            runtime.Changed -= Bind;
            MenuOpen = false;
            if (navigationInstance.valid) navigationInstance.Remove();
            if (navigation != null) Destroy(navigation);
            navigation = null;
            if (route != null) Destroy(route.gameObject);
            route = null;
        }
        private void Bind()
        {
            rooms.Clear();
            anchors.Clear();
            foreach (var anchor in GetComponentsInChildren<RoomAnchor>())
                if (!string.IsNullOrEmpty(anchor.Key) && !anchors.ContainsKey(anchor.Key)) anchors.Add(anchor.Key, anchor);
            if (runtime.Contract != null)
                rooms.AddRange(runtime.Contract.rooms.Where(room => anchors.ContainsKey(room.key)));
            StopGuidance();
            CurrentRoomKey = null;
            MenuOpen = false;
            Changed?.Invoke();
        }
        public void ToggleMenu()
        {
            if (interaction.BlocksMovement) return;
            MenuOpen = !MenuOpen;
            if (MenuOpen) visitor?.GetComponent<DesktopVisitorController>()?.CapturePointer(false);
            Changed?.Invoke();
        }
        public void CloseMenu() { MenuOpen = false; Changed?.Invoke(); }
        public void StopGuidance()
        {
            suggested = false;
            TargetRoomKey = null;
            ClearPath();
            Status = MuseumInterfaceText.Get("wayfinding_free");
            Changed?.Invoke();
        }
        public void FollowSuggested()
        {
            UpdateRoom();
            var index = rooms.FindIndex(room => room.key == CurrentRoomKey);
            if (index >= rooms.Count - 1 && index >= 0)
            { StopGuidance(); Status = MuseumInterfaceText.Get("wayfinding_last_room"); CloseMenu(); return; }
            if (rooms.Count == 0) return;
            suggested = true;
            SetTarget(rooms[index + 1].key);
        }
        public void SelectRoom(string key)
        {
            if (!rooms.Any(room => room.key == key)) return;
            UpdateRoom();
            if (key == CurrentRoomKey)
            { StopGuidance(); Status = MuseumInterfaceText.Format("wayfinding_already_here", CurrentRoomName); CloseMenu(); return; }
            suggested = false;
            SetTarget(key);
        }
        private void SetTarget(string key)
        {
            TargetRoomKey = key;
            MenuOpen = false;
            lastPosition = Vector3.positiveInfinity;
            RefreshPath();
            Changed?.Invoke();
        }
        private void Update()
        {
            if (visitor == null) return;
            UpdateRoom();
            if (route != null) route.enabled = !MenuOpen && !interaction.BlocksMovement
                && interaction.State != InteractionState.PointFocus && route.positionCount > 1;
            if (TargetRoomKey != null && !MenuOpen && Time.unscaledTime >= nextRefresh
                && (visitor.position - lastPosition).sqrMagnitude > .25f)
            {
                nextRefresh = Time.unscaledTime + .5f;
                RefreshPath();
            }
        }
        private void UpdateRoom()
        {
            if (visitor == null) return;
            string key = rooms.FirstOrDefault(room => anchors[room.key].Contains(visitor.position))?.key;
            if (key == CurrentRoomKey) return;
            CurrentRoomKey = key;
            if (key != null && key == TargetRoomKey)
            {
                var index = rooms.FindIndex(room => room.key == key);
                if (suggested && index + 1 < rooms.Count) SetTarget(rooms[index + 1].key);
                else
                {
                    bool finished = suggested;
                    StopGuidance();
                    Status = finished ? MuseumInterfaceText.Get("wayfinding_route_finished")
                        : MuseumInterfaceText.Format("wayfinding_arrived", CurrentRoomName);
                }
            }
            Changed?.Invoke();
        }
        private bool EnsureNavigation()
        {
            if (navigation != null) return true;
            if (NavMesh.GetSettingsCount() == 0) return false;
            var settings = NavMesh.GetSettingsByIndex(0);
            agentType = settings.agentTypeID;
            settings.agentRadius = .3f;
            settings.agentHeight = 1.75f;
            settings.agentClimb = .3f;
            settings.agentSlope = 30;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .1f;
            var sources = new List<NavMeshBuildSource>();
            var excluded = new List<NavMeshBuildMarkup>();
            foreach (var controller in GetComponentsInChildren<CharacterController>())
                excluded.Add(new NavMeshBuildMarkup { root = controller.transform, ignoreFromBuild = true });
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (collider.isTrigger || collider.attachedRigidbody != null)
                    excluded.Add(new NavMeshBuildMarkup { root = collider.transform, ignoreFromBuild = true });
            NavMeshBuilder.CollectSources(transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, excluded, sources);
            if (sources.Count == 0) return false;
            var bounds = new Bounds(transform.position, Vector3.one);
            foreach (var collider in GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && !(collider is CharacterController)) bounds.Encapsulate(collider.bounds);
            bounds.Expand(4);
            navigation = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navigation == null) return false;
            navigationInstance = NavMesh.AddNavMeshData(navigation);
            var lineObject = new GameObject("OptionalWalkingRoute");
            lineObject.transform.SetParent(transform, false);
            route = lineObject.AddComponent<LineRenderer>();
            route.useWorldSpace = true;
            route.widthMultiplier = .08f;
            route.shadowCastingMode = ShadowCastingMode.Off;
            route.receiveShadows = false;
            route.sharedMaterial = Resources.Load<Material>("MuseumRoute");
            route.positionCount = 0;
            return true;
        }
        public bool TryPath(Vector3 start, string roomKey, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            if (!anchors.TryGetValue(roomKey, out var destination) || !EnsureNavigation()) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(start, out var from, 2, filter)
                || !NavMesh.SamplePosition(destination.transform.position, out var to, 2, filter)
                || !NavMesh.CalculatePath(from.position, to.position, filter, path)
                || path.status != NavMeshPathStatus.PathComplete) return false;
            corners = path.corners;
            return corners.Length > 1;
        }
        private void RefreshPath()
        {
            if (visitor == null || TargetRoomKey == null) return;
            lastPosition = visitor.position;
            var body = visitor.GetComponent<CharacterController>();
            var feet = body != null ? visitor.TransformPoint(body.center) - Vector3.up * body.height * .5f : visitor.position;
            if (!TryPath(feet, TargetRoomKey, out var corners))
            {
                ClearPath();
                Status = MuseumInterfaceText.Get("wayfinding_no_path");
            }
            else
            {
                route.positionCount = corners.Length;
                route.SetPositions(corners.Select(corner => corner + Vector3.up * .035f).ToArray());
                Status = MuseumInterfaceText.Format("wayfinding_heading", rooms.First(room => room.key == TargetRoomKey).name);
            }
            Changed?.Invoke();
        }
        private void ClearPath() { if (route != null) route.positionCount = 0; }
    }
}
