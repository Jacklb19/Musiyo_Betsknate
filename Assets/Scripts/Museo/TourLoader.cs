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
        private Func<UnityWebRequest, UnityWebRequestAsyncOperation> sendRequest = request => request.SendWebRequest();
        private int failedAttempts;
        private float retryAt = float.PositiveInfinity;
        public TourRuntime Runtime => GetComponent<TourRuntime>();
        public TourLoadState State { get; private set; }
        public string Status { get; private set; } = "";
        public string ApiBase => ResolveApiBase(apiBase, Application.absoluteURL);
        /// <summary>True while the service could not be reached and another attempt is scheduled on its own.</summary>
        public bool RetryScheduled => State == TourLoadState.Unavailable && !float.IsPositiveInfinity(retryAt);
        /// <summary>Seconds until the next automatic attempt, or 0 when none is scheduled.</summary>
        public float RetryInSeconds => RetryScheduled ? Mathf.Max(0, retryAt - Time.realtimeSinceStartup) : 0;
        public event Action Changed;

        private void Start() { if (loadOnStart) Reload(); }
        private void OnDisable() => Cancel();
        /// <summary>An unreachable service is tried again on its own, waiting longer each time; the visit goes on meanwhile.</summary>
        private void Update()
        {
            if (!RetryScheduled || Time.realtimeSinceStartup < retryAt) return;
            // A tour that arrived another way, such as a selection from the web page, is never cleared by a retry.
            if (Runtime.Contract == null) Reload();
            else retryAt = float.PositiveInfinity;
        }

        /// <summary>Wait before the next attempt after a number of failed ones.</summary>
        public static float RetryDelay(float[] waits, int failures)
            => waits == null || waits.Length == 0 ? 30 : waits[Mathf.Clamp(failures - 1, 0, waits.Length - 1)];

        /// <summary>Only a service that did not answer is tried again; an incompatible answer would fail the same way.</summary>
        private void ScheduleRetry()
        {
            failedAttempts++;
            retryAt = Time.realtimeSinceStartup + RetryDelay(MuseumExperienceConfiguration.Current.ServiceRetrySeconds, failedAttempts);
        }

        public void Reload()
        {
            Cancel();
            retryAt = float.PositiveInfinity;
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
            SetState(TourLoadState.Loading, MuseumInterfaceText.Get("tour_loading"));
            string url;
            try { url = ApiBase + "/tours/" + Uri.EscapeDataString(Runtime.TourKey) + "?schema_version=1"; }
            catch (ArgumentException) { SetState(TourLoadState.Unavailable, MuseumInterfaceText.Get("tour_invalid_service")); yield break; }
            activeRequest = UnityWebRequest.Get(url);
            activeRequest.timeout = 15;
            UnityWebRequestAsyncOperation operation = null;
            try { operation = sendRequest(activeRequest); }
            catch (InvalidOperationException) { }
            if (operation == null)
            {
                activeRequest.Dispose(); activeRequest = null; loading = null;
                ScheduleRetry();
                SetState(TourLoadState.Unavailable, MuseumInterfaceText.Get("tour_unavailable"));
                yield break;
            }
            yield return operation;
            var success = activeRequest.result == UnityWebRequest.Result.Success;
            var json = success ? activeRequest.downloadHandler.text : null;
            activeRequest.Dispose();
            activeRequest = null;
            loading = null;
            if (!success)
            { ScheduleRetry(); SetState(TourLoadState.Unavailable, MuseumInterfaceText.Get("tour_unavailable")); yield break; }
            if (!TourContractV1.TryParse(json, out var contract, out var error) || !Runtime.Apply(contract, out error))
            { SetState(TourLoadState.Unavailable, MuseumInterfaceText.Get("tour_incompatible")); yield break; }
            failedAttempts = 0;
            SetState(TourLoadState.Ready, MuseumInterfaceText.Format("tour_ready", MuseumInterfaceText.Current.SuggestedVisitMinutes));
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
