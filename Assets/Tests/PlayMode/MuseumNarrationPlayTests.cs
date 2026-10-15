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
                Assert.That(narration.Attribution, Does.Contain("Tono sintético de prueba"));
                for (float waited = 0; narration.Subtitle != "Audio sintético de prueba." && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(narration.Subtitle, Is.EqualTo("Audio sintético de prueba."), "The WebVTT cue in force is shown.");

                // The panel follows the fragment being heard, found in the transcription from the published cues.
                var reading = UnityEngine.Object.FindAnyObjectByType<MuseumNarrationReading>();
                Assert.That(reading, Is.Not.Null);
                for (float waited = 0; !reading.Aligned && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(reading.Aligned, Is.True, "The synthetic cues are found in the transcription.");
                Assert.That(reading.ActiveCue, Is.EqualTo(0));
                var transcriptText = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(text => text.name == "NarrationStatus");
                Assert.That(transcriptText.text, Does.Contain(Transcript), "The transcription is shown literally, without markup.");
                Assert.That(transcriptText.richText, Is.False, "Service text is never parsed as rich text.");
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
