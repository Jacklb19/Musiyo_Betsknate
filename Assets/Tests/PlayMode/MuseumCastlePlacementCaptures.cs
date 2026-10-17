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
    /// Location proposal for the Castle as part of the characters courtyard. Explicit: it reads the model named by the
    /// MUSIYO_CASTLE_GLB environment variable from outside the project and serves it through a local synthetic API. In
    /// memory only, it takes the central pedestal out of the row of pedestals, adds the agreed point
    /// punto.personajes.castillo where the pedestal's point was, gives it the reading stand of v3 that stood beside the
    /// pedestal, and stands the Castle at real size on the courtyard floor behind the arc of pedestals. Pictures and
    /// measurements of each variant go to TestResults/castle-placement. The scene file is not changed.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumCastlePlacementCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string PedestalKey = "punto.personajes.pedestal_3";
        private const string CastleKey = "punto.personajes.castillo";
        private const string Folder = "TestResults/castle-placement";
        /// <summary>
        /// The row of pedestals of the characters room is delivered as merged meshes, the earthen bodies in one and their
        /// wooden tops in another; the central one stands at (0, 1).
        /// </summary>
        private static readonly string[] PedestalMeshes = { "Personajes_Mobiliario_Tapia", "Personajes_Mobiliario_Madera" };
        private const string PedestalBand = "Faja_Pedestal_03_Bandereros";
        private static readonly Bounds CentralPedestal = new Bounds(new Vector3(0, .75f, 1), new Vector3(1.1f, 1.6f, 1.1f));
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
        private static string Vector(Vector3 value) => "(" + Number(value.x) + ", " + Number(value.y) + ", " + Number(value.z) + ")";

#if UNITY_EDITOR
        /// <summary>Built height of the Castle reconstruction, in metres; its measurements are approximate.</summary>
        private const float BuiltHeight = 4.11f;

        /// <summary>
        /// Variants: courtyard depth, in metres north of the room centre, of the Castle's base, and times its built size.
        /// </summary>
        private static readonly TestCaseData[] Variants =
        {
            new TestCaseData(2.8f, 1f).Returns(null), new TestCaseData(2.8f, 1.5f).Returns(null),
            new TestCaseData(3.4f, 2f).Returns(null), new TestCaseData(5f, 2f).Returns(null),
        };

        [UnityTest, TestCaseSource(nameof(Variants))]
        public IEnumerator StandTheCastleBehindTheArcOfPedestals(float depth, float factor)
        {
            var path = Environment.GetEnvironmentVariable("MUSIYO_CASTLE_GLB");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Ignore("Set MUSIYO_CASTLE_GLB to the Castle GLB.");
            string variant = (depth < 4 ? "A_near" : "B_back") + "_x" + factor.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '_');
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

            // The proposal, applied in memory: the central pedestal and its band go, the point keeps its place under the
            // agreed key, and the v3 reading stand beside it now serves the Castle.
            var pedestal = runtime.FindAnchor(PedestalKey);
            Assert.That(pedestal, Is.Not.Null);
            int removed = PedestalMeshes.Sum(RemoveCentralPedestal);
            Assert.That(removed, Is.GreaterThan(0), "The central pedestal is found in the delivered row of pedestals.");
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.name == PedestalBand) renderer.enabled = false;
            var castle = UnityEngine.Object.Instantiate(pedestal.gameObject, pedestal.transform.parent);
            castle.name = "PI_Personajes_Castillo";
            pedestal.gameObject.SetActive(false);
            foreach (var old in castle.GetComponentsInChildren<PointExhibitMount>(true)) UnityEngine.Object.DestroyImmediate(old.gameObject);
            // The status marker stood on the pedestal top; where it goes beside the Castle is decided with the location.
            foreach (var marker in castle.GetComponentsInChildren<Renderer>(true)) marker.enabled = false;
            var floor = FloorAt(new Vector3(0, 0, depth));
            var target = new GameObject("LookTarget").transform;
            target.SetParent(castle.transform, false);
            target.position = floor + Vector3.up * 2;
            var anchor = castle.GetComponent<PointAnchor>();
            anchor.Configure(CastleKey, 2, target);
            var mount = new GameObject("ExhibitMount").transform;
            mount.SetParent(castle.transform, false);
            mount.SetPositionAndRotation(floor, Quaternion.LookRotation(Vector3.back, Vector3.up));
            mount.gameObject.AddComponent<PointExhibitMount>().Configure(12, 12, true, factor);
            var stand = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None).First(item => item.Point == pedestal);
            stand.Configure(anchor, stand.Board, stand.BoardSize);

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
            Assert.That(point, Is.Not.Null);
            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            for (float waited = 0; presenter.State != ModelLoadState.Ready && waited < 60; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Ready), presenter.Status);
            yield return new WaitForSecondsRealtime(MuseumModelPresenter.RevealSeconds + .5f);
            for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;

            var model = presenter.Model;
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            int triangles = model.GetComponentsInChildren<MeshFilter>().Sum(filter => filter.sharedMesh.triangles.Length / 3);
            bool roofAbove = Physics.Raycast(new Vector3(bounds.center.x, bounds.max.y + .02f, bounds.center.z), Vector3.up, 20,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearestPedestal = new[] { "punto.personajes.pedestal_2", "punto.personajes.pedestal_4" }
                .Select(key => runtime.FindAnchor(key)).Where(item => item != null)
                .Min(item => Vector2.Distance(new Vector2(item.transform.position.x, item.transform.position.z),
                    new Vector2(bounds.ClosestPoint(item.transform.position).x, bounds.ClosestPoint(item.transform.position).z)));
            File.WriteAllLines(Path.Combine(Folder, variant + "_placement.txt"), new[]
            {
                "variant: " + variant,
                "model_file: " + Path.GetFileName(path),
                "triangles: " + triangles,
                "scale_applied_to_normalized_model: " + Number(model.lossyScale.x),
                "size_m: " + Number(bounds.size.x) + " x " + Number(bounds.size.y) + " x " + Number(bounds.size.z),
                "base_centre: " + Vector(floor),
                "bottom_above_floor_m: " + Number(bounds.min.y - floor.y),
                "point_anchor: " + Vector(castle.transform.position) + " radius 2.0",
                "reading_stand: " + stand.name + " at " + Vector(stand.transform.position),
                "nearest_side_pedestal_m: " + Number(nearestPedestal),
                "anything_above_top: " + roofAbove,
                "central_pedestal_triangles_removed (body and wooden top): " + removed,
            });
            Assert.That(bounds.size.y, Is.EqualTo(BuiltHeight * factor).Within(.15f * factor), "The Castle stands at the chosen size.");
            Assert.That(bounds.min.y - floor.y, Is.EqualTo(0).Within(.03f), "It stands on the courtyard floor.");

            var camera = new GameObject("PlacementCamera").AddComponent<Camera>();
            camera.fieldOfView = 60;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            var middle = bounds.center;
            foreach (var (name, eye, look) in new[]
            {
                ("1_desktop_courtyard", new Vector3(0, 1.75f, -7.5f), middle),
                ("2_eye_height_point", new Vector3(-.6f, 1.6f, Mathf.Min(depth - 2.6f, 0.4f) - (factor - 1) * 1.5f), middle),
                ("3_three_quarter", new Vector3(4.2f, 1.6f, depth - 4.5f), middle),
            })
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                Render(camera, variant + "_" + name);
            }
            Render(visitor.GetComponentInChildren<Camera>(), variant + "_4_visitor_view_with_screen");
            UnityEngine.Object.Destroy(camera.gameObject);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>
        /// The delivered row of pedestals may be batched with the rest of the building, so its renderer is switched off and a
        /// copy of the delivered mesh without the central pedestal is drawn in its place.
        /// </summary>
        private static int RemoveCentralPedestal(string meshName)
        {
            var original = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).First(item => item.name == meshName);
            var source = UnityEditor.AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Musiyo/Museum" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .SelectMany(UnityEditor.AssetDatabase.LoadAllAssetsAtPath).OfType<Mesh>().First(item => item.name == meshName);
            var mesh = UnityEngine.Object.Instantiate(source);
            int removed = MuseumMeshCut.RemoveInside(mesh, original.transform.localToWorldMatrix, CentralPedestal);
            var copy = new GameObject(meshName + "_Proposal");
            copy.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            copy.transform.localScale = original.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = original.sharedMaterials;
            original.enabled = false;
            foreach (var collider in original.GetComponents<Collider>()) collider.enabled = false;
            Physics.SyncTransforms();
            return removed;
        }

        private static Vector3 FloorAt(Vector3 spot)
        {
            var from = new Vector3(spot.x, 3, spot.z);
            return Physics.Raycast(from, Vector3.down, out var hit, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point : new Vector3(spot.x, 0, spot.z);
        }
#endif
    }
}
