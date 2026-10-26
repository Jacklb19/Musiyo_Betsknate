using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum CompanionVoiceStage
    {
        /// <summary>The companion is not listening and nothing is on its panel.</summary>
        Idle,
        /// <summary>The processing notice waits for the visitor to accept or decline.</summary>
        Notice,
        /// <summary>The device asked for the microphone and waits for the visitor's answer.</summary>
        Permission,
        Recording,
        Transcribing,
        /// <summary>The transcript waits for the visitor to confirm it, record again or drop it.</summary>
        Review,
        /// <summary>The confirmed question went to the guide service.</summary>
        Thinking,
        /// <summary>The answer is on the panel, and heard when the service spoke it.</summary>
        Answer,
        /// <summary>Something went wrong; the panel says what and the visitor can try again.</summary>
        Problem
    }

    /// <summary>
    /// Asking the companion anywhere in the museum with one button: X on the left controller, a key on a computer or in the
    /// browser (V by default). The companion listens, shows what it understood for the visitor to confirm, and answers in
    /// text with the cited records and, when the service speaks, aloud. The same voice service and the same consent as the
    /// hearth guide are used: the notice comes first and nothing is recorded or sent before it is accepted
    /// (<see cref="GuideVoiceConsent"/>). A narration playing when the visitor starts talking is paused, the panel says so,
    /// and it goes on when the conversation ends. In the hearth, where the companion waits at the door, the button is
    /// silent: the hearth guide takes the questions there. Recordings, transcripts and answers live in memory only; nothing is stored or logged.
    /// </summary>
    public sealed class MuseumCompanionVoice : MonoBehaviour
    {
        /// <summary>Holding the button at least this long makes releasing it end the question (push to talk).</summary>
        public const float HoldSeconds = .6f;
        /// <summary>The notice must be readable for this long before the button can accept it.</summary>
        public const float NoticeReadSeconds = 1;
        /// <summary>An answer stays on the panel this long after its voice ends, plus time to read its text.</summary>
        public const float AnswerLingerSeconds = 8;

        private MuseumInteraction interaction;
        private MuseumCompanion companion;
        private MuseumVisitFlow flow;
        private TourRuntime runtime;
        private TourLoader loader;
        private MuseumNarration narration;
        private MuseumGuideVoice guideVoice;
        private GuideVoiceClient voice;
        private GuideQuestionClient question;
        private IGuideVoiceRecorder recorder;
        private AudioSource speaker;
        private AudioClip spoken;
        private GuideResponseContractV1 heard;
        private string problem = "";
        private float stageSince;
        private float pressedAt = -1;
        private float answerUntil;
        private bool pausedNarration;
        private bool connected;
        private int shownSeconds = -1;
        private VoiceCapabilityState loggedCapability;

        public CompanionVoiceStage Stage { get; private set; }
        /// <summary>Interface text key of the last problem, while <see cref="Stage"/> is Problem.</summary>
        public string ProblemKey => problem;
        public GuideVoiceClient Voice { get { Connect(); return voice; } }
        public GuideQuestionClient Question { get { Connect(); return question; } }
        /// <summary>The microphone; tests set their own.</summary>
        public IGuideVoiceRecorder Recorder
        {
            get => recorder ??= new MicrophoneVoiceRecorder();
            set { recorder?.Dispose(); recorder = value; }
        }
        public bool Speaking => speaker != null && speaker.isPlaying;
        /// <summary>The visitor is talking with the companion: something of the conversation is on its panel.</summary>
        public bool Conversing => Stage != CompanionVoiceStage.Idle;
        /// <summary>The conversation paused a narration, which goes on when it ends.</summary>
        public bool PausedNarration => pausedNarration;
        public bool Consented => GuideVoiceConsent.Covers(voice?.Capabilities);
        /// <summary>The tour publishes the guide's answers; questions to the companion go to the same service.</summary>
        public bool QuestionsOffered => runtime != null && runtime.Guide?.available == true;
        /// <summary>The service transcribes and this device can record.</summary>
        public bool Listens => voice != null && voice.CapabilityState == VoiceCapabilityState.Ready && voice.Capabilities.Listens && Recorder.Available;
        /// <summary>The visit is in free exploration, not in a menu, paused, at the hearth guide or examining a piece.</summary>
        public bool CanAsk => interaction != null && (flow == null || flow.Phase == VisitPhase.Exploring && !flow.BlocksInput)
            && (companion == null || !companion.Waiting)
            && interaction.State != InteractionState.Paused && interaction.State != InteractionState.GuideQuestion
            && interaction.State != InteractionState.ModelExamination && interaction.State != InteractionState.Reading;
        public event Action Changed;

        private void OnEnable() => Connect();

        private void Connect()
        {
            if (connected) return;
            interaction = FindAnyObjectByType<MuseumInteraction>();
            if (interaction == null) return;
            connected = true;
            companion = GetComponent<MuseumCompanion>();
            flow = interaction.GetComponent<MuseumVisitFlow>();
            runtime = interaction.GetComponent<TourRuntime>();
            loader = interaction.GetComponent<TourLoader>();
            voice = new GuideVoiceClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            question = new GuideQuestionClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            question.SpeechTimeout = () => Consented && voice.Capabilities?.Speaks == true
                ? Mathf.Clamp(voice.Capabilities.request_timeout_seconds, 5, 55) : (float?)null;
            voice.Changed += OnVoiceChanged;
            question.Changed += OnAnswerChanged;
            interaction.Changed += OnInteractionChanged;
            if (runtime != null) runtime.Changed += OnTourChanged;
        }

        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            End();
            voice.Changed -= OnVoiceChanged;
            question.Changed -= OnAnswerChanged;
            if (interaction != null) interaction.Changed -= OnInteractionChanged;
            if (runtime != null) runtime.Changed -= OnTourChanged;
            voice.Dispose();
            question.Dispose();
            recorder?.Dispose();
            recorder = null;
        }

        /// <summary>The questions go to the same API as the tour; no second address exists.</summary>
        private string ApiBase() => loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);

        private void OnTourChanged()
        {
            End();
            voice.Clear();
        }

        /// <summary>Pausing, the hearth guide or another screen that takes the visitor ends the conversation.</summary>
        private void OnInteractionChanged()
        {
            if (Conversing && !CanAsk) End();
        }

        private void Update()
        {
            if (!connected) { Connect(); if (!connected) return; }
            voice.Poll();
            question.Poll();
            if (voice.CapabilityState == VoiceCapabilityState.Unknown && QuestionsOffered && flow != null && flow.Phase == VisitPhase.Exploring)
                voice.LoadCapabilities();
            // Walking into the hearth, where the companion waits outside, ends a conversation with it.
            if (Conversing && !CanAsk) End();
            ReadButton();
            if (Stage == CompanionVoiceStage.Permission && !Recorder.AwaitingPermission) StartRecording();
            // A microphone that sends nothing is told at once, not after the visitor has spoken into it.
            else if (Stage == CompanionVoiceStage.Recording && Recorder.Muted) Fail("voice_microphone_muted");
            // Reaching the longest allowed question ends the recording by itself, and it is sent as when stopped.
            else if (Stage == CompanionVoiceStage.Recording && !Recorder.Recording) StopTalking();
            if ((Stage == CompanionVoiceStage.Answer || Stage == CompanionVoiceStage.Problem) && !Speaking && Time.unscaledTime >= answerUntil
                && voice.RetryWaitRemaining == 0) End();
            if (Stage == CompanionVoiceStage.Answer && Speaking) answerUntil = Time.unscaledTime + AnswerLingerSeconds;
            int seconds = Stage == CompanionVoiceStage.Recording ? Mathf.FloorToInt(Recorder.Elapsed)
                : Stage == CompanionVoiceStage.Problem && problem == "voice_rate_limited" ? voice.RetryWaitRemaining : -1;
            if (seconds != shownSeconds) { shownSeconds = seconds; Changed?.Invoke(); }
        }

        /// <summary>One button does the next natural step; holding it while talking and releasing it ends the question.</summary>
        private void ReadButton()
        {
            var input = MuseumInput.Current;
            if (!input.Alive) return;
            var action = input.AskCompanion;
            if (action.WasPressedThisFrame())
            {
                pressedAt = Time.unscaledTime;
                Press();
            }
            if (action.WasReleasedThisFrame() && pressedAt >= 0)
            {
                bool held = Time.unscaledTime - pressedAt >= HoldSeconds;
                pressedAt = -1;
                if (held && Stage == CompanionVoiceStage.Recording && Time.unscaledTime - stageSince >= HoldSeconds) StopTalking();
            }
        }

        /// <summary>What the button does now: talk, accept the notice, end the question, ask the transcript, or talk again.</summary>
        public bool Press()
        {
            switch (Stage)
            {
                case CompanionVoiceStage.Notice: return Time.unscaledTime - stageSince >= NoticeReadSeconds && Accept();
                case CompanionVoiceStage.Recording: return StopTalking();
                case CompanionVoiceStage.Review: return Confirm();
                case CompanionVoiceStage.Permission:
                case CompanionVoiceStage.Transcribing:
                case CompanionVoiceStage.Thinking: return false;
                default: return Talk();
            }
        }

        /// <summary>Starts a question: the notice first while the visitor has not accepted the processing reported now.</summary>
        public bool Talk()
        {
            if (!CanAsk)
            {
                Debug.Log("[MuseumCompanionVoice] not now: " + (interaction != null ? interaction.State.ToString() : "no visit")
                    + (companion != null && companion.Waiting ? ", waiting outside" : ""));
                return false;
            }
            if (!QuestionsOffered) return Fail("companion_voice_unavailable");
            if (voice.CapabilityState != VoiceCapabilityState.Ready)
            {
                if (voice.CapabilityState != VoiceCapabilityState.Loading) voice.LoadCapabilities();
                return Fail(voice.CapabilityState == VoiceCapabilityState.Failed ? "companion_voice_unavailable" : "companion_voice_loading");
            }
            if (!voice.Capabilities.Listens) return Fail("companion_voice_unavailable");
            if (!Recorder.Available) return Fail("voice_microphone_unavailable");
            if (voice.RetryWaitRemaining > 0 || question.RetryWaitRemaining > 0) return false;
            StopSpeech();
            question.Cancel();
            voice.Forget();
            HoldNarration();
            if (!Consented) { Set(CompanionVoiceStage.Notice); return true; }
            StartRecording();
            return true;
        }

        /// <summary>The visitor accepts the processing named in the notice; recording starts at once.</summary>
        public bool Accept()
        {
            if (Stage != CompanionVoiceStage.Notice) return false;
            GuideVoiceConsent.Accept(voice.Capabilities);
            StartRecording();
            return true;
        }

        /// <summary>The visitor declines: nothing is recorded or sent.</summary>
        public void Decline()
        {
            if (Stage == CompanionVoiceStage.Notice) End();
        }

        private void StartRecording()
        {
            switch (Recorder.Begin(MaxSeconds))
            {
                case GuideRecorderStart.Started: Set(CompanionVoiceStage.Recording); break;
                case GuideRecorderStart.PermissionPending: Set(CompanionVoiceStage.Permission); break;
                case GuideRecorderStart.Denied: Fail("voice_permission_denied"); break;
                default: Fail("voice_microphone_unavailable"); break;
            }
        }

        private int MaxSeconds => Mathf.Clamp(voice.Capabilities?.max_input_seconds ?? 30, 1, 30);

        /// <summary>Ends the recording and sends it to be transcribed.</summary>
        public bool StopTalking()
        {
            if (Stage != CompanionVoiceStage.Recording) return false;
            if (Recorder.Muted) return Fail("voice_microphone_muted");
            var wav = Recorder.End();
            if (wav == null) return Fail("voice_too_short");
            if (WavAudio.Silent(wav, "MuseumCompanionVoice")) return Fail("voice_not_heard");
            if (!Consented || !voice.Transcribe(wav)) return Fail("voice_failed");
            Set(CompanionVoiceStage.Transcribing);
            return true;
        }

        /// <summary>Asks the transcript as the question, as the visitor confirmed it.</summary>
        public bool Confirm()
        {
            if (Stage != CompanionVoiceStage.Review || string.IsNullOrWhiteSpace(voice.Transcript)) return false;
            var text = voice.Transcript;
            voice.Forget();
            heard = null;
            question.Ask(text);
            Set(question.Busy ? CompanionVoiceStage.Thinking : CompanionVoiceStage.Answer);
            return true;
        }

        /// <summary>Drops the transcript and listens again at once.</summary>
        public bool Again()
        {
            if (Stage != CompanionVoiceStage.Review && Stage != CompanionVoiceStage.Problem && Stage != CompanionVoiceStage.Answer) return false;
            Recorder.Discard();
            voice.Forget();
            StopSpeech();
            if (Stage == CompanionVoiceStage.Problem || Stage == CompanionVoiceStage.Answer) return Talk();
            StartRecording();
            return true;
        }

        /// <summary>Ends the conversation: the microphone stops, nothing in flight reaches the visitor, a paused narration goes on.</summary>
        public void End()
        {
            recorder?.Discard();
            voice?.Forget();
            question?.Cancel();
            StopSpeech();
            ReleaseNarration();
            if (Stage != CompanionVoiceStage.Idle) Set(CompanionVoiceStage.Idle);
        }

        /// <summary>Silences the spoken answer; its text stays.</summary>
        public void StopSpeech()
        {
            if (speaker != null) speaker.Stop();
            if (spoken != null) Destroy(spoken);
            spoken = null;
        }

        private void HoldNarration()
        {
            narration ??= interaction.GetComponent<MuseumNarration>();
            if (narration == null || narration.State != NarrationState.Playing) return;
            narration.TogglePause();
            pausedNarration = true;
        }

        private void ReleaseNarration()
        {
            if (!pausedNarration) return;
            pausedNarration = false;
            if (narration != null && narration.State == NarrationState.Paused && CanAsk) narration.TogglePause();
        }

        private void OnVoiceChanged()
        {
            if (voice.CapabilityState != loggedCapability)
            {
                loggedCapability = voice.CapabilityState;
                Debug.Log("[MuseumCompanionVoice] capabilities " + loggedCapability
                    + (loggedCapability == VoiceCapabilityState.Ready ? ": listens " + voice.Capabilities.Listens + ", speaks " + voice.Capabilities.Speaks : ""));
            }
            switch (voice.TranscriptionState)
            {
                case GuideTranscriptionState.Transcribed:
                    if (Stage == CompanionVoiceStage.Transcribing) Set(CompanionVoiceStage.Review);
                    break;
                case GuideTranscriptionState.RateLimited: if (Stage == CompanionVoiceStage.Transcribing) Fail("voice_rate_limited"); break;
                case GuideTranscriptionState.Rejected: if (Stage == CompanionVoiceStage.Transcribing) Fail("voice_rejected"); break;
                case GuideTranscriptionState.NotUnderstood: if (Stage == CompanionVoiceStage.Transcribing) Fail("voice_not_understood"); break;
                case GuideTranscriptionState.TimedOut:
                case GuideTranscriptionState.Failed:
                case GuideTranscriptionState.Incompatible: if (Stage == CompanionVoiceStage.Transcribing) Fail("voice_failed"); break;
            }
            Changed?.Invoke();
        }

        /// <summary>A finished question shows its answer; the speech that came with it is heard once, at the companion.</summary>
        private void OnAnswerChanged()
        {
            if (Stage != CompanionVoiceStage.Thinking || question.Busy) { Changed?.Invoke(); return; }
            answerUntil = Time.unscaledTime + AnswerLingerSeconds + (question.Response?.answer?.Length ?? 0) / 18f;
            Set(CompanionVoiceStage.Answer);
            if (question.State != GuideQuestionState.Answered || question.Speech == null || question.Response == heard) return;
            heard = question.Response;
            if (!WavAudio.TryDecodePcm16(question.Speech, out var samples, out var rate)) return;
            StopSpeech();
            spoken = AudioClip.Create("CompanionAnswer", samples.Length, 1, rate, false);
            spoken.SetData(samples, 0);
            Array.Clear(samples, 0, samples.Length);
            if (speaker == null)
            {
                speaker = new GameObject("CompanionVoice").AddComponent<AudioSource>();
                speaker.transform.SetParent(transform, false);
                speaker.transform.localPosition = Vector3.up * 1.5f;
                speaker.playOnAwake = false;
                // Heard as clearly as the hearth guide, wherever the companion stands.
                speaker.spatialBlend = 0;
            }
            guideVoice ??= interaction.GetComponent<MuseumGuideVoice>();
            speaker.volume = guideVoice != null ? guideVoice.Volume : 1;
            speaker.clip = spoken;
            speaker.Play();
            Debug.Log("[MuseumCompanionVoice] speaking answer: " + samples.Length + " samples at " + rate + " Hz, volume "
                + speaker.volume.ToString("F2"));
            Changed?.Invoke();
        }

        /// <summary>What the panel says now; empty while idle.</summary>
        public string Status
        {
            get
            {
                switch (Stage)
                {
                    case CompanionVoiceStage.Notice: return Notice;
                    case CompanionVoiceStage.Permission: return MuseumInterfaceText.Get("companion_voice_permission");
                    case CompanionVoiceStage.Recording:
                        return MuseumInterfaceText.Format("companion_voice_recording", Mathf.FloorToInt(Recorder.Elapsed), MaxSeconds);
                    case CompanionVoiceStage.Transcribing: return MuseumInterfaceText.Get("voice_transcribing");
                    case CompanionVoiceStage.Review: return MuseumInterfaceText.Format("voice_review", voice.Transcript);
                    case CompanionVoiceStage.Thinking: return MuseumInterfaceText.Get("companion_voice_thinking");
                    case CompanionVoiceStage.Answer: return AnswerText;
                    case CompanionVoiceStage.Problem:
                        return problem == "voice_rate_limited" ? MuseumInterfaceText.Format(problem, voice.RetryWaitRemaining) : MuseumInterfaceText.Get(problem);
                    default: return "";
                }
            }
        }

        private string AnswerText
        {
            get
            {
                switch (question.State)
                {
                    case GuideQuestionState.Answered:
                        bool unheard = question.SpokeInText || question.SpeechStatus == "unavailable";
                        return question.Response.answer + (Consented && voice.Capabilities?.Speaks == true && unheard
                            ? "\n\n" + MuseumInterfaceText.Get("voice_speech_unavailable") : "");
                    case GuideQuestionState.Abstained: return MuseumInterfaceText.Get("guide_abstained");
                    case GuideQuestionState.ProviderUnavailable: return MuseumInterfaceText.Get("guide_provider_unavailable");
                    case GuideQuestionState.RateLimited: return MuseumInterfaceText.Format("guide_rate_limited", question.RetryWaitRemaining);
                    case GuideQuestionState.TimedOut: return MuseumInterfaceText.Get("guide_timed_out");
                    case GuideQuestionState.Unreachable: return MuseumInterfaceText.Get("guide_unreachable");
                    case GuideQuestionState.Invalid: return MuseumInterfaceText.Get("guide_invalid");
                    case GuideQuestionState.Rejected: return MuseumInterfaceText.Get("guide_rejected");
                    case GuideQuestionState.Incompatible: return MuseumInterfaceText.Get("guide_incompatible");
                    default: return MuseumInterfaceText.Get("guide_service_error");
                }
            }
        }

        /// <summary>The notice before the first recording; providers are named exactly as the service reports them.</summary>
        public string Notice
        {
            get
            {
                var capabilities = voice?.Capabilities;
                if (capabilities == null) return "";
                if (!capabilities.External || capabilities.providers.Length == 0) return MuseumInterfaceText.Get("companion_voice_notice_local");
                var names = capabilities.providers;
                string joined = names.Length == 1 ? names[0]
                    : string.Join(", ", names, 0, names.Length - 1) + " " + MuseumInterfaceText.Get("voice_and") + " " + names[names.Length - 1];
                return MuseumInterfaceText.Format("companion_voice_notice_external", joined);
            }
        }

        /// <summary>Titles of the records the answer cites.</summary>
        public IReadOnlyList<GuideCitationContractV1> Citations => Stage == CompanionVoiceStage.Answer && question.State == GuideQuestionState.Answered
            ? (IReadOnlyList<GuideCitationContractV1>)question.Response.citations ?? Array.Empty<GuideCitationContractV1>()
            : Array.Empty<GuideCitationContractV1>();

        private bool Fail(string key)
        {
            recorder?.Discard();
            problem = key;
            Set(CompanionVoiceStage.Problem);
            answerUntil = Time.unscaledTime + AnswerLingerSeconds;
            return true;
        }

        private void Set(CompanionVoiceStage stage)
        {
            if (stage != Stage) Debug.Log("[MuseumCompanionVoice] " + stage + (stage == CompanionVoiceStage.Problem ? " " + problem : ""));
            Stage = stage;
            stageSince = Time.unscaledTime;
            if (stage != CompanionVoiceStage.Problem) problem = "";
            Changed?.Invoke();
        }
    }
}
