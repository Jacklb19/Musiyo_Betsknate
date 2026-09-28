using UnityEngine;

namespace MusiyoBetsknate.Museo
{
    // Diagrama 2, Sección 3 — Capa de experiencia museográfica
    // Precisión adoptada (Secciones 3.3 y 8.2 del plan): Escena = sala caminable con colisión real
    /// <summary>
    /// Sala del museo virtual: componente compuesto que agrupa los puntos de interés de un espacio
    /// físico recorrible. A diferencia del modelo inicial, no representa un punto de cámara fijo
    /// sino una sala por la que el visitante camina libremente con colisión real.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Decisión #4 de la bitácora, Sección 8.2 del plan):
    /// El cambio respecto al Diagrama 2 es de COMPORTAMIENTO, no de estructura: la clase sigue siendo
    /// el nivel intermedio del patrón Composite entre RecorridoVirtual y PuntoInteres.
    /// La sala no debe depender del backend de XR activo: es idéntica en el build WebGL y en el build
    /// Android/Quest, tal como exige la arquitectura de movimiento compartida sobre XR Origin.
    /// </remarks>
    public class Escena : ComponenteMuseoBase
    {
        [Header("Configuración de la Sala Caminable")]
        [SerializeField]
        [Tooltip("Punto donde se ubica al visitante (XR Origin) al entrar a esta sala")]
        private Transform puntoDeEntrada;

        [SerializeField]
        [TextArea(2, 4)]
        [Tooltip("Descripción hablada/textual de la sala, anunciada al entrar (accesibilidad WCAG 2.2)")]
        private string descripcionAccesible;

        [SerializeField]
        [Tooltip("Si está activo, verifica al iniciar que la sala tenga geometría de colisión real y no solo geometría de relleno")]
        private bool verificarColisionAlIniciar = true;

        /// <summary>
        /// Posición y orientación de aparición del visitante en esta sala.
        /// Si no se asigna, se usa el transform de la propia sala.
        /// </summary>
        public Transform PuntoDeEntrada => puntoDeEntrada != null ? puntoDeEntrada : transform;

        /// <summary>
        /// Descripción textual de la sala para lectores de pantalla y subtítulos.
        /// </summary>
        public string DescripcionAccesible => descripcionAccesible;

        protected override void Awake()
        {
            base.Awake();

            if (verificarColisionAlIniciar)
            {
                VerificarColisionReal();
            }
        }

        /// <inheritdoc/>
        protected override void RecolectarHijosDeJerarquia()
        {
            RegistrarHijosDeTipo<PuntoInteres>();
        }

        /// <inheritdoc/>
        public override void Activar()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            base.Activar();
        }

        /// <inheritdoc/>
        public override void Desactivar()
        {
            base.Desactivar();

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Ubica al visitante en el punto de entrada de la sala.
        /// </summary>
        /// <param name="visitante">Transform raíz del visitante (XR Origin en ambos builds).</param>
        public void UbicarVisitante(Transform visitante)
        {
            if (visitante == null)
            {
                Debug.LogWarning($"[{NombreComponente}] No se recibió un visitante válido para ubicar en la sala.", this);
                return;
            }

            Transform destino = PuntoDeEntrada;
            visitante.SetPositionAndRotation(destino.position, destino.rotation);
        }

        /// <summary>
        /// Comprueba que la sala tenga al menos un Collider no disparador, es decir, geometría
        /// que realmente detenga al visitante. Una sala sin colisión permitiría atravesar los muros
        /// y rompería la premisa de "sala caminable" de la Sección 8.2 del plan.
        /// </summary>
        /// <returns>True si la sala tiene colisión real; false en caso contrario.</returns>
        public bool VerificarColisionReal()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);

            foreach (Collider collider in colliders)
            {
                if (!collider.isTrigger)
                {
                    return true;
                }
            }

            Debug.LogWarning(
                $"[{NombreComponente}] La sala no tiene geometría de colisión real (solo disparadores o ninguna). " +
                "El visitante podría atravesar los muros; revisar el blockout antes de las pruebas de usabilidad.",
                this);

            return false;
        }
    }
}
