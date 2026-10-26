using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The governor's reading stand in the saved museum: it waits unseen while the tour has no point for it, and once the
    /// tour declares the point it opens like any stand and reads a record that has text, sources and credits but no model,
    /// picture or narration.
    /// </summary>
    public sealed class MuseumGovernorPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Key = "punto.personajes.gobernador";
        private const string Folder = "TestResults/governor";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static TourContractV1 Tour(TourRuntime runtime, bool withGovernor)
        {
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            var points = new[]
            {
                new PointContractV1
                {
                    key = "punto.personajes.pedestal_5", name = "Saraguayes", order = 4, activation = new[] { "proximity", "gaze", "keyboard" },
                    elements = new[] { new ElementSummaryContractV1 { slug = "saraguayes", title = "Saraguayes" } }
                },
                new PointContractV1
                {
                    key = Key, name = "Gobernador y cabildantes", order = 6, activation = new[] { "proximity", "gaze", "keyboard" },
                    elements = new[] { new ElementSummaryContractV1 { slug = "gobernador-y-cabildantes", title = "El gobernador y los cabildantes" } }
                },
            };
            return new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1),
                    Room("room.characters", 2, withGovernor ? points : points.Take(1).ToArray()),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            };
        }

        private static string ScreenText(MuseumLecternDisplay display)
            => string.Join("\n", display.Surface.GetComponentsInChildren<TMP_Text>(false).Select(text => { text.ForceMeshUpdate(); return text.GetParsedText(); }));

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator TheGovernorStandWaitsForItsPointAndReadsATextOnlyRecord()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            service.AddRecord("gobernador-y-cabildantes", SyntheticMuseumService.TextRecord("gobernador-y-cabildantes", "El gobernador y los cabildantes"));
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            var anchor = runtime.FindAnchor(Key);
            Assert.That(anchor, Is.Not.Null, "The saved museum has the governor's anchor.");
            var stand = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None).Single(candidate => candidate.Point == anchor);
            var standRenderer = stand.GetComponent<Renderer>();
            var standCollider = stand.GetComponent<Collider>();

            // Without the point in the tour, as the API is before the assignment: nothing shows and nothing fails.
            Assert.That(runtime.Apply(Tour(runtime, false), out var error), Is.True, error);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            Assert.That(runtime.FindPoint(Key).State, Is.EqualTo(PointState.Undeclared));
            Assert.That(stand.Standing, Is.False);
            Assert.That(standRenderer.enabled || standCollider.enabled, Is.False, "The waiting stand is neither seen nor in the way.");
            Assert.That(visitor.FocusPoint(anchor, stand.ViewingSpot(1.2f), stand.ViewTarget), Is.True);
            for (int frame = 0; frame < 30; frame++) yield return null;
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration), "An undeclared point never opens.");
            Assert.That(display.Open, Is.False);
            Assert.That(runtime.Diagnostics, Is.Empty);

            // The tour declares the point: the stand appears and opens by proximity on its only record.
            int requests = presenter.RequestCount;
            Assert.That(runtime.Apply(Tour(runtime, true), out error), Is.True, error);
            yield return null;
            Assert.That(stand.Standing, Is.True);
            Assert.That(standRenderer.enabled && standCollider.enabled, Is.True);
            Assert.That(runtime.OrderedPoints.Select(point => point.Anchor.Key), Has.Member(Key));
            Assert.That(visitor.FocusPoint(anchor, stand.ViewingSpot(1.2f), stand.ViewTarget), Is.True);
            for (float waited = 0; interaction.State != InteractionState.ElementSelected && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            Assert.That(interaction.ActivePoint.Anchor.Key, Is.EqualTo(Key));
            for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(display.Host, Is.EqualTo(stand), "The record unfolds from the governor's own stand.");
            string text = "";
            for (float waited = 0; !(text = ScreenText(display)).Contains("Referencias") && waited < 10; waited += Time.unscaledDeltaTime) yield return null;
            StringAssert.Contains("Gobernador y cabildantes", text);
            StringAssert.Contains("El gobernador y los cabildantes", text);
            StringAssert.Contains("Interpretación sintética para validar el contrato.", text);
            // The reading ends with its short reference and credit line, in small type, instead of naming them inline.
            StringAssert.Contains("Interpretación sintética para validar el contrato.1", text);
            StringAssert.Contains("1. Referencia sintética.", text);
            StringAssert.Contains("Autor de prueba", text);
            StringAssert.DoesNotContain("Fuente:", text);
            StringAssert.DoesNotContain("Cargando", text);
            // No model, picture or narration is asked for or offered, and no arrows step to other pieces.
            yield return new WaitForSecondsRealtime(1);
            Assert.That(presenter.RequestCount, Is.EqualTo(requests));
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Idle));
            var toggle = display.Surface.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "NarrationToggle");
            Assert.That(toggle == null || !toggle.gameObject.activeInHierarchy, Is.True, "No narration control for a record without narration.");
            var icons = display.Surface.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "NarrationButtons");
            Assert.That(icons == null || !icons.gameObject.activeInHierarchy, Is.True, "No narration icons for a record without narration.");
            var cues = flow.GetComponent<MuseumPieceCues>();
            Assert.That(cues == null || !cues.Surface.gameObject.activeInHierarchy, Is.True);

            Directory.CreateDirectory(Folder);
            var camera = visitor.GetComponentInChildren<Camera>();
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, "governor_stand_screen.png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
#endif
    }
}
