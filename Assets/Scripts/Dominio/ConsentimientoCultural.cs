using System;
using UnityEngine;

namespace MusiyoBetsknate.Dominio
{
    // Diagrama 1, Sección 3 — Modelo de dominio cultural
    // Corrección #5 aplicada (Sección 3.2): Protección cultural y política fail-closed.
    /// <summary>
    /// Estados posibles del consentimiento cultural emitido por una autoridad comunitaria.
    /// </summary>
    public enum EstadoConsentimiento
    {
        /// <summary>
        /// Estado por defecto / inicial. El contenido no puede ser divulgado públicamente.
        /// </summary>
        Pendiente = 0,

        /// <summary>
        /// El contenido fue revisado y cuenta con aprobación comunitaria para exhibición pública.
        /// </summary>
        Aprobado = 1,

        /// <summary>
        /// Contenido de uso sagrado, restringido o no autorizado para divulgación pública.
        /// </summary>
        Restringido = 2
    }

    // Diagrama 1, Sección 3 — Modelo de dominio cultural
    /// <summary>
    /// Modela el estado de consentimiento y gobernanza cultural sobre un elemento o recurso.
    /// </summary>
    /// <remarks>
    /// REGLA ARQUITECTÓNICA "FAIL CLOSED" (Corrección #5, Sección 3.2 del plan):
    /// Ningún contenido cultural se divulga sin una aprobación explícita. La ausencia de este
    /// objeto, un estado 'Pendiente' o un estado 'Restringido' bloquean el acceso público por defecto.
    /// </remarks>
    [Serializable]
    public class ConsentimientoCultural
    {
        [Header("Estado de Consentimiento")]
        [SerializeField]
        [Tooltip("Estado actual de validación cultural")]
        private EstadoConsentimiento estado = EstadoConsentimiento.Pendiente;

        [SerializeField]
        [TextArea(2, 4)]
        [Tooltip("Justificación u observaciones del estado (obligatoria si es Restringido)")]
        private string observaciones;

        [SerializeField]
        [Tooltip("Fecha de registro o última actualización del consentimiento (formato ISO 8601)")]
        private string fechaRegistro;

        /// <summary>
        /// Estado actual de la revisión cultural.
        /// </summary>
        public EstadoConsentimiento Estado => estado;

        /// <summary>
        /// Justificación, condiciones de uso u observaciones registradas.
        /// </summary>
        public string Observaciones => observaciones;

        /// <summary>
        /// Fecha en que se emitió o actualizó la decisión.
        /// </summary>
        public string FechaRegistro => fechaRegistro;

        /// <summary>
        /// Constructor para inicialización del consentimiento.
        /// </summary>
        public ConsentimientoCultural(
            EstadoConsentimiento estado = EstadoConsentimiento.Pendiente,
            string observaciones = "",
            string fechaRegistro = "")
        {
            this.estado = estado;
            this.observaciones = observaciones;
            this.fechaRegistro = fechaRegistro;
        }

        /// <summary>
        /// Evalúa si el consentimiento permite la divulgación pública del contenido.
        /// Implementa la regla "fail closed" (Corrección #5): si la instancia es nula o el estado no es Aprobado, retorna false.
        /// </summary>
        /// <param name="consentimiento">Instancia de ConsentimientoCultural a evaluar (puede ser null).</param>
        /// <returns>True única y exclusivamente si el consentimiento existe y su estado es EstadoConsentimiento.Aprobado; false en cualquier otro caso.</returns>
        public static bool EsAptoParaDivulgacionPublica(ConsentimientoCultural consentimiento)
        {
            if (consentimiento == null)
            {
                return false; // Fail closed: la ausencia de consentimiento deniega acceso.
            }

            return consentimiento.Estado == EstadoConsentimiento.Aprobado;
        }
    }
}
