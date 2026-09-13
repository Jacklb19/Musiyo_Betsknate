using UnityEngine;
using TMPro;
using MusiyoBetsknate.Dominio;

namespace MusiyoBetsknate.UI
{
    // Diagrama 2 / Diagrama 3, Sección 4.2 — Capa de Presentación
    // Sprint 1: Verificación de punta a punta del modelo de dominio y visualización de FichaCultural
    /// <summary>
    /// Componente de presentación para mostrar los datos de una Ficha Cultural en la interfaz de usuario.
    /// Consulta el estado de gobernanza cultural (fail-closed) para ocultar contenido no aprobado.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Sprint 1):
    /// Este script pertenece estrictamente a la capa de UI. No contiene lógica de negocio;
    /// delega la evaluación de divulgación a la propiedad 'EsDivulgablePublicamente' de ElementoCultural.
    /// </remarks>
    public class VisorFichaCulturalUI : MonoBehaviour
    {
        [Header("Datos de Entrada (Prueba Sprint 1)")]
        [SerializeField]
        [Tooltip("Elemento cultural a inspeccionar y mostrar en pantalla")]
        private ElementoCultural elementoActual;

        [Header("Referencias de UI - Contenido Aprobado")]
        [SerializeField]
        [Tooltip("Panel contenedor de la información del elemento cultural")]
        private GameObject panelContenido;

        [SerializeField]
        [Tooltip("Campo de texto para el nombre o título del elemento cultural")]
        private TextMeshProUGUI textoNombre;

        [SerializeField]
        [Tooltip("Campo de texto para la comunidad de origen y procedencia")]
        private TextMeshProUGUI textoOrigen;

        [SerializeField]
        [Tooltip("Campo de texto para la descripción breve y detallada de la ficha")]
        private TextMeshProUGUI textoDescripcion;

        [Header("Referencias de UI - Bloqueo Cultural (Fail-Closed)")]
        [SerializeField]
        [Tooltip("Panel o mensaje que se muestra cuando el contenido está PENDIENTE o RESTRINGIDO")]
        private GameObject panelContenidoNoDisponible;

        [SerializeField]
        [Tooltip("Texto explicativo de no disponibilidad del contenido cultural")]
        private TextMeshProUGUI textoMensajeBloqueo;

        private void Start()
        {
            if (elementoActual != null)
            {
                MostrarElemento(elementoActual);
            }
        }

        /// <summary>
        /// Actualiza la interfaz de usuario con los datos del elemento cultural suministrado.
        /// </summary>
        /// <param name="elemento">Instancia de ElementoCultural a visualizar.</param>
        public void MostrarElemento(ElementoCultural elemento)
        {
            elementoActual = elemento;

            if (elementoActual == null)
            {
                MostrarBloqueo("No se ha seleccionado ningún elemento cultural.");
                return;
            }

            // Validación de gobernanza cultural (Fail-Closed)
            if (!elementoActual.EsDivulgablePublicamente)
            {
                string mensaje = "Contenido no disponible para divulgación pública.\n" +
                                 $"Estado: {(elementoActual.ConsentimientoCultural != null ? elementoActual.ConsentimientoCultural.Estado.ToString() : "Sin Consentimiento (Pendiente)")}";
                MostrarBloqueo(mensaje);
                return;
            }

            // Visualización de contenido aprobado
            if (panelContenido != null) panelContenido.SetActive(true);
            if (panelContenidoNoDisponible != null) panelContenidoNoDisponible.SetActive(false);

            FichaCultural ficha = elementoActual.FichaCultural;

            if (textoNombre != null)
            {
                textoNombre.text = ficha != null && !string.IsNullOrEmpty(ficha.Titulo)
                    ? ficha.Titulo
                    : elementoActual.Nombre;
            }

            if (textoOrigen != null)
            {
                string procedencia = ficha != null && !string.IsNullOrEmpty(ficha.ProcedenciaComunitaria)
                    ? ficha.ProcedenciaComunitaria
                    : elementoActual.ComunidadOrigen;

                textoOrigen.text = $"Origen: {procedencia} | Categoría: {elementoActual.Categoria}";
            }

            if (textoDescripcion != null)
            {
                string descripcion = ficha != null && !string.IsNullOrEmpty(ficha.DescripcionDetallada)
                    ? ficha.DescripcionDetallada
                    : elementoActual.DescripcionBreve;

                textoDescripcion.text = descripcion;
            }
        }

        /// <summary>
        /// Oculta el contenido y despliega el mensaje de bloqueo cultural.
        /// </summary>
        private void MostrarBloqueo(string mensaje)
        {
            if (panelContenido != null) panelContenido.SetActive(false);
            if (panelContenidoNoDisponible != null) panelContenidoNoDisponible.SetActive(true);
            if (textoMensajeBloqueo != null) textoMensajeBloqueo.text = mensaje;
        }
    }
}
