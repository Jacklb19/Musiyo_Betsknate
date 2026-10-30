using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the masks of the characters room at each size the owner may choose (owner, 28/10/2026: «que sean mas
    /// como en la vida real, que son mucho mas grandes»), from a visitor's eyes, against a running museum API. Explicit:
    /// it writes TestResults/mask-sizes, or the folder in MUSIYO_CAPTURE_DIR (pictures and where each mask stands against
    /// the floor, the ceiling and the screen of its stand). Run with MUSIYO_LIVE_API set, for example
    /// https://musiyo-api.onrender.com/api/v1.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumMaskSizeCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder
            && folder.Length > 0 ? folder : "TestResults/mask-sizes";
        private static readonly string[] Masks = { "matachin-mask", "sanjuan-animal-mask", "sanjuan-whistling-mask", "sanjuan-smooth-mask" };

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); MuseumPieceSizes.Replace(null); base.TearDown(); }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static string F(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "[{0:F2}, {1:F2}, {2:F2}]", value.x, value.y, value.z);

        private static void Render(Camera like, Vector3 position, Vector3 lookAt, string name)
        {
            const int width = 1600, height = 1000;
            var camera = new GameObject("CaptureCamera").AddComponent<Camera>();
            camera.CopyFrom(like);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
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
            UnityEngine.Object.Destroy(camera.gameObject);
        }

        private static Bounds BoundsOf(Transform model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var item in renderers) bounds.Encapsulate(item.bounds);
            return bounds;
        }

#if UNITY_EDITOR
        [UnityTest, Timeout(1200000)]
        public IEnumerator CaptureTheMasksAtEachSize()
        {
            var api = Environment.GetEnvironmentVariable("MUSIYO_LIVE_API");
            if (string.IsNullOrEmpty(api)) Assert.Ignore("Set MUSIYO_LIVE_API to a running museum API.");
            Directory.CreateDirectory(Folder);
            var notes = new List<string> { "api: " + api };
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = api;
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            loader.Reload();
            yield return Until(() => loader.State == TourLoadState.Ready, 240);
            Assert.That(loader.State, Is.EqualTo(TourLoadState.Ready), loader.Status);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            var stands = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None);
            var characters = runtime.FindRoom("room.characters");
            Assert.That(characters, Is.Not.Null);
            var entry = characters.transform.TransformPoint(new Vector3(0, 0, -characters.HalfSize.y + 1.2f));
            visitor.PlaceAt(entry, Quaternion.LookRotation(characters.transform.forward));
            yield return Until(() => presenter.ResidentCount >= 3, 180);
            notes.Add("residents standing: " + presenter.ResidentCount);
            var points = new[] { "punto.personajes.pedestal_1", "punto.personajes.pedestal_4" }.Select(runtime.FindPoint).Where(point => point != null).ToArray();

            // Sizes to compare: as before (0.6 m on the longest side), the size of the digitisation, and three measured ones.
            var options = new List<(string name, MuseumPieceSizes sizes)>
            {
                ("antes_0_6m", MuseumPieceSizes.Parse("{\"schema_version\":1}")),
                ("escaneo", MuseumPieceSizes.Parse("{\"schema_version\":1,\"scanned_size\":[" + string.Join(",", Masks.Select(slug => "\"" + slug + "\"")) + "]}")),
            };
            foreach (var metres in new[] { .9f, 1.2f, 1.5f })
                options.Add(("lado_" + metres.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', '_') + "m", MuseumPieceSizes.Parse(
                    "{\"schema_version\":1,\"longest_side\":{" + string.Join(",", Masks.Select(slug => "\"" + slug + "\":" + metres.ToString(CultureInfo.InvariantCulture))) + "}}")));

            foreach (var (name, sizes) in options)
            {
                MuseumPieceSizes.Replace(sizes);
                presenter.RefreshPose();
                // The residents grow into place over a few frames as they turn toward the visitor.
                visitor.PlaceAt(entry, Quaternion.LookRotation(characters.transform.forward));
                yield return new WaitForSecondsRealtime(1.5f);
                var eyes = camera.transform.position;
                Render(camera, eyes, eyes + characters.transform.forward * 5 + Vector3.down * .4f, name + "_a_entrada_sala");
                foreach (var point in points)
                {
                    var stand = stands.FirstOrDefault(candidate => candidate.Point == point.Anchor);
                    Assert.That(stand != null ? visitor.FocusPoint(point.Anchor, stand.ViewingSpot(1.5f), stand.ViewTarget) : visitor.FocusPoint(point.Anchor), Is.True);
                    yield return Until(() => interaction.ActivePoint == point, 5);
                    yield return Until(() => presenter.State != ModelLoadState.Loading, 60);
                    yield return new WaitForSecondsRealtime(2.5f);
                    var model = presenter.Model;
                    if (model == null) { notes.Add(name + " " + point.Anchor.Key + ": no piece"); continue; }
                    var bounds = BoundsOf(model);
                    // From the visitor's eyes at 1.6 m, looking at the piece.
                    var eye = camera.transform.position;
                    eye.y = point.transform.position.y + 1.6f;
                    string label = name + (point.Anchor.Key.EndsWith("pedestal_1") ? "_b_matachin" : "_c_sanjuan");
                    Render(camera, eye, bounds.center, label + "_ojos");
                    // From the side, to see the floor, the stand's screen and the piece together.
                    var toEye = eye - bounds.center;
                    toEye.y = 0;
                    var side = Quaternion.Euler(0, 70, 0) * toEye.normalized;
                    Render(camera, bounds.center + side * 3.2f + Vector3.up * .2f, bounds.center, label + "_lado");
                    float floor = point.transform.position.y;
                    float ceiling = Physics.Raycast(bounds.center, Vector3.up, out var hit, 20) ? hit.point.y : float.NaN;
                    float screen = stand != null ? Vector3.Distance(stand.BoardCentre, bounds.ClosestPoint(stand.BoardCentre)) : float.NaN;
                    notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0} {1} ({2}): size {3}, bottom {4:F2} m above the floor, top {5:F2} m (ceiling {6:F2}), screen {7:F2} m away, look target {8}, scale {9:F3}",
                        name, point.Anchor.Key, presenter.ModelSlug, F(bounds.size), bounds.min.y - floor, bounds.max.y - floor, ceiling - floor,
                        screen, F(point.Anchor.LookTarget.position), model.lossyScale.x));
                }
            }
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }
#endif
    }
}
