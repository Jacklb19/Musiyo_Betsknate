using System.Collections;
using System.IO;
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
    /// Pictures of the visit interface at fixed moments, for review. Explicit: it asserts nothing about the design and
    /// runs only when named, writing to TestResults/hud.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumHudCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/hud";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        /// <summary>
        /// Batch mode never reaches the end of a frame, so the camera in use renders into a texture, with the overlay
        /// canvases drawn through it for that one picture.
        /// </summary>
        private static IEnumerator Shot(string name)
        {
            yield return null;
            const int width = 1440, height = 900;
            Camera view = null;
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.isActiveAndEnabled && camera.targetTexture == null && (view == null || camera.depth > view.depth)) view = camera;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var overlays = new System.Collections.Generic.List<Canvas>();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    overlays.Add(canvas);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = view;
                    canvas.planeDistance = view.nearClipPlane + .05f;
                }
            Canvas.ForceUpdateCanvases();
            view.targetTexture = target;
            view.Render();
            view.targetTexture = null;
            foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

        private static PointContractV1 Point(string key, params string[] titles)
        {
            var elements = new ElementSummaryContractV1[titles.Length];
            for (int index = 0; index < titles.Length; index++)
                elements[index] = new ElementSummaryContractV1 { slug = "synthetic-" + index, title = titles[index] };
            return new PointContractV1 { key = key, name = "Punto de prueba", order = 0, activation = new[] { "proximity", "keyboard", "gaze" }, elements = elements };
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheVisitInterface()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            yield return new WaitForSecondsRealtime(.5f);
            yield return Shot("01_menu");
            RoomContractV1 Room(string key, string name, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = name, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Musiyo Bëtsknaté" },
                rooms = new[]
                {
                    Room("room.arrival", "Llegada", 0), Room("room.welcome", "Bienvenida", 1, Point("punto.bienvenida.significados", "Elemento sintético")),
                    Room("room.characters", "Personajes", 2, Point("punto.personajes.pedestal_1", "Elemento A", "Elemento B")),
                    Room("room.instruments", "Instrumentos", 3), Room("room.hearth", "Fogón", 4), Room("room.overlook", "Mirador", 5),
                },
                guide = new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = true },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return new WaitForSecondsRealtime(.5f);
            yield return Shot("02_arrival");
            flow.GetComponent<MuseumArrival>().Dismiss();
            yield return new WaitForSecondsRealtime(.3f);
            yield return Shot("03_exploring");
            var point = runtime.FindPoint("punto.personajes.pedestal_1");
            visitor.FocusPoint(point.Anchor);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("04_point_choice");
            interaction.SelectElement(0);
            yield return new WaitForSecondsRealtime(.6f);
            yield return Shot("05_point_element");
            interaction.Close();
            visitor.FocusPoint(runtime.GuidePoint.Anchor);
            yield return new WaitForSecondsRealtime(.3f);
            yield return Shot("06_guide_prompt");
            flow.GetComponent<MuseumGuide>().Ask();
            yield return new WaitForSecondsRealtime(.8f);
            yield return Shot("07_guide_open");
            interaction.Close();
            flow.GetComponent<MuseumWayfinding>().ToggleMenu();
            yield return new WaitForSecondsRealtime(.3f);
            yield return Shot("08_wayfinding");
            flow.GetComponent<MuseumWayfinding>().CloseMenu();
            interaction.SetPaused(true);
            yield return new WaitForSecondsRealtime(.3f);
            yield return Shot("09_pause");
            interaction.SetPaused(false);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
