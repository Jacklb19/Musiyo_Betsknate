using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Lets the visitor take the published piece on show straight from its pedestal, move and turn it, and let it go: it
    /// then glides back to its exhibition pose. A headset takes it with the right controller's grip, close by or along the
    /// pointer ray; a desktop holds the right mouse button over it and turns it with the mouse. The piece has no colliders,
    /// so it never pushes the visitor or anything else, and nothing is thrown.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(MuseumModelPresenter))]
    public sealed class MuseumModelGrab : MonoBehaviour
    {
        private enum Holder { None, Hand, Desktop }
        /// <summary>A piece taken along the ray comes to the hand over this time instead of jumping.</summary>
        private const float AttachSeconds = .2f;
        private MuseumInteraction interaction;
        private MuseumModelPresenter presenter;
        private MuseumVisitFlow flow;
        private MuseumAutoTour autoTour;
        private Camera viewCamera;
        private DesktopVisitorController desktop;
        private Transform trackingOrigin;
        private Holder holder;
        private Transform piece;
        private Vector3 offset;
        private Quaternion turn;
        private Vector3 takenPosition;
        private Quaternion takenRotation;
        private float attach;
        private float returnElapsed;
        private Vector3 returnPosition;
        private Quaternion returnRotation;
        public bool Held => holder != Holder.None;
        public bool Returning { get; private set; }
        /// <summary>The pointer or hand reaches a piece that the grab would take.</summary>
        public bool Hovered { get; private set; }
        /// <summary>Distance along the pointer to the reached piece, for drawing the pointer up to it.</summary>
        public float HoverDistance { get; private set; }
        public Transform Piece => piece;
        public event Action Changed;

        public void Configure(Camera camera, DesktopVisitorController visitor)
        {
            viewCamera = camera;
            desktop = visitor;
        }

        /// <summary>A headset holds pieces with the right controller, whose pose is relative to the tracking origin.</summary>
        public void ConfigureSpatial(Transform origin) => trackingOrigin = origin;

        private void Awake()
        {
            interaction = GetComponent<MuseumInteraction>();
            presenter = GetComponent<MuseumModelPresenter>();
        }

        /// <summary>Whether a piece is on show and the visit is in a state where it may be taken.</summary>
        public bool CanTake
        {
            get
            {
                if (flow == null) flow = GetComponent<MuseumVisitFlow>();
                if (autoTour == null) autoTour = GetComponent<MuseumAutoTour>();
                return presenter.State == ModelLoadState.Ready && presenter.Model != null && presenter.Renderers.Length > 0
                    && interaction.State != InteractionState.ModelExamination && interaction.State != InteractionState.Paused
                    && !interaction.BlocksMovement && (flow == null || !flow.BlocksInput) && autoTour?.Moving != true;
            }
        }

        /// <summary>World bounds of the piece on show.</summary>
        public bool TryBounds(out Bounds bounds)
        {
            bounds = default;
            var renderers = presenter.Renderers;
            bool found = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        /// <summary>
        /// Whether a hand at a position, or its ray, reaches the piece: the hand is within the piece's bounds plus a small
        /// margin, or the ray meets them before anything solid.
        /// </summary>
        public static bool Reaches(Bounds piece, Ray ray, Vector3 hand, float reach, float clearDistance, out float distance)
        {
            var near = piece;
            near.Expand(reach * 2);
            if (near.Contains(hand)) { distance = 0; return true; }
            return piece.IntersectRay(ray, out distance) && distance <= clearDistance + .05f;
        }

        /// <summary>
        /// Takes the piece: from then on it follows the holder at the offset it was taken with. A piece taken from a
        /// distance first glides into the hand.
        /// </summary>
        public bool Take(Vector3 holderPosition, Quaternion holderRotation, bool fromDistance, bool desktopHold)
        {
            if (!CanTake || Held) return false;
            piece = presenter.Model;
            presenter.PoseHeld = true;
            // A piece taken while it was still growing in has its full exhibition size in the hand.
            if (presenter.TryExhibitPose(out _, out _, out var scale)) piece.localScale = scale;
            Returning = false;
            takenPosition = piece.position;
            takenRotation = piece.rotation;
            turn = Quaternion.Inverse(holderRotation) * piece.rotation;
            if (desktopHold)
            {
                holder = Holder.Desktop;
                offset = Vector3.forward * MuseumExperienceConfiguration.Current.GrabDesktopDistance;
                attach = 0;
                if (desktop != null) desktop.LookSuspended = true;
            }
            else
            {
                holder = Holder.Hand;
                TryBounds(out var bounds);
                offset = fromDistance ? Vector3.forward * (.08f + bounds.extents.magnitude * .5f)
                    : Quaternion.Inverse(holderRotation) * (piece.position - holderPosition);
                attach = fromDistance ? 0 : 1;
                Pulse(.3f, .05f);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Moves the held piece with its holder. The pose kept for the exhibition is never derived from this one.</summary>
        public void Follow(Vector3 holderPosition, Quaternion holderRotation, float elapsed)
        {
            if (!Held || piece == null) return;
            attach = Mathf.Min(1, attach + Mathf.Max(0, elapsed) / AttachSeconds);
            var position = holderPosition + holderRotation * offset;
            var rotation = holderRotation * turn;
            float blend = Mathf.SmoothStep(0, 1, attach);
            piece.SetPositionAndRotation(Vector3.Lerp(takenPosition, position, blend), Quaternion.Slerp(takenRotation, rotation, blend));
            // Carried too far from its pedestal, the piece goes back on its own.
            if (presenter.TryExhibitPose(out var home, out _, out _)
                && Vector3.Distance(piece.position, home) > MuseumXrConfiguration.Current.GrabMaxDistance) Release();
        }

        /// <summary>Turns a piece held on a desktop, around the view's own axes.</summary>
        public void Turn(Vector2 degrees)
        {
            if (holder != Holder.Desktop || !MuseumExperienceConfiguration.Finite(degrees.x) || !MuseumExperienceConfiguration.Finite(degrees.y)) return;
            turn = Quaternion.AngleAxis(-degrees.x, Vector3.up) * Quaternion.AngleAxis(degrees.y, Vector3.right) * turn;
        }

        /// <summary>Lets go: the piece glides back to the pose computed from its pedestal.</summary>
        public void Release()
        {
            if (!Held) return;
            if (holder == Holder.Desktop && desktop != null) desktop.LookSuspended = false;
            holder = Holder.None;
            if (piece != null)
            {
                Returning = true;
                returnElapsed = 0;
                returnPosition = piece.position;
                returnRotation = piece.rotation;
            }
            Changed?.Invoke();
        }

        /// <summary>Forgets the piece at once: it was replaced or removed, or the examination now places it.</summary>
        private void Drop()
        {
            if (holder == Holder.Desktop && desktop != null) desktop.LookSuspended = false;
            bool changed = Held || Returning;
            holder = Holder.None;
            Returning = false;
            piece = null;
            presenter.PoseHeld = false;
            if (changed) Changed?.Invoke();
        }

        /// <summary>Advances the glide back; tests call it directly.</summary>
        public void StepReturn(float elapsed)
        {
            if (!Returning) return;
            if (piece == null || !presenter.TryExhibitPose(out var home, out var homeRotation, out _)) { Drop(); return; }
            returnElapsed += Mathf.Max(0, elapsed);
            float progress = Mathf.Clamp01(returnElapsed / MuseumExperienceConfiguration.Current.GrabReturnSeconds);
            float eased = Mathf.SmoothStep(0, 1, progress);
            piece.SetPositionAndRotation(Vector3.Lerp(returnPosition, home, eased), Quaternion.Slerp(returnRotation, homeRotation, eased));
            if (progress < 1) return;
            Returning = false;
            piece = null;
            presenter.PoseHeld = false;
            presenter.RefreshPose();
            Changed?.Invoke();
        }

        private void Update()
        {
            // A replaced or removed piece, or an examination that now places it, ends any hold at once.
            if ((Held || Returning) && (piece == null || presenter.Model != piece
                || interaction.State == InteractionState.ModelExamination)) { Drop(); }
            // Pausing, a menu, a screen panel or the automatic tour sends a held piece back.
            if (Held && !CanTakeIgnoringHold()) Release();
            if (Returning) StepReturn(Time.unscaledDeltaTime);
            var input = MuseumInput.Current;
            if (input.Spatial) UpdateHand(input);
            else UpdateDesktop(input);
        }

        private bool CanTakeIgnoringHold()
        {
            if (flow == null) flow = GetComponent<MuseumVisitFlow>();
            if (autoTour == null) autoTour = GetComponent<MuseumAutoTour>();
            return interaction.State != InteractionState.Paused && !interaction.BlocksMovement
                && (flow == null || !flow.BlocksInput) && autoTour?.Moving != true;
        }

        private void UpdateHand(MuseumInput input)
        {
            bool tracked = trackingOrigin != null && input.RightTracked.IsPressed() && (input.HeadState.ReadValue<int>() & 3) == 3;
            // Losing the controller or the headset lets go, so the piece is never left hanging in the air.
            if (!tracked) { if (holder == Holder.Hand) Release(); SetHover(false, 0); return; }
            var position = trackingOrigin.TransformPoint(input.RightPosition.ReadValue<Vector3>());
            var rotation = trackingOrigin.rotation * input.RightRotation.ReadValue<Quaternion>();
            if (holder == Holder.Hand)
            {
                SetHover(false, 0);
                if (!input.Grab.IsPressed()) Release();
                else Follow(position, rotation, Time.unscaledDeltaTime);
                return;
            }
            var ray = new Ray(position, rotation * Vector3.forward);
            var settings = MuseumXrConfiguration.Current;
            float clear = Physics.Raycast(ray, out var hit, settings.RayLength, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ? hit.distance : settings.RayLength;
            float distance = 0;
            bool reaches = CanTake && TryBounds(out var bounds) && Reaches(bounds, ray, position, settings.GrabReach, clear, out distance);
            SetHover(reaches, reaches ? distance : 0);
            if (reaches && input.Grab.WasPressedThisFrame()) Take(position, rotation, distance > 0, false);
        }

        private void UpdateDesktop(MuseumInput input)
        {
            if (viewCamera == null) { SetHover(false, 0); return; }
            var view = viewCamera.transform;
            if (holder == Holder.Desktop)
            {
                SetHover(false, 0);
                if (!input.Grab.IsPressed()) { Release(); return; }
                var settings = MuseumExperienceConfiguration.Current;
                var delta = input.Look.ReadValue<Vector2>() * settings.GrabDesktopRotation
                    + input.KeyboardLook.ReadValue<Vector2>() * (settings.KeyboardLookSpeed * Time.unscaledDeltaTime);
                Turn(delta);
                Follow(view.position, view.rotation, Time.unscaledDeltaTime);
                return;
            }
            bool captured = Cursor.lockState == CursorLockMode.Locked;
            // With the mouse free, the pointer must not be over the interface.
            if (!captured && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) { SetHover(false, 0); return; }
            var ray = captured ? new Ray(view.position, view.forward) : viewCamera.ScreenPointToRay(input.Point.ReadValue<Vector2>());
            float reach = MuseumExperienceConfiguration.Current.GrabDesktopReach;
            float clear = Physics.Raycast(ray, out var hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.distance : reach;
            float distance = 0;
            bool reaches = CanTake && TryBounds(out var bounds) && bounds.IntersectRay(ray, out distance)
                && distance <= Mathf.Min(reach, clear + .05f);
            SetHover(reaches, reaches ? distance : 0);
            if (reaches && input.Grab.WasPressedThisFrame()) Take(view.position, view.rotation, true, true);
        }

        private void SetHover(bool value, float distance)
        {
            HoverDistance = distance;
            if (Hovered == value) return;
            Hovered = value;
            Changed?.Invoke();
        }

        private static void Pulse(float amplitude, float seconds)
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid) device.SendHapticImpulse(0, amplitude, seconds);
        }

        private void OnDisable() => Drop();
    }
}
