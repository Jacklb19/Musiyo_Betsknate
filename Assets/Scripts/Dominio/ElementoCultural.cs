using System;
using UnityEngine;

namespace MusiyoBetsknate.Dominio
{
    // Diagrama 1, Sección 3 — Modelo de dominio cultural
    // Relación: Composición (ElementoCultural 1 *-* 1 FichaCultural)
    /// <summary>
    /// Clase base abstracta que representa cualquier manifestación o patrimonio cultural del Bëtsknaté.
    /// Diseñada como ScriptableObject para permitir la creación de datos de prueba en el Editor de Unity (Sprint 1)
    /// y servir como estructura base para la futura deserialización de datos desde el backend FastAPI.
    /// Heredan de esta clase: Mascara, Danza, Canto y RelatoOral.
    /// </summary>
    public abstract class ElementoCultural : ScriptableObject
    {
        [Header("Identificación y Atribución Cultural")]
        [SerializeField]
        [Tooltip("Identificador único del elemento cultural (sincronizado con backend)")]
        private string id;

        [SerializeField]
        [Tooltip("Nombre representativo del elemento cultural")]
        private string nombre;

        [SerializeField]
        [Tooltip("Comunidad o territorio de origen (ej. Comunidad Kamëntsá / Inga del Valle de Sibundoy)")]
        private string comunidadOrigen;

        [SerializeField]
        [Tooltip("Categoría temática o cultural a la que pertenece")]
        private string categoria;

        [SerializeField]
        [TextArea(3, 6)]
        [Tooltip("Descripción general o contextual del elemento")]
        private string descripcionBreve;

        [Header("Ficha Cultural (Composición 1 a 1)")]
        [SerializeField]
        [Tooltip("Ficha informativa y descriptiva detallada del elemento cultural")]
        private FichaCultural fichaCultural;

        [Header("Gobernanza Cultural (Relación 1 -> 0..1)")]
        [SerializeField]
        [Tooltip("Consentimiento cultural registrado. Si es nulo o no aprobado, aplica la política fail-closed (Corrección #5)")]
        private ConsentimientoCultural consentimientoCultural;

        /// <summary>
        /// Identificador único del elemento cultural.
        /// </summary>
        public string Id => id;

        /// <summary>
        /// Nombre del elemento cultural.
        /// </summary>
        public string Nombre => nombre;

        /// <summary>
        /// Comunidad o territorio de origen del conocimiento.
        /// </summary>
        public string ComunidadOrigen => comunidadOrigen;

        /// <summary>
        /// Categoría temática o clasificación cultural.
        /// </summary>
        public string Categoria => categoria;

        /// <summary>
        /// Descripción contextual o resumen del elemento.
        /// </summary>
        public string DescripcionBreve => descripcionBreve;

        /// <summary>
        /// Ficha cultural detallada embebida por composición.
        /// </summary>
        public FichaCultural FichaCultural => fichaCultural;

        /// <summary>
        /// Consentimiento cultural asociado (puede ser nulo).
        /// </summary>
        public ConsentimientoCultural ConsentimientoCultural => consentimientoCultural;

        /// <summary>
        /// Evalúa si este elemento cultural puede ser divulgado públicamente según su consentimiento.
        /// </summary>
        public bool EsDivulgablePublicamente => ConsentimientoCultural.EsAptoParaDivulgacionPublica(consentimientoCultural);

        /// <summary>
        /// Asigna valores base al elemento cultural en tiempo de ejecución o pruebas.
        /// </summary>
        public void InicializarBase(
            string id, 
            string nombre, 
            string comunidadOrigen, 
            string categoria, 
            string descripcionBreve, 
            FichaCultural ficha = null,
            ConsentimientoCultural consentimiento = null)
        {
            this.id = id;
            this.nombre = nombre;
            this.comunidadOrigen = comunidadOrigen;
            this.categoria = categoria;
            this.descripcionBreve = descripcionBreve;
            this.fichaCultural = ficha;
            this.consentimientoCultural = consentimiento;
        }
    }
}
