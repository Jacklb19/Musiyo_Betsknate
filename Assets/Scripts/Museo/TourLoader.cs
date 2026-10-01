using System;
using System.Collections;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    public enum TourLoadState { Idle, Loading, Ready, Unavailable }

    [RequireComponent(typeof(TourRuntime))]
    public sealed class TourLoader : MonoBehaviour
    {
        [SerializeField] private string apiBase;
        [SerializeField] private bool loadOnStart = true;
        private UnityWebRequest activeRequest;
        private Coroutine loading;
        public TourRuntime Runtime => GetComponent<TourRuntime>();
        public TourLoadState State { get; private set; }
        public string Status { get; private set; } = "";
        public string ApiBase => ResolveApiBase(apiBase, Application.absoluteURL);
        public event Action Changed;

        private void Start() { if (loadOnStart) Reload(); }
        private void OnDisable() => Cancel();

        public void Reload()
        {
            Cancel();
            Runtime.Clear();
            loading = StartCoroutine(Load());
        }

        private void Cancel()
        {
            if (loading != null) StopCoroutine(loading);
            loading = null;
            if (activeRequest == null) return;
            activeRequest.Abort();
            activeRequest.Dispose();
            activeRequest = null;
        }

        private IEnumerator Load()
        {
            SetState(TourLoadState.Loading, "Cargando recorrido…");
            string url;
            try { url = ApiBase + "/tours/" + Uri.EscapeDataString(Runtime.TourKey) + "?schema_version=1"; }
            catch (ArgumentException) { SetState(TourLoadState.Unavailable, "La dirección del servicio no es válida."); yield break; }
            activeRequest = UnityWebRequest.Get(url);
            activeRequest.timeout = 15;
            yield return activeRequest.SendWebRequest();
            var success = activeRequest.result == UnityWebRequest.Result.Success;
            var json = success ? activeRequest.downloadHandler.text : null;
            activeRequest.Dispose();
            activeRequest = null;
            loading = null;
            if (!success)
            { SetState(TourLoadState.Unavailable, "No fue posible cargar el contenido. Puedes seguir recorriendo el museo y reintentar."); yield break; }
            if (!TourContractV1.TryParse(json, out var contract, out var error) || !Runtime.Apply(contract, out error))
            { SetState(TourLoadState.Unavailable, "El recorrido recibido no es compatible."); yield break; }
            SetState(TourLoadState.Ready, "Recorrido disponible. Exploración libre · ruta sugerida de unos 30 minutos.");
        }

        private void SetState(TourLoadState state, string status)
        {
            State = state;
            Status = status;
            Changed?.Invoke();
        }

        public static string ResolveApiBase(string configured, string pageUrl)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
                    || (configuredUri.Scheme != "http" && configuredUri.Scheme != "https"))
                    throw new ArgumentException("API base must be an HTTP or HTTPS URL");
                return configured.TrimEnd('/');
            }
            if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) && (page.Scheme == "http" || page.Scheme == "https"))
                return new Uri(page, "/api/v1").AbsoluteUri.TrimEnd('/');
            return "http://127.0.0.1:8000/api/v1";
        }
    }
}
