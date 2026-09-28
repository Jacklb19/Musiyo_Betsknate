using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MusiyoBetsknate.Museo
{
    // Diagrama 2, Sección 3 — Capa de experiencia museográfica
    // Patrón Composite: raíz de la jerarquía RecorridoVirtual -> Escena -> PuntoInteres
    /// <summary>
    /// Raíz del recorrido museográfico: agrupa las salas (<see cref="Escena"/>) que componen una visita
    /// y controla cuál de ellas está activa, ubicando al visitante en su punto de entrada.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Secciones 7.5.8 y 8.7 del plan):
    /// La estructura del recorrido es DATO, no código. En el Sprint 2 las salas y vitrinas se declaran
    /// en la jerarquía de la escena de prueba; en el Sprint 4 la misma jerarquía se poblará con la
    /// respuesta de GET /recorridos/{id}, sin recompilar el build de Unity.
    ///
    /// Este componente es independiente del backend de XR activo: se comporta igual en el build WebGL
    /// (teclado y mouse) y en el build Android/Quest (OpenXR), tal como exige la Sección 8.1.
    /// </remarks>
    public class RecorridoVirtual : ComponenteMuseoBase
    {
        [Header("Visitante y Arranque del Recorrido")]
        [SerializeField]
        [Tooltip("Transform raíz del visitante (XR Origin), reubicado al cambiar de sala")]
        private Transform visitante;

        [SerializeField]
        [Tooltip("Activa la primera sala automáticamente al iniciar la ejecución")]
        private bool activarPrimeraSalaAlIniciar = true;

        [Header("Eventos del Recorrido")]
        [SerializeField]
        [Tooltip("Se invoca al entrar a una sala, con la sala activada como argumento")]
        private UnityEvent<Escena> alCambiarDeSala;

        private Escena escenaActual;

        /// <summary>
        /// Sala actualmente activa del recorrido (nula antes de iniciar).
        /// </summary>
        public Escena EscenaActual => escenaActual;

        /// <summary>
        /// Transform del visitante utilizado por el recorrido.
        /// </summary>
        public Transform Visitante => visitante;

        /// <summary>
        /// Salas que componen el recorrido, en el orden declarado en la jerarquía.
        /// </summary>
        public IReadOnlyList<Escena> Salas
        {
            get
            {
                List<Escena> salas = new List<Escena>();

                foreach (IComponenteMuseo hijo in Hijos)
                {
                    if (hijo is Escena sala)
                    {
                        salas.Add(sala);
                    }
                }

                return salas;
            }
        }

        private void Start()
        {
            if (!activarPrimeraSalaAlIniciar)
            {
                return;
            }

            IReadOnlyList<Escena> salas = Salas;

            if (salas.Count == 0)
            {
                Debug.LogWarning($"[{NombreComponente}] El recorrido no contiene ninguna sala (Escena) en su jerarquía.", this);
                return;
            }

            Activar();
            IrASala(salas[0]);
        }

        /// <inheritdoc/>
        protected override void RecolectarHijosDeJerarquia()
        {
            RegistrarHijosDeTipo<Escena>();
        }

        /// <inheritdoc/>
        public override void Activar()
        {
            EstablecerVisitanteEnVitrinas();
            base.Activar();
        }

        /// <summary>
        /// Activa la sala indicada, desactiva las demás y ubica al visitante en su punto de entrada.
        /// </summary>
        /// <param name="sala">Sala destino, perteneciente a este recorrido.</param>
        public void IrASala(Escena sala)
        {
            if (sala == null)
            {
                Debug.LogWarning($"[{NombreComponente}] Se solicitó entrar a una sala nula.", this);
                return;
            }

            if (!Hijos.Contains(sala))
            {
                Debug.LogWarning($"[{NombreComponente}] La sala {sala.NombreComponente} no pertenece a este recorrido.", this);
                return;
            }

            foreach (Escena otraSala in Salas)
            {
                if (otraSala != sala && otraSala.EstaActivo)
                {
                    otraSala.Desactivar();
                }
            }

            sala.Activar();
            sala.UbicarVisitante(visitante);

            escenaActual = sala;
            alCambiarDeSala?.Invoke(sala);
        }

        /// <summary>
        /// Activa la sala ubicada en la posición indicada dentro del recorrido.
        /// </summary>
        /// <param name="indice">Índice de la sala, empezando en cero.</param>
        public void IrASala(int indice)
        {
            IReadOnlyList<Escena> salas = Salas;

            if (indice < 0 || indice >= salas.Count)
            {
                Debug.LogWarning($"[{NombreComponente}] Índice de sala fuera de rango: {indice}.", this);
                return;
            }

            IrASala(salas[indice]);
        }

        /// <summary>
        /// Declara explícitamente quién es el visitante y lo propaga a todas las vitrinas del recorrido.
        /// </summary>
        /// <param name="transformVisitante">Transform raíz del visitante (XR Origin).</param>
        public void AsignarVisitante(Transform transformVisitante)
        {
            visitante = transformVisitante;
            EstablecerVisitanteEnVitrinas();
        }

        /// <summary>
        /// Propaga la referencia del visitante a cada punto de interés del recorrido, para que la
        /// activación por proximidad no dependa de resolver la cámara principal en cada vitrina.
        /// </summary>
        private void EstablecerVisitanteEnVitrinas()
        {
            if (visitante == null)
            {
                return;
            }

            RecorrerJerarquia(componente =>
            {
                if (componente is PuntoInteres vitrina)
                {
                    vitrina.AsignarVisitante(visitante);
                }
            });
        }
    }
}
