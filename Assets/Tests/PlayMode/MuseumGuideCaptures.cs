using System;
using System.Collections;
using System.IO;
using System.Linq;
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
    /// Pictures of the two guides and the hearth, for review. Explicit: it asserts nothing about the design and runs only
    /// when named, writing to MUSIYO_CAPTURE_DIR or TestResults/guides. No content service is needed.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumGuideCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/guides";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        /// <summary>Renders a camera of its own at a chosen eye and target, with every world screen in view.</summary>
        private static IEnumerator Shot(string name, Vector3 eye, Vector3 target, float fov = 70)
        {
            yield return null;
            const int width = 1440, height = 900;
            var holder = new GameObject("CaptureCamera");
            var camera = holder.AddComponent<Camera>();
            camera.fieldOfView = fov;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            holder.transform.position = eye;
            holder.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
            var main = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(other => other != camera && other.isActiveAndEnabled && other.targetTexture == null);
            if (main != null)
            {
                camera.clearFlags = main.clearFlags;
                camera.backgroundColor = main.backgroundColor;
                camera.cullingMask = main.cullingMask;
            }
            var target2 = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = target2;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target2;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target2.Release();
            UnityEngine.Object.Destroy(target2);
            UnityEngine.Object.Destroy(holder);
        }

        private static IEnumerator OpenMuseum()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            RoomContractV1 Room(string key, int order) => new RoomContractV1 { key = key, name = key, order = order, points = new PointContractV1[0] };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[] { Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                    Room("room.hearth", 4), Room("room.overlook", 5) },
                guide = new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = true },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheGuidesAndTheHearth()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return OpenMuseum();
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            void Place(Vector3 spot) { body.enabled = false; visitor.transform.position = spot; body.enabled = true; }
            Place(new Vector3(0, .05f, -50));
            for (float waited = 0; waited < 4; waited += Time.unscaledDeltaTime) yield return null;
            var at = companion.transform.position;
            var side = companion.transform.right;
            yield return Shot("companion_front", at + companion.transform.forward * 2.4f + Vector3.up * 1.5f, at + Vector3.up * 1.0f, 50);
            yield return Shot("companion_side", at + side * 2.4f + Vector3.up * 1.4f, at + Vector3.up * .95f, 50);
            yield return Shot("companion_visitor_view", visitor.transform.position + Vector3.up * 1.65f, at + Vector3.up * 1.1f);

            // The scene's own anchor: the tour may still be retrying its unreachable service.
            var anchor = UnityEngine.Object.FindObjectsByType<PointAnchor>(FindObjectsSortMode.None).First(item => item.Key == "guia.principal");
            var hearth = anchor.transform.position;
            var stones = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == "Fogon_Piedras_Shinyac");
            var fire = stones != null ? stones.bounds.center : hearth;
            Place(hearth + (hearth - fire).normalized * 6);
            for (float waited = 0; waited < 1.5f; waited += Time.unscaledDeltaTime) yield return null;
            // From inside the room, at eye height, around the fire; then from above with the roof put aside for the picture.
            for (int view = 0; view < 6; view++)
            {
                var direction = Quaternion.Euler(0, view * 60, 0) * Vector3.forward;
                yield return Shot("hearth_" + view, new Vector3(fire.x, hearth.y + 1.6f, fire.z) + direction * 2.2f,
                    new Vector3(fire.x, hearth.y + .5f, fire.z) - direction * 1.5f, 80);
            }
            var roofs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(item => item.enabled && (item.name.Contains("Techo") || item.name.Contains("Fleco"))).ToList();
            foreach (var roof in roofs) roof.enabled = false;
            yield return Shot("hearth_top", new Vector3(fire.x, hearth.y + 6.5f, fire.z) - Vector3.forward * .01f, new Vector3(fire.x, hearth.y, fire.z), 75);
            foreach (var roof in roofs) roof.enabled = true;
            yield return Shot("hearth_arrival", visitor.transform.position + Vector3.up * 1.65f, fire + Vector3.up * .9f, 70);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
