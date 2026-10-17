using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Spatial presentation shares the visitor, action asset, views and EventSystem with desktop.</summary>
    public sealed class MuseumXrHost : MonoBehaviour
    {
        /// <summary>A head cannot turn this far between two frames; such a jump is the tracking space being recentred.</summary>
        private const float RecenterJumpDegrees = 45;
        private MuseumInput input;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumMenuHud menu;
        private Camera head;
        private Transform visitor;
        private Transform trackingOrigin;
        private CharacterController body;
        private Canvas canvas;
        private MuseumXrConfiguration configuration;
        private LineRenderer rayLine;
        private MuseumTeleport teleport;
        private InputSystemUIInputModule module;
        private TrackedDeviceRaycaster menuRaycaster;
        private readonly MenuPlacementGate menuGate = new MenuPlacementGate();
        private readonly SnapTurnGate snapTurn = new SnapTurnGate();
        private readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        private RoomAnchor[] rooms;
        private HashSet<string> outdoorRooms;
        private HashSet<string> outdoorGround;
        private Color rayColor;
        private Color reachColor;
        private float? lastHeadYaw;
        private MuseumArrival arrival;
        private MuseumWayfinding wayfinding;
        private MuseumXrEntry entry;
        /// <summary>The headset's entry: the scenic set during the entry menu and the veil of the way in.</summary>
        public MuseumXrEntry Entry => entry;
        /// <summary>True while the shared canvas has something to show: a menu or the arrival card.</summary>
        private bool menuVisible;
        private bool moveArmed;
        private bool runRequested;
        private float verticalSpeed;
        public XROrigin Origin { get; private set; }
        public Transform TrackingOrigin => trackingOrigin;
        public bool Ready => Origin != null;
        /// <summary>True while a menu waits for a valid, steady head before it is placed in front of it.</summary>
        public bool MenuPlacementPending => menuGate.Pending;
        /// <summary>Walking speed in use, in metres per second; it only exceeds walking outside the museum.</summary>
        public float Speed { get; private set; }
        public bool Outdoors { get; private set; }
        /// <summary>Turning in use: the visitor's choice or the shipped default.</summary>
        public MuseumTurnMode TurnMode => menu.Preferences?.Turn(configuration.DefaultTurn) ?? configuration.DefaultTurn;
        public float TurnSpeed => configuration.TurnSpeed(menu.Preferences?.TurnSpeed ?? 0);

        public void Configure(DesktopVisitorController desktop, MuseumInteraction state, MuseumMenuHud presenter, Canvas host)
        {
            if (Ready) throw new InvalidOperationException("Spatial host is already configured.");
            if (desktop == null || state == null || presenter == null || host == null || EventSystem.current == null)
                throw new ArgumentException("Spatial host requires the existing visitor, menu, Canvas and EventSystem.");
            input = MuseumInput.Current;
            configuration = MuseumXrConfiguration.Current;
            interaction = state; menu = presenter; canvas = host;
            flow = interaction.GetComponent<MuseumVisitFlow>();
            visitor = desktop.transform; body = desktop.GetComponent<CharacterController>();
            head = desktop.GetComponentInChildren<Camera>();
            if (head == null || body == null || flow == null) throw new InvalidOperationException("Incomplete visitor rig.");
            desktop.enabled = false;
            // A headset never receives the staged camera or scripted desktop look.
            if (flow.Entry != null) { flow.Entry.HeadsetActive = () => true; flow.Entry.Apply(); }
            var offset = new GameObject("CameraFloorOffset");
            offset.transform.SetParent(visitor, false);
            trackingOrigin = offset.transform;
            head.transform.SetParent(trackingOrigin, false);
            head.transform.localPosition = Vector3.zero;
            head.transform.localRotation = Quaternion.identity;
            Origin = visitor.gameObject.AddComponent<XROrigin>();
            Origin.Origin = visitor.gameObject;
            Origin.Camera = head;
            Origin.CameraFloorOffsetObject = offset;
            Origin.CameraYOffset = configuration.DeviceHeight;
            Origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            input.BindUi(module);
            input.SetSpatial(true, trackingOrigin);
            var pose = head.gameObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(input.Reference(input.HeadPosition));
            pose.rotationInput = new InputActionProperty(input.Reference(input.HeadRotation));
            pose.trackingStateInput = new InputActionProperty(input.Reference(input.HeadState));
            interaction.GetComponent<DesktopPointInput>()?.Configure(head.transform, head);
            interaction.GetComponent<MuseumPointPresence>()?.Configure(head.transform);
            interaction.GetComponent<MuseumArrival>()?.Configure(head.transform);
            interaction.GetComponent<MuseumGuide>()?.Configure(head.transform);
            interaction.GetComponent<MuseumFarewell>()?.Configure(head.transform);
            interaction.GetComponent<MuseumWayfinding>()?.Configure(head.transform);
            interaction.GetComponent<MuseumServiceNotice>()?.Configure(head.transform);
            interaction.GetComponent<MuseumModelGrab>()?.ConfigureSpatial(trackingOrigin);
            interaction.GetComponent<MuseumAutoTour>()?.Configure(body, head.transform);
            if (canvas.TryGetComponent<CanvasScaler>(out var scaler)) scaler.enabled = false;
            if (canvas.TryGetComponent<GraphicRaycaster>(out var graphics)) graphics.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = head;
            var rect = (RectTransform)canvas.transform;
            rect.sizeDelta = new Vector2(1024, 900);
            rect.localScale = Vector3.one * (configuration.MenuWidth / rect.sizeDelta.x);
            menuRaycaster = canvas.gameObject.AddComponent<TrackedDeviceRaycaster>();
            menuRaycaster.checkFor3DOcclusion = true;
            menuRaycaster.maxDistance = configuration.RayLength;
            menu.SetSpatial(true);
            GetComponent<MuseumHud>()?.RefreshDeviceLabels();
            var line = new GameObject("ControllerPointer");
            line.transform.SetParent(visitor, false);
            rayLine = line.AddComponent<LineRenderer>();
            rayLine.useWorldSpace = true; rayLine.positionCount = 2;
            rayLine.startWidth = .004f; rayLine.endWidth = .002f;
            rayLine.sharedMaterial = Resources.Load<Material>("MuseumXrPointer");
            rayColor = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            reachColor = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.SecondaryAccentColor);
            rayLine.startColor = rayLine.endColor = rayColor;
            rayLine.enabled = false;
            teleport = new MuseumTeleport(visitor, body, rayLine.sharedMaterial, rayColor, new Color(.85f, .35f, .3f));
            rooms = interaction.Runtime.GetComponentsInChildren<RoomAnchor>(true);
            outdoorRooms = new HashSet<string>(configuration.OutdoorRooms, StringComparer.Ordinal);
            outdoorGround = new HashSet<string>(configuration.OutdoorGroundObjects, StringComparer.Ordinal);
            Speed = configuration.WalkSpeed;
            // The first menu waits for a real head pose; the visitor may be looking anywhere when the application starts.
            arrival = interaction.GetComponent<MuseumArrival>();
            wayfinding = interaction.GetComponent<MuseumWayfinding>();
            if (Debug.isDebugBuild) gameObject.AddComponent<MuseumSurfaceProbe>().Configure(head.transform);
            // The entry menu opens in the scenic set, away from the museum, when one is configured.
            entry = gameObject.AddComponent<MuseumXrEntry>();
            entry.Configure(flow, interaction.GetComponent<MuseumEntryBackdrop>(), visitor, head, () => HeadTracked);
            menuVisible = VisibleMenu;
            if (menuVisible) RequestMenuPlacement();
            else ShowCanvas();
            flow.Changed += RefreshPosition;
            interaction.Changed += RefreshPosition;
            if (arrival != null) arrival.Changed += RefreshPosition;
            if (wayfinding != null) wayfinding.Changed += RefreshPosition;
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems) subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        /// <summary>
        /// The shared canvas shows the entry and pause menus, the orientation menu and the arrival card. Nothing else on it
        /// is shown on a headset: left where the last menu opened, it would stand in the museum like a wall.
        /// </summary>
        private bool VisibleMenu => flow.Phase == VisitPhase.Menu || interaction.State == InteractionState.Paused
            || wayfinding != null && wayfinding.MenuOpen || !flow.BlocksInput && arrival != null && arrival.Visible;
        private bool HeadTracked => (input.HeadState.ReadValue<int>() & 3) == 3;

        private void RefreshPosition()
        {
            bool visible = VisibleMenu;
            // Every opening is placed anew in front of the gaze; while it stays open it stays where it is.
            if (visible && !menuVisible) RequestMenuPlacement();
            if (!visible) menuGate.Cancel();
            menuVisible = visible;
            ShowCanvas();
        }

        /// <summary>The system recentred the view: an open menu moves in front of the new forward direction.</summary>
        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            if (menuVisible) RequestMenuPlacement();
        }

        private void RequestMenuPlacement()
        {
            menuGate.Request();
            ShowCanvas();
        }

        /// <summary>
        /// A menu waiting for its place stays hidden instead of showing at the side of the view, and without a menu the
        /// canvas is not drawn at all.
        /// </summary>
        private void ShowCanvas() => canvas.enabled = menuVisible && !menuGate.Pending;

        /// <summary>The menu stays where it opened. It never pulls the visitor's view along.</summary>
        public void PlaceMenu()
        {
            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(head.transform.up, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = visitor.forward;
            canvas.transform.SetPositionAndRotation(head.transform.position + forward * configuration.MenuDistance,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        private void Update()
        {
            if (!Ready) return;
            if (!HeadTracked) { StopWalking(); teleport.Cancel(); return; }
            bool blocked = flow.BlocksInput || interaction.BlocksMovement || interaction.HoldsVisitor
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true || input.Context != MuseumInputContext.Visit
                || flow.InputConsumedThisFrame;
            if (blocked) { StopWalking(); teleport.Cancel(); return; }
            Teleport();
            var stick = input.Turn.ReadValue<Vector2>();
            float degrees;
            // While a piece is in the hand the right stick turns the piece, not the visitor.
            if (interaction.GetComponent<MuseumModelGrab>()?.Held == true) { snapTurn.Disarm(); degrees = 0; }
            else if (TurnMode == MuseumTurnMode.Snap) degrees = snapTurn.Step(stick.x, configuration.SnapTurnDegrees);
            else
            {
                // Switching to steps while the stick is held must not fire a step at once.
                snapTurn.Disarm();
                degrees = MuseumTurning.ContinuousDegrees(stick, configuration.TurnDeadzone, TurnSpeed, Time.deltaTime);
            }
            // The play area turns around the head, so the view turns in place and head tracking is untouched.
            if (degrees != 0) visitor.RotateAround(head.transform.position, Vector3.up, degrees);
            var move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1);
            if (move.sqrMagnitude < .04f) { moveArmed = true; runRequested = false; }
            if (!moveArmed) move = Vector2.zero;
            if (input.Sprint.WasPressedThisFrame() && move.sqrMagnitude >= .04f) runRequested = !runRequested;
            Outdoors = runRequested && MuseumOutdoors.Contains(Feet(), rooms, outdoorRooms, outdoorGround, visitor);
            float target = Outdoors ? configuration.RunSpeed : configuration.WalkSpeed;
            // Running eases in outside and eases out faster at a door, so indoor speed is never exceeded for long.
            Speed = Mathf.MoveTowards(Speed, target,
                configuration.RunAcceleration * (target < Speed ? 2 : 1) * Time.deltaTime);
            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            var direction = forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x;
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -1;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            body.Move((direction * Speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        /// <summary>
        /// Holding the button aims the arc from the right controller; releasing it on valid floor jumps there. A piece in
        /// the hand or a lost controller cancels the jump.
        /// </summary>
        private void Teleport()
        {
            bool held = input.Teleport.IsPressed() && input.RightTracked.IsPressed()
                && interaction.GetComponent<MuseumModelGrab>()?.Held != true;
            if (!teleport.Aiming && !input.Teleport.WasPressedThisFrame()) return;
            if (!input.RightTracked.IsPressed() || interaction.GetComponent<MuseumModelGrab>()?.Held == true) { teleport.Cancel(); return; }
            if (held)
            {
                var position = trackingOrigin.TransformPoint(input.RightPosition.ReadValue<Vector3>());
                var rotation = trackingOrigin.rotation * input.RightRotation.ReadValue<Quaternion>();
                teleport.Aim(position, rotation * Vector3.forward, Feet(), head.transform.forward);
                return;
            }
            if (teleport.Jump(Feet()))
            {
                verticalSpeed = 0;
                interaction.GetComponent<MuseumAutoTour>()?.Stop();
            }
        }

        private void StopWalking()
        {
            verticalSpeed = 0;
            snapTurn.Disarm();
            moveArmed = runRequested = Outdoors = false;
            Speed = configuration.WalkSpeed;
        }

        /// <summary>Floor point under the visitor's head, where the visitor actually stands in the play area.</summary>
        private Vector3 Feet()
        {
            var feet = visitor.TransformPoint(body.center) - Vector3.up * (body.height * .5f);
            return new Vector3(head.transform.position.x, feet.y, head.transform.position.z);
        }

        private void LateUpdate()
        {
            if (!Ready) return;
            bool headTracked = HeadTracked;
            // Being carried into or out of the entry set places an open menu again, in front of the new view.
            if (entry != null && entry.Step() && menuVisible) RequestMenuPlacement();
            float yaw = head.transform.eulerAngles.y;
            if (headTracked && lastHeadYaw.HasValue && menuVisible && !menuGate.Pending
                && Mathf.Abs(Mathf.DeltaAngle(lastHeadYaw.Value, yaw)) > RecenterJumpDegrees)
                RequestMenuPlacement();
            lastHeadYaw = headTracked ? yaw : (float?)null;
            if (menuGate.Step(headTracked, yaw, Time.unscaledDeltaTime, configuration.MenuSettleSeconds,
                    configuration.MenuSettleTurnSpeed, configuration.MenuPlacementTimeout))
            { PlaceMenu(); ShowCanvas(); }
            bool tracked = input.RightTracked.IsPressed() && headTracked;
            bool interfaceOpen = flow.BlocksInput || interaction.State == InteractionState.Paused
                || interaction.GetComponent<MuseumArrival>()?.Visible == true
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true;
            // The visit chrome cannot receive a click through an exhibit's floating screen.
            menuRaycaster.enabled = interfaceOpen;
            module.enabled = tracked;
            var grab = interaction.GetComponent<MuseumModelGrab>();
            rayLine.enabled = tracked && grab?.Held != true && !teleport.Aiming;
            var display = interaction.GetComponent<MuseumLecternDisplay>();
            if (!tracked) { display?.ClearAim(); return; }
            var position = trackingOrigin.TransformPoint(input.RightPosition.ReadValue<Vector3>());
            var rotation = trackingOrigin.rotation * input.RightRotation.ReadValue<Quaternion>();
            var ray = new Ray(position, rotation * Vector3.forward);
            float distance = Physics.Raycast(ray, out var hit, configuration.RayLength, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ? hit.distance : configuration.RayLength;
            // A piece within reach of the ray is shown with the second accent, so the visitor knows the grip takes it.
            if (grab != null && grab.Hovered && grab.HoverDistance > 0) distance = Mathf.Min(distance, grab.HoverDistance);
            rayLine.startColor = rayLine.endColor = grab?.Hovered == true ? reachColor : rayColor;
            rayLine.SetPosition(0, position); rayLine.SetPosition(1, ray.GetPoint(distance));
            var pointerDevice = input.Click.activeControl?.device;
            bool overInterface = pointerDevice != null && module.IsPointerOverGameObject(pointerDevice.deviceId);
            if (interfaceOpen || overInterface || flow.InputConsumedThisFrame)
            { display?.ClearAim(); return; }
            if (display != null && display.Surface != null &&
                new Plane(display.Surface.forward, display.Surface.position).Raycast(ray, out var screenDistance)
                && screenDistance <= distance + .01f) display.Aim(ray);
            else display?.ClearAim();
            if (input.Click.WasPressedThisFrame()) display?.Press();
        }

        private void OnDestroy()
        {
            foreach (var subsystem in subsystems) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            if (flow != null) flow.Changed -= RefreshPosition;
            if (interaction != null) interaction.Changed -= RefreshPosition;
            if (arrival != null) arrival.Changed -= RefreshPosition;
            if (wayfinding != null) wayfinding.Changed -= RefreshPosition;
            if (input != null && input.Alive) input.SetSpatial(false);
        }
    }
}
