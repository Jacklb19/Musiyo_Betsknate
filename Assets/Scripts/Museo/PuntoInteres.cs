using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using MusiyoBetsknate.Dominio;

namespace MusiyoBetsknate.Museo
{
    // Diagrama 2, Sección 3 — Capa de experiencia museográfica
    // Precisión adoptada (Secciones 3.3 y 8.2 del plan): PuntoInteres = vitrina activada por proximidad o mirada
    /// <summary>
    /// Vitrina o pedestal dentro de una sala: componente hoja del patrón Composite que expone
    /// varios <see cref="ElementoCultural"/> al visitante. Se enfoca al acercarse (proximidad) o al mirarlo
    /// (mirada), y también mediante el equivalente de teclado exigido por WCAG 2.2.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Decisión #4 de la bitácora, Sección 8.2 del plan):
    /// Ya no se activa con un botón de "siguiente punto". El enfoque y el examen son dos momentos
    /// distintos: enfocar anuncia la pieza y ofrece examinarla; examinar despliega la FichaCultural.
    ///
    /// REGLA "FAIL CLOSED" (Corrección #5, Sección 3.2 del plan):
    /// Solo presenta revisiones aprobadas con permisos vigentes de publicación y reutilización.
    /// Es un filtro de presentación; el backend debe autorizar también las consultas y los archivos.
    /// </remarks>
    public class PuntoInteres : ComponenteMuseoBase
    {
        /// <summary>
        /// Origen de la activación del punto de interés, utilizado para diferenciar el recorrido
        /// libre del recorrido asistido por teclado.
        /// </summary>
        public enum FuenteEnfoque
        {
            /// <summary>El visitante se acercó físicamente a la vitrina.</summary>
            Proximidad = 0,

            /// <summary>El visitante seleccionó la vitrina con el teclado (Tab).</summary>
            Teclado = 1
        }

        [Header("Contenido Cultural Exhibido")]
        [SerializeField]
        [Tooltip("Elemento cultural que exhibe esta vitrina. En el Sprint 4 se sustituye por datos del backend")]
        private ElementoCultural elementoCultural; // Campo legado: conserva referencias de escenas existentes.

        [SerializeField] private List<ElementoCultural> elementosCulturales = new List<ElementoCultural>();
        private ElementoCultural seleccion;

        /// <summary>Lista ordenada y filtrada en cada consulta, sin nombres de contenido bloqueado.</summary>
        public IReadOnlyList<ElementoCultural> ObtenerElementosPublicables()
        {
            var resultado = new List<ElementoCultural>();
            if (elementosCulturales.Count == 0)
            {
                if (elementoCultural != null && elementoCultural.EsDivulgablePublicamente)
                    resultado.Add(elementoCultural);
            }
            else
            {
                foreach (var elemento in elementosCulturales)
                    if (elemento != null && elemento.EsDivulgablePublicamente && !resultado.Contains(elemento))
                        resultado.Add(elemento);
            }
            return resultado.AsReadOnly();
        }

        public void AsignarElementos(IEnumerable<ElementoCultural> elementos)
        {
            var nuevos = elementos == null ? new List<ElementoCultural>() : new List<ElementoCultural>(elementos);
            elementosCulturales = nuevos;
            elementoCultural = null;
            seleccion = null;
        }

        public bool SeleccionarElemento(int indice)
        {
            var disponibles = ObtenerElementosPublicables();
            if (indice < 0 || indice >= disponibles.Count) return false;
            seleccion = disponibles[indice];
            return true;
        }

        public bool CambiarElemento(int direccion)
        {
            var actual = ElementoCultural;
            var disponibles = ObtenerElementosPublicables();
            if (disponibles.Count == 0 || direccion == 0) return false;
            int indice = 0;
            for (int i = 0; i < disponibles.Count; i++)
                if (disponibles[i] == actual) indice = i;
            return SeleccionarElemento((indice + (direccion > 0 ? 1 : -1) + disponibles.Count) % disponibles.Count);
        }

        [Header("Activación por Proximidad y Mirada")]
        [SerializeField]
        [Tooltip("Transform del visitante (XR Origin). Si se deja vacío, se resuelve con la cámara principal")]
        private Transform visitante;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Distancia en metros a la que la vitrina se enfoca automáticamente")]
        private float radioProximidad = 2.5f;

        [SerializeField]
        [Tooltip("Si está activo, además de estar cerca el visitante debe estar mirando la vitrina")]
        private bool requiereMirada;

        [SerializeField]
        [Range(5f, 180f)]
        [Tooltip("Apertura del cono de mirada, en grados, cuando se exige mirar la vitrina")]
        private float anguloMiradaGrados = 45f;

        [SerializeField]
        [Tooltip("Examina automáticamente la pieza al enfocarla por proximidad, sin esperar una confirmación")]
        private bool examinarAlEnfocar;

        [Header("Eventos de la Vitrina")]
        [SerializeField]
        [Tooltip("Se invoca al enfocar la vitrina (resaltado, indicación en pantalla, anuncio accesible)")]
        private UnityEvent<PuntoInteres> alEnfocar;

        [SerializeField]
        [Tooltip("Se invoca al perder el enfoque de la vitrina")]
        private UnityEvent<PuntoInteres> alDesenfocar;

        [SerializeField]
        [Tooltip("Se invoca al examinar contenido aprobado (conectar a VisorFichaCulturalUI.MostrarElemento)")]
        private UnityEvent<ElementoCultural> alExaminar;

        [SerializeField]
        [Tooltip("Se invoca cuando la divulgación se deniega por la regla fail-closed, con el motivo como texto")]
        private UnityEvent<string> alDenegarDivulgacion;

        private bool enfocadoPorProximidad;
        private bool enfocadoPorTeclado;
        private bool estaEnfocado;

        /// <summary>
        /// Elemento cultural exhibido en esta vitrina (puede ser nulo mientras no se asigne contenido).
        /// </summary>
        public ElementoCultural ElementoCultural
        {
            get
            {
                var disponibles = ObtenerElementosPublicables();
                foreach (var elemento in disponibles)
                    if (elemento == seleccion) return seleccion;
                seleccion = disponibles.Count > 0 ? disponibles[0] : null;
                return seleccion;
            }
        }

        /// <summary>
        /// Indica si la vitrina está enfocada por proximidad, mirada o selección de teclado.
        /// </summary>
        public bool EstaEnfocado => estaEnfocado;

        /// <summary>
        /// Indica si el contenido asignado puede mostrarse al público según su consentimiento cultural.
        /// </summary>
        public bool TieneContenidoDivulgable => ElementoCultural != null;

        /// <summary>
        /// Los puntos de interés son componentes hoja del patrón Composite.
        /// </summary>
        protected override bool AdmiteHijos => false;

        protected override void Awake()
        {
            base.Awake();
            ResolverVisitante();
        }

        private void Update()
        {
            if (!EstaActivo)
            {
                return;
            }

            EvaluarProximidad();
        }

        /// <inheritdoc/>
        public override void Desactivar()
        {
            base.Desactivar();

            enfocadoPorProximidad = false;
            enfocadoPorTeclado = false;
            ActualizarEnfoque();
        }

        /// <summary>
        /// Asigna en tiempo de ejecución el elemento cultural exhibido. Lo utilizará el Sprint 4
        /// al poblar el recorrido con la respuesta de GET /recorridos/{id}.
        /// </summary>
        /// <param name="elemento">Elemento cultural aprobado a exhibir.</param>
        public void AsignarElemento(ElementoCultural elemento)
        {
            AsignarElementos(elemento == null ? null : new[] { elemento });
        }

        /// <summary>
        /// Declara explícitamente quién es el visitante, evitando depender de la cámara principal.
        /// </summary>
        /// <param name="transformVisitante">Transform raíz del visitante (XR Origin).</param>
        public void AsignarVisitante(Transform transformVisitante)
        {
            visitante = transformVisitante;
        }

        /// <summary>
        /// Enfoca la vitrina desde la fuente indicada (proximidad o teclado).
        /// </summary>
        public void Enfocar(FuenteEnfoque fuente)
        {
            if (fuente == FuenteEnfoque.Teclado)
            {
                enfocadoPorTeclado = true;
            }
            else
            {
                enfocadoPorProximidad = true;
            }

            ActualizarEnfoque();
        }

        /// <summary>
        /// Retira el enfoque proveniente de la fuente indicada, conservando el de la otra fuente.
        /// </summary>
        public void Desenfocar(FuenteEnfoque fuente)
        {
            if (fuente == FuenteEnfoque.Teclado)
            {
                enfocadoPorTeclado = false;
            }
            else
            {
                enfocadoPorProximidad = false;
            }

            ActualizarEnfoque();
        }

        /// <summary>
        /// Despliega la ficha cultural de la pieza, aplicando la regla fail-closed antes de divulgar nada.
        /// Es el destino común del acercamiento físico y de la tecla Enter del recorrido accesible.
        /// </summary>
        /// <returns>True si el contenido se divulgó; false si fue denegado.</returns>
        public bool Examinar()
        {
            var elemento = ElementoCultural;
            if (!EstaActivo || !isActiveAndEnabled || elemento == null)
            {
                alDenegarDivulgacion?.Invoke("Contenido no disponible para divulgación pública.");
                return false;
            }
            alExaminar?.Invoke(elemento);
            return true;
        }

        public string ObtenerAnuncioAccesible()
        {
            var elemento = ElementoCultural;
            if (elemento == null) return "Contenido no disponible para divulgación pública.";
            var disponibles = ObtenerElementosPublicables();
            int indice = 0;
            for (int i = 0; i < disponibles.Count; i++)
                if (disponibles[i] == elemento) indice = i;
            return $"{elemento.Nombre}. Elemento {indice + 1} de {disponibles.Count}. "
                + "Presione Enter para examinar. Re Pág / Av Pág cambia de elemento.";
        }

        /// <summary>
        /// Evalúa la distancia y, si corresponde, el ángulo de mirada del visitante respecto a la vitrina.
        /// </summary>
        private void EvaluarProximidad()
        {
            if (visitante == null && !ResolverVisitante())
            {
                return;
            }

            bool dentroDelRadio = Vector3.Distance(visitante.position, transform.position) <= radioProximidad;
            bool condicionCumplida = dentroDelRadio && (!requiereMirada || VisitanteEstaMirando());

            if (condicionCumplida == enfocadoPorProximidad)
            {
                return;
            }

            if (condicionCumplida)
            {
                Enfocar(FuenteEnfoque.Proximidad);

                if (examinarAlEnfocar)
                {
                    Examinar();
                }
            }
            else
            {
                Desenfocar(FuenteEnfoque.Proximidad);
            }
        }

        /// <summary>
        /// Comprueba que la vitrina esté dentro del cono de mirada del visitante.
        /// </summary>
        private bool VisitanteEstaMirando()
        {
            Vector3 direccionAVitrina = transform.position - visitante.position;

            if (direccionAVitrina.sqrMagnitude < Mathf.Epsilon)
            {
                return true;
            }

            float angulo = Vector3.Angle(visitante.forward, direccionAVitrina);
            return angulo <= anguloMiradaGrados * 0.5f;
        }

        /// <summary>
        /// Consolida el estado de enfoque a partir de sus dos fuentes y notifica el cambio una sola vez.
        /// </summary>
        private void ActualizarEnfoque()
        {
            bool enfoqueCalculado = enfocadoPorProximidad || enfocadoPorTeclado;

            if (enfoqueCalculado == estaEnfocado)
            {
                return;
            }

            estaEnfocado = enfoqueCalculado;

            if (estaEnfocado)
            {
                alEnfocar?.Invoke(this);
            }
            else
            {
                alDesenfocar?.Invoke(this);
            }
        }

        /// <summary>
        /// Resuelve el transform del visitante usando la cámara principal cuando no se asignó uno.
        /// </summary>
        private bool ResolverVisitante()
        {
            if (visitante != null)
            {
                return true;
            }

            Camera camaraPrincipal = Camera.main;

            if (camaraPrincipal != null)
            {
                visitante = camaraPrincipal.transform;
                return true;
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.65f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radioProximidad);
        }
    }
}
