using UnityEngine;
using TMPro;
using MusiyoBetsknate.Museo;

namespace MusiyoBetsknate.UI
{
    // Sección 8.2 del plan — Retroalimentación en pantalla del recorrido por proximidad y por teclado
    // WCAG 2.2: todo anuncio sonoro debe tener su equivalente textual visible
    /// <summary>
    /// Franja de anuncios del recorrido: muestra en texto qué vitrina está enfocada, qué tecla la
    /// examina y por qué un contenido no puede divulgarse.
    /// </summary>
    /// <remarks>
    /// NOTA DE DISEÑO (Sprint 2):
    /// Es el destino natural de los eventos de PuntoInteres y de NavegacionTecladoPuntosInteres, que se
    /// conectan desde el Inspector sin escribir código de pegamento. En el Sprint 3 este mismo componente
    /// servirá para mostrar la transcripción del audio de narración pregenerada.
    /// </remarks>
    public class AnunciadorAccesibleUI : MonoBehaviour
    {
        [Header("Referencias de UI")]
        [SerializeField]
        [Tooltip("Panel contenedor del anuncio; se oculta cuando no hay nada que anunciar")]
        private GameObject panelAnuncio;

        [SerializeField]
        [Tooltip("Campo de texto donde se escribe el anuncio accesible")]
        private TextMeshProUGUI textoAnuncio;

        [Header("Comportamiento")]
        [SerializeField]
        [Min(0f)]
        [Tooltip("Segundos que permanece visible un anuncio. Cero mantiene el texto hasta el siguiente anuncio")]
        private float segundosVisible = 4f;

        private float tiempoRestante;
        private PuntoInteres puntoAnunciado;

        private void Awake()
        {
            Limpiar();
        }

        private void Update()
        {
            if (puntoAnunciado != null && !puntoAnunciado.TieneContenidoDivulgable)
            {
                MostrarAnuncio("Contenido no disponible para divulgación pública.");
            }
            if (segundosVisible <= 0f || tiempoRestante <= 0f)
            {
                return;
            }

            tiempoRestante -= Time.deltaTime;

            if (tiempoRestante <= 0f)
            {
                Limpiar();
            }
        }

        /// <summary>
        /// Muestra un texto de anuncio en la franja accesible.
        /// </summary>
        /// <param name="mensaje">Texto a mostrar; si está vacío, la franja se oculta.</param>
        public void MostrarAnuncio(string mensaje)
        {
            puntoAnunciado = null;
            if (string.IsNullOrWhiteSpace(mensaje))
            {
                Limpiar();
                return;
            }

            if (panelAnuncio != null) panelAnuncio.SetActive(true);
            if (textoAnuncio != null) textoAnuncio.text = mensaje;

            tiempoRestante = segundosVisible;
        }

        /// <summary>
        /// Muestra el anuncio correspondiente a una vitrina enfocada por proximidad, mirada o teclado.
        /// </summary>
        /// <param name="vitrina">Punto de interés enfocado.</param>
        public void MostrarAnuncioDeVitrina(PuntoInteres vitrina)
        {
            if (vitrina == null)
            {
                Limpiar();
                return;
            }

            MostrarAnuncio(vitrina.ObtenerAnuncioAccesible());
            puntoAnunciado = vitrina;
        }

        /// <summary>
        /// Oculta la franja de anuncios.
        /// </summary>
        public void Limpiar()
        {
            tiempoRestante = 0f;
            puntoAnunciado = null;

            if (textoAnuncio != null) textoAnuncio.text = string.Empty;
            if (panelAnuncio != null) panelAnuncio.SetActive(false);
        }
    }
}
