using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Who holds the piece on show.</summary>
    public enum GrabHand { Right, Left, Desktop }

    /// <summary>
    /// Lets the visitor take the published piece on show straight from its pedestal, move it, turn it and let it go. On a
    /// headset either grip takes it, close by or along the pointer ray; the other grip takes it too, so the piece passes
    /// from hand to hand or turns between both hands, and the right stick turns it. Let go, it stays where it was left and
    /// goes back to its exhibition pose when the visitor walks away, or at once when it is let go on its pedestal. A
    /// desktop holds the right mouse button over it, turns it with the mouse and sends it back on release. The piece has
    /// no colliders, so it never pushes the visitor or anything else, and nothing is thrown.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(MuseumModelPresenter))]
    public sealed class MuseumModelGrab : MonoBehaviour
    {
        /// <summary>A piece taken along the ray comes to the hand over this time instead of jumping.</summary>
        private const float AttachSeconds = .2f;
        private MuseumInteraction interaction;
        private MuseumModelPresenter presenter;
        private MuseumVisitFlow flow;
        private MuseumAutoTour autoTour;
        private Camera viewCamera;
        private DesktopVisitorController desktop;
        private Transform trackingOrigin;
        private Transform piece;
        private readonly Grip[] grips = { new Grip(), new Grip(), new Grip() };
        /// <summary>Pose of the piece relative to the frame spanned by both hands, while both hold it.</summary>
        private Vector3 pairOffset;
        private Quaternion pairTurn;
        private bool paired;
        private Vector3 takenPosition;
        private Quaternion takenRotation;
        private float attach;
        private float returnElapsed;
        private Vector3 returnPosition;
        private Quaternion returnRotation;

        private sealed class Grip
        {
            public bool Holding;
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
            /// <summary>Pose of the piece in this holder's frame.</summary>
            public Vector3 Offset;
            public Quaternion Turn = Quaternion.identity;
        }

        public bool Held => grips[0].Holding || grips[1].Holding || grips[2].Holding;
        /// <summary>Both hands of a headset hold the piece.</summary>
        public bool HeldWithBothHands => grips[(int)GrabHand.Right].Holding && grips[(int)GrabHand.Left].Holding;
        public bool HeldBy(GrabHand hand) => grips[(int)hand].Holding;
        public bool Returning { get; private set; }
        /// <summary>Let go away from its pedestal, the piece floats where it was left until the visitor walks away.</summary>
        public bool Resting { get; private set; }
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

        /// <summary>A headset holds pieces with its controllers, whose poses are relative to the tracking origin.</summary>
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
                return presenter.State == ModelLoadState.Ready && presenter.Model != null && presenter.Renderers.Length > 0
                    && interaction.State != InteractionState.ModelExamination && CanTakeIgnoringHold();
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

        /// <summary>The right hand, or the desktop view with <paramref name="desktopHold"/>, takes the piece.</summary>
        public bool Take(Vector3 holderPosition, Quaternion holderRotation, bool fromDistance, bool desktopHold)
            => Take(desktopHold ? GrabHand.Desktop : GrabHand.Right, holderPosition, holderRotation, fromDistance);

        /// <summary>
        /// Takes the piece: from then on it follows the holder at the pose it was taken with. A piece taken from a distance
        /// first glides into the hand. A hand that takes a piece the other hand holds joins it; both then carry it.
        /// </summary>
        public bool Take(GrabHand hand, Vector3 holderPosition, Quaternion holderRotation, bool fromDistance)
        {
            var grip = grips[(int)hand];
            if (grip.Holding) return false;
            bool joining = Held && hand != GrabHand.Desktop && !HeldBy(GrabHand.Desktop);
            if (!joining && (Held || !CanTake && !(Resting && CanTakeIgnoringHold() && piece == presenter.Model))) return false;
            grip.Position = holderPosition;
            grip.Rotation = holderRotation;
            if (!joining)
            {
                bool fromRest = Resting;
                piece = presenter.Model;
                presenter.PoseHeld = true;
                // A piece taken while it was still growing in has its full exhibition size in the hand.
                if (!fromRest && presenter.TryExhibitPose(out _, out _, out var scale)) piece.localScale = scale;
                Returning = Resting = false;
                takenPosition = piece.position;
                takenRotation = piece.rotation;
            }
            grip.Holding = true;
            grip.Turn = Quaternion.Inverse(holderRotation) * piece.rotation;
            if (hand == GrabHand.Desktop)
            {
                grip.Offset = Vector3.forward * MuseumExperienceConfiguration.Current.GrabDesktopDistance;
                attach = 0;
                if (desktop != null) desktop.LookSuspended = true;
            }
            else if (joining)
            {
                // The piece stays exactly where it is; from now on it follows the frame between both hands.
                grip.Offset = Quaternion.Inverse(holderRotation) * (piece.position - holderPosition);
                attach = 1;
                Anchor();
                Pulse(hand, .25f, .04f);
            }
            else
            {
                TryBounds(out var bounds);
                grip.Offset = fromDistance ? Vector3.forward * (.08f + bounds.extents.magnitude * .5f)
                    : Quaternion.Inverse(holderRotation) * (piece.position - holderPosition);
                attach = fromDistance ? 0 : 1;
                Pulse(hand, .3f, .05f);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Moves the piece held by the right hand or the desktop view. Tests and the desktop call it directly.</summary>
        public void Follow(Vector3 holderPosition, Quaternion holderRotation, float elapsed)
            => Follow(HeldBy(GrabHand.Desktop) ? GrabHand.Desktop : HeldBy(GrabHand.Right) ? GrabHand.Right : GrabHand.Left,
                holderPosition, holderRotation, elapsed);

        /// <summary>Moves the held piece with one holder's new pose. The pose kept for the exhibition is never derived from this one.</summary>
        public void Follow(GrabHand hand, Vector3 holderPosition, Quaternion holderRotation, float elapsed)
        {
            var grip = grips[(int)hand];
            if (!grip.Holding || piece == null) return;
            grip.Position = holderPosition;
            grip.Rotation = holderRotation;
            Place(elapsed);
        }

        /// <summary>Moves the piece held by both hands with their new poses.</summary>
        public void FollowHands(Vector3 rightPosition, Quaternion rightRotation, Vector3 leftPosition, Quaternion leftRotation, float elapsed)
        {
            if (piece == null) return;
            var right = grips[(int)GrabHand.Right];
            var left = grips[(int)GrabHand.Left];
            if (right.Holding) { right.Position = rightPosition; right.Rotation = rightRotation; }
            if (left.Holding) { left.Position = leftPosition; left.Rotation = leftRotation; }
            if (right.Holding || left.Holding) Place(elapsed);
        }

        private void Place(float elapsed)
        {
            attach = Mathf.Min(1, attach + Mathf.Max(0, elapsed) / AttachSeconds);
            Vector3 position;
            Quaternion rotation;
            if (paired && HeldWithBothHands)
            {
                var frame = PairFrame(out var centre);
                position = centre + frame * pairOffset;
                rotation = frame * pairTurn;
            }
            else
            {
                var grip = Holder();
                position = grip.Position + grip.Rotation * grip.Offset;
                rotation = grip.Rotation * grip.Turn;
            }
            float blend = Mathf.SmoothStep(0, 1, attach);
            piece.SetPositionAndRotation(Vector3.Lerp(takenPosition, position, blend), Quaternion.Slerp(takenRotation, rotation, blend));
            // Carried too far from its pedestal, the piece goes back on its own.
            if (presenter.TryExhibitPose(out var home, out _, out _)
                && Vector3.Distance(piece.position, home) > MuseumXrConfiguration.Current.GrabMaxDistance) ReturnHome();
        }

        private Grip Holder() => grips[(int)GrabHand.Desktop].Holding ? grips[(int)GrabHand.Desktop]
            : grips[(int)GrabHand.Right].Holding ? grips[(int)GrabHand.Right] : grips[(int)GrabHand.Left];

        /// <summary>
        /// Frame spanned by both hands: centred between them, facing along the line from the left hand to the right one,
        /// and rolled with the hands, so moving either hand around the other turns the piece.
        /// </summary>
        private Quaternion PairFrame(out Vector3 centre)
        {
            var right = grips[(int)GrabHand.Right];
            var left = grips[(int)GrabHand.Left];
            centre = (right.Position + left.Position) * .5f;
            var across = right.Position - left.Position;
            if (across.sqrMagnitude < 1e-6f) across = right.Rotation * Vector3.right;
            var up = right.Rotation * Vector3.up + left.Rotation * Vector3.up;
            if (Vector3.Cross(across, up).sqrMagnitude < 1e-6f) up = Vector3.up;
            return Quaternion.LookRotation(across.normalized, up);
        }

        /// <summary>Keeps the piece where it is and measures every current holder from there, so nothing jumps.</summary>
        private void Anchor()
        {
            if (piece == null) return;
            foreach (var grip in grips)
            {
                if (!grip.Holding) continue;
                grip.Offset = Quaternion.Inverse(grip.Rotation) * (piece.position - grip.Position);
                grip.Turn = Quaternion.Inverse(grip.Rotation) * piece.rotation;
            }
            paired = HeldWithBothHands;
            if (paired)
            {
                var frame = PairFrame(out var centre);
                pairOffset = Quaternion.Inverse(frame) * (piece.position - centre);
                pairTurn = Quaternion.Inverse(frame) * piece.rotation;
            }
            takenPosition = piece.position;
            takenRotation = piece.rotation;
            attach = 1;
        }

        /// <summary>Turns a piece held on a desktop, around the view's own axes.</summary>
        public void Turn(Vector2 degrees)
        {
            var grip = grips[(int)GrabHand.Desktop];
            if (!grip.Holding || !MuseumExperienceConfiguration.Finite(degrees.x) || !MuseumExperienceConfiguration.Finite(degrees.y)) return;
            grip.Turn = Quaternion.AngleAxis(-degrees.x, Vector3.up) * Quaternion.AngleAxis(degrees.y, Vector3.right) * grip.Turn;
        }

        /// <summary>
        /// Turns a piece held on a headset about its own centre: sideways about the vertical, forward about the line across
        /// the visitor's view. The hands keep holding it where they hold it now.
        /// </summary>
        public void TurnInHands(Vector2 degrees, Vector3 viewRight)
        {
            if (piece == null || !(HeldBy(GrabHand.Right) || HeldBy(GrabHand.Left)) || HeldBy(GrabHand.Desktop)) return;
            if (!MuseumExperienceConfiguration.Finite(degrees.x) || !MuseumExperienceConfiguration.Finite(degrees.y)) return;
            if (degrees.sqrMagnitude < 1e-6f) return;
            if (attach < 1) Place(1);
            var across = Vector3.ProjectOnPlane(viewRight, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.right;
            var turn = Quaternion.AngleAxis(-degrees.x, Vector3.up) * Quaternion.AngleAxis(degrees.y, across.normalized);
            piece.rotation = turn * piece.rotation;
            Anchor();
        }

        /// <summary>Lets go with every holder: a desktop sends the piece back, hands leave it floating where it is.</summary>
        public void Release()
        {
            if (!Held) return;
            bool desktopHold = HeldBy(GrabHand.Desktop);
            foreach (var grip in grips) grip.Holding = false;
            paired = false;
            if (desktopHold && desktop != null) desktop.LookSuspended = false;
            if (desktopHold) BeginReturn();
            else Rest();
            Changed?.Invoke();
        }

        /// <summary>
        /// One hand lets go. While the other one holds the piece it simply carries on from where it is; the last hand to
        /// let go leaves it floating, or puts it back when it is let go on its pedestal.
        /// </summary>
        public void Release(GrabHand hand)
        {
            var grip = grips[(int)hand];
            if (!grip.Holding) return;
            if (hand == GrabHand.Desktop) { Release(); return; }
            grip.Holding = false;
            if (Held)
            {
                Anchor();
                Changed?.Invoke();
                return;
            }
            paired = false;
            Rest();
            Changed?.Invoke();
        }

        /// <summary>Sends the piece back to its exhibition pose, whether it is held or floating.</summary>
        public void ReturnHome()
        {
            if (!Held && !Resting) return;
            bool desktopHold = HeldBy(GrabHand.Desktop);
            foreach (var grip in grips) grip.Holding = false;
            paired = false;
            if (desktopHold && desktop != null) desktop.LookSuspended = false;
            BeginReturn();
            Changed?.Invoke();
        }

        private void Rest()
        {
            if (piece == null) return;
            // Let go on its pedestal, it goes back onto it; anywhere else it stays where the visitor left it.
            if (presenter.TryExhibitPose(out var home, out _, out _)
                && Vector3.Distance(piece.position, home) <= MuseumXrConfiguration.Current.GrabSnapDistance) { BeginReturn(); return; }
            Resting = true;
            Returning = false;
        }

        private void BeginReturn()
        {
            Resting = false;
            if (piece == null) return;
            Returning = true;
            returnElapsed = 0;
            returnPosition = piece.position;
            returnRotation = piece.rotation;
        }

        /// <summary>Forgets the piece at once: it was replaced or removed, or the examination now places it.</summary>
        private void Drop()
        {
            if (HeldBy(GrabHand.Desktop) && desktop != null) desktop.LookSuspended = false;
            bool changed = Held || Returning || Resting;
            foreach (var grip in grips) grip.Holding = false;
            paired = false;
            Returning = Resting = false;
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

        /// <summary>A floating piece goes back once the visitor is further than the configured distance from its pedestal.</summary>
        public void StepRest(Vector3 visitorHead)
        {
            if (!Resting || piece == null || !presenter.TryExhibitPose(out var home, out _, out _)) return;
            var away = home - visitorHead;
            away.y = 0;
            if (away.magnitude > MuseumXrConfiguration.Current.GrabRestReturnDistance) ReturnHome();
        }

        private void Update()
        {
            // A replaced or removed piece, or an examination that now places it, ends any hold at once.
            if ((Held || Returning || Resting) && (piece == null || presenter.Model != piece
                || interaction.State == InteractionState.ModelExamination)) { Drop(); }
            // Pausing, a menu, a screen panel or the automatic tour sends a held or floating piece back.
            if ((Held || Resting) && !CanTakeIgnoringHold()) ReturnHome();
            if (Returning) StepReturn(Time.unscaledDeltaTime);
            if (Resting && viewCamera != null) StepRest(viewCamera.transform.position);
            var input = MuseumInput.Current;
            if (input.Spatial) UpdateHands(input);
            else UpdateDesktop(input);
        }

        private bool CanTakeIgnoringHold()
        {
            if (flow == null) flow = GetComponent<MuseumVisitFlow>();
            if (autoTour == null) autoTour = GetComponent<MuseumAutoTour>();
            return interaction.State != InteractionState.Paused && !interaction.BlocksMovement
                && (flow == null || !flow.BlocksInput) && autoTour?.Moving != true;
        }

        private void UpdateHands(MuseumInput input)
        {
            bool head = trackingOrigin != null && (input.HeadState.ReadValue<int>() & 3) == 3;
            bool rightTracked = head && input.RightTracked.IsPressed();
            bool leftTracked = head && input.LeftTracked.IsPressed();
            var rightPosition = rightTracked ? trackingOrigin.TransformPoint(input.RightPosition.ReadValue<Vector3>()) : Vector3.zero;
            var rightRotation = rightTracked ? trackingOrigin.rotation * input.RightRotation.ReadValue<Quaternion>() : Quaternion.identity;
            var leftPosition = leftTracked ? trackingOrigin.TransformPoint(input.LeftPosition.ReadValue<Vector3>()) : Vector3.zero;
            var leftRotation = leftTracked ? trackingOrigin.rotation * input.LeftRotation.ReadValue<Quaternion>() : Quaternion.identity;
            // Losing a controller or the headset lets go with that hand, so the piece is never carried by a lost pose.
            if (HeldBy(GrabHand.Right) && (!rightTracked || !input.Grab.IsPressed())) Release(GrabHand.Right);
            if (HeldBy(GrabHand.Left) && (!leftTracked || !input.GrabLeft.IsPressed())) Release(GrabHand.Left);
            if (HeldBy(GrabHand.Right) || HeldBy(GrabHand.Left))
            {
                FollowHands(rightPosition, rightRotation, leftPosition, leftRotation, Time.unscaledDeltaTime);
                var stick = input.Turn.ReadValue<Vector2>();
                float deadzone = MuseumXrConfiguration.Current.TurnDeadzone;
                if (stick.magnitude > deadzone && viewCamera != null)
                    TurnInHands(stick * (MuseumXrConfiguration.Current.GrabStickTurnSpeed * Time.unscaledDeltaTime), viewCamera.transform.right);
            }
            if (!rightTracked && !leftTracked) { SetHover(false, 0); return; }
            // The free hand that reaches the piece takes it: from the pedestal, from the air, or from the other hand.
            float rightDistance = 0, leftDistance = 0;
            bool rightReaches = rightTracked && !HeldBy(GrabHand.Right) && Reaching(rightPosition, rightRotation, out rightDistance);
            bool leftReaches = leftTracked && !HeldBy(GrabHand.Left) && Reaching(leftPosition, leftRotation, out leftDistance);
            SetHover(rightReaches, rightReaches ? rightDistance : 0);
            if (rightReaches && input.Grab.WasPressedThisFrame()) Take(GrabHand.Right, rightPosition, rightRotation, rightDistance > 0 && !Held);
            if (leftReaches && input.GrabLeft.WasPressedThisFrame()) Take(GrabHand.Left, leftPosition, leftRotation, leftDistance > 0 && !Held);
        }

        private bool Reaching(Vector3 position, Quaternion rotation, out float distance)
        {
            distance = 0;
            if (!(CanTake || Held || Resting && CanTakeIgnoringHold()) || !TryBounds(out var bounds)) return false;
            var ray = new Ray(position, rotation * Vector3.forward);
            var settings = MuseumXrConfiguration.Current;
            float clear = Physics.Raycast(ray, out var hit, settings.RayLength, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) ? hit.distance : settings.RayLength;
            // A piece in the other hand is passed over close by, never pulled along a ray.
            if (Held) { var near = bounds; near.Expand(settings.GrabReach * 2); return near.Contains(position); }
            return Reaches(bounds, ray, position, settings.GrabReach, clear, out distance);
        }

        private void UpdateDesktop(MuseumInput input)
        {
            if (viewCamera == null) { SetHover(false, 0); return; }
            var view = viewCamera.transform;
            if (HeldBy(GrabHand.Desktop))
            {
                SetHover(false, 0);
                if (!input.Grab.IsPressed()) { Release(); return; }
                var settings = MuseumExperienceConfiguration.Current;
                var delta = input.Look.ReadValue<Vector2>() * settings.GrabDesktopRotation
                    + input.KeyboardLook.ReadValue<Vector2>() * (settings.KeyboardLookSpeed * Time.unscaledDeltaTime);
                Turn(delta);
                Follow(GrabHand.Desktop, view.position, view.rotation, Time.unscaledDeltaTime);
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
            if (reaches && input.Grab.WasPressedThisFrame()) Take(GrabHand.Desktop, view.position, view.rotation, true);
        }

        private void SetHover(bool value, float distance)
        {
            HoverDistance = distance;
            if (Hovered == value) return;
            Hovered = value;
            Changed?.Invoke();
        }

        private static void Pulse(GrabHand hand, float amplitude, float seconds)
        {
            if (hand == GrabHand.Desktop) return;
            var device = InputDevices.GetDeviceAtXRNode(hand == GrabHand.Right ? XRNode.RightHand : XRNode.LeftHand);
            if (device.isValid) device.SendHapticImpulse(0, amplitude, seconds);
        }

        private void OnDisable() => Drop();
    }
}
