using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The conversation with the guide in the saved museum, against a local synthetic guide service.</summary>
    public sealed class MuseumGuidePlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private HttpListener service;
        private int requests;
        /// <summary>Query strings of the hearth question requests the service received.</summary>
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> quizQueries = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>A further route of the synthetic service: method, path, headers and body in, status and JSON out, or null to pass.</summary>
        private delegate (int status, string body)? Route(string method, string path, System.Collections.Specialized.NameValueCollection headers, byte[] body);

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            try { service?.Stop(); service?.Close(); } catch (Exception) { }
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        /// <summary>Answers questions with the canonical example, the hearth questions with theirs, and everything else with 404.</summary>
        private string StartService(Route extra = null)
        {
            // The fixture is shared by its tests: each service counts its own requests.
            requests = 0;
            while (quizQueries.TryDequeue(out _)) { }
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            string Example(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));
            var answered = Example("guide_answered.json");
            var quiz = Example("guide_quiz.json");
            var explained = Example("guide_quiz_answer_result.json");
            service = new HttpListener();
            service.Prefixes.Add("http://127.0.0.1:" + port + "/");
            service.Start();
            var listener = service;
            void Receive(IAsyncResult pending)
            {
                HttpListenerContext context;
                try { context = listener.EndGetContext(pending); listener.BeginGetContext(Receive, null); }
                catch (Exception) { return; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var method = context.Request.HttpMethod;
                        var path = context.Request.Url.AbsolutePath;
                        byte[] received;
                        using (var buffer = new MemoryStream()) { context.Request.InputStream.CopyTo(buffer); received = buffer.ToArray(); }
                        bool guide = method == "POST" && path == "/api/v1/guide/questions";
                        if (guide) Interlocked.Increment(ref requests);
                        (int status, string body)? routed = extra?.Invoke(method, path, context.Request.Headers, received);
                        if (routed == null && method == "GET" && path == "/api/v1/guide/quiz")
                        {
                            quizQueries.Enqueue(context.Request.Url.Query);
                            routed = (200, quiz);
                        }
                        if (routed == null && method == "POST" && path == "/api/v1/guide/quiz/answers") routed = (200, explained);
                        if (routed == null && guide) routed = (200, answered);
                        var reply = routed ?? (404, "{\"detail\":\"Not Found\"}");
                        var bytes = Encoding.UTF8.GetBytes(reply.body);
                        context.Response.StatusCode = reply.status;
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = bytes.Length;
                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        context.Response.OutputStream.Close();
                    }
                    catch (Exception) { }
                });
            }
            service.BeginGetContext(Receive, null);
            return "http://127.0.0.1:" + port + "/api/v1";
        }

        private static RoomContractV1 Room(string key, int order, params PointContractV1[] points)
            => new RoomContractV1 { key = key, name = key == "room.overlook" ? "Mirador" : key, order = order, points = points };

        [UnityTest]
        public IEnumerator TheGuideConversationUnfoldsAtTheGuideAndWritingStopsTheVisitor()
        {
            bool hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
                var loader = flow.GetComponent<TourLoader>();
                // The guide must use the tour's address; the scene is pointed at the synthetic service before it starts.
                var settings = new UnityEditor.SerializedObject(loader);
                settings.FindProperty("apiBase").stringValue = api;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                var runtime = flow.GetComponent<TourRuntime>();
                var interaction = flow.GetComponent<MuseumInteraction>();
                var guide = flow.GetComponent<MuseumGuide>();
                var display = flow.GetComponent<MuseumLecternDisplay>();
                var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
                var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
                var body = visitor.GetComponent<CharacterController>();
                flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
                for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                var element = new ElementSummaryContractV1 { slug = "synthetic-item", title = "Ficha sintética de prueba" };
                Assert.That(runtime.Apply(new TourContractV1
                {
                    schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                    rooms = new[]
                    {
                        Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                        Room("room.hearth", 4), Room("room.overlook", 5, new PointContractV1
                        {
                            key = "punto.mirador.creditos", name = "Créditos", order = 0,
                            activation = new[] { "keyboard" }, elements = new[] { element }
                        }),
                    },
                    guide = new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = true },
                }, out var error), Is.True, error);
                flow.EnterImmediately();
                yield return null;

                var anchor = runtime.GuidePoint.Anchor;
                Assert.That(visitor.FocusPoint(anchor), Is.True, "There is room to stand by the guide.");
                yield return null;
                Assert.That(guide.InRange, Is.True);
                Assert.That(guide.Ask(), Is.True);
                for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(display.InWorld && display.Open && display.Openness >= 1, Is.True, "The conversation unfolds inside the museum.");
                Assert.That(display.Anchor, Is.SameAs(anchor));
                Assert.That(interaction.BlocksMovement, Is.False, "The floating conversation does not hold the visitor.");
                Assert.That(view.Field.gameObject.activeInHierarchy, Is.True);
                yield return null;
                Assert.That(view.Field.isFocused, Is.True, "Opening the conversation gives the field the keyboard.");
                Assert.That(guide.Typing, Is.True);
                Assert.That(MuseumInput.Current.Context, Is.EqualTo(MuseumInputContext.Typing));

                // While writing, the walking keys type instead of moving the visitor.
                var standing = visitor.transform.position;
                Press(keyboard.wKey);
                for (float waited = 0; waited < .5f; waited += Time.deltaTime) yield return null;
                Release(keyboard.wKey);
                yield return null;
                Assert.That(Vector3.Distance(visitor.transform.position, standing), Is.LessThan(.02f), "Writing never walks.");
                Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion));

                view.Field.text = "¿Qué dice la ficha de prueba?";
                view.Submit();
                yield return null;
                Assert.That(guide.Typing, Is.False, "Sending gives the keyboard back to the visit.");
                Assert.That(MuseumInput.Current.Context, Is.EqualTo(MuseumInputContext.Visit));
                for (float waited = 0; guide.Client.Busy && waited < 10; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(guide.Client.State, Is.EqualTo(GuideQuestionState.Answered));
                Assert.That(requests, Is.EqualTo(1));
                yield return null;
                Assert.That(view.Answer.gameObject.activeInHierarchy, Is.True);
                Assert.That(view.Answer.text, Is.EqualTo("Texto de una ficha sintética de prueba."));
                Assert.That(view.Answer.richText, Is.False, "Service text is shown literally.");
                Assert.That(view.Sources.Count, Is.EqualTo(1));
                Assert.That(view.Sources[0].GetComponentInChildren<TMPro.TMP_Text>().text, Does.Contain("Mirador"),
                    "A cited record in the tour names its room.");
                Assert.That(view.Field.text, Is.EqualTo("¿Qué dice la ficha de prueba?"), "The question stays in the field.");

                // Walking away ends the conversation.
                body.enabled = false;
                visitor.transform.position = anchor.transform.position + new Vector3(0, 0, -anchor.ActivationRadius - 4);
                body.enabled = true;
                for (float waited = 0; interaction.State == InteractionState.GuideQuestion && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
            }
            finally
            {
                if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Loads the saved museum against the synthetic service with the guide published at the hearth.</summary>
        private IEnumerator OpenMuseumAtTheGuide(string api)
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = api;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            var element = new ElementSummaryContractV1 { slug = "synthetic-item", title = "Ficha sintética de prueba" };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                    Room("room.hearth", 4), Room("room.overlook", 5, new PointContractV1
                    {
                        key = "punto.mirador.creditos", name = "Créditos", order = 0,
                        activation = new[] { "keyboard" }, elements = new[] { element }
                    }),
                },
                guide = new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = true },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>A microphone that records a second of a tone, so no device or person is needed.</summary>
        private sealed class ToneRecorder : IGuideVoiceRecorder
        {
            private bool recording;
            public int Takes;
            public bool Available => true;
            public bool Recording => recording;
            public float Elapsed => recording ? 1 : 0;
            public bool AwaitingPermission => false;
            public GuideRecorderStart Begin(int maxSeconds) { recording = true; Takes++; return GuideRecorderStart.Started; }
            public byte[] End()
            {
                recording = false;
                var samples = new float[48000];
                for (int index = 0; index < samples.Length; index++) samples[index] = .3f * Mathf.Sin(2 * Mathf.PI * 300 * index / 48000f);
                return WavAudio.EncodePcm16Mono(samples, 48000);
            }
            public void Discard() => recording = false;
            public void Dispose() { }
        }

        private static string SpokenAnswer(int rate)
        {
            string Example(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));
            var samples = new float[rate / 2];
            for (int index = 0; index < samples.Length; index++) samples[index] = .2f * Mathf.Sin(2 * Mathf.PI * 220 * index / rate);
            var wav = Convert.ToBase64String(WavAudio.EncodePcm16Mono(samples, rate, rate));
            return "{\"schema_version\": 1, \"response\": " + Example("guide_answered.json") + ", \"speech_status\": \"ready\", "
                + "\"audio\": {\"mime\": \"audio/wav\", \"data_base64\": \"" + wav + "\"}}";
        }

        [UnityTest]
        public IEnumerator TheGuideListensOnlyAfterConsentAndAnswersAloudWithItsCitations()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            string capabilities = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "voice_capabilities_external.json"));
            string transcription = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "guide_transcription.json"));
            int transcriptions = 0, spoken = 0, wavRate = 0, wavFrames = 0;
            string consent = null, mime = null, answerConsent = null;
            var api = StartService((method, path, headers, body) =>
            {
                if (method == "GET" && path == "/api/v1/guide/voice-capabilities") return (200, capabilities);
                if (method == "POST" && path == "/api/v1/guide/transcriptions")
                {
                    Interlocked.Increment(ref transcriptions);
                    consent = headers["X-Musiyo-Voice-Consent"];
                    mime = headers["Content-Type"];
                    if (WavAudio.TryDecodePcm16(body, out var samples, out var rate)) { wavRate = rate; wavFrames = samples.Length; }
                    return (200, transcription);
                }
                if (method == "POST" && path == "/api/v1/guide/voice-answers")
                {
                    Interlocked.Increment(ref spoken);
                    answerConsent = headers["X-Musiyo-Voice-Consent"];
                    return (200, SpokenAnswer(22050));
                }
                return null;
            });
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var guide = flow.GetComponent<MuseumGuide>();
            var voice = flow.GetComponent<MuseumGuideVoice>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            var recorder = new ToneRecorder();
            voice.Recorder = recorder;
            var anchor = runtime.GuidePoint.Anchor;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => voice.Offered, 10);
            Assert.That(voice.Offered, Is.True, "The API says the voice is configured and the device records.");
            yield return Until(() => display.Openness >= 1, 3);
            yield return null;
            Assert.That(view.VoiceTalk.gameObject.activeInHierarchy, Is.True);

            // The notice comes first, on the guide's own screen, naming the providers the API reported; nothing is sent.
            var rect = (RectTransform)view.VoiceTalk.transform;
            var spot = rect.TransformPoint(rect.rect.center);
            display.Aim(new Ray(spot - display.Surface.forward, display.Surface.forward));
            Assert.That(display.Aimed, Is.SameAs(view.VoiceTalk), "The headset pointer reaches the voice control.");
            Assert.That(display.Press(), Is.True);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Notice));
            StringAssert.Contains("ElevenLabs y Groq", guide.Message);
            StringAssert.Contains("No se guardan ni la grabación ni el texto", guide.Message);
            Assert.That(view.VoiceAccept.gameObject.activeInHierarchy && view.VoiceDecline.gameObject.activeInHierarchy, Is.True);
            Assert.That(recorder.Takes, Is.EqualTo(0), "Nothing is recorded before the visitor accepts.");
            Assert.That(transcriptions + spoken, Is.EqualTo(0));
            view.VoiceDecline.onClick.Invoke();
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Ready));
            Assert.That(voice.Consented, Is.False);

            view.VoiceTalk.onClick.Invoke();
            view.VoiceAccept.onClick.Invoke();
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Recording));
            Assert.That(view.VoiceStop.gameObject.activeInHierarchy && view.VoiceDiscard.gameObject.activeInHierarchy, Is.True);
            StringAssert.StartsWith("Te escucho", guide.Message);
            view.VoiceStop.onClick.Invoke();
            yield return Until(() => voice.Stage == GuideVoiceStage.Review, 10);
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Review));
            Assert.That(consent, Is.EqualTo("request-only-v1"), "The recording goes with the visitor's consent.");
            Assert.That(mime, Is.EqualTo("audio/wav"));
            Assert.That(wavRate, Is.EqualTo(16000), "PCM16 mono at 16 kHz, as the contract asks.");
            Assert.That(wavFrames, Is.EqualTo(16000));
            yield return null;
            StringAssert.Contains("«Pregunta sintética de prueba»", guide.Message);
            Assert.That(view.Field.text, Is.EqualTo("Pregunta sintética de prueba"), "With a keyboard it can be corrected in the field.");
            Assert.That(spoken, Is.EqualTo(0), "Nothing is asked before the visitor confirms the transcript.");

            view.VoiceConfirm.onClick.Invoke();
            yield return Until(() => guide.Client.State == GuideQuestionState.Answered, 10);
            Assert.That(guide.Client.State, Is.EqualTo(GuideQuestionState.Answered));
            Assert.That(spoken, Is.EqualTo(1));
            Assert.That(answerConsent, Is.EqualTo("request-only-v1"));
            yield return null;
            Assert.That(view.Answer.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.Answer.text, Is.EqualTo("Texto de una ficha sintética de prueba."));
            Assert.That(view.Sources.Count, Is.EqualTo(1), "The citations stay visible with the voice.");
            Assert.That(voice.Speaking, Is.True);
            Assert.That(voice.Speaker.clip.frequency, Is.EqualTo(22050), "Played at the rate of its own header.");
            Assert.That(view.VoiceStopSpeech.gameObject.activeInHierarchy, Is.True);

            // A second question needs no second notice in the same visit.
            view.VoiceTalk.onClick.Invoke();
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Recording));
            Assert.That(voice.Speaking, Is.False, "Talking again silences the guide.");
            view.VoiceDiscard.onClick.Invoke();
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Ready));
            Assert.That(recorder.Recording, Is.False);

            // Walking away stops the microphone and the voice and forgets the transcript.
            view.VoiceTalk.onClick.Invoke();
            yield return null;
            body.enabled = false;
            visitor.transform.position = anchor.transform.position + new Vector3(0, 0, -anchor.ActivationRadius - 4);
            body.enabled = true;
            yield return Until(() => interaction.State != InteractionState.GuideQuestion, 2);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
            Assert.That(recorder.Recording, Is.False, "The microphone stops when the visitor leaves.");
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Off));
            Assert.That(voice.Client.Transcript, Is.Null);
        }

        [UnityTest]
        public IEnumerator ALateTranscriptIsDiscardedAndAVoiceThatCannotSpeakLeavesTheText()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            string capabilities = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "voice_capabilities_external.json"));
            string transcription = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "guide_transcription.json"));
            int delay = 2500, written = 0;
            var api = StartService((method, path, headers, body) =>
            {
                if (method == "GET" && path == "/api/v1/guide/voice-capabilities") return (200, capabilities);
                if (method == "POST" && path == "/api/v1/guide/transcriptions") { Thread.Sleep(delay); return (200, transcription); }
                // The voice is busy: the same question must go as a written one.
                if (method == "POST" && path == "/api/v1/guide/voice-answers") return (503, "{\"detail\": \"La voz está ocupada\"}");
                if (method == "POST" && path == "/api/v1/guide/questions") Interlocked.Increment(ref written);
                return null;
            });
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var guide = flow.GetComponent<MuseumGuide>();
            var voice = flow.GetComponent<MuseumGuideVoice>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            voice.Recorder = new ToneRecorder();
            var anchor = runtime.GuidePoint.Anchor;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => voice.Offered, 10);
            Assert.That(voice.Talk() && voice.Accept() && voice.StopTalking(), Is.True);
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Transcribing));
            // Leaving while the transcription is on its way: its late answer never reaches the visitor.
            body.enabled = false;
            visitor.transform.position = anchor.transform.position + new Vector3(0, 0, -anchor.ActivationRadius - 4);
            body.enabled = true;
            yield return Until(() => interaction.State != InteractionState.GuideQuestion, 2);
            yield return new WaitForSecondsRealtime(delay / 1000f + 1);
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Off));
            Assert.That(voice.Client.Transcript, Is.Null);
            Assert.That(voice.Client.TranscriptionState, Is.EqualTo(GuideTranscriptionState.Idle));

            delay = 0;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return null;
            Assert.That(voice.Consented, Is.True, "The consent lasts for the visit.");
            Assert.That(voice.Talk(), Is.True);
            Assert.That(voice.Stage, Is.EqualTo(GuideVoiceStage.Recording), "No second notice.");
            Assert.That(voice.StopTalking(), Is.True);
            yield return Until(() => voice.Stage == GuideVoiceStage.Review, 10);
            Assert.That(voice.Confirm(), Is.True);
            yield return Until(() => guide.Client.State == GuideQuestionState.Answered, 10);
            Assert.That(guide.Client.State, Is.EqualTo(GuideQuestionState.Answered), "The text answer still comes.");
            Assert.That(written, Is.EqualTo(1));
            Assert.That(guide.Client.SpokeInText, Is.True);
            yield return null;
            Assert.That(view.Answer.text, Is.EqualTo("Texto de una ficha sintética de prueba."));
            Assert.That(view.Sources.Count, Is.EqualTo(1));
            Assert.That(voice.Speaking, Is.False);
            StringAssert.Contains("aquí tienes la respuesta escrita", view.VoiceText.text);
        }

        [UnityTest]
        public IEnumerator WithoutAConfiguredVoiceNothingAboutItIsShown()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            string capabilities = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "voice_capabilities_disabled.json"));
            var api = StartService((method, path, headers, body) =>
                method == "GET" && path == "/api/v1/guide/voice-capabilities" ? (200, capabilities) : ((int, string)?)null);
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var guide = flow.GetComponent<MuseumGuide>();
            var voice = flow.GetComponent<MuseumGuideVoice>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            voice.Recorder = new ToneRecorder();
            Assert.That(visitor.FocusPoint(runtime.GuidePoint.Anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => voice.Client.CapabilityState == VoiceCapabilityState.Ready, 10);
            yield return null;
            Assert.That(voice.Offered, Is.False);
            Assert.That(voice.Talk(), Is.False);
            Assert.That(view.VoiceTalk.gameObject.activeInHierarchy || view.VoiceText.gameObject.activeInHierarchy, Is.False);
            Assert.That(view.Field.gameObject.activeInHierarchy, Is.True, "The written question stays as it was.");
        }

        [UnityTest]
        public IEnumerator TheHearthConversationAsksThePublishedQuestionsAndExplainsEachAnswer()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var guide = flow.GetComponent<MuseumGuide>();
            var quiz = flow.GetComponent<MuseumHearthQuiz>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            Assert.That(quiz, Is.Not.Null);
            // A visitor arrives from an exhibit whose screen is still waiting for their return.
            Assert.That(interaction.Activate(runtime.FindPoint("punto.mirador.creditos"), ActivationSource.Keyboard), Is.True);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            var anchor = runtime.GuidePoint.Anchor;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            yield return null;
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration), "Reaching the guide leaves the exhibit behind.");
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => quiz.Available, 10);
            Assert.That(quiz.Available, Is.True, "Opening the guide reads the hearth questions.");
            Assert.That(quizQueries.TryPeek(out var query), Is.True);
            StringAssert.Contains("tour_key=museum-main", query);
            StringAssert.Contains("schema_version=1", query);
            StringAssert.DoesNotContain("room_key", query, "The whole tour's questions: by room the hearth has none.");
            yield return Until(() => display.Openness >= 1, 3);
            Assert.That(view.QuizStart.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.QuizStart.IsInteractable(), Is.True);

            // The headset pointer chooses on the conversation's screen like on any stand's.
            var rect = (RectTransform)view.QuizStart.transform;
            var spot = rect.TransformPoint(rect.rect.center);
            display.Aim(new Ray(spot - display.Surface.forward, display.Surface.forward));
            Assert.That(display.Aimed, Is.SameAs(view.QuizStart));
            Assert.That(display.Press(), Is.True);
            yield return null;
            Assert.That(quiz.Active, Is.True);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion));
            Assert.That(view.Field.gameObject.activeInHierarchy, Is.False, "The question field steps aside meanwhile.");
            StringAssert.Contains("¿Qué ficha contiene «Texto sintético de prueba»?", view.QuizText.text);
            Assert.That(view.QuizOptions.Select(option => option.GetComponentInChildren<TMPro.TMP_Text>().text),
                Is.EqualTo(new[] { "Ficha sintética A", "Ficha sintética B", "Ficha sintética C" }), "Options in published order.");
            Assert.That(view.QuizSkip.gameObject.activeInHierarchy && view.QuizClose.gameObject.activeInHierarchy, Is.True);
            Assert.That(guide.Message, Does.StartWith("Pregunta 1 de 1"));

            view.QuizOptions[0].onClick.Invoke();
            yield return Until(() => quiz.Explanation != null, 10);
            Assert.That(quiz.Explanation, Is.Not.Null);
            yield return null;
            StringAssert.Contains("Fragmento de apoyo: «Texto sintético de prueba»", view.QuizText.text);
            StringAssert.Contains("Fuente: Fuente sintética de pruebas", view.QuizText.text);
            Assert.That(view.QuizText.richText, Is.False, "Service text is shown literally.");
            Assert.That(guide.Message, Does.Contain("Tu respuesta coincide con lo documentado."));
            Assert.That(view.QuizOptions.All(option => !option.IsInteractable()), Is.True, "One choice per question.");
            Assert.That(view.QuizOptions[0].GetComponentInChildren<TMPro.TMP_Text>().text, Does.StartWith("Tu respuesta: "));
            Assert.That(view.QuizNext.gameObject.activeInHierarchy, Is.True);
            Assert.That(view.QuizSkip.gameObject.activeInHierarchy, Is.False);
            Assert.That(guide.Message, Does.Not.Contain("punt"), "No score is kept or shown.");

            // After the last question the conversation thanks the visitor; walking away forgets it all.
            view.QuizNext.onClick.Invoke();
            yield return null;
            Assert.That(quiz.Done, Is.True);
            Assert.That(quiz.Active, Is.False);
            Assert.That(guide.Message, Is.EqualTo("Gracias por compartir este momento."));
            Assert.That(view.QuizStart.gameObject.activeInHierarchy, Is.True, "The conversation can start again.");
            body.enabled = false;
            visitor.transform.position = anchor.transform.position + new Vector3(0, 0, -anchor.ActivationRadius - 4);
            body.enabled = true;
            yield return Until(() => interaction.State != InteractionState.GuideQuestion, 2);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
            Assert.That(quiz.Done || quiz.Active, Is.False, "Leaving the guide abandons the conversation.");
            Assert.That(quiz.Chosen, Is.Null);

            // Passing a question and ending early are always possible.
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => display.Openness >= 1, 3);
            Assert.That(quiz.Begin(), Is.True);
            yield return null;
            view.QuizClose.onClick.Invoke();
            yield return null;
            Assert.That(quiz.Done, Is.True);
            Assert.That(quiz.Client.Result, Is.Null, "Ending keeps no answer.");
        }
#endif
    }
}
