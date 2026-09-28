using System;
using System.Globalization;
using UnityEngine;

namespace MusiyoBetsknate.Dominio
{
    public enum EstadoEditorial { Borrador, Pendiente, Aprobado, Restringido, Retirado }
    public enum EstadoAutorizacion { Pendiente, Autorizado, Denegado, Revocado }
    public enum UsoCultural { Capturar, ProcesarLocalmente, ProcesarExternamente, Publicar, Reutilizar }

    /// <summary>Resumen de permiso recibido del backend. No contiene evidencia privada.</summary>
    [Serializable]
    public sealed class AutorizacionUso
    {
        [SerializeField] private string revisionId;
        [SerializeField] private UsoCultural uso;
        [SerializeField] private EstadoAutorizacion estado;
        [SerializeField] private string inicioUtc;
        [SerializeField] private string finUtc;

        public AutorizacionUso(string revisionId, UsoCultural uso, EstadoAutorizacion estado,
            string inicioUtc, string finUtc)
        {
            this.revisionId = revisionId;
            this.uso = uso;
            this.estado = estado;
            this.inicioUtc = inicioUtc;
            this.finUtc = finUtc;
        }

        public bool Permite(string revision, UsoCultural usoSolicitado, DateTimeOffset ahora)
        {
            return !string.IsNullOrWhiteSpace(revision) && revisionId == revision && uso == usoSolicitado
                && estado == EstadoAutorizacion.Autorizado
                && LeerFecha(inicioUtc, out var inicio) && LeerFecha(finUtc, out var fin)
                && inicio < fin && ahora >= inicio && ahora < fin;
        }

        // Sin zona horaria o sin intervalo explícito, se deniega. El reloj local no es autoridad.
        private static bool LeerFecha(string valor, out DateTimeOffset fecha)
        {
            return DateTimeOffset.TryParseExact(valor,
                new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fecha);
        }
    }

    /// <summary>
    /// Política local de presentación (RF-11 y RNF-09 vigentes). El backend sigue autorizando
    /// consultas y archivos; este resumen local no sustituye su control ni su auditoría.
    /// </summary>
    [Serializable]
    public sealed class RevisionPublicacion
    {
        [SerializeField] private string id;
        [SerializeField] private EstadoEditorial estado;
        [SerializeField] private bool representacionRevisada;
        [SerializeField] private bool alternativasRevisadas;
        [SerializeField] private AutorizacionUso publicacion;
        [SerializeField] private AutorizacionUso reutilizacion;

        public string Id => id;
        public EstadoEditorial Estado => estado;

        public RevisionPublicacion(string id, EstadoEditorial estado, bool representacionRevisada,
            bool alternativasRevisadas, AutorizacionUso publicacion, AutorizacionUso reutilizacion)
        {
            this.id = id;
            this.estado = estado;
            this.representacionRevisada = representacionRevisada;
            this.alternativasRevisadas = alternativasRevisadas;
            this.publicacion = publicacion;
            this.reutilizacion = reutilizacion;
        }

        public bool EsPublicableEn(DateTimeOffset ahora) => !string.IsNullOrWhiteSpace(id)
            && estado == EstadoEditorial.Aprobado && representacionRevisada && alternativasRevisadas
            && publicacion != null && publicacion.Permite(id, UsoCultural.Publicar, ahora)
            && reutilizacion != null && reutilizacion.Permite(id, UsoCultural.Reutilizar, ahora);
    }
}
