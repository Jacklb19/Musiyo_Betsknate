using System;
using MusiyoBetsknate.Museo;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    public enum VoiceCapabilityState { Unknown, Loading, Ready, Failed }

    public enum GuideTranscriptionState
    {
        Idle,
        Sending,
        /// <summary>The recording became text, to be confirmed by the visitor before it is asked.</summary>
        Transcribed,
        /// <summary>HTTP 429: wait for the announced delay.</summary>
        RateLimited,
        /// <summary>HTTP 413 or 422: the recording was too long, too large or not valid audio.</summary>
        Rejected,
        /// <summary>HTTP 503: voice disabled, busy, or nothing understood.</summary>
        NotUnderstood,
        TimedOut,
        Failed,
        Incompatible
    }

    /// <summary>
    /// The museum API's voice service, never a provider directly: GET {api}/guide/voice-capabilities says whether the
    /// visit may offer voice and who processes it; POST {api}/guide/transcriptions turns one recording into text. A
    /// recording is sent only with the visitor's consent header and is held in memory for the request alone. One request
    /// at a time; a cancelled one never reaches the visitor. Its owner calls <see cref="Poll"/> every frame.
    /// </summary>
    public sealed class GuideVoiceClient : IDisposable
    {
        public const string ConsentHeader = "X-Musiyo-Voice-Consent";
        public const string ConsentValue = "request-only-v1";
        public const float CapabilityTimeoutSeconds = 10;
        private readonly Func<string> apiBase;
        private readonly Func<double> clock;
        private UnityWebRequest active;
        private UnityWebRequestAsyncOperation operation;
        private bool transcribing;
        private double deadline;
        private double retryAt;

        public VoiceCapabilityState CapabilityState { get; private set; }
        public VoiceCapabilitiesContractV1 Capabilities { get; private set; }
        public GuideTranscriptionState TranscriptionState { get; private set; }
        /// <summary>Text of the last recording, until it is asked, discarded or replaced.</summary>
        public string Transcript { get; private set; }
        public int RetryWaitRemaining => TranscriptionState == GuideTranscriptionState.RateLimited
            ? Math.Max(0, (int)Math.Ceiling(retryAt - clock())) : 0;
        public bool Busy => active != null;
        public event Action Changed;

        public GuideVoiceClient(Func<string> apiBase, Func<double> clock)
        {
            this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>Asks what the service offers. Configured is not a promise that it will answer.</summary>
        public void LoadCapabilities()
        {
            if (transcribing && active != null) return;
            Abandon();
            string url;
            try { url = apiBase() + "/guide/voice-capabilities"; }
            catch (ArgumentException) { SetCapability(VoiceCapabilityState.Failed); return; }
            transcribing = false;
            if (!Send(UnityWebRequest.Get(url), CapabilityTimeoutSeconds)) { SetCapability(VoiceCapabilityState.Failed); return; }
            SetCapability(VoiceCapabilityState.Loading);
        }

        /// <summary>Sends a recording (WAV PCM16 mono, 16 kHz) with the visitor's consent; false when it cannot be sent.</summary>
        public bool Transcribe(byte[] wav)
        {
            if (Capabilities == null || !Capabilities.Listens || wav == null || wav.Length == 0) return false;
            if (wav.Length > Capabilities.max_input_bytes) { Abandon(); Transcript = null; SetTranscription(GuideTranscriptionState.Rejected); return true; }
            Abandon();
            Transcript = null;
            string url;
            try { url = apiBase() + "/guide/transcriptions?schema_version=1"; }
            catch (ArgumentException) { SetTranscription(GuideTranscriptionState.Failed); return true; }
            transcribing = true;
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(wav) { contentType = "audio/wav" },
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader(ConsentHeader, ConsentValue);
            if (!Send(request, Math.Max(5, Capabilities.request_timeout_seconds))) { SetTranscription(GuideTranscriptionState.Failed); return true; }
            SetTranscription(GuideTranscriptionState.Sending);
            return true;
        }

        /// <summary>Forgets the transcript, as when it is asked or discarded; abandons a transcription in flight.</summary>
        public void Forget()
        {
            if (transcribing) Abandon();
            Transcript = null;
            if (TranscriptionState != GuideTranscriptionState.Idle) SetTranscription(GuideTranscriptionState.Idle);
        }

        /// <summary>Forgets everything, capabilities included, as when another tour or service is in use.</summary>
        public void Clear()
        {
            Abandon();
            Transcript = null;
            Capabilities = null;
            TranscriptionState = GuideTranscriptionState.Idle;
            SetCapability(VoiceCapabilityState.Unknown);
        }

        public void Poll()
        {
            if (active == null) return;
            if (!operation.isDone)
            {
                if (clock() < deadline) return;
                bool wasTranscribing = transcribing;
                Abandon();
                if (wasTranscribing) SetTranscription(GuideTranscriptionState.TimedOut); else SetCapability(VoiceCapabilityState.Failed);
                return;
            }
            var result = active.result;
            var code = active.responseCode;
            var text = active.downloadHandler?.text;
            var retryHeader = active.GetResponseHeader("Retry-After");
            bool transcription = transcribing;
            Abandon();
            if (!transcription)
            {
                if (code == 200 && result == UnityWebRequest.Result.Success && VoiceCapabilitiesContractV1.TryParse(text, out var capabilities))
                {
                    Capabilities = capabilities;
                    SetCapability(VoiceCapabilityState.Ready);
                }
                else SetCapability(VoiceCapabilityState.Failed);
                return;
            }
            var state = ClassifyTranscription(result, code, text, out var transcript);
            if (state == GuideTranscriptionState.RateLimited)
                retryAt = clock() + GuideQuestionClient.ParseRetryAfter(retryHeader, DateTimeOffset.UtcNow);
            Transcript = transcript;
            SetTranscription(state);
        }

        public static GuideTranscriptionState ClassifyTranscription(UnityWebRequest.Result result, long code, string body, out string transcript)
        {
            transcript = null;
            if (result == UnityWebRequest.Result.ConnectionError) return GuideTranscriptionState.Failed;
            if (code == 429) return GuideTranscriptionState.RateLimited;
            if (code == 413 || code == 422) return GuideTranscriptionState.Rejected;
            if (code == 503) return GuideTranscriptionState.NotUnderstood;
            if (code != 200 || result != UnityWebRequest.Result.Success) return GuideTranscriptionState.Failed;
            if (!GuideTranscriptionContractV1.TryParse(body, out var parsed)) return GuideTranscriptionState.Incompatible;
            transcript = parsed.text.Trim();
            return GuideTranscriptionState.Transcribed;
        }

        private bool Send(UnityWebRequest request, float timeoutSeconds)
        {
            active = request;
            active.SetRequestHeader("Accept", "application/json");
            active.timeout = (int)Math.Ceiling(timeoutSeconds) + 5;
            deadline = clock() + timeoutSeconds;
            try { operation = active.SendWebRequest(); }
            catch (InvalidOperationException) { operation = null; }
            if (operation != null) return true;
            Abandon();
            return false;
        }

        private void Abandon()
        {
            if (active == null) return;
            if (operation != null && !operation.isDone) active.Abort();
            active.Dispose();
            active = null;
            operation = null;
            transcribing = false;
        }

        private void SetCapability(VoiceCapabilityState state)
        {
            CapabilityState = state;
            Changed?.Invoke();
        }

        private void SetTranscription(GuideTranscriptionState state)
        {
            TranscriptionState = state;
            Changed?.Invoke();
        }

        public void Dispose() => Abandon();
    }
}
