using System;
using System.Collections.Generic;

namespace MusiyoBetsknate.Museo
{
    // Diagrama 2, Sección 3 — Capa de experiencia museográfica
    // Patrón Composite (RecorridoVirtual -> Escena -> PuntoInteres)
    /// <summary>
    /// Interfaz común del patrón Composite para todos los componentes del museo virtual.
    /// Permite tratar de forma uniforme a un recorrido completo, a una sala individual
    /// y a una vitrina (punto de interés), de modo que activar un recorrido active en
    /// cascada sus salas y las vitrinas que contienen.
    /// </summary>
    /// <remarks>
    /// NOTA ARQUITECTÓNICA (Sprint 2, Sección 8.2 del plan):
    /// Los componentes compuestos (RecorridoVirtual, Escena) implementan Agregar/Remover;
    /// los componentes hoja (PuntoInteres) exponen las mismas operaciones para conservar la
    /// uniformidad del patrón, pero las rechazan explícitamente registrando una advertencia.
    /// </remarks>
    public interface IComponenteMuseo
    {
        /// <summary>
        /// Identificador único del componente, sincronizable con el backend
        /// (respuesta de GET /recorridos/{id}, Sección 6.6 del plan).
        /// </summary>
        string IdComponente { get; }

        /// <summary>
        /// Nombre legible del componente, utilizado en la interfaz y en los anuncios de accesibilidad.
        /// </summary>
        string NombreComponente { get; }

        /// <summary>
        /// Indica si el componente está actualmente activo dentro del recorrido.
        /// </summary>
        bool EstaActivo { get; }

        /// <summary>
        /// Hijos directos del componente. Vacío en los componentes hoja.
        /// </summary>
        IReadOnlyList<IComponenteMuseo> Hijos { get; }

        /// <summary>
        /// Agrega un componente hijo (solo en componentes compuestos).
        /// </summary>
        void Agregar(IComponenteMuseo hijo);

        /// <summary>
        /// Remueve un componente hijo (solo en componentes compuestos).
        /// </summary>
        void Remover(IComponenteMuseo hijo);

        /// <summary>
        /// Activa el componente y propaga la activación a sus hijos.
        /// </summary>
        void Activar();

        /// <summary>
        /// Desactiva el componente y propaga la desactivación a sus hijos.
        /// </summary>
        void Desactivar();

        /// <summary>
        /// Aplica una acción a este componente y, recursivamente, a todos sus descendientes.
        /// </summary>
        /// <param name="accion">Acción a ejecutar sobre cada componente del subárbol.</param>
        void RecorrerJerarquia(Action<IComponenteMuseo> accion);
    }
}
