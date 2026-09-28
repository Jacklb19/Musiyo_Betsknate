using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museo
{
    // El build WebGL comparte origen con la web; consulta la API antes de aceptar una selección.
    public sealed class PuenteRecorridoWeb : MonoBehaviour
    {
        private RecorridoVirtual recorrido;
        private NavegacionTecladoPuntosInteres navegacion;
        private ContratoRecorridoV1 contrato;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MusiyoNotificarPunto(string recorridoId, string puntoId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Instalar()
        {
            var recorridoActivo = FindAnyObjectByType<RecorridoVirtual>();
            if (recorridoActivo != null && recorridoActivo.GetComponent<PuenteRecorridoWeb>() == null)
                recorridoActivo.gameObject.AddComponent<PuenteRecorridoWeb>();
        }
#endif

        private IEnumerator Start()
        {
            recorrido = GetComponent<RecorridoVirtual>();
            navegacion = FindAnyObjectByType<NavegacionTecladoPuntosInteres>();
            if (recorrido == null || navegacion == null)
            {
                Debug.LogError("Falta recorrido o navegación para el puente web.", this);
                yield break;
            }

            navegacion.PuntoSeleccionado += NotificarPunto;
            yield return null; // Permite que RecorridoVirtual active la sala inicial.

            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var pagina))
                yield break;

            var idSolicitado = Parametro(pagina, "recorrido");
            if (!string.IsNullOrEmpty(idSolicitado) && idSolicitado != recorrido.IdComponente)
            {
                Debug.LogWarning("El recorrido solicitado no coincide con la escena.", this);
                yield break;
            }

            var url = new Uri(pagina, "/api/v1/recorridos/" + Uri.EscapeDataString(recorrido.IdComponente));
            using (var solicitud = UnityWebRequest.Get(url.AbsoluteUri))
            {
                yield return solicitud.SendWebRequest();
                if (solicitud.result != UnityWebRequest.Result.Success
                    || !ContratoRecorridoV1.IntentarLeer(solicitud.downloadHandler.text, out var recibido, out var error)
                    || !recibido.CoincideCon(recorrido, out error))
                {
                    Debug.LogError("No se pudo validar el recorrido con la API.", this);
                    yield break;
                }
                contrato = recibido;
            }

            var puntoId = Parametro(pagina, "punto");
            var elementoId = Parametro(pagina, "elemento");
            if (string.IsNullOrEmpty(puntoId)) yield break;
            if (!IntentarResolver(contrato, puntoId, elementoId, out var salaId))
            {
                Debug.LogWarning("El punto o elemento solicitado no está disponible.", this);
                yield break;
            }

            foreach (var sala in recorrido.Salas)
            {
                if (sala.IdComponente != salaId) continue;
                recorrido.IrASala(sala);
                foreach (var hijo in sala.Hijos)
                    if (hijo is PuntoInteres punto && punto.IdComponente == puntoId)
                    {
                        navegacion.Seleccionar(punto);
                        yield break;
                    }
            }
        }

        private void OnDestroy()
        {
            if (navegacion != null) navegacion.PuntoSeleccionado -= NotificarPunto;
        }

        private void NotificarPunto(PuntoInteres punto)
        {
            if (contrato == null || punto == null
                || !IntentarResolver(contrato, punto.IdComponente, null, out _)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            MusiyoNotificarPunto(recorrido.IdComponente, punto.IdComponente);
#endif
        }

        public static bool IntentarResolver(ContratoRecorridoV1 origen, string puntoId,
            string elementoId, out string salaId)
        {
            salaId = null;
            if (origen == null || string.IsNullOrWhiteSpace(puntoId) || origen.salas == null)
                return false;
            foreach (var sala in origen.salas)
                foreach (var punto in sala.puntos)
                {
                    if (punto.anclajeId != puntoId) continue;
                    if (!string.IsNullOrEmpty(elementoId)
                        && Array.IndexOf(punto.elementoIds, elementoId) < 0) return false;
                    salaId = sala.id;
                    return true;
                }
            return false;
        }

        private static string Parametro(Uri pagina, string nombre)
        {
            foreach (var parte in pagina.Query.TrimStart('?').Split('&'))
            {
                var par = parte.Split(new[] { '=' }, 2);
                if (par.Length == 2 && par[0] == nombre)
                    return Uri.UnescapeDataString(par[1].Replace('+', ' '));
            }
            return null;
        }
    }
}
