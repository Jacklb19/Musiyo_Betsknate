using System;
using System.Collections;
using System.IO;
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
        /// <summary>Answers questions with the canonical example and everything else with 404.</summary>
        private string StartService()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var answered = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "guide_answered.json"));
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
                        bool guide = context.Request.HttpMethod == "POST" && context.Request.Url.AbsolutePath == "/api/v1/guide/questions";
                        if (guide) Interlocked.Increment(ref requests);
                        var bytes = Encoding.UTF8.GetBytes(guide ? answered : "{\"detail\":\"Not Found\"}");
                        context.Response.StatusCode = guide ? 200 : 404;
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
#endif
    }
}
