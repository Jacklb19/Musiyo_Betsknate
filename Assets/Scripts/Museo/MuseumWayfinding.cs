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
        /// <summary>Navigation area of open ground, maize and garden beds: walkable, but avoided where a path leads.</summary>
        public const int OpenGroundArea = 3;
        /// <summary>Navigation area of paths, thresholds and room floors.</summary>
        public const int PathArea = 4;
        /// <summary>
        /// How much a metre of open ground counts against a metre of path for the automatic tour, so it goes back to the
        /// nearest path and follows the paths instead of crossing the maize (owner, 22/10/2026).
        /// </summary>
        public const float OpenGroundCost = 8;

        /// <summary>
        /// Area of a walkable surface of the delivered museum, from its name or its parents' (museum_delivery.json):
        /// the valley's provisional ground and the dressed valley ground are open ground; paths, thresholds, passages
        /// and room floors are paths; anything else keeps the default area.
        /// </summary>
        public static int WalkArea(Transform surface)
        {
            for (int depth = 0; surface != null && depth < 4; depth++, surface = surface.parent)
            {
                int area = WalkArea(surface.name);
                if (area != 0) return area;
            }
            return 0;
        }

        public static int WalkArea(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (name.StartsWith("Terreno_") || name.StartsWith("ValleyGround") || name.StartsWith("Jajan_")) return OpenGroundArea;
            if (name.StartsWith("Sendero_") || name.StartsWith("Umbral_") || name.StartsWith("Pasaje_") || name.Contains("_Piso")
                || name.StartsWith("Patio_Sendero")) return PathArea;
            return 0;
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
            // Paths and floors lie a few centimetres over the valley ground; where they overlap the higher area wins,
            // so a path keeps its own area across the open ground it crosses.
            for (int index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                int area = WalkArea(source.component != null ? source.component.transform : null);
                if (area == 0) continue;
                source.area = area;
                sources[index] = source;
            }
            // Guidance runs between rooms, so the mesh covers them and not the whole terrain around the museum.
            var bounds = new Bounds(transform.position, Vector3.one);
            foreach (var anchor in anchors.Values)
                foreach (var corner in new[] { new Vector3(anchor.HalfSize.x, 0, anchor.HalfSize.y), new Vector3(-anchor.HalfSize.x, 0, anchor.HalfSize.y),
                    new Vector3(anchor.HalfSize.x, 3, -anchor.HalfSize.y), new Vector3(-anchor.HalfSize.x, 3, -anchor.HalfSize.y) })
                    bounds.Encapsulate(anchor.transform.TransformPoint(corner));
            bounds.Expand(8);
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
            if (!NavMesh.SamplePosition(start, out var from, 2, filter)) return false;
            // An exhibit may stand at the centre of its room, so the route may end beside any point of the room instead.
            var targets = new List<Vector3> { destination.transform.position };
            foreach (var point in destination.GetComponentsInChildren<PointAnchor>()) targets.Add(point.transform.position);
            foreach (var target in targets)
            {
                if (!NavMesh.SamplePosition(target, out var to, 2, filter)
                    || !NavMesh.CalculatePath(from.position, to.position, filter, path)
                    || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2) continue;
                corners = path.corners;
                return true;
            }
            return false;
        }
        /// <summary>
        /// Path between two floor positions that keeps to the paths: open ground and garden beds cost
        /// <see cref="OpenGroundCost"/> times a path's length, so the way first goes back to the nearest path and then
        /// follows the paths. False when either end is off the mesh or they do not connect.
        /// </summary>
        public bool TryPathAlongPaths(Vector3 start, Vector3 destination, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            if (!EnsureNavigation()) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            filter.SetAreaCost(OpenGroundArea, OpenGroundCost);
            filter.SetAreaCost(PathArea, 1);
            if (!NavMesh.SamplePosition(start, out var from, 2, filter) || !NavMesh.SamplePosition(destination, out var to, .6f, filter)
                || !NavMesh.CalculatePath(from.position, to.position, filter, path)
                || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 1) return false;
            corners = path.corners;
            return true;
        }

        /// <summary>Navigation area under a floor position, or -1 off the mesh.</summary>
        public int AreaAt(Vector3 position)
        {
            if (!EnsureNavigation()) return -1;
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(position, out var hit, .5f, filter)) return -1;
            for (int area = 0; area < 32; area++) if ((hit.mask & (1 << area)) != 0) return area;
            return -1;
        }

        /// <summary>
        /// Walkable path between two floor positions on the museum's navigation mesh, built from its colliders, so it
        /// goes round walls, the lagoon's rim and pedestals. False when either end is off the mesh or they do not connect.
        /// </summary>
        public bool TryPathBetween(Vector3 start, Vector3 destination, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            if (!EnsureNavigation()) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(start, out var from, 2, filter) || !NavMesh.SamplePosition(destination, out var to, .6f, filter)
                || !NavMesh.CalculatePath(from.position, to.position, filter, path)
                || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 1) return false;
            corners = path.corners;
            return true;
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
