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

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); GuideVoiceConsent.Forget(); }
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
                // Free questions are tested here; the closing interview has its own test.
                flow.GetComponent<MuseumHearthInterviewer>().BeginsInterview = false;
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
            flow.GetComponent<MuseumHearthInterviewer>().BeginsInterview = false;
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

        /// <summary>
        /// The choices on show keep their size and their captions' size from frame to frame: nothing re-measures itself
        /// while the visitor reads (on the headset the choices flickered, 23/10/2026).
        /// </summary>
        private static IEnumerator AssertSteady(System.Collections.Generic.IReadOnlyList<UnityEngine.UI.Button> choices)
        {
            Assert.That(choices.Select(choice => choice.GetComponentInChildren<TMPro.TMP_Text>()).All(caption => !caption.enableAutoSizing), Is.True,
                "No caption sizes itself to its box.");
            // The screen lays the new choices out at the end of their first frames.
            for (int frame = 0; frame < 3; frame++) yield return null;
            var first = choices.Select(choice => (((RectTransform)choice.transform).rect.size, choice.GetComponentInChildren<TMPro.TMP_Text>().fontSize)).ToArray();
            for (int frame = 0; frame < 15; frame++)
            {
                yield return null;
                var now = choices.Select(choice => (((RectTransform)choice.transform).rect.size, choice.GetComponentInChildren<TMPro.TMP_Text>().fontSize)).ToArray();
                Assert.That(now, Is.EqualTo(first), "The choices hold still, frame " + frame);
            }
        }

        /// <summary>Every caption fits inside its button: none runs out of its box (owner, 25/10/2026).</summary>
        private static void AssertFits(System.Collections.Generic.IEnumerable<UnityEngine.UI.Button> choices)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var choice in choices.Where(choice => choice != null && choice.gameObject.activeInHierarchy))
            {
                var caption = choice.GetComponentInChildren<TMPro.TMP_Text>();
                var box = ((RectTransform)choice.transform).rect;
                Assert.That(caption.preferredHeight, Is.LessThanOrEqualTo(box.height + 1), "«" + caption.text + "» fits its button's height.");
                Assert.That(caption.textBounds.size.x, Is.LessThanOrEqualTo(box.width + 1), "«" + caption.text + "» fits its button's width.");
            }
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
            /// <summary>Zero for a microphone that records silence.</summary>
            public float Amplitude = .3f;
            /// <summary>A single knock in the middle of the take, as a quiet microphone with one click records.</summary>
            public float Click;
            /// <summary>A browser's muted track: the device sends nothing while recording.</summary>
            public bool MutedTrack;
            public bool Muted => recording && MutedTrack;
            public bool Available => true;
            public bool Recording => recording;
            public float Elapsed => recording ? 1 : 0;
            public bool AwaitingPermission => false;
            public GuideRecorderStart Begin(int maxSeconds) { recording = true; Takes++; return GuideRecorderStart.Started; }
            public byte[] End()
            {
                recording = false;
                var samples = new float[48000];
                for (int index = 0; index < samples.Length; index++) samples[index] = Amplitude * Mathf.Sin(2 * Mathf.PI * 300 * index / 48000f);
                for (int index = samples.Length / 2; index < samples.Length / 2 + 6; index++) samples[index] = Click;
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

        /// <summary>
        /// Anywhere in the museum, one button asks the companion: the notice first, then it listens, shows what it understood
        /// for the visitor to confirm, and answers aloud with the text and its sources on its own screen.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCompanionAnswersASpokenQuestionAnywhereAfterConsent()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            string capabilities = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "voice_capabilities_external.json"));
            string transcription = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "guide_transcription.json"));
            int transcriptions = 0, spoken = 0;
            string consent = null, answerConsent = null;
            var api = StartService((method, path, headers, body) =>
            {
                if (method == "GET" && path == "/api/v1/guide/voice-capabilities") return (200, capabilities);
                if (method == "POST" && path == "/api/v1/guide/transcriptions")
                {
                    Interlocked.Increment(ref transcriptions);
                    consent = headers["X-Musiyo-Voice-Consent"];
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
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            yield return Until(() => companion.Voice != null, 3);
            var voice = companion.Voice;
            var panel = companion.GetComponent<MuseumCompanionVoicePanel>();
            var recorder = new ToneRecorder();
            voice.Recorder = recorder;
            yield return Until(() => voice.Listens, 10);
            Assert.That(voice.Listens, Is.True, "The API says the voice is configured and the device records.");
            yield return null;
            Assert.That(panel.Inviting && panel.Shown, Is.True, "The first time it can listen, the companion says how to ask.");
            StringAssert.Contains("V", panel.Body.text);
            Assert.That(guide.InRange, Is.False, "The visitor is far from the hearth guide.");

            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Notice));
            StringAssert.Contains("ElevenLabs y Groq", panel.Body.text);
            StringAssert.Contains("No se guardan ni la grabación ni el texto", panel.Body.text);
            Assert.That(recorder.Takes, Is.EqualTo(0), "Nothing is recorded before the visitor accepts.");
            Assert.That(transcriptions + spoken, Is.EqualTo(0));
            Assert.That(panel.Accept.gameObject.activeInHierarchy && panel.Decline.gameObject.activeInHierarchy, Is.True);
            yield return null;
            // The headset ray (or the centre of the view) reaches the companion's screen like any screen in the museum.
            var rect = (RectTransform)panel.Accept.transform;
            var spot = rect.TransformPoint(rect.rect.center);
            display.Aim(new Ray(spot - panel.Surface.forward, panel.Surface.forward));
            Assert.That(display.Aimed, Is.SameAs(panel.Accept), "The pointer reaches the companion's choices.");
            Assert.That(display.Press(), Is.True);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Recording));
            Assert.That(panel.Chip.text, Is.EqualTo("Te escucho"), "It shows that it is listening.");
            Assert.That(companion.Figure.Gesture, Is.EqualTo(GuideGesture.Listen));

            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return Until(() => voice.Stage == CompanionVoiceStage.Review, 10);
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Review));
            Assert.That(consent, Is.EqualTo("request-only-v1"), "The recording goes with the visitor's consent.");
            yield return null;
            StringAssert.Contains("«Pregunta sintética de prueba»", panel.Body.text);
            Assert.That(spoken, Is.EqualTo(0), "Nothing is asked before the visitor confirms what it understood.");

            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Thinking));
            Assert.That(panel.Chip.text, Is.EqualTo("Pensando…"), "It shows that it is thinking.");
            yield return Until(() => voice.Stage == CompanionVoiceStage.Answer, 10);
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Answer));
            Assert.That(spoken, Is.EqualTo(1));
            Assert.That(answerConsent, Is.EqualTo("request-only-v1"));
            yield return null;
            StringAssert.Contains("Texto de una ficha sintética de prueba.", panel.Body.text);
            Assert.That(panel.Sources.gameObject.activeInHierarchy, Is.True, "The answer cites its records.");
            StringAssert.StartsWith("Fuente: ", panel.Sources.text);
            Assert.That(voice.Speaking && companion.Speaking, Is.True, "The answer is heard.");
            Assert.That(companion.Figure.Gesture, Is.EqualTo(GuideGesture.Speak));

            panel.Close.onClick.Invoke();
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Idle));
            Assert.That(voice.Speaking, Is.False);

            // A take that holds no sound is never sent; the companion asks to hear it again (the browser recorded
            // silence and the recognition made up a phrase, 25/10/2026).
            int sentBefore = transcriptions;
            // Quiet but for one click, as a muted browser microphone records: its peak passes, its loudness does not.
            recorder.Amplitude = 0;
            recorder.Click = .05f;
            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Recording), "Consent given, it listens at once.");
            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Problem));
            Assert.That(voice.ProblemKey, Is.EqualTo("voice_not_heard"));
            Assert.That(transcriptions, Is.EqualTo(sentBefore), "Silence is not sent to be transcribed.");
            yield return null;
            StringAssert.Contains("No te escuché, ¿puedes repetir?", panel.Body.text);
            panel.Close.onClick.Invoke();
            recorder.Amplitude = .3f;
            recorder.Click = 0;
            yield return null;

            // A muted microphone is told at once, while recording, and nothing is sent (owner's browser, 26/10/2026).
            recorder.MutedTrack = true;
            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Problem), "It does not wait for the visitor to speak into a muted microphone.");
            Assert.That(voice.ProblemKey, Is.EqualTo("voice_microphone_muted"));
            Assert.That(transcriptions, Is.EqualTo(sentBefore), "Nothing is sent.");
            yield return null;
            StringAssert.Contains("Tu micrófono no está enviando sonido", panel.Body.text);
            panel.Close.onClick.Invoke();
            recorder.MutedTrack = false;
            yield return null;
            // Once the device sends sound again, the next question works without reloading.
            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Recording));
            panel.Close.onClick.Invoke();
            yield return null;

            // By the hearth guide the button is silent: the guide's own screen takes the questions there.
            Assert.That(visitor.FocusPoint(runtime.GuidePoint.Anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return null;
            Press(keyboard.vKey);
            yield return null;
            Release(keyboard.vKey);
            yield return null;
            Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion));
            Assert.That(voice.Stage, Is.EqualTo(CompanionVoiceStage.Idle));
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

        /// <summary>
        /// The hearth guide is a large ball of yarn floating gently by the fire. From across the room it invites the visitor
        /// and turns to them; sitting down on a bench of the hearth opens the conversation and begins the interview. Seated,
        /// the visitor's view is lower and walking is held; the sit control stands them up where they were.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHearthGuideIsTheOvillitoAndTheInterviewBeginsOnSittingDown()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var guide = flow.GetComponent<MuseumGuide>();
            var quiz = flow.GetComponent<MuseumHearthQuiz>();
            var seating = flow.GetComponent<MuseumSeating>();
            var interviewer = flow.GetComponent<MuseumHearthInterviewer>();
            interviewer.BeginsInterview = true;
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            var eye = visitor.GetComponentInChildren<Camera>().transform;
            yield return Until(() => interviewer.Figure != null, 3);
            Assert.That(interviewer.Figure, Is.Not.Null);
            Assert.That(interviewer.Figure.FromModel && interviewer.Rests, Is.True, "No human figure: the ovillito, resting on the bench.");
            var bounds = interviewer.Figure.WorldBounds();
            Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.InRange(.35f, .6f), "A large ovillito: " + bounds.size);
            var blanket = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).First(piece => piece.name == "Fogon_Manta_Norte");
            Assert.That(bounds.min.y - blanket.bounds.max.y, Is.EqualTo(0).Within(.03f), "It rests on the north bench, by the fire.");
            Assert.That(blanket.bounds.Contains(new Vector3(bounds.center.x, blanket.bounds.center.y, bounds.center.z)), Is.True, "On the bench, not beside it.");
            Assert.That(interviewer.Figure.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(renderer => renderer.name == MuseumCompanion.BodyPiece || renderer.name == MuseumCompanion.HeadPiece)
                .All(renderer => !renderer.enabled), Is.True, "The museum model's grey placeholder is hidden.");

            // From across the room: the invitation, and the ball turns to the visitor; coming close opens nothing yet.
            var anchor = runtime.GuidePoint.Anchor;
            var away = anchor.transform.position - interviewer.Place;
            away.y = 0;
            body.enabled = false;
            visitor.transform.position = anchor.transform.position + away.normalized * (anchor.ActivationRadius + 3.5f);
            body.enabled = true;
            yield return Until(() => interviewer.Inviting, 3);
            Assert.That(interviewer.Inviting, Is.True, "Nobody can pass it by without knowing.");
            Assert.That(interviewer.InvitationText, Is.EqualTo("Acércate a conversar"));
            Assert.That(interviewer.Figure.LookAt.HasValue, Is.True, "It turns to the visitor.");
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration), "Coming close opens nothing: sitting down does.");

            // A bench of the hearth: «Sentarte» near it, and sitting down opens the conversation and the interview.
            var bench = seating.Seats.Where(seat => seat.Hearth && seat.Along.sqrMagnitude > .1f).OrderBy(seat => Vector3.Distance(seat.Top, visitor.transform.position)).First();
            var front = bench.Nearest(visitor.transform.position) + bench.Facing * .7f;
            body.enabled = false;
            visitor.transform.position = new Vector3(front.x, visitor.transform.position.y, front.z);
            body.enabled = true;
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.That(seating.Offered, Is.Not.Null, "A seat is offered nearby.");
            Assert.That(seating.PromptShown, Is.True);
            Assert.That(seating.SitButton.GetComponentInChildren<TMPro.TMP_Text>().text, Is.EqualTo("Sentarte"));
            float standingEyes = eye.position.y;
            var standing = visitor.transform.position;
            Press(keyboard.cKey);
            yield return null;
            Release(keyboard.cKey);
            yield return null;
            Assert.That(seating.Seated, Is.True, "The sit key sits the visitor down.");
            Assert.That(seating.Current.Hearth, Is.True);
            Assert.That(Vector3.Distance(new Vector3(seating.SeatPoint.x, 0, seating.SeatPoint.z), new Vector3(interviewer.Place.x, 0, interviewer.Place.z)),
                Is.GreaterThan(.5f), "The visitor sits beside the ovillito, never on it.");
            Assert.That(standingEyes - eye.position.y, Is.GreaterThan(.3f), "The view goes down to a seated person's eyes.");
            Assert.That(eye.position.y - seating.SeatPoint.y, Is.EqualTo(MuseumSeating.SeatedEyes).Within(.05f));
            yield return Until(() => interaction.State == InteractionState.GuideQuestion, 2);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion), "Sitting down opens the conversation.");
            yield return Until(() => quiz.Active, 10);
            Assert.That(quiz.Active, Is.True, "The interview begins with the published questions.");
            yield return Until(() => !interviewer.Inviting, 2);
            Assert.That(interviewer.Inviting, Is.False, "Seated, the invitation fades away.");

            // Seated, walking is held; the conversation stays open.
            var seated = visitor.transform.position;
            Press(keyboard.dKey);
            for (float waited = 0; waited < .4f; waited += Time.deltaTime) yield return null;
            Release(keyboard.dKey);
            yield return null;
            Assert.That(seating.Seated, Is.False, "Moving stands the visitor up.");
            Assert.That(Vector3.Distance(new Vector3(visitor.transform.position.x, 0, visitor.transform.position.z),
                new Vector3(standing.x, 0, standing.z)), Is.LessThan(.6f), "They stand where they stood.");
            // Measured from the feet: walking on after standing up may climb a low step.
            Assert.That(eye.position.y - visitor.transform.position.y, Is.EqualTo(standingEyes - standing.y).Within(.05f), "The view is back at its height.");
        }

        /// <summary>Any stool or bench outside the hearth can be sat on, and the sit key stands the visitor up.</summary>
        [UnityTest]
        public IEnumerator AnySeatOfTheMuseumCanBeSatOnAndLeft()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var seating = flow.GetComponent<MuseumSeating>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            yield return Until(() => seating.Seats.Count > 0, 6);
            Assert.That(seating.Seats.Count(seat => !seat.Hearth), Is.GreaterThan(3), "Benches and stools of the museum are found while it runs.");
            var stool = seating.Seats.First(seat => !seat.Hearth && seat.Along.sqrMagnitude < 1e-4f);
            var front = stool.Top + stool.Facing * .7f;
            body.enabled = false;
            visitor.transform.position = new Vector3(front.x, stool.Top.y - .35f, front.z);
            body.enabled = true;
            for (float waited = 0; waited < .5f; waited += Time.deltaTime) yield return null;
            Assert.That(seating.Offered, Is.Not.Null);
            Assert.That(seating.Sit(seating.Offered), Is.True);
            yield return null;
            Assert.That(visitor.Seated, Is.True);
            Assert.That(body.enabled, Is.False, "Seated, the body does not walk.");
            for (float waited = 0; waited < .4f; waited += Time.unscaledDeltaTime) yield return null;
            Press(keyboard.cKey);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Release(keyboard.cKey);
            yield return null;
            Assert.That(seating.Seated, Is.False, "The sit key stands the visitor up.");
            Assert.That(body.enabled && !visitor.Seated, Is.True);
        }

        /// <summary>A survey service that keeps what it is sent, so no answer leaves the test.</summary>
        private sealed class KeptSurvey : IClosingSurveySource
        {
            public readonly System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<ClosingSurveyAnswer>> Sent
                = new System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<ClosingSurveyAnswer>>();
            public ClosingSurveyLoad LoadState { get; private set; }
            public ClosingSurveyDefinition Survey { get; private set; }
            public ClosingSurveySubmit SubmitState { get; private set; }
            public event Action Changed;
            public void Load(string tourKey)
            {
                Survey = new ClosingSurveyDefinition
                {
                    Key = "synthetic", Version = "1", NoticeTitle = "Aviso sintético", NoticeText = "Texto sintético del aviso de la encuesta.",
                    Questions = new[]
                    {
                        new ClosingSurveyQuestion { Id = "q1", Text = "Pregunta sintética de opción",
                            Options = new[] { new ClosingSurveyOption { Id = "a", Text = "Opción A" }, new ClosingSurveyOption { Id = "b", Text = "Opción B" } } },
                        new ClosingSurveyQuestion { Id = "q2", Text = "Pregunta sintética de escala", Scale = true, LowLabel = "poco", HighLabel = "mucho",
                            Options = Enumerable.Range(1, 5).Select(value => new ClosingSurveyOption { Id = "v" + value, Text = value.ToString(), Value = value }).ToArray() },
                    }
                };
                LoadState = ClosingSurveyLoad.Ready;
                Changed?.Invoke();
            }
            public void Submit(ClosingSurveyDefinition survey, System.Collections.Generic.IReadOnlyList<ClosingSurveyAnswer> answers)
            {
                Sent.Add(answers);
                SubmitState = ClosingSurveySubmit.Stored;
                Changed?.Invoke();
            }
            public void Poll() { }
            public void Dispose() { }
        }

        /// <summary>
        /// After the interview, the opinion questions: the notice first, with answering and storing anonymously, answering
        /// without storing, or not answering. Without consent nothing is sent; with it, only the questions and choices.
        /// </summary>
        [UnityTest]
        public IEnumerator TheOpinionQuestionsAskConsentFirstAndSendNothingWithoutIt()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return OpenMuseumAtTheGuide(api);
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var guide = flow.GetComponent<MuseumGuide>();
            var quiz = flow.GetComponent<MuseumHearthQuiz>();
            var survey = flow.GetComponent<MuseumClosingSurvey>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            Assert.That(survey, Is.Not.Null);
            var kept = new KeptSurvey();
            survey.Source = kept;
            var anchor = runtime.GuidePoint.Anchor;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return Until(() => quiz.Available && survey.Available, 10);
            yield return Until(() => display.Openness >= 1, 3);
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Off), "Nothing about it shows before the interview ends.");
            Assert.That(quiz.Begin(), Is.True);
            yield return null;
            quiz.Next();
            yield return null;
            Assert.That(quiz.Done, Is.True);
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Notice), "The notice comes before the first opinion question.");
            Assert.That(view.SurveyText.text, Is.EqualTo("Texto sintético del aviso de la encuesta."), "The notice is the service's own text.");
            Assert.That(view.SurveyAccept.gameObject.activeInHierarchy && view.SurveyWithoutStoring.gameObject.activeInHierarchy
                && view.SurveyDecline.gameObject.activeInHierarchy, Is.True, "Store, answer without storing, or not answer.");
            Assert.That(view.QuizStart.gameObject.activeInHierarchy || view.Field.gameObject.activeInHierarchy, Is.False, "Nothing else stands on the screen.");
            yield return null;
            Assert.That(((RectTransform)view.SurveyAccept.transform).rect.height, Is.GreaterThanOrEqualTo(80), "Large choices for the headset.");
            var rect = (RectTransform)view.SurveyWithoutStoring.transform;
            var spot = rect.TransformPoint(rect.rect.center);
            display.Aim(new Ray(spot - display.Surface.forward, display.Surface.forward));
            Assert.That(display.Aimed, Is.SameAs(view.SurveyWithoutStoring), "The headset pointer reaches the choices.");
            Assert.That(display.Press(), Is.True);
            yield return null;
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Answering));
            Assert.That(survey.Storing, Is.False);
            Assert.That(guide.Message, Is.EqualTo("Tu opinión · pregunta 1 de 2"));
            Assert.That(view.SurveyOptions.Select(option => option.GetComponentInChildren<TMPro.TMP_Text>().text), Is.EqualTo(new[] { "Opción A", "Opción B" }));
            yield return AssertSteady(view.SurveyOptions);
            AssertFits(view.SurveyOptions.Concat(new[] { view.SurveySkip, view.SurveyFinish }));
            view.SurveyOptions[1].onClick.Invoke();
            yield return null;
            StringAssert.Contains("1 = poco", view.SurveyText.text);
            Assert.That(view.SurveyOptions.Count, Is.EqualTo(5));
            view.SurveyOptions[3].onClick.Invoke();
            yield return null;
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Done));
            Assert.That(kept.Sent, Is.Empty, "Without consent nothing is sent.");
            Assert.That(guide.Message, Is.EqualTo("Gracias por responder. No se guardó ninguna respuesta."));

            // Leaving forgets the answers. Offered again and accepted, only the question and the choice are sent.
            body.enabled = false;
            visitor.transform.position = anchor.transform.position + new Vector3(0, 0, -anchor.ActivationRadius - 4);
            body.enabled = true;
            yield return Until(() => survey.Stage == ClosingSurveyStage.Off, 2);
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Off));
            Assert.That(survey.Answers, Is.Empty, "Answers are forgotten when the conversation ends.");
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return null;
            Assert.That(guide.Ask(), Is.True);
            yield return null;
            Assert.That(survey.Offer(), Is.True);
            yield return null;
            view.SurveyAccept.onClick.Invoke();
            Assert.That(survey.Storing, Is.True);
            yield return null;
            view.SurveyOptions[0].onClick.Invoke();
            yield return null;
            view.SurveySkip.onClick.Invoke();
            yield return null;
            Assert.That(kept.Sent, Has.Count.EqualTo(1));
            Assert.That(kept.Sent[0].Select(answer => answer.QuestionId + ":" + answer.OptionId), Is.EqualTo(new[] { "q1:a" }),
                "Only the question and the choice; a passed question is not sent.");
            Assert.That(guide.Message, Is.EqualTo("Gracias. Tus respuestas se guardaron de forma anónima."));
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
            yield return AssertSteady(view.QuizOptions);
            AssertFits(view.QuizOptions.Concat(new[] { view.QuizSkip, view.QuizClose }));
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
