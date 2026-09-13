using System;
using UnityEngine;

namespace MusiyoBetsknate.Dominio
{
    // Diagrama 1, Sección 3 — Modelo de dominio cultural
    // Corrección #2 aplicada (Sección 3.2): Se elimina cualquier campo de modelo 3D (URL o referencia directa).
    /// <summary>
    /// Representa una máscara tradicional del Carnaval del Perdón (Bëtsknaté).
    /// Contiene metadatos específicos del oficio artesanal (materiales, técnica, significado).
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Corrección #2, Sección 3.2 del plan):
    /// Esta clase NO contiene ningún campo 'modelo3DUrl' ni referencia directa a assets 3D.
    /// La representación visual y los modelos 3D se asocian y resuelven exclusivamente a través
    /// de la colección de 'RecursoMultimedia' (Sprint 3), garantizando una única fuente de verdad
    /// y permitiendo la aplicación de los decoradores de accesibilidad (WCAG 2.2).
    /// </remarks>
    [CreateAssetMenu(fileName = "NuevaMascara", menuName = "Musiyo Bëtsknaté/Dominio/Máscara")]
    public class Mascara : ElementoCultural
    {
        [Header("Atributos Específicos de la Máscara")]
        [SerializeField]
        [Tooltip("Materiales tradicionales empleados en la elaboración (ej. Madera de sauce, chaquiras, pintura natural)")]
        private string materiales;

        [SerializeField]
        [Tooltip("Técnica artesanal de tallado o confección (ej. Tallado en madera y enchaquirado)")]
        private string tecnicaElaboracion;

        [SerializeField]
        [Tooltip("Nombre del artesano o portador que elaboró la pieza física (metadato de atribución)")]
        private string autorArtesano;

        [SerializeField]
        [Tooltip("Simbolismo cultural o rol del personaje en el Bëtsknaté (ej. Matachín, Sanjuanero, Autoridad)")]
        private string significadoRitual;

        /// <summary>
        /// Materiales tradicionales utilizados en la creación de la máscara.
        /// </summary>
        public string Materiales => materiales;

        /// <summary>
        /// Técnica artesanal de manufactura aplicada por el portador de conocimiento.
        /// </summary>
        public string TecnicaElaboracion => tecnicaElaboracion;

        /// <summary>
        /// Portador del conocimiento/artesano creador de la pieza (atribución cultural).
        /// </summary>
        public string AutorArtesano => autorArtesano;

        /// <summary>
        /// Contexto ritual y simbolismo dentro del Carnaval del Perdón.
        /// </summary>
        public string SignificadoRitual => significadoRitual;

        /// <summary>
        /// Inicializa los campos específicos de la máscara junto con los base en tiempo de ejecución o pruebas.
        /// </summary>
        public void InicializarMascara(
            string id,
            string nombre,
            string comunidadOrigen,
            string categoria,
            string descripcionBreve,
            string materiales,
            string tecnicaElaboracion,
            string autorArtesano,
            string significadoRitual,
            FichaCultural ficha = null,
            ConsentimientoCultural consentimiento = null)
        {
            InicializarBase(id, nombre, comunidadOrigen, categoria, descripcionBreve, ficha, consentimiento);
            this.materiales = materiales;
            this.tecnicaElaboracion = tecnicaElaboracion;
            this.autorArtesano = autorArtesano;
            this.significadoRitual = significadoRitual;
        }
    }
}
