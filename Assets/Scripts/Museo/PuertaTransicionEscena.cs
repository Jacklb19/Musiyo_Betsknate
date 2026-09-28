using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace MusiyoBetsknate.Museo
{
    // Sección 8.2 del plan — Cinemachine en rol secundario: transiciones de cámara entre Escena y Escena
    /// <summary>
    /// Umbral entre dos salas del museo. Al cruzarlo, ejecuta la transición de cámara y entrega el
    /// control de la sala destino al <see cref="RecorridoVirtual"/>.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Decisión de la Sección 8.2 del plan):
    /// Cinemachine ya no maneja la navegación momento a momento —de eso se encarga el XR Origin del
    /// XR Interaction Toolkit—, sino únicamente estas transiciones puntuales entre salas.
    ///
    /// Para no acoplar la capa Museo al paquete de Cinemachine, este componente no referencia sus tipos:
    /// recibe el GameObject que contiene la cámara virtual de transición y se limita a activarlo y
    /// desactivarlo. Si más adelante se cambia de solución de cámara, este script no cambia.
    ///
    /// ACCESIBILIDAD: además del disparador físico, el método público <see cref="Cruzar"/> permite
    /// accionar la misma transición desde el teclado o desde un botón de la interfaz.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public class PuertaTransicionEscena : MonoBehaviour
    {
        [Header("Recorrido y Sala Destino")]
        [SerializeField]
        [Tooltip("Recorrido al que pertenecen ambas salas")]
        private RecorridoVirtual recorridoVirtual;

        [SerializeField]
        [Tooltip("Sala a la que se entra al cruzar este umbral")]
        private Escena salaDestino;

        [Header("Transición de Cámara (Cinemachine, rol secundario)")]
        [SerializeField]
        [Tooltip("GameObject con la cámara virtual de transición. Se activa durante el cruce y se apaga al terminar")]
        private GameObject camaraDeTransicion;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Duración en segundos de la transición de cámara antes de entregar el control de la sala destino")]
        private float duracionTransicion = 1f;

        [Header("Eventos")]
        [SerializeField]
        [Tooltip("Se invoca al iniciar el cruce (útil para atenuar el audio o anunciar el cambio de sala)")]
        private UnityEvent<Escena> alIniciarCruce;

        [SerializeField]
        [Tooltip("Se invoca cuando el visitante ya está dentro de la sala destino")]
        private UnityEvent<Escena> alCompletarCruce;

        private bool cruceEnCurso;

        private void Reset()
        {
            Collider disparador = GetComponent<Collider>();
            disparador.isTrigger = true;

            recorridoVirtual = FindAnyObjectByType<RecorridoVirtual>();
        }

        private void Awake()
        {
            if (recorridoVirtual == null)
            {
                recorridoVirtual = FindAnyObjectByType<RecorridoVirtual>();
            }

            if (camaraDeTransicion != null)
            {
                camaraDeTransicion.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!EsElVisitante(other.transform))
            {
                return;
            }

            Cruzar();
        }

        /// <summary>
        /// Ejecuta el cruce hacia la sala destino. Es público para poder accionarse también desde el
        /// teclado o desde la interfaz, sin depender del contacto físico con el disparador.
        /// </summary>
        public void Cruzar()
        {
            if (cruceEnCurso || salaDestino == null || recorridoVirtual == null)
            {
                return;
            }

            StartCoroutine(EjecutarCruce());
        }

        /// <summary>
        /// Activa la cámara de transición, espera su duración y entrega el control a la sala destino.
        /// </summary>
        private IEnumerator EjecutarCruce()
        {
            cruceEnCurso = true;
            alIniciarCruce?.Invoke(salaDestino);

            if (camaraDeTransicion != null)
            {
                camaraDeTransicion.SetActive(true);
            }

            if (duracionTransicion > 0f)
            {
                yield return new WaitForSeconds(duracionTransicion);
            }

            recorridoVirtual.IrASala(salaDestino);

            if (camaraDeTransicion != null)
            {
                camaraDeTransicion.SetActive(false);
            }

            alCompletarCruce?.Invoke(salaDestino);
            cruceEnCurso = false;
        }

        /// <summary>
        /// Determina si el collider que entró al umbral pertenece al visitante del recorrido.
        /// </summary>
        private bool EsElVisitante(Transform candidato)
        {
            Transform visitante = recorridoVirtual != null ? recorridoVirtual.Visitante : null;

            if (visitante != null)
            {
                return candidato == visitante || candidato.IsChildOf(visitante);
            }

            return candidato.CompareTag("Player");
        }
    }
}
