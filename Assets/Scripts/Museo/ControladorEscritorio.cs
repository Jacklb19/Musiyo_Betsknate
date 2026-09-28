using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusiyoBetsknate.Museo
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ControladorEscritorio : MonoBehaviour
    {
        [SerializeField] private Camera camara;
        [SerializeField, Min(0.1f)] private float velocidad = 3f;
        [SerializeField, Min(0.1f)] private float sensibilidadRaton = 0.12f;

        private CharacterController controlador;
        private float inclinacion;
        private float velocidadVertical;
        private bool ratonCapturado;

        private void Awake()
        {
            controlador = GetComponent<CharacterController>();
            if (camara == null) camara = GetComponentInChildren<Camera>();
        }

        private void OnDisable() => CapturarRaton(false);

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var teclado = Keyboard.current;
            var raton = Mouse.current;
            if (teclado == null) return;

            if (teclado.escapeKey.wasPressedThisFrame) CapturarRaton(false);
            if (raton != null && raton.leftButton.wasPressedThisFrame) CapturarRaton(true);

            if (ratonCapturado && raton != null && camara != null)
            {
                Vector2 delta = raton.delta.ReadValue() * sensibilidadRaton;
                transform.Rotate(Vector3.up, delta.x, Space.World);
                inclinacion = Mathf.Clamp(inclinacion - delta.y, -80f, 80f);
                camara.transform.localRotation = Quaternion.Euler(inclinacion, 0f, 0f);
            }

            Vector2 movimiento = Vector2.zero;
            if (teclado.wKey.isPressed || teclado.upArrowKey.isPressed) movimiento.y += 1;
            if (teclado.sKey.isPressed || teclado.downArrowKey.isPressed) movimiento.y -= 1;
            if (teclado.dKey.isPressed || teclado.rightArrowKey.isPressed) movimiento.x += 1;
            if (teclado.aKey.isPressed || teclado.leftArrowKey.isPressed) movimiento.x -= 1;
            movimiento = Vector2.ClampMagnitude(movimiento, 1f);

            if (controlador.isGrounded && velocidadVertical < 0f) velocidadVertical = -1f;
            velocidadVertical += Physics.gravity.y * Time.deltaTime;
            Vector3 desplazamiento = (transform.right * movimiento.x + transform.forward * movimiento.y) * velocidad;
            desplazamiento.y = velocidadVertical;
            controlador.Move(desplazamiento * Time.deltaTime);
#endif
        }

        private void CapturarRaton(bool capturar)
        {
            ratonCapturado = capturar;
            Cursor.lockState = capturar ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capturar;
        }
    }
}
