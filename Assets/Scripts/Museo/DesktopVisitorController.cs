using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace MusiyoBetsknate.Museum
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class DesktopVisitorController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private Camera viewCamera;
        [SerializeField, Range(.02f, 1)] private float lookSensitivity = .12f;
        [SerializeField] private MuseumInteraction interaction;
        private CharacterController body;
        private InputActionAsset actions;
        private InputAction move;
        private InputAction look;
        private InputAction keyboardLook;
        private InputAction sprint;
        private InputAction capture;
        private MuseumExperienceConfiguration configuration;
        private float pitch;
        private float verticalSpeed;
        private Vector3 planarVelocity;
        private bool captured;
        private bool lockObserved;
        public float LookSensitivity => lookSensitivity;
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

        public void Configure(InputActionAsset asset, Camera camera, MuseumInteraction state)
        { inputActions = asset; viewCamera = camera; interaction = state; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
            configuration = MuseumExperienceConfiguration.Current;
            // Unity ignores moves shorter than minMoveDistance, which swallows walking at high frame rates.
            body.minMoveDistance = 0;
            if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
            if (inputActions == null) return;
            actions = Instantiate(inputActions);
            move = actions.FindAction("Desktop/Move", true);
            look = actions.FindAction("Desktop/Look", true);
            keyboardLook = actions.FindAction("Desktop/KeyboardLook", true);
            sprint = actions.FindAction("Desktop/Sprint", true);
            capture = actions.FindAction("Desktop/CapturePointer", true);
        }

        private void OnEnable() { if (actions != null) actions.Enable(); }
        private void OnDisable() { if (actions != null) actions.Disable(); CapturePointer(false); }
        private void OnDestroy() { if (actions != null) Destroy(actions); }

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

        /// <summary>Stands on the preferred floor spot when it is free, otherwise around the point, and looks at the target.</summary>
        public bool FocusPoint(PointAnchor anchor, Vector3? preferredSpot, Vector3 lookAt)
        {
            if (anchor == null || body == null || viewCamera == null) return false;
            for (int index = preferredSpot.HasValue ? -1 : 0; index < 8; index++)
            {
                var direction = Quaternion.Euler(0, index * 45, 0) * -anchor.transform.forward;
                var floor = index < 0 ? preferredSpot.Value : anchor.transform.position + direction * 1.2f;
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

        public void CapturePointer(bool value)
        {
            captured = value;
            lockObserved = false;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void Update()
        {
            if (actions == null || body == null || viewCamera == null) return;
            DetectReleasedPointer();
            bool blocked = interaction != null && (interaction.BlocksMovement
                || interaction.GetComponent<MuseumWayfinding>()?.MenuOpen == true);
            bool choosing = interaction != null && interaction.State == InteractionState.PointFocus;
            if (blocked || choosing || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CapturePointer(false);
            else if (capture.WasPressedThisFrame() && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                CapturePointer(true);
            if (!blocked)
            {
                var delta = captured && PointerLocked ? look.ReadValue<Vector2>() * lookSensitivity : Vector2.zero;
                if (!choosing) delta += keyboardLook.ReadValue<Vector2>() * (configuration.KeyboardLookSpeed * Time.deltaTime);
                transform.Rotate(Vector3.up, delta.x, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y, -configuration.MaximumPitch, configuration.MaximumPitch);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            if (blocked || choosing) planarVelocity = Vector3.zero; // Menus, reading and pauses stop at once.
            else
            {
                var input = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
                float speed = sprint.IsPressed() ? configuration.SprintSpeed : configuration.WalkSpeed;
                var target = (transform.forward * input.y + transform.right * input.x) * speed;
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
        private void DetectReleasedPointer()
        {
            if (!captured || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return;
            if (PointerLocked) { lockObserved = true; return; }
            if (!lockObserved) return;
            CapturePointer(false);
            if (interaction == null || interaction.BlocksMovement) return;
            interaction.SetPaused(true);
            PointerReleasePauseTime = Time.unscaledTime;
        }
    }
}
