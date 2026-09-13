using System;
using UnityEngine;

namespace MusiyoBetsknate.Dominio
{
    // Diagrama 1, Sección 3 — Modelo de dominio cultural
    // Relación: Composición (ElementoCultural 1 *-* 1 FichaCultural)
    /// <summary>
    /// Representa el contenido informativo y descriptivo de un Elemento Cultural.
    /// Diseñada como estructura de datos serializable ([Serializable]) para modelar la relación de COMPOSICIÓN estricta
    /// con ElementoCultural: no tiene ciclo de vida independiente ni es un ScriptableObject suelto.
    /// </summary>
    /// <remarks>
    /// NOTA DE DISEÑO (Sprint 1):
    /// - No contiene referencias a modelos 3D, imágenes o videos (responsabilidad de RecursoMultimedia en Sprint 3).
    /// - No contiene estado de moderación ni permisos (responsabilidad de ConsentimientoCultural y ContenidoCulturalProxy).
    /// </remarks>
    [Serializable]
    public class FichaCultural
    {
        [Header("Contenido Textual")]
        [SerializeField]
        [Tooltip("Título o denominación formal del elemento en la ficha")]
        private string titulo;

        [SerializeField]
        [TextArea(3, 8)]
        [Tooltip("Descripción detallada del elemento cultural y su relevancia en el Bëtsknaté")]
        private string descripcionDetallada;

        [SerializeField]
        [TextArea(2, 5)]
        [Tooltip("Contexto tradicional, historia transmitida o uso ceremonial")]
        private string contextoTradicional;

        [SerializeField]
        [Tooltip("Comunidad, vereda o territorio de procedencia")]
        private string procedenciaComunitaria;

        /// <summary>
        /// Título o denominación del elemento cultural.
        /// </summary>
        public string Titulo => titulo;

        /// <summary>
        /// Descripción textual completa del elemento.
        /// </summary>
        public string DescripcionDetallada => descripcionDetallada;

        /// <summary>
        /// Contexto ceremonial, historia y uso en el Carnaval del Perdón.
        /// </summary>
        public string ContextoTradicional => contextoTradicional;

        /// <summary>
        /// Comunidad o territorio de origen.
        /// </summary>
        public string ProcedenciaComunitaria => procedenciaComunitaria;

        /// <summary>
        /// Constructor para inicialización directa de la ficha cultural.
        /// </summary>
        public FichaCultural(
            string titulo,
            string descripcionDetallada,
            string contextoTradicional,
            string procedenciaComunitaria)
        {
            this.titulo = titulo;
            this.descripcionDetallada = descripcionDetallada;
            this.contextoTradicional = contextoTradicional;
            this.procedenciaComunitaria = procedenciaComunitaria;
        }
    }
}
