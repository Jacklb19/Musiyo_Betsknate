using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusiyoBetsknate.Museo
{
    // Sección 8.2 del plan — Equivalente de teclado de la activación por proximidad/mirada
    // WCAG 2.2, criterio 2.1.1 (Teclado): toda funcionalidad debe ser operable solo con teclado
    /// <summary>
    /// Recorrido accesible por teclado: permite seleccionar con Tab la vitrina más cercana de la sala
    /// activa, avanzar entre vitrinas y examinarlas con Enter, sin necesidad de caminar hasta ellas.
    /// </summary>
    /// <remarks>
    /// NOTA DE ACCESIBILIDAD (Sección 8.2 del plan):
    /// Este equivalente se diseña desde el mismo Sprint 2 y no como un ajuste posterior. Es la ruta
    /// garantizada para quien no pueda usar mouse, controles de VR o desplazamiento continuo, y también
    /// la alternativa cuando la locomoción continua produce mareo.
    ///
    /// Teclas: Tab (siguiente vitrina), Shift+Tab (anterior), Enter (examinar), Escape (soltar selección).
    /// Se implementa sobre el Input System, que es el manejador de entrada activo del proyecto.
    /// </remarks>
    public class NavegacionTecladoPuntosInteres : MonoBehaviour
    {
        [Header("Recorrido Observado")]
        [SerializeField]
        [Tooltip("Recorrido del que se toman las vitrinas de la sala activa")]
        private RecorridoVirtual recorridoVirtual;

        [SerializeField]
        [Tooltip("Transform de referencia para ordenar las vitrinas por cercanía. Si se omite, se usa el visitante del recorrido")]
        private Transform referenciaDeDistancia;

        [Header("Eventos de Accesibilidad")]
        [SerializeField]
        [Tooltip("Se invoca al seleccionar una vitrina con el teclado")]
        private UnityEvent<PuntoInteres> alSeleccionar;

        [SerializeField]
        [Tooltip("Texto a anunciar en pantalla o por voz al cambiar la selección (subtítulo accesible)")]
        private UnityEvent<string> alAnunciar;

        private readonly List<PuntoInteres> vitrinasOrdenadas = new List<PuntoInteres>();
        private PuntoInteres vitrinaSeleccionada;

        /// <summary>
        /// Vitrina actualmente seleccionada mediante teclado (puede ser nula).
        /// </summary>
        public PuntoInteres VitrinaSeleccionada => vitrinaSeleccionada;

        public event Action<PuntoInteres> PuntoSeleccionado;

        private void Reset()
        {
            recorridoVirtual = FindAnyObjectByType<RecorridoVirtual>();
        }

        private void Awake()
        {
            if (recorridoVirtual == null)
            {
                recorridoVirtual = FindAnyObjectByType<RecorridoVirtual>();
            }
        }

        private bool entradaSuspendida;
        public void SuspenderEntrada(bool suspendida) => entradaSuspendida = suspendida;

        private void OnDisable() => SoltarSeleccion();

        private void Update()
        {
            if (vitrinaSeleccionada != null)
            {
                ActualizarVitrinasDeSalaActiva();
                if (!vitrinasOrdenadas.Contains(vitrinaSeleccionada)) SoltarSeleccion();
            }
            if (entradaSuspendida) return;
            var foco = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            // No capturar Tab/Enter mientras el visitante usa un campo o control de interfaz.
            if (foco != null && (foco.GetComponentInParent<Selectable>() != null
                || foco.GetComponentInParent<TMP_InputField>() != null)) return;

#if ENABLE_INPUT_SYSTEM
            Keyboard teclado = Keyboard.current;

            if (teclado == null)
            {
                return;
            }

            if (teclado.tabKey.wasPressedThisFrame)
            {
                bool haciaAtras = teclado.leftShiftKey.isPressed || teclado.rightShiftKey.isPressed;
                Desplazar(haciaAtras ? -1 : 1);
            }

            if (teclado.enterKey.wasPressedThisFrame || teclado.numpadEnterKey.wasPressedThisFrame)
            {
                ExaminarSeleccion();
            }

            if (teclado.pageDownKey.wasPressedThisFrame) CambiarElementoSeleccionado(1);
            if (teclado.pageUpKey.wasPressedThisFrame) CambiarElementoSeleccionado(-1);

            if (teclado.escapeKey.wasPressedThisFrame)
            {
                SoltarSeleccion();
            }
#endif
        }

        /// <summary>
        /// Avanza o retrocede la selección dentro de las vitrinas de la sala activa.
        /// </summary>
        /// <param name="direccion">1 para la siguiente vitrina, -1 para la anterior.</param>
        public void Desplazar(int direccion)
        {
            if (direccion == 0) return;
            direccion = direccion > 0 ? 1 : -1;
            ActualizarVitrinasDeSalaActiva();

            if (vitrinasOrdenadas.Count == 0)
            {
                Anunciar("La sala actual no tiene vitrinas disponibles.");
                return;
            }

            int indiceActual = vitrinaSeleccionada != null ? vitrinasOrdenadas.IndexOf(vitrinaSeleccionada) : -1;
            int siguiente;

            if (indiceActual < 0)
            {
                // Sin selección previa: se empieza por la vitrina más cercana al visitante.
                siguiente = direccion >= 0 ? 0 : vitrinasOrdenadas.Count - 1;
            }
            else
            {
                siguiente = (indiceActual + direccion + vitrinasOrdenadas.Count) % vitrinasOrdenadas.Count;
            }

            Seleccionar(vitrinasOrdenadas[siguiente]);
        }

        /// <summary>
        /// Selecciona explícitamente una vitrina, retirando el enfoque de teclado de la anterior.
        /// </summary>
        /// <param name="vitrina">Vitrina a enfocar por teclado.</param>
        public void Seleccionar(PuntoInteres vitrina)
        {
            ActualizarVitrinasDeSalaActiva();
            if (vitrina != null && !vitrinasOrdenadas.Contains(vitrina)) return;
            if (vitrinaSeleccionada != null && vitrinaSeleccionada != vitrina)
            {
                vitrinaSeleccionada.Desenfocar(PuntoInteres.FuenteEnfoque.Teclado);
            }

            vitrinaSeleccionada = vitrina;

            if (vitrinaSeleccionada == null)
            {
                return;
            }

            vitrinaSeleccionada.Enfocar(PuntoInteres.FuenteEnfoque.Teclado);
            alSeleccionar?.Invoke(vitrinaSeleccionada);
            PuntoSeleccionado?.Invoke(vitrinaSeleccionada);
            Anunciar(vitrinaSeleccionada.ObtenerAnuncioAccesible());
        }

        /// <summary>
        /// Examina la vitrina seleccionada. La regla fail-closed se aplica dentro del propio punto de interés.
        /// </summary>
        public void CambiarElementoSeleccionado(int direccion)
        {
            ActualizarVitrinasDeSalaActiva();
            if (vitrinaSeleccionada == null || !vitrinasOrdenadas.Contains(vitrinaSeleccionada)) return;
            if (vitrinaSeleccionada.CambiarElemento(direccion))
                Anunciar(vitrinaSeleccionada.ObtenerAnuncioAccesible());
        }

        public void ExaminarSeleccion()
        {
            ActualizarVitrinasDeSalaActiva();
            if (vitrinaSeleccionada != null && !vitrinasOrdenadas.Contains(vitrinaSeleccionada)) SoltarSeleccion();
            if (vitrinaSeleccionada == null)
            {
                Anunciar("Presione Tab para seleccionar una vitrina antes de examinarla.");
                return;
            }

            vitrinaSeleccionada.Examinar();
        }

        /// <summary>
        /// Retira la selección de teclado actual sin afectar el enfoque por proximidad.
        /// </summary>
        public void SoltarSeleccion()
        {
            if (vitrinaSeleccionada == null)
            {
                return;
            }

            vitrinaSeleccionada.Desenfocar(PuntoInteres.FuenteEnfoque.Teclado);
            vitrinaSeleccionada = null;
            Anunciar("Selección liberada.");
        }

        /// <summary>
        /// Reconstruye la lista de vitrinas de la sala activa, ordenadas de la más cercana a la más lejana.
        /// </summary>
        private void ActualizarVitrinasDeSalaActiva()
        {
            vitrinasOrdenadas.Clear();

            if (recorridoVirtual == null || recorridoVirtual.EscenaActual == null)
            {
                return;
            }

            foreach (IComponenteMuseo hijo in recorridoVirtual.EscenaActual.Hijos)
            {
                if (hijo is PuntoInteres vitrina && vitrina.EstaActivo && vitrina.isActiveAndEnabled)
                {
                    vitrinasOrdenadas.Add(vitrina);
                }
            }

            Transform referencia = ObtenerReferenciaDeDistancia();

            if (referencia == null)
            {
                return;
            }

            vitrinasOrdenadas.Sort((primera, segunda) =>
            {
                float distanciaPrimera = (primera.transform.position - referencia.position).sqrMagnitude;
                float distanciaSegunda = (segunda.transform.position - referencia.position).sqrMagnitude;
                return distanciaPrimera.CompareTo(distanciaSegunda);
            });
        }

        /// <summary>
        /// Resuelve el transform desde el cual se mide la cercanía de las vitrinas.
        /// </summary>
        private Transform ObtenerReferenciaDeDistancia()
        {
            if (referenciaDeDistancia != null)
            {
                return referenciaDeDistancia;
            }

            if (recorridoVirtual != null && recorridoVirtual.Visitante != null)
            {
                return recorridoVirtual.Visitante;
            }

            return Camera.main != null ? Camera.main.transform : null;
        }

        /// <summary>
        /// Emite el texto de anuncio accesible hacia la interfaz (subtítulo o lector de pantalla).
        /// </summary>
        private void Anunciar(string mensaje)
        {
            alAnunciar?.Invoke(mensaje);
        }
    }
}
