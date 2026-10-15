using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Placement check of an external model on the central pedestal mount. Explicit: it reads the model named by the
    /// MUSIYO_EXHIBIT_GLB environment variable from outside the project, serves it through a local synthetic API, lets
    /// the visit load it as a published model, and writes measurements and pictures to TestResults/exhibit-placement.
    /// Nothing is copied into the project.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumExhibitPlacementCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string PointKey = "punto.personajes.pedestal_3";
        private const string Folder = "TestResults/exhibit-placement";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        private static void Render(Camera camera, string name)
        {
            const int width = 1440, height = 900;
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

        private static string Number(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator PlaceTheExternalModelOnTheCentralPedestal()
        {
            var path = Environment.GetEnvironmentVariable("MUSIYO_EXHIBIT_GLB");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Ignore("Set MUSIYO_EXHIBIT_GLB to an external GLB.");
            Directory.CreateDirectory(Folder);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            service.AddModel("exhibit-placement-check", "Comprobación de colocación", File.ReadAllBytes(path));
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
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2, new PointContractV1
                    {
                        key = PointKey, name = "Comprobación de colocación", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "exhibit-placement-check", title = "Comprobación de colocación", has_3d_model = true } }
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            var point = runtime.FindPoint(PointKey);
            var mount = point.Anchor.GetComponentInChildren<PointExhibitMount>();
            Assert.That(mount, Is.Not.Null, "The central pedestal carries a mount.");
            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            for (float waited = 0; presenter.State != ModelLoadState.Ready && waited < 60; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Ready), presenter.Status);
            yield return new WaitForSecondsRealtime(.5f);
            for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;

            var model = presenter.Model;
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            int triangles = model.GetComponentsInChildren<MeshFilter>().Sum(filter => (int)(filter.sharedMesh.triangles.Length / 3));
            var top = mount.transform.position;
            float overhang = Mathf.Max(Mathf.Abs(bounds.min.x - top.x), Mathf.Abs(bounds.max.x - top.x), Mathf.Abs(bounds.min.z - top.z), Mathf.Abs(bounds.max.z - top.z));
            var corners = new Vector3[4];
            display.Surface.GetWorldCorners(corners);
            var screen = new Bounds(corners[0], Vector3.zero);
            foreach (var corner in corners) screen.Encapsulate(corner);
            screen.Expand(new Vector3(.02f, .02f, .02f));
            bool overlapsScreen = screen.Intersects(bounds);
            var front = model.forward;
            var report = new List<string>
            {
                "model_file: " + Path.GetFileName(path),
                "triangles: " + triangles,
                "size_m: " + Number(bounds.size.x) + " x " + Number(bounds.size.y) + " x " + Number(bounds.size.z),
                "bottom_above_pedestal_top_m: " + Number(bounds.min.y - top.y),
                "top_height_m: " + Number(bounds.max.y),
                "largest_reach_from_pedestal_centre_m: " + Number(overhang) + " (pedestal radius 0.458)",
                "front_direction: " + front.ToString("F2"),
                "colliders_on_model: " + model.GetComponentsInChildren<Collider>().Length,
                "overlaps_floating_screen: " + overlapsScreen,
            };
            File.WriteAllLines(Path.Combine(Folder, "placement.txt"), report);
            Assert.That(bounds.min.y - top.y, Is.EqualTo(0).Within(.01f), "It stands on the pedestal top.");
            Assert.That(overhang, Is.LessThan(.46f), "It stays within the pedestal.");
            Assert.That(model.GetComponentsInChildren<Collider>(), Is.Empty, "It blocks no path.");
            Assert.That(overlapsScreen, Is.False, "It does not cover the floating screen.");

            var camera = new GameObject("PlacementCamera").AddComponent<Camera>();
            camera.fieldOfView = 50;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            var target = top + Vector3.up * .7f;
            foreach (var (name, eye) in new[]
            {
                ("01_courtyard_entrance", new Vector3(0, 1.6f, -10.5f)), ("02_approach", top + new Vector3(0, .45f, -3.2f)),
                ("03_three_quarter", top + new Vector3(2.4f, .5f, -2.2f)), ("04_side", top + new Vector3(3, .4f, 0)),
                ("05_from_corridor_behind", top + new Vector3(-1.5f, .6f, 5f)),
            })
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                Render(camera, name);
            }
            Render(visitor.GetComponentInChildren<Camera>(), "06_visitor_view_with_screen");
            UnityEngine.Object.Destroy(camera.gameObject);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
