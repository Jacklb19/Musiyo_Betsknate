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
        private float pitch;
        private float verticalSpeed;
        private bool captured;
        public float LookSensitivity => lookSensitivity;

        public void Configure(InputActionAsset asset, Camera camera, MuseumInteraction state)
        { inputActions = asset; viewCamera = camera; interaction = state; }

        private void Awake()
        {
            body = GetComponent<CharacterController>();
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

        public void SetSensitivity(float value) => lookSensitivity = Mathf.Clamp(value, .02f, 1);

        public bool FocusPoint(PointAnchor anchor)
        {
            if (anchor == null || body == null || viewCamera == null) return false;
            for (int index = 0; index < 8; index++)
            {
                var direction = Quaternion.Euler(0, index * 45, 0) * -anchor.transform.forward;
                var center = anchor.transform.position + direction * 1.2f + Vector3.up * (body.height * .5f + .02f);
                float half = Mathf.Max(0, body.height * .5f - body.radius);
                bool obstructed = false;
                foreach (var obstacle in Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                    body.radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!obstacle.transform.IsChildOf(transform)) { obstructed = true; break; }
                if (obstructed) continue;
                body.enabled = false;
                transform.position = center - body.center;
                var target = anchor.LookTarget.position - viewCamera.transform.position;
                transform.rotation = Quaternion.Euler(0, Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg, 0);
                pitch = -Mathf.Atan2(target.y, new Vector2(target.x, target.z).magnitude) * Mathf.Rad2Deg;
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                verticalSpeed = 0;
                body.enabled = true;
                CapturePointer(false);
                return true;
            }
            return false;
        }

        public void CapturePointer(bool value)
        {
            captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }

        private void Update()
        {
            if (actions == null || body == null || viewCamera == null) return;
            bool blocked = interaction != null && interaction.BlocksMovement;
            bool choosing = interaction != null && interaction.State == InteractionState.PointFocus;
            if (blocked || choosing || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                CapturePointer(false);
            else if (capture.WasPressedThisFrame() && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
                CapturePointer(true);
            if (!blocked)
            {
                var delta = captured ? look.ReadValue<Vector2>() * lookSensitivity : Vector2.zero;
                if (!choosing) delta += keyboardLook.ReadValue<Vector2>() * (70 * Time.deltaTime);
                transform.Rotate(Vector3.up, delta.x, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y, -80, 80);
                viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            var input = blocked || choosing ? Vector2.zero : Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1);
            float speed = sprint.IsPressed() ? 3 : 2;
            if (body.isGrounded && verticalSpeed < 0) verticalSpeed = -1;
            verticalSpeed += Physics.gravity.y * Time.deltaTime;
            var displacement = (transform.forward * input.y + transform.right * input.x) * speed;
            displacement.y = verticalSpeed;
            body.Move(displacement * Time.deltaTime);
        }
    }
}
