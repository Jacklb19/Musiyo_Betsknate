using System;
using System.Collections;
using System.Globalization;
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
    /// <summary>
    /// A published narration in the saved museum, served by a local synthetic API: an eight-second synthetic tone with a
    /// transcription and WebVTT cues, and no cultural content. No real narration exists yet; this checks the playback rules.
    /// </summary>
    public sealed class MuseumNarrationPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Slug = "synthetic-narrated";
        /// <summary>A second record of the same point with the same synthetic narration under its own slug.</summary>
        private const string SecondSlug = "synthetic-narrated-second";
        private const string Transcript = "Audio sintético de prueba. Comprueba pausa, silencio y repetición. Sin contenido cultural.";
        private const string Subtitles = "WEBVTT\n\n00:00.000 --> 00:04.000\nAudio sintético de prueba.\n\n00:04.000 --> 00:08.000\nSin contenido cultural.\n";
        private HttpListener service;

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
        private static string Element(int audioBytes, int subtitleBytes)
        {
            var example = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "element.json"));
            var resources = "\"resources\": [" +
                "{\"id\": \"synthetic-narration\", \"kind\": \"narration\", \"mime\": \"audio/mpeg\", \"byte_count\": " + audioBytes +
                ", \"duration_seconds\": 8.0, \"credit\": \"Tono sintético de prueba\", \"provenance\": \"Generado para pruebas técnicas\"" +
                ", \"transcription\": \"" + Transcript + "\", \"subtitles_resource_id\": \"synthetic-subtitles\"}," +
                "{\"id\": \"synthetic-subtitles\", \"kind\": \"subtitles\", \"mime\": \"text/vtt\", \"byte_count\": " + subtitleBytes + "}]";
            return example.Replace("\"slug\": \"synthetic-item\"", "\"slug\": \"" + Slug + "\"").Replace("\"resources\": []", resources);
        }

        private static string Access(string path, string mime, int bytes) => "{\"schema_version\": 1, \"url\": \"" + path +
            "\", \"expires_at\": \"" + DateTimeOffset.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) +
            "\", \"mime\": \"" + mime + "\", \"byte_count\": " + bytes + ", \"sha256\": null}";

        private string StartService()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var audio = File.ReadAllBytes(Path.Combine(Application.dataPath, "Tests", "PlayMode", "Fixtures", "synthetic-narration.mp3"));
            var subtitles = Encoding.UTF8.GetBytes(Subtitles);
            var element = Encoding.UTF8.GetBytes(Element(audio.Length, subtitles.Length));
            var second = Encoding.UTF8.GetBytes(Element(audio.Length, subtitles.Length).Replace("\"slug\": \"" + Slug + "\"", "\"slug\": \"" + SecondSlug + "\""));
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
                        var path = context.Request.Url.AbsolutePath;
                        byte[] body; string type; int status = 200;
                        if (path == "/api/v1/elements/" + Slug) { body = element; type = "application/json"; }
                        else if (path == "/api/v1/elements/" + SecondSlug) { body = second; type = "application/json"; }
                        else if (path == "/api/v1/resources/synthetic-narration/access")
                        { body = Encoding.UTF8.GetBytes(Access("/files/narration.mp3", "audio/mpeg", audio.Length)); type = "application/json"; }
                        else if (path == "/api/v1/resources/synthetic-subtitles/access")
                        { body = Encoding.UTF8.GetBytes(Access("/files/narration.vtt", "text/vtt", subtitles.Length)); type = "application/json"; }
                        else if (path == "/files/narration.mp3") { body = audio; type = "audio/mpeg"; }
                        else if (path == "/files/narration.vtt") { body = subtitles; type = "text/vtt"; }
                        else { body = Encoding.UTF8.GetBytes("{\"detail\":\"Not Found\"}"); type = "application/json"; status = 404; }
                        context.Response.StatusCode = status;
                        context.Response.ContentType = type;
                        context.Response.ContentLength64 = body.Length;
                        context.Response.OutputStream.Write(body, 0, body.Length);
                        context.Response.OutputStream.Close();
                    }
                    catch (Exception) { }
                });
            }
            service.BeginGetContext(Receive, null);
            return "http://127.0.0.1:" + port + "/api/v1";
        }

        private static void Place(CharacterController body, Vector3 position)
        {
            body.enabled = false;
            body.transform.position = position;
            body.enabled = true;
        }

        [UnityTest]
        public IEnumerator APublishedNarrationFollowsTheVisitorAndItsControls()
        {
            bool hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
                var loader = flow.GetComponent<TourLoader>();
                var settings = new UnityEditor.SerializedObject(loader);
                settings.FindProperty("apiBase").stringValue = api;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                var runtime = flow.GetComponent<TourRuntime>();
                var interaction = flow.GetComponent<MuseumInteraction>();
                var narration = flow.GetComponent<MuseumNarration>();
                var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
                var body = visitor.GetComponent<CharacterController>();
                flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
                for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                    => new RoomContractV1 { key = key, name = key, order = order, points = points };
                Assert.That(runtime.Apply(new TourContractV1
                {
                    schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                    rooms = new[]
                    {
                        Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                        Room("room.hearth", 4), Room("room.overlook", 5, new PointContractV1
                        {
                            key = "punto.mirador.creditos", name = "Créditos", order = 0, activation = new[] { "proximity", "keyboard" },
                            elements = new[] { new ElementSummaryContractV1 { slug = Slug, title = "Elemento sintético", has_narration = true } }
                        }),
                    },
                }, out var error), Is.True, error);
                flow.EnterImmediately();
                yield return null;

                var point = runtime.FindPoint("punto.mirador.creditos");
                Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
                for (float waited = 0; interaction.SelectedElement == null && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(interaction.SelectedElement?.slug, Is.EqualTo(Slug), "A point with one element selects it.");
                for (float waited = 0; narration.State != NarrationState.Playing && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), narration.Status);
                Assert.That(narration.Duration, Is.EqualTo(8).Within(.3f));
                Assert.That(narration.Transcription, Is.EqualTo(Transcript));
                Assert.That(narration.Attribution, Is.Empty, "The narration's credit and provenance belong to the closing credits.");
                for (float waited = 0; narration.Subtitle != "Audio sintético de prueba." && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.Subtitle, Is.EqualTo("Audio sintético de prueba."), "The WebVTT cue in force is shown.");
                // On the screen the subtitles stand centred at the very bottom with no box behind (owner, 25/10/2026).
                yield return null;
                var subtitles = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(text => text.name == "NarrationSubtitles");
                Assert.That(subtitles.gameObject.activeInHierarchy && subtitles.text == "Audio sintético de prueba.", Is.True);
                Assert.That(subtitles.rectTransform.anchorMin.x == 0 && subtitles.rectTransform.anchorMax.x == 1 && subtitles.rectTransform.anchorMax.y == 0, Is.True,
                    "Across the bottom of the view.");
                Assert.That(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Any(item => item.name == "SubtitleBackground"), Is.False, "No box behind the subtitles.");

                // The panel follows the fragment being heard, found in the transcription from the published cues.
                var reading = UnityEngine.Object.FindAnyObjectByType<MuseumNarrationReading>();
                Assert.That(reading, Is.Not.Null);
                for (float waited = 0; !reading.Aligned && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(reading.Aligned, Is.True, "The synthetic cues are found in the transcription.");
                Assert.That(reading.ActiveCue, Is.EqualTo(0));
                var transcriptText = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(text => text.name == "NarrationStatus");
                transcriptText.ForceMeshUpdate();
                Assert.That(transcriptText.GetParsedText(), Does.Contain(Transcript), "The transcription is shown as published.");
                Assert.That(transcriptText.text, Does.Contain("<noparse>Audio sintético de prueba.</noparse>"), "Service text is shown literally.");
                Assert.That(transcriptText.text, Does.Contain(MuseumNarrationReading.MarkOpen), "The fragment being heard is marked.");
                Assert.That(transcriptText.GetParsedText(), Does.Not.Contain("Tono sintético").And.Not.Contain("Generado para pruebas"),
                    "No narration credit or provenance on the screen.");
                var follow = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(button => button.name == "FollowNarration");
                Assert.That(follow.gameObject.activeSelf, Is.False, "Following needs no button while it follows.");
                reading.Suspend();
                Assert.That(reading.Following, Is.False, "Scrolling by hand stops following.");
                Assert.That(follow.gameObject.activeSelf, Is.True, "The visitor can ask to follow again.");

                // Walking away pauses where it was; coming back resumes from there.
                var near = visitor.transform.position;
                var radius = point.Anchor.ActivationRadius + MuseumExperienceConfiguration.Current.PointLeaveMargin + 1;
                Place(body, point.Anchor.transform.position + (near - point.Anchor.transform.position).normalized * radius
                    + Vector3.up * (near.y - point.Anchor.transform.position.y));
                for (float waited = 0; narration.State == NarrationState.Playing && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused));
                Assert.That(narration.PausedByDistance, Is.True);
                float stoppedAt = narration.PlaybackTime;
                yield return new WaitForSecondsRealtime(.5f);
                Assert.That(narration.PlaybackTime, Is.EqualTo(stoppedAt).Within(.05f), "Nothing plays while the visitor is away.");
                Place(body, near);
                for (float waited = 0; narration.State != NarrationState.Playing && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), "Returning resumes the narration.");
                Assert.That(narration.PlaybackTime, Is.GreaterThanOrEqualTo(stoppedAt - .05f));

                narration.ToggleMute();
                Assert.That(narration.Muted, Is.True);
                narration.ToggleMute();
                Assert.That(narration.Muted, Is.False);
                narration.TogglePause();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused));
                narration.TogglePause();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));
                yield return new WaitForSecondsRealtime(.4f);
                narration.Repeat();
                yield return null;
                Assert.That(narration.PlaybackTime, Is.LessThan(.3f), "Repeat starts from the beginning.");
                yield return null;
                Assert.That(reading.Following, Is.True, "Repeating follows the narration again.");
                Assert.That(reading.ActiveCue, Is.EqualTo(0), "Back at the first fragment.");

                interaction.SetPaused(true);
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused), "Pausing the visit pauses the narration.");
                interaction.SetPaused(false);
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));

                // Another record of the same point: the narration heard fades out and the new one is offered, not started.
                Assert.That(runtime.Apply(new TourContractV1
                {
                    schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                    rooms = new[]
                    {
                        Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                        Room("room.hearth", 4), Room("room.overlook", 5, new PointContractV1
                        {
                            key = "punto.mirador.creditos", name = "Créditos", order = 0, activation = new[] { "proximity", "keyboard" },
                            elements = new[]
                            {
                                new ElementSummaryContractV1 { slug = Slug, title = "Elemento sintético", has_narration = true },
                                new ElementSummaryContractV1 { slug = SecondSlug, title = "Segundo elemento sintético", has_narration = true },
                            }
                        }),
                    },
                }, out error), Is.True, error);
                point = runtime.FindPoint("punto.mirador.creditos");
                Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
                for (float waited = 0; (interaction.ActivePoint != point || narration.State != NarrationState.Playing) && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), "Arriving at a point starts its narration.");
                float before = narration.ContextSource.volume;
                // Two records without a model share the card as tabs (owner, 25/10/2026), named by their titles here
                // because the synthetic records have no community.
                yield return null;
                var tabs = UnityEngine.Object.FindAnyObjectByType<MuseumRecordTabs>();
                Assert.That(tabs.Row.gameObject.activeInHierarchy, Is.True, "Two records on the card are chosen with tabs.");
                Assert.That(tabs.Tabs.Count, Is.EqualTo(2));
                Assert.That(tabs.Tabs[0].caption.text, Is.EqualTo("Elemento sintético"));
                Assert.That(tabs.Chosen, Is.EqualTo(0));
                tabs.Tabs[1].button.onClick.Invoke();
                Assert.That(interaction.SelectedElement.slug, Is.EqualTo(SecondSlug), "The tab chooses its record.");
                yield return null;
                yield return null;
                Assert.That(narration.Fading, Is.True, "The narration heard does not stop at once.");
                Assert.That(narration.ContextSource.volume, Is.LessThan(before), "It fades out.");
                for (float waited = 0; narration.State != NarrationState.Ready && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Ready), "The new record's narration is offered with its button.");
                Assert.That(narration.NarratedSlug, Is.EqualTo(SecondSlug));
                Assert.That(narration.ContextSource.isPlaying, Is.False);
                Assert.That(narration.ContextSource.volume, Is.EqualTo(before).Within(.001f), "Its volume is the visitor's again.");
                // Small icons on the screen's header line, not a caption button over the text (owner, 25/10/2026).
                var icons = UnityEngine.Object.FindAnyObjectByType<MuseumNarrationButtons>();
                yield return null;
                Assert.That(icons.Row.gameObject.activeInHierarchy, Is.True, "The narration's icons stand on the header line.");
                Assert.That(icons.ToggleGlyph, Is.EqualTo(MuseumNarrationButtons.Glyph.Play));
                Assert.That(icons.ToggleLabel, Does.StartWith("Escuchar"), "Its play icon invites to listen.");
                Assert.That(icons.RestartLabel, Does.StartWith("Reiniciar"));
                Assert.That(icons.Hint.text, Does.Contain("pausa").And.Contain("reinicia"), "A short hint names the keys.");
                var big = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(button => button.name == "NarrationToggle");
                Assert.That(big.gameObject.activeInHierarchy, Is.False, "The big caption button is gone.");
                // The text starts below the icons and, with nothing in the side column, takes the screen's whole width.
                var viewport = (RectTransform)transcriptText.GetComponentInParent<UnityEngine.UI.ScrollRect>().viewport;
                var corners = new Vector3[4];
                viewport.GetWorldCorners(corners);
                var rowCorners = new Vector3[4];
                icons.Row.GetWorldCorners(rowCorners);
                var page = (RectTransform)viewport.GetComponentInParent<UnityEngine.UI.ScrollRect>().transform;
                float textTop = page.InverseTransformPoint(corners[1]).y, rowBottom = page.InverseTransformPoint(rowCorners[0]).y;
                Assert.That(textTop, Is.LessThanOrEqualTo(rowBottom + 1), "The icons never cover the text.");
                Assert.That(viewport.rect.width, Is.GreaterThan(page.rect.width - 80), "No empty side column narrows the text.");
                Assert.That(icons.Toggle.GetComponent<RectTransform>().rect.width, Is.LessThanOrEqualTo(MuseumNarrationButtons.ButtonSize + .5f), "Small controls.");
                icons.Toggle.onClick.Invoke();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));
                Assert.That(narration.PlaybackTime, Is.LessThan(.3f), "It starts from the beginning.");
                yield return null;
                Assert.That(icons.ToggleGlyph, Is.EqualTo(MuseumNarrationButtons.Glyph.Pause));
                icons.Toggle.onClick.Invoke();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused), "The same icon pauses.");
                icons.Toggle.onClick.Invoke();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), "And resumes.");
                yield return new WaitForSecondsRealtime(.6f);
                icons.Restart.onClick.Invoke();
                Assert.That(narration.PlaybackTime, Is.LessThan(.3f), "Restart goes back to the beginning.");
            }
            finally
            {
                if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// The 1.5x control beside pause and restart (owner, 28/10/2026): the narration goes on from the same words, faster
        /// and at the same pitch, its subtitles keep time with the recording, every pause still holds and the choice is
        /// remembered.
        /// </summary>
        [UnityTest]
        public IEnumerator ANarrationIsHeardAtOneAndAHalfAndKeepsItsSubtitlesInTime()
        {
            bool hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var api = StartService();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
                var loader = flow.GetComponent<TourLoader>();
                var settings = new UnityEditor.SerializedObject(loader);
                settings.FindProperty("apiBase").stringValue = api;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return null;
                var runtime = flow.GetComponent<TourRuntime>();
                var interaction = flow.GetComponent<MuseumInteraction>();
                var narration = flow.GetComponent<MuseumNarration>();
                var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
                var body = visitor.GetComponent<CharacterController>();
                flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
                for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                    => new RoomContractV1 { key = key, name = key, order = order, points = points };
                Assert.That(runtime.Apply(new TourContractV1
                {
                    schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                    rooms = new[]
                    {
                        Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                        Room("room.hearth", 4), Room("room.overlook", 5, new PointContractV1
                        {
                            key = "punto.mirador.creditos", name = "Créditos", order = 0, activation = new[] { "proximity", "keyboard" },
                            elements = new[] { new ElementSummaryContractV1 { slug = Slug, title = "Elemento sintético", has_narration = true } }
                        }),
                    },
                }, out var error), Is.True, error);
                flow.EnterImmediately();
                yield return null;
                var point = runtime.FindPoint("punto.mirador.creditos");
                Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
                for (float waited = 0; narration.State != NarrationState.Playing && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), narration.Status);
                Assert.That(narration.Speed, Is.EqualTo(1), "A first visit hears 1x.");
                yield return null;
                var icons = UnityEngine.Object.FindAnyObjectByType<MuseumNarrationButtons>();
                Assert.That(icons.Speed, Is.Not.Null);
                Assert.That(icons.Speed.gameObject.activeInHierarchy, Is.True, "The speed control stands beside pause and restart.");
                Assert.That(icons.SpeedLit, Is.False);
                Assert.That(icons.SpeedLabel, Does.Contain("1,5"));

                float before = narration.PlaybackTime;
                icons.Speed.onClick.Invoke();
                Assert.That(narration.Speed, Is.EqualTo(1.5f));
                Assert.That(PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, ""), Does.Contain("\"narration_speed\":1.5"),
                    "Remembered for the next visit.");
                for (float waited = 0; !narration.SpeedApplied && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.SpeedApplied, Is.True, "The faster narration is ready in a moment.");
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));
                Assert.That(narration.PlaybackTime, Is.GreaterThanOrEqualTo(before - .1f), "It goes on from the same words.");
                Assert.That(narration.ContextSource.pitch, Is.EqualTo(1), "Never a raised voice.");
                Assert.That(narration.Duration, Is.EqualTo(8).Within(.3f), "The recording's own length.");
                yield return null;
                Assert.That(icons.SpeedLit, Is.True, "Lit while heard faster.");
                Assert.That(icons.SpeedLabel, Does.Contain("normal"));

                float start = narration.PlaybackTime, clock = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(1);
                float rate = (narration.PlaybackTime - start) / (Time.realtimeSinceStartup - clock);
                Assert.That(rate, Is.EqualTo(1.5f).Within(.25f), "One second heard covers a second and a half of the recording.");

                // The subtitles follow the recording's time: its second cue starts at 4 s of the recording.
                for (float waited = 0; narration.PlaybackTime < 4.1f && narration.State == NarrationState.Playing && waited < 4; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.Subtitle, Is.EqualTo("Sin contenido cultural."), "The cue in force at 4 s of the recording.");

                narration.TogglePause();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused));
                float stoppedAt = narration.PlaybackTime;
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(narration.PlaybackTime, Is.EqualTo(stoppedAt).Within(.05f), "Pause holds at 1.5x.");
                narration.TogglePause();
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));

                narration.Repeat();
                yield return null;
                Assert.That(narration.PlaybackTime, Is.LessThan(.3f), "Restart goes back to the beginning.");
                Assert.That(narration.SpeedApplied, Is.True, "And stays faster.");
                for (float waited = 0; narration.Subtitle != "Audio sintético de prueba." && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.Subtitle, Is.EqualTo("Audio sintético de prueba."));

                // Walking away pauses where it was; coming back resumes from there, still faster.
                var near = visitor.transform.position;
                var radius = point.Anchor.ActivationRadius + MuseumExperienceConfiguration.Current.PointLeaveMargin + 1;
                Place(body, point.Anchor.transform.position + (near - point.Anchor.transform.position).normalized * radius
                    + Vector3.up * (near.y - point.Anchor.transform.position.y));
                for (float waited = 0; narration.State == NarrationState.Playing && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.PausedByDistance, Is.True);
                stoppedAt = narration.PlaybackTime;
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(narration.PlaybackTime, Is.EqualTo(stoppedAt).Within(.05f), "Nothing plays while the visitor is away.");
                Place(body, near);
                for (float waited = 0; narration.State != NarrationState.Playing && waited < 2; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing), "Returning resumes the narration.");
                Assert.That(narration.SpeedApplied, Is.True);

                interaction.SetPaused(true);
                Assert.That(narration.State, Is.EqualTo(NarrationState.Paused), "Pausing the visit pauses the narration.");
                interaction.SetPaused(false);
                Assert.That(narration.State, Is.EqualTo(NarrationState.Playing));

                // Back to 1x from the same words.
                before = narration.PlaybackTime;
                icons.Speed.onClick.Invoke();
                Assert.That(narration.Speed, Is.EqualTo(1));
                Assert.That(narration.SpeedApplied, Is.True, "1x needs no preparation.");
                Assert.That(narration.PlaybackTime, Is.EqualTo(before).Within(.1f), "It goes on from the same words.");
                start = narration.PlaybackTime;
                clock = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(.6f);
                rate = (narration.PlaybackTime - start) / (Time.realtimeSinceStartup - clock);
                Assert.That(rate, Is.EqualTo(1).Within(.2f));
                yield return null;
                Assert.That(icons.SpeedLit, Is.False);
            }
            finally
            {
                if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }
#endif
    }
}
