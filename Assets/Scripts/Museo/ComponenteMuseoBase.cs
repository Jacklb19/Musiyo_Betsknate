using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museo
{
    // Diagrama 2, Sección 3 — Capa de experiencia museográfica
    // Patrón Composite: implementación base compartida por compuestos y hojas
    /// <summary>
    /// Implementación base de <see cref="IComponenteMuseo"/> sobre MonoBehaviour.
    /// Centraliza identificación, estado de activación y recorrido de la jerarquía, de modo que
    /// RecorridoVirtual, Escena y PuntoInteres solo definan su comportamiento propio.
    /// </summary>
    /// <remarks>
    /// NOTA DE DISEÑO (Sprint 2):
    /// La jerarquía lógica del Composite se resuelve a partir de la jerarquía de GameObjects de Unity
    /// (ver <see cref="RecolectarHijosDeJerarquia"/>), para que el modelo del Diagrama 2 coincida con lo
    /// que el desarrollador ve en la ventana Hierarchy y no exija mantener dos estructuras paralelas.
    /// </remarks>
    public abstract class ComponenteMuseoBase : MonoBehaviour, IComponenteMuseo
    {
        [Header("Identificación del Componente (Composite)")]
        [SerializeField]
        [Tooltip("Identificador único del componente, sincronizado con el backend (GET /recorridos/{id})")]
        private string idComponente;

        [SerializeField]
        [Tooltip("Nombre legible del componente, usado en la interfaz y en los anuncios de accesibilidad")]
        private string nombreComponente;

        /// <summary>
        /// Hijos directos registrados en este componente.
        /// </summary>
        protected readonly List<IComponenteMuseo> hijos = new List<IComponenteMuseo>();

        // Los componentes nacen activos: una sala o vitrina solo deja de estarlo cuando el recorrido
        // la desactiva explícitamente. Así, una escena de prueba sin RecorridoVirtual sigue siendo usable.
        private bool estaActivo = true;

        /// <inheritdoc/>
        public string IdComponente => string.IsNullOrEmpty(idComponente) ? name : idComponente;

        /// <inheritdoc/>
        public string NombreComponente => string.IsNullOrEmpty(nombreComponente) ? name : nombreComponente;

        /// <inheritdoc/>
        public bool EstaActivo => estaActivo;

        /// <inheritdoc/>
        public IReadOnlyList<IComponenteMuseo> Hijos => hijos;

        /// <summary>
        /// Indica si este componente admite hijos. Los componentes hoja lo sobrescriben con false.
        /// </summary>
        protected virtual bool AdmiteHijos => true;

        protected virtual void Awake()
        {
            RecolectarHijosDeJerarquia();
        }

        /// <inheritdoc/>
        public virtual void Agregar(IComponenteMuseo hijo)
        {
            if (!AdmiteHijos)
            {
                Debug.LogWarning($"[{NombreComponente}] es un componente hoja del museo y no admite hijos.", this);
                return;
            }

            if (hijo == null || hijos.Contains(hijo))
            {
                return;
            }

            hijos.Add(hijo);
        }

        /// <inheritdoc/>
        public virtual void Remover(IComponenteMuseo hijo)
        {
            if (!AdmiteHijos)
            {
                Debug.LogWarning($"[{NombreComponente}] es un componente hoja del museo y no contiene hijos.", this);
                return;
            }

            hijos.Remove(hijo);
        }

        /// <inheritdoc/>
        public virtual void Activar()
        {
            estaActivo = true;

            foreach (IComponenteMuseo hijo in hijos)
            {
                hijo.Activar();
            }
        }

        /// <inheritdoc/>
        public virtual void Desactivar()
        {
            estaActivo = false;

            foreach (IComponenteMuseo hijo in hijos)
            {
                hijo.Desactivar();
            }
        }

        /// <inheritdoc/>
        public void RecorrerJerarquia(Action<IComponenteMuseo> accion)
        {
            if (accion == null)
            {
                return;
            }

            accion(this);

            foreach (IComponenteMuseo hijo in hijos)
            {
                hijo.RecorrerJerarquia(accion);
            }
        }

        /// <summary>
        /// Reconstruye la lista de hijos a partir de los componentes del tipo esperado que cuelgan
        /// de este GameObject en la jerarquía de Unity. Los componentes hoja no recolectan nada.
        /// </summary>
        protected virtual void RecolectarHijosDeJerarquia()
        {
        }

        /// <summary>
        /// Busca en los descendientes directos de la jerarquía de Unity los componentes museográficos
        /// del tipo indicado, ignorando los que pertenecen a otro subárbol ya gestionado.
        /// </summary>
        /// <typeparam name="T">Tipo de componente museográfico hijo esperado.</typeparam>
        protected void RegistrarHijosDeTipo<T>() where T : ComponenteMuseoBase
        {
            hijos.Clear();

            T[] candidatos = GetComponentsInChildren<T>(includeInactive: true);

            foreach (T candidato in candidatos)
            {
                if (candidato == (object)this)
                {
                    continue;
                }

                Agregar(candidato);
            }
        }
    }
}
