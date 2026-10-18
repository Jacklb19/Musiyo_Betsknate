using System;
using System.Collections;
using System.Globalization;
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
    /// The Castle at its approved place: the point punto.personajes.castillo of the saved museum, without the central
    /// pedestal, with the published model standing on the courtyard floor behind the arc of pedestals. Explicit: it reads
    /// the model named by MUSIYO_CASTLE_GLB from outside the project, serves it through a local synthetic API and writes
    /// pictures and measurements to TestResults/castle-placement. MUSIYO_CASTLE_SIZE gives its built size, "width x height".
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumCastlePlacementCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string CastleKey = "punto.personajes.castillo";
        private const string Folder = "TestResults/castle-placement";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        private static Vector2 BuiltSize
        {
            get
            {
                var parts = (Environment.GetEnvironmentVariable("MUSIYO_CASTLE_SIZE") ?? "6.012x3.745").Split('x');
                return new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
            }
        }

        private static void Render(Camera camera, string name)
        {
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
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }

        private static string Number(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator StandTheCastleBehindTheArcOfPedestals()
        {
            var path = Environment.GetEnvironmentVariable("MUSIYO_CASTLE_GLB");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Ignore("Set MUSIYO_CASTLE_GLB to the Castle GLB.");
            Directory.CreateDirectory(Folder);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            service.AddModel("castillo", "El Castillo", File.ReadAllBytes(path));
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2, new PointContractV1
                    {
                        key = CastleKey, name = "El Castillo", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "castillo", title = "El Castillo", has_3d_model = true } }
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            var point = runtime.FindPoint(CastleKey);
            Assert.That(point, Is.Not.Null, "The saved museum has the Castle's point.");
            Assert.That(runtime.FindPoint("punto.personajes.pedestal_3"), Is.Null, "The central pedestal's point is gone.");
            var mount = point.Anchor.GetComponentInChildren<PointExhibitMount>();
            Assert.That(mount, Is.Not.Null);
            Assert.That(mount.KeepNaturalScale, Is.True);
            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            for (float waited = 0; presenter.State != ModelLoadState.Ready && waited < 60; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Ready), presenter.Status);
            yield return new WaitForSecondsRealtime(MuseumModelPresenter.RevealSeconds + .5f);
            for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;

            var model = presenter.Model;
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var floor = mount.transform.position;
            // Nothing of the central pedestal stands where it was.
            bool pedestalLeft = Physics.CheckBox(new Vector3(0, .6f, 1), new Vector3(.4f, .4f, .4f), Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            File.WriteAllLines(Path.Combine(Folder, "placement.txt"), new[]
            {
                "model_file: " + Path.GetFileName(path),
                "triangles: " + model.GetComponentsInChildren<MeshFilter>().Sum(filter => filter.sharedMesh.triangles.Length / 3),
                "size_m: " + Number(bounds.size.x) + " x " + Number(bounds.size.y) + " x " + Number(bounds.size.z),
                "base: " + floor.ToString("F2"),
                "bottom_above_floor_m: " + Number(bounds.min.y - floor.y),
                "central_pedestal_collides: " + pedestalLeft,
            });
            Assert.That(bounds.size.y, Is.EqualTo(BuiltSize.y).Within(.15f), "The Castle stands at its built height.");
            Assert.That(bounds.size.x, Is.EqualTo(BuiltSize.x).Within(.2f), "The Castle spreads to its built width.");
            Assert.That(bounds.min.y - floor.y, Is.EqualTo(0).Within(.03f), "It stands on the courtyard floor.");
            Assert.That(pedestalLeft, Is.False, "The central pedestal is gone, walls and all.");

            var camera = new GameObject("PlacementCamera").AddComponent<Camera>();
            camera.fieldOfView = 60;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            var middle = bounds.center;
            foreach (var (name, eye) in new[]
            {
                ("1_desktop_courtyard", new Vector3(0, 1.75f, -7.5f)), ("2_eye_height_point", new Vector3(-.6f, 1.6f, .4f)),
                ("3_three_quarter", new Vector3(4.2f, 1.6f, floor.z - 4.5f)),
            })
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(middle - eye, Vector3.up));
                Render(camera, name);
            }
            Render(visitor.GetComponentInChildren<Camera>(), "4_visitor_view_with_screen");
            UnityEngine.Object.Destroy(camera.gameObject);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
