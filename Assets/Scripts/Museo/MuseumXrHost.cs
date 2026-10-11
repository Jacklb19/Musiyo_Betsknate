using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Spatial presentation shares the visitor, action asset, views and EventSystem with desktop.</summary>
    public sealed class MuseumXrHost : MonoBehaviour
    {
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
        private InputSystemUIInputModule module;
        private TrackedDeviceRaycaster menuRaycaster;
        private bool menuVisible;
        private bool menuPlaced;
        private bool turnArmed;
        private bool moveArmed;
        private float verticalSpeed;
        public XROrigin Origin { get; private set; }
        public Transform TrackingOrigin => trackingOrigin;
        public bool Ready => Origin != null;

        public void Configure(DesktopVisitorController desktop, MuseumInteraction state, MuseumMenuHud presenter, Canvas host)
        {
            if (Ready) throw new InvalidOperationException("Spatial host is already configured.");
            if (desktop == null || state == null || presenter == null || host == null || EventSystem.current == null)
                throw new ArgumentException("Spatial host requires the existing visitor, menu, Canvas and EventSystem.");
            input = MuseumInput.Current;
            configuration = MuseumXrConfiguration.Load();
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
            rayLine.startColor = rayLine.endColor = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            rayLine.enabled = false;
            PlaceMenu();
            menuVisible = VisibleMenu;
            flow.Changed += RefreshPosition;
            interaction.Changed += RefreshPosition;
        }

        private bool VisibleMenu => flow.Phase == VisitPhase.Menu || interaction.State == InteractionState.Paused;
        private void RefreshPosition()
        {
            bool visible = VisibleMenu;
            if (visible && !menuVisible) PlaceMenu();
            menuVisible = visible;
        }

        /// <summary>The menu stays where it opened. It never pulls the visitor's view along.</summary>
        public void PlaceMenu()
        {
            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = visitor.forward;
            canvas.transform.SetPositionAndRotation(head.transform.position + forward * configuration.MenuDistance,
                Quaternion.LookRotation(forward, Vector3.up));
        }

        private void Update()
        {
            if (!Ready) return;
            if ((input.HeadState.ReadValue<int>() & 3) != 3) { turnArmed = moveArmed = false; verticalSpeed = 0; return; }
            bool blocked = flow.BlocksInput || interaction.BlocksMovement || interaction.HoldsVisitor
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true || input.Context != MuseumInputContext.Visit
                || flow.InputConsumedThisFrame;
            if (blocked) { verticalSpeed = 0; turnArmed = moveArmed = false; return; }
            var turn = input.SnapTurn.ReadValue<Vector2>().x;
            if (Mathf.Abs(turn) < .25f) turnArmed = true;
            if (turnArmed && Mathf.Abs(turn) > .7f)
            {
                visitor.RotateAround(head.transform.position, Vector3.up, Mathf.Sign(turn) * configuration.SnapTurnDegrees);
                turnArmed = false;
            }
            var move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1);
            if (move.sqrMagnitude < .04f) moveArmed = true;
            if (!moveArmed) move = Vector2.zero;
            var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            var direction = forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x;
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -1;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            body.Move((direction * configuration.WalkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (!Ready) return;
            if (!menuPlaced && (input.HeadState.ReadValue<int>() & 3) == 3) { PlaceMenu(); menuPlaced = true; }
            bool tracked = input.RightTracked.IsPressed() && (input.HeadState.ReadValue<int>() & 3) == 3;
            bool interfaceOpen = flow.BlocksInput || interaction.State == InteractionState.Paused
                || interaction.GetComponent<MuseumArrival>()?.Visible == true
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true;
            // The visit chrome cannot receive a click through an exhibit's floating screen.
            menuRaycaster.enabled = interfaceOpen;
            module.enabled = tracked;
            rayLine.enabled = tracked;
            var display = interaction.GetComponent<MuseumLecternDisplay>();
            if (!tracked) { display?.ClearAim(); return; }
            var position = trackingOrigin.TransformPoint(input.RightPosition.ReadValue<Vector3>());
            var rotation = trackingOrigin.rotation * input.RightRotation.ReadValue<Quaternion>();
            var ray = new Ray(position, rotation * Vector3.forward);
            float distance = Physics.Raycast(ray, out var hit, configuration.RayLength, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ? hit.distance : configuration.RayLength;
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
            if (flow != null) flow.Changed -= RefreshPosition;
            if (interaction != null) interaction.Changed -= RefreshPosition;
            if (input != null && input.Alive) input.SetSpatial(false);
        }
    }
}
