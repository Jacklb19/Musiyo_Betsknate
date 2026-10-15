using System;
using System.Globalization;
using System.Text;
using MusiyoBetsknate.Museo;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Where a visitor's question stands. The three answers of the service arrive as HTTP 200; every failure on the way
    /// there has its own state, so the visitor is told what happened and keeps the question to send again.
    /// </summary>
    public enum GuideQuestionState
    {
        Idle,
        Sending,
        Answered,
        Abstained,
        ProviderUnavailable,
        /// <summary>The question does not meet the shared contract and was not sent.</summary>
        Invalid,
        /// <summary>The service rejected the request as invalid (HTTP 400 or 422).</summary>
        Rejected,
        /// <summary>HTTP 429: wait for the announced delay before asking again; nothing is retried automatically.</summary>
        RateLimited,
        /// <summary>No answer within the client deadline.</summary>
        TimedOut,
        /// <summary>The service could not be reached.</summary>
        Unreachable,
        /// <summary>The service answered with an error other than validation or rate limit.</summary>
        ServiceError,
        /// <summary>HTTP 200 whose body does not follow the shared response contract.</summary>
        Incompatible
    }

    /// <summary>
    /// Sends one anonymous question at a time to POST {api base}/guide/questions. A new question, a cancellation or a
    /// change of context abandons the request in flight, and nothing it returns afterwards reaches the visitor.
    /// Its owner calls <see cref="Poll"/> every frame.
    /// </summary>
    public sealed class GuideQuestionClient : IDisposable
    {
        public const float DefaultTimeoutSeconds = 15;
        public const int DefaultRetryAfterSeconds = 60;
        private readonly Func<string> apiBase;
        private readonly Func<double> clock;
        private readonly float timeoutSeconds;
        private UnityWebRequest active;
        private UnityWebRequestAsyncOperation operation;
        private double deadline;
        private double retryAt;

        public GuideQuestionState State { get; private set; }
        /// <summary>The last question the visitor wrote, kept after any outcome so it can be sent again by hand.</summary>
        public string Question { get; private set; } = "";
        public string ElementSlug { get; private set; }
        /// <summary>Address of the last question sent.</summary>
        public string Endpoint { get; private set; }
        /// <summary>The validated response of the last completed question; null unless it ended in one of the three answers.</summary>
        public GuideResponseContractV1 Response { get; private set; }
        /// <summary>Seconds the service asked to wait after HTTP 429.</summary>
        public int RetryAfterSeconds { get; private set; }
        /// <summary>Seconds left before a rate-limited question may be sent again; zero otherwise.</summary>
        public int RetryWaitRemaining => State == GuideQuestionState.RateLimited
            ? Math.Max(0, (int)Math.Ceiling(retryAt - clock())) : 0;
        public bool Busy => State == GuideQuestionState.Sending;
        /// <summary>A completed, failed or cancelled question can be sent again once any announced wait is over.</summary>
        public bool CanRetry => !Busy && !string.IsNullOrWhiteSpace(Question) && RetryWaitRemaining == 0
            && State != GuideQuestionState.Invalid;
        public event Action Changed;

        /// <param name="apiBase">Resolved API base ending in /api/v1, read when each question is sent.</param>
        /// <param name="clock">Seconds of real time; the visit's clock by default. Tests pass their own.</param>
        public GuideQuestionClient(Func<string> apiBase, Func<double> clock, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// Sends a question, abandoning any one still in flight. The slug only narrows the evidence to one element; it
        /// grants nothing. Returns false, without sending, when the question breaks the shared contract.
        /// </summary>
        public bool Ask(string question, string elementSlug = null)
        {
            Abandon();
            Question = question ?? "";
            ElementSlug = string.IsNullOrEmpty(elementSlug) ? null : elementSlug;
            Response = null;
            RetryAfterSeconds = 0;
            if (!GuideQuestionContractV1.TryCreate(Question, ElementSlug, out var body))
            { Set(GuideQuestionState.Invalid); return false; }
            string url;
            try { url = apiBase() + "/guide/questions"; }
            catch (ArgumentException) { Set(GuideQuestionState.Unreachable); return true; }
            Endpoint = url;
            active = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                // The client deadline decides; the transport limit only guards against a request that never ends.
                timeout = (int)Math.Ceiling(timeoutSeconds) + 5
            };
            active.SetRequestHeader("Accept", "application/json");
            deadline = clock() + timeoutSeconds;
            try { operation = active.SendWebRequest(); }
            catch (InvalidOperationException) { operation = null; }
            if (operation == null) { Abandon(); Set(GuideQuestionState.Unreachable); return true; }
            Set(GuideQuestionState.Sending);
            return true;
        }

        /// <summary>Sends the kept question again; the visitor decides when.</summary>
        public bool Retry() => CanRetry && Ask(Question, ElementSlug);

        /// <summary>Abandons the question in flight. The question stays for a manual retry.</summary>
        public void Cancel()
        {
            if (!Busy) return;
            Abandon();
            Set(GuideQuestionState.Idle);
        }

        /// <summary>Forgets the conversation, as when the visit starts over.</summary>
        public void Clear()
        {
            Abandon();
            Question = "";
            ElementSlug = null;
            Response = null;
            RetryAfterSeconds = 0;
            Set(GuideQuestionState.Idle);
        }

        /// <summary>Completes the request in flight when it has finished or run out of time.</summary>
        public void Poll()
        {
            if (active == null) return;
            if (!operation.isDone)
            {
                if (clock() < deadline) return;
                Abandon();
                Set(GuideQuestionState.TimedOut);
                return;
            }
            var result = active.result;
            var code = active.responseCode;
            var error = active.error;
            var text = active.downloadHandler?.text;
            var retryHeader = active.GetResponseHeader("Retry-After");
            Abandon();
            var state = Classify(result, code, error, text, out var response);
            Response = response;
            if (state == GuideQuestionState.RateLimited)
            {
                RetryAfterSeconds = ParseRetryAfter(retryHeader, DateTimeOffset.UtcNow);
                retryAt = clock() + RetryAfterSeconds;
            }
            Set(state);
        }

        /// <summary>Maps a finished exchange to the visitor's outcome; the body is accepted only if the shared schema accepts it.</summary>
        public static GuideQuestionState Classify(UnityWebRequest.Result result, long code, string error, string body,
            out GuideResponseContractV1 response)
        {
            response = null;
            if (result == UnityWebRequest.Result.ConnectionError)
                return error != null && error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                    ? GuideQuestionState.TimedOut : GuideQuestionState.Unreachable;
            if (code == 429) return GuideQuestionState.RateLimited;
            if (code == 400 || code == 422) return GuideQuestionState.Rejected;
            if (code != 200) return code == 0 ? GuideQuestionState.Unreachable : GuideQuestionState.ServiceError;
            if (result != UnityWebRequest.Result.Success || !GuideResponseContractV1.TryParse(body, out response))
            { response = null; return GuideQuestionState.Incompatible; }
            switch (response.status)
            {
                case "answered": return GuideQuestionState.Answered;
                case "abstained": return GuideQuestionState.Abstained;
                case "provider_unavailable": return GuideQuestionState.ProviderUnavailable;
                default: response = null; return GuideQuestionState.Incompatible;
            }
        }

        /// <summary>Delay in seconds or an HTTP date; anything missing or unreadable means the default minute.</summary>
        public static int ParseRetryAfter(string header, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(header)) return DefaultRetryAfterSeconds;
            header = header.Trim();
            if (int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return seconds;
            if (DateTimeOffset.TryParseExact(header, "r", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                return Math.Max(0, (int)Math.Ceiling((date - now).TotalSeconds));
            return DefaultRetryAfterSeconds;
        }

        private void Abandon()
        {
            if (active == null) return;
            if (operation != null && !operation.isDone) active.Abort();
            active.Dispose();
            active = null;
            operation = null;
        }

        private void Set(GuideQuestionState state)
        {
            State = state;
            Changed?.Invoke();
        }

        public void Dispose() => Abandon();
    }
}
