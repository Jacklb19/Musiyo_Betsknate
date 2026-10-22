using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MusiyoBetsknate.Museum
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class DesktopVisitorController : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField, Range(.02f, 1)] private float lookSensitivity = .12f;
        [SerializeField] private MuseumInteraction interaction;
        private CharacterController body;
        private MuseumExperienceConfiguration configuration;
        private float pitch;
        private float verticalSpeed;
        private Vector3 planarVelocity;
        private bool captured;
        private bool lockObserved;
        public float LookSensitivity => lookSensitivity;
        /// <summary>While a held piece takes the mouse and look keys to turn, the view stays still.</summary>
        public bool LookSuspended { get; set; }
        public Vector3 PlanarVelocity => planarVelocity;
        /// <summary>Unscaled time of the last pause caused by the browser releasing the pointer.</summary>
        public float PointerReleasePauseTime { get; private set; } = float.NegativeInfinity;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int MusiyoIsPointerLocked();
        private static bool PointerLocked => MusiyoIsPointerLocked() == 1;
#else
        private static bool PointerLocked => Cursor.lockState == CursorLockMode.Locked;
#endif

        public void Configure(Camera camera, MuseumInteraction state)
        { viewCamera = camera; interaction = state; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            configuration = MuseumExperienceConfiguration.Current;
            // Unity ignores moves shorter than minMoveDistance, which swallows walking at high frame rates.
            body.minMoveDistance = 0;
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        }

        private void OnEnable()
        {
            // The visit decides when walking listens; a visitor without one simply walks.
            if (interaction != null) MuseumInputDirector.Ensure(interaction);
            else MuseumInput.Current.SetContext(MuseumInputContext.Visit);
        }
        private void OnDisable() => CapturePointer(false);

        public void SetSensitivity(float value)
        {
            var settings = MuseumExperienceConfiguration.Current;
            if (MuseumExperienceConfiguration.Finite(value))
                lookSensitivity = Mathf.Clamp(value, settings.MinimumSensitivity, settings.MaximumSensitivity);
        }

        /// <summary>Eases horizontal velocity toward the requested one; stopping or reversing uses the deceleration rate.</summary>
        public static Vector3 StepVelocity(Vector3 current, Vector3 target, float acceleration, float deceleration, float elapsed)
        {
            if (!MuseumExperienceConfiguration.Finite(elapsed) || elapsed <= 0) return current;
            bool speedingUp = target.sqrMagnitude > current.sqrMagnitude && Vector3.Dot(target, current) >= 0;
            return Vector3.MoveTowards(current, target, (speedingUp ? acceleration : deceleration) * elapsed);
        }

        public bool FocusPoint(PointAnchor anchor)
            => anchor != null && FocusPoint(anchor, null, anchor.LookTarget.position);

        /// <summary>Height of the floor under a spot: room floors lie a few centimetres above or below their point anchors.</summary>
        private float FloorHeight(Vector3 spot)
        {
            float height = spot.y, nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(spot + Vector3.up * body.stepOffset, Vector3.down, body.stepOffset * 2,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(transform) && hit.distance < nearest) { nearest = hit.distance; height = hit.point.y; }
            return height;
        }

        /// <summary>Stands on the preferred floor spot when it is free, otherwise around the point, and looks at the target.</summary>
        public bool FocusPoint(PointAnchor anchor, Vector3? preferredSpot, Vector3 lookAt)
        {
            if (anchor == null || body == null || viewCamera == null) return false;
            for (int index = preferredSpot.HasValue ? -1 : 0; index < 8; index++)
            {
                var direction = Quaternion.Euler(0, index * 45, 0) * -anchor.transform.forward;
                var floor = index < 0 ? preferredSpot.Value : anchor.transform.position + direction * 1.2f;
                floor.y = FloorHeight(floor);
                var center = floor + Vector3.up * (body.height * .5f + .02f);
                float half = Mathf.Max(0, body.height * .5f - body.radius);
                bool obstructed = false;
                foreach (var obstacle in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                    body.radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!obstacle.transform.IsChildOf(transform)) { obstructed = true; break; }
                if (obstructed) continue;
                body.enabled = false;
                transform.position = center - body.center;
                var target = lookAt - viewCamera.transform.position;
                transform.rotation = Quaternion.Euler(0, Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg, 0);
                pitch = -Mathf.Atan2(target.y, new Vector2(target.x, target.z).magnitude) * Mathf.Rad2Deg;
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                verticalSpeed = 0;
                planarVelocity = Vector3.zero;
                body.enabled = true;
                CapturePointer(false);
                return true;
            }
            return false;
        }

        /// <summary>Stands the visitor at a pose, looking level, at rest; a new visit starts this way after the credits.</summary>
        public void PlaceAt(Vector3 position, Quaternion rotation)
        {
            if (body == null) body = GetComponent<CharacterController>();
            body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, rotation.eulerAngles.y, 0));
            pitch = 0;
            if (viewCamera != null) viewCamera.transform.localRotation = Quaternion.identity;
            verticalSpeed = 0;
            planarVelocity = Vector3.zero;
            body.enabled = true;
            Physics.SyncTransforms();
        }

        public void CapturePointer(bool value)
        {
            captured = value;
            lockObserved = false;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void Update()
        {
            if (body == null || viewCamera == null) return;
            var input = MuseumInput.Current;
            DetectReleasedPointer(input);
            bool blocked = interaction != null && (interaction.BlocksMovement
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true);
            bool choosing = interaction != null && interaction.HoldsVisitor;
            if (blocked || choosing || input.Pause.WasPressedThisFrame())
                CapturePointer(false);
            else if (input.Click.WasPressedThisFrame() && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                CapturePointer(true);
            if (!blocked && !LookSuspended)
            {
                var delta = captured && PointerLocked ? input.Look.ReadValue<Vector2>() * lookSensitivity : Vector2.zero;
                if (!choosing) delta += input.KeyboardLook.ReadValue<Vector2>() * (configuration.KeyboardLookSpeed * Time.deltaTime);
                transform.Rotate(Vector3.up, delta.x, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y, -configuration.MaximumPitch, configuration.MaximumPitch);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            if (blocked || choosing) planarVelocity = Vector3.zero; // Menus, on-screen panels and pauses stop at once.
            else
            {
                var move = Vector2.ClampMagnitude(input.Move.ReadValue<Vector2>(), 1);
                float speed = input.Sprint.IsPressed() ? configuration.SprintSpeed : configuration.WalkSpeed;
                var target = (transform.forward * move.y + transform.right * move.x) * speed;
                planarVelocity = StepVelocity(planarVelocity, target, configuration.Acceleration, configuration.Deceleration, Time.deltaTime);
            }
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -1;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            var before = transform.position;
            body.Move((planarVelocity + Vector3.up * verticalSpeed) * Time.deltaTime);
            // Keep the eased velocity consistent with walls so sliding does not build up hidden speed.
            if (Time.deltaTime > 0 && (body.collisionFlags & CollisionFlags.Sides) != 0)
            {
                var moved = (transform.position - before) / Time.deltaTime;
                planarVelocity = Vector3.ClampMagnitude(new Vector3(moved.x, 0, moved.z), planarVelocity.magnitude);
            }
        }

        /// <summary>Browsers release pointer lock on Escape without always delivering the key, so treat that as a pause.</summary>
        private void DetectReleasedPointer(MuseumInput input)
        {
            if (!captured || input.Pause.WasPressedThisFrame()) return;
            if (PointerLocked) { lockObserved = true; return; }
            if (!lockObserved) return;
            CapturePointer(false);
            if (interaction == null || interaction.BlocksMovement) return;
            interaction.SetPaused(true);
            PointerReleasePauseTime = Time.unscaledTime;
        }
    }
}
