using System;
using System.Linq;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum GuideVoiceStage
    {
        /// <summary>Nothing to offer: no voice service, no microphone or the guide is closed.</summary>
        Off,
        Ready,
        /// <summary>The processing notice is on the guide's screen, waiting for the visitor to accept or decline.</summary>
        Notice,
        /// <summary>The headset asked for the microphone and waits for the visitor's answer.</summary>
        Permission,
        Recording,
        Transcribing,
        /// <summary>The transcript waits for the visitor to confirm it as the question, or discard it.</summary>
        Review,
        /// <summary>Something went wrong; the screen says what and the visitor can talk again.</summary>
        Problem
    }

    /// <summary>
    /// Speaking with the guide by the hearth. It is offered only when the museum API says its voice service is configured
    /// and the device can record (Quest, Windows; not the browser build). Before the first recording, the guide's own
    /// screen shows who processes the voice, as the API reports it, and nothing is sent until the visitor accepts; the
    /// consent (<see cref="GuideVoiceConsent"/>, shared with the companion) lasts for this run only and is asked again if
    /// the providers change. A recording becomes text that the
    /// visitor confirms before it is asked; the answer keeps its text and citations and, when the service sends speech,
    /// is also heard at the guide. Walking away, closing or pausing stops the microphone, abandons any request, silences
    /// the guide and forgets the recording and the transcript. Nothing of either is stored or logged.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(TourRuntime))]
    public sealed class MuseumGuideVoice : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourRuntime runtime;
        private MuseumGuide guide;
        private GuideVoiceClient client;
        private IGuideVoiceRecorder recorder;
        private AudioSource speaker;
        private float volume = 1;
        private AudioClip spoken;
        private GuideResponseContractV1 heard;
        private string problem = "";
        private bool connected;
        private int shownSeconds = -1;

        public GuideVoiceClient Client { get { Connect(); return client; } }
        /// <summary>The microphone; tests set their own before talking.</summary>
        public IGuideVoiceRecorder Recorder
        {
            get => recorder ??= new MicrophoneVoiceRecorder();
            set { recorder?.Dispose(); recorder = value; }
        }
        public GuideVoiceStage Stage { get; private set; }
        /// <summary>Interface text key of the last problem, while <see cref="Stage"/> is Problem.</summary>
        public string ProblemKey => problem;
        public AudioSource Speaker => speaker;
        /// <summary>Volume of the guide's voice, from 0 to 1: the general volume times the visitor's level for the guide.</summary>
        public float Volume => volume;
        public void SetVolume(float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return;
            volume = Mathf.Clamp01(value);
            if (speaker != null) speaker.volume = volume;
        }
        /// <summary>The guide's answer is being heard.</summary>
        public bool Speaking => speaker != null && speaker.isPlaying;
        /// <summary>The guide is open, the API offers transcription and this device can record.</summary>
        public bool Offered
        {
            get
            {
                Connect();
                return guide != null && guide.Open && guide.Offered && client.CapabilityState == VoiceCapabilityState.Ready
                    && client.Capabilities.Listens && Recorder.Available;
            }
        }
        /// <summary>The visitor accepted the processing the service reports now.</summary>
        public bool Consented => GuideVoiceConsent.Covers(client.Capabilities);
        /// <summary>Answers are asked for with speech: the visitor accepted voice and the service speaks.</summary>
        public bool SpeaksAnswers => Consented && client.Capabilities?.Speaks == true;
        public event Action Changed;

        /// <summary>The notice before the first recording; providers are named exactly as the service reports them.</summary>
        public string Notice
        {
            get
            {
                var capabilities = client?.Capabilities;
                if (capabilities == null) return "";
                if (!capabilities.External || capabilities.providers.Length == 0) return MuseumInterfaceText.Get("voice_notice_local");
                var names = capabilities.providers;
                string joined = names.Length == 1 ? names[0]
                    : string.Join(", ", names.Take(names.Length - 1)) + " " + MuseumInterfaceText.Get("voice_and") + " " + names[names.Length - 1];
                return MuseumInterfaceText.Format("voice_notice_external", joined);
            }
        }

        /// <summary>What the guide's screen says about the voice now; empty when there is nothing to say.</summary>
        public string Status
        {
            get
            {
                if (client == null) return "";
                switch (Stage)
                {
                    case GuideVoiceStage.Notice: return Notice;
                    case GuideVoiceStage.Permission: return MuseumInterfaceText.Get("voice_permission");
                    case GuideVoiceStage.Recording:
                        return MuseumInterfaceText.Format("voice_recording", Mathf.FloorToInt(Recorder.Elapsed), MaxSeconds);
                    case GuideVoiceStage.Transcribing: return MuseumInterfaceText.Get("voice_transcribing");
                    case GuideVoiceStage.Review: return MuseumInterfaceText.Format("voice_review", client.Transcript);
                    case GuideVoiceStage.Problem:
                        return problem == "voice_rate_limited"
                            ? MuseumInterfaceText.Format(problem, client.RetryWaitRemaining) : MuseumInterfaceText.Get(problem);
                }
                // After the answer: a spoken answer that could not be heard says that its text is there.
                var question = guide?.Client;
                if (question != null && SpeaksAnswers && question.State == GuideQuestionState.Answered
                    && (question.SpokeInText || question.SpeechStatus == "unavailable"))
                    return MuseumInterfaceText.Get("voice_speech_unavailable");
                return "";
            }
        }

        private int MaxSeconds => Mathf.Clamp(client.Capabilities?.max_input_seconds ?? 30, 1, 30);

        private void OnEnable() => Connect();

        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            interaction.Changed -= OnInteractionChanged;
            runtime.Changed -= OnTourChanged;
            client.Changed -= OnVoiceChanged;
            if (guide != null)
            {
                guide.Client.Changed -= OnAnswerChanged;
                guide.Client.SpeechTimeout = null;
            }
            StopAll();
            client.Dispose();
            client = null;
            recorder?.Dispose();
            recorder = null;
        }

        private void Connect()
        {
            if (connected) return;
            connected = true;
            interaction = GetComponent<MuseumInteraction>();
            runtime = GetComponent<TourRuntime>();
            client = new GuideVoiceClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            interaction.Changed += OnInteractionChanged;
            runtime.Changed += OnTourChanged;
            client.Changed += OnVoiceChanged;
            guide = GetComponent<MuseumGuide>();
            if (guide != null)
            {
                guide.Client.SpeechTimeout = () => SpeaksAnswers ? Mathf.Clamp(client.Capabilities.request_timeout_seconds, 5, 55) : (float?)null;
                guide.Client.Changed += OnAnswerChanged;
            }
        }

        private string ApiBase()
        {
            var loader = GetComponent<TourLoader>();
            return loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);
        }

        private void Update()
        {
            if (client == null) return;
            client.Poll();
            if (Stage == GuideVoiceStage.Permission && !Recorder.AwaitingPermission) StartRecording();
            // Reaching the longest allowed question ends the recording by itself, and it is sent as when stopped.
            else if (Stage == GuideVoiceStage.Recording && !Recorder.Recording) StopTalking();
            // The recording time and a rate-limit wait change the line once a second.
            int seconds = Stage == GuideVoiceStage.Recording ? Mathf.FloorToInt(Recorder.Elapsed)
                : Stage == GuideVoiceStage.Problem && problem == "voice_rate_limited" ? client.RetryWaitRemaining : -1;
            if (seconds != shownSeconds) { shownSeconds = seconds; Changed?.Invoke(); }
        }

        private void OnTourChanged()
        {
            StopAll();
            client.Clear();
            Set(GuideVoiceStage.Off);
        }

        private void OnInteractionChanged()
        {
            if (interaction.State == InteractionState.GuideQuestion)
            {
                if (client.CapabilityState == VoiceCapabilityState.Unknown || client.CapabilityState == VoiceCapabilityState.Failed)
                    client.LoadCapabilities();
                if (Stage == GuideVoiceStage.Off) Set(GuideVoiceStage.Ready);
                return;
            }
            // Walking away, closing or pausing: the microphone stops and nothing in flight reaches the visitor.
            StopAll();
            Set(GuideVoiceStage.Off);
        }

        /// <summary>Starts talking: the notice comes first while the visitor has not accepted the current processing.</summary>
        public bool Talk()
        {
            if (!Offered || Stage == GuideVoiceStage.Recording || Stage == GuideVoiceStage.Transcribing
                || Stage == GuideVoiceStage.Permission || client.RetryWaitRemaining > 0) return false;
            StopSpeech();
            client.Forget();
            if (!Consented) { Set(GuideVoiceStage.Notice); return true; }
            StartRecording();
            return true;
        }

        /// <summary>The visitor accepts the processing named in the notice; recording starts at once.</summary>
        public bool Accept()
        {
            if (Stage != GuideVoiceStage.Notice) return false;
            GuideVoiceConsent.Accept(client.Capabilities);
            StartRecording();
            return true;
        }

        /// <summary>The visitor declines: nothing is recorded or sent, and the guide stays as it was.</summary>
        public void Decline()
        {
            if (Stage == GuideVoiceStage.Notice) Set(GuideVoiceStage.Ready);
        }

        private void StartRecording()
        {
            switch (Recorder.Begin(MaxSeconds))
            {
                case GuideRecorderStart.Started: Set(GuideVoiceStage.Recording); break;
                case GuideRecorderStart.PermissionPending: Set(GuideVoiceStage.Permission); break;
                case GuideRecorderStart.Denied: Fail("voice_permission_denied"); break;
                default: Fail("voice_microphone_unavailable"); break;
            }
        }

        /// <summary>Ends the recording and sends it to be transcribed.</summary>
        public bool StopTalking()
        {
            if (Stage != GuideVoiceStage.Recording) return false;
            var wav = Recorder.End();
            if (wav == null) { Fail("voice_too_short"); return true; }
            if (!Consented || !client.Transcribe(wav)) { Fail("voice_failed"); return true; }
            Set(GuideVoiceStage.Transcribing);
            return true;
        }

        /// <summary>Drops the recording, the transcription in flight or the transcript; nothing of it remains.</summary>
        public void Discard()
        {
            Recorder.Discard();
            client.Forget();
            Set(Offered ? GuideVoiceStage.Ready : GuideVoiceStage.Off);
        }

        /// <summary>Asks the transcript as the question, as the visitor confirmed it.</summary>
        public bool Confirm()
        {
            if (Stage != GuideVoiceStage.Review || string.IsNullOrWhiteSpace(client.Transcript)) return false;
            var question = client.Transcript;
            client.Forget();
            Set(GuideVoiceStage.Ready);
            return guide != null && guide.Send(question);
        }

        /// <summary>Forgets the transcript once the visitor asked it another way, such as edited in the question field.</summary>
        public void Forget()
        {
            if (Stage != GuideVoiceStage.Review) return;
            client.Forget();
            Set(GuideVoiceStage.Ready);
        }

        /// <summary>Silences the guide's spoken answer.</summary>
        public void StopSpeech()
        {
            if (speaker != null) speaker.Stop();
            if (spoken != null) Destroy(spoken);
            spoken = null;
            Changed?.Invoke();
        }

        private void OnVoiceChanged()
        {
            switch (client.TranscriptionState)
            {
                case GuideTranscriptionState.Transcribed:
                    if (Stage == GuideVoiceStage.Transcribing) Set(GuideVoiceStage.Review);
                    break;
                case GuideTranscriptionState.RateLimited: Fail("voice_rate_limited"); break;
                case GuideTranscriptionState.Rejected: Fail("voice_rejected"); break;
                case GuideTranscriptionState.NotUnderstood: Fail("voice_not_understood"); break;
                case GuideTranscriptionState.TimedOut:
                case GuideTranscriptionState.Failed:
                case GuideTranscriptionState.Incompatible: Fail("voice_failed"); break;
            }
            Changed?.Invoke();
        }

        /// <summary>Plays the speech of a new answer at the guide; each answer is heard once.</summary>
        private void OnAnswerChanged()
        {
            var question = guide.Client;
            if (question.State == GuideQuestionState.Sending) StopSpeech();
            if (question.State != GuideQuestionState.Answered || question.Speech == null || question.Response == heard) { Changed?.Invoke(); return; }
            heard = question.Response;
            if (!interaction || interaction.State != InteractionState.GuideQuestion) return;
            if (!WavAudio.TryDecodePcm16(question.Speech, out var samples, out var rate)) { Changed?.Invoke(); return; }
            StopSpeech();
            spoken = AudioClip.Create("GuideAnswer", samples.Length, 1, rate, false);
            spoken.SetData(samples, 0);
            Array.Clear(samples, 0, samples.Length);
            if (speaker == null)
            {
                speaker = new GameObject("GuideVoice").AddComponent<AudioSource>();
                speaker.transform.SetParent(transform, false);
                speaker.playOnAwake = false;
                speaker.spatialBlend = 0;
                speaker.volume = volume;
            }
            var point = runtime.GuidePoint;
            if (point != null) speaker.transform.position = point.Anchor.LookTarget.position;
            speaker.clip = spoken;
            // The guide's voice and a narration are never heard together (UD-8): the guide pauses any narration it starts over.
            var narration = GetComponent<MuseumNarration>();
            if (narration != null && narration.State == NarrationState.Playing) narration.TogglePause();
            speaker.Play();
            Changed?.Invoke();
        }

        private void StopAll()
        {
            recorder?.Discard();
            client?.Forget();
            StopSpeech();
        }

        private void Fail(string key)
        {
            problem = key;
            Set(GuideVoiceStage.Problem);
        }

        private void Set(GuideVoiceStage stage)
        {
            Stage = stage;
            if (stage != GuideVoiceStage.Problem) problem = "";
            Changed?.Invoke();
        }
    }
}
