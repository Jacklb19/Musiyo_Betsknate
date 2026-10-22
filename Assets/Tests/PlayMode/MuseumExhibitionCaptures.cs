using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Pictures of the exhibition against a running museum API, for the owner's review of the reading stands, the record on
    /// them, the pieces on their pedestals, the point cues and the automatic tour's routes. Explicit: it asserts only that
    /// the API answered, and writes to TestResults/exhibition (pictures, the text on each screen and the routes walked).
    /// Run with MUSIYO_LIVE_API set, for example https://musiyo-api.onrender.com/api/v1.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumExhibitionCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/exhibition";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

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

        /// <summary>A camera of its own, so the visitor's view and its height stay as they are.</summary>
        private static Camera Spectator(Camera like, Vector3 position, Vector3 lookAt)
        {
            var camera = new GameObject("CaptureCamera").AddComponent<Camera>();
            camera.CopyFrom(like);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            return camera;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheExhibitionAgainstTheLiveApi()
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
            yield return Until(() => loader.State == TourLoadState.Ready, 60);
            Assert.That(loader.State, Is.EqualTo(TourLoadState.Ready), loader.Status);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var narration = flow.GetComponent<MuseumNarration>();
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            var stands = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None);
            string Screen() => display.Surface == null ? "" : string.Join("\n", display.Surface.GetComponentsInChildren<TMP_Text>(false)
                .Select(text => { text.ForceMeshUpdate(); return text.GetParsedText(); }));

            // Where things stand, for planning and for comparing before and after.
            foreach (var stand in stands)
                notes.Add("stand " + stand.name + " point=" + (stand.Point != null ? stand.Point.Key : "-") + " base=" + F(stand.transform.position)
                    + " board=" + F(stand.Board.position) + " boardCentre=" + F(stand.BoardCentre) + " facing=" + F(stand.Facing)
                    + " boardSize=" + stand.BoardSize.ToString("F2") + " boardUp=" + F(stand.Board.up));
            foreach (var room in flow.GetComponentsInChildren<RoomAnchor>())
                notes.Add("room " + room.Key + " at " + F(room.transform.position) + " half=" + room.HalfSize.ToString("F1") + " yaw=" + room.transform.eulerAngles.y.ToString("F0"));
            foreach (var point in runtime.OrderedPoints)
                notes.Add("point " + point.Anchor.Key + " at " + F(point.transform.position) + " forward=" + F(point.transform.forward)
                    + " look=" + F(point.Anchor.LookTarget.position) + " radius=" + point.Anchor.ActivationRadius.ToString("F1"));

            IEnumerator Visit(string key, string picture)
            {
                var point = runtime.FindPoint(key);
                if (point == null || !point.HasContent) { notes.Add(key + ": not declared"); yield break; }
                var stand = stands.FirstOrDefault(candidate => candidate.Point == point.Anchor);
                Assert.That(stand != null ? visitor.FocusPoint(point.Anchor, stand.ViewingSpot(1.5f), stand.ViewTarget) : visitor.FocusPoint(point.Anchor), Is.True);
                yield return Until(() => interaction.ActivePoint == point, 5);
                yield return Until(() => narration.State == NarrationState.Playing || narration.State == NarrationState.Unavailable, 40);
                yield return Until(() => display.Openness >= 1, 3);
                yield return Until(() => presenter.State != ModelLoadState.Loading, 40);
                yield return new WaitForSecondsRealtime(2.5f);
                Render(camera, picture);
                File.WriteAllText(Path.Combine(Folder, picture + ".txt"), Screen());
                notes.Add(key + ": narration " + narration.State + ", model " + presenter.State + " " + presenter.ModelSlug + ", screen at "
                    + (display.Surface != null ? F(display.Surface.position) + " size " + display.Surface.sizeDelta.ToString("F0") + " scale " + display.Surface.lossyScale.x.ToString("F5") : "-"));
                if (stand != null)
                {
                    // From the side, to see how the screen sits on its stand.
                    var side = Quaternion.Euler(0, 55, 0) * stand.Facing;
                    var spectator = Spectator(camera, stand.BoardCentre + side * 2.4f + Vector3.up * .25f, stand.BoardCentre + Vector3.up * .2f);
                    Render(spectator, picture + "_side");
                    UnityEngine.Object.Destroy(spectator.gameObject);
                }
            }

            // Entering the characters room: what the visitor sees first.
            var characters = runtime.FindRoom("room.characters");
            if (characters != null)
            {
                var entry = characters.transform.TransformPoint(new Vector3(0, 0, -characters.HalfSize.y + 1.2f));
                visitor.PlaceAt(entry, Quaternion.LookRotation(characters.transform.forward));
                yield return new WaitForSecondsRealtime(4);
                Render(camera, "a_characters_entry");
                notes.Add("characters entry " + F(entry) + ": model on show " + presenter.ModelSlug + " " + presenter.State);
            }

            yield return Visit("punto.personajes.pedestal_1", "b_matachin");
            yield return Visit("punto.personajes.pedestal_4", "c_sanjuanes");
            if (interaction.ActivePoint == runtime.FindPoint("punto.personajes.pedestal_4"))
            {
                bool stepped = hud.StepCarousel(1);
                yield return new WaitForSecondsRealtime(.4f);
                Render(camera, "c_sanjuanes_step_moment");
                notes.Add("carousel step: " + stepped + ", narration right after " + narration.State);
                yield return Until(() => presenter.State != ModelLoadState.Loading, 40);
                yield return new WaitForSecondsRealtime(1.5f);
                Render(camera, "c_sanjuanes_step");
                File.WriteAllText(Path.Combine(Folder, "c_sanjuanes_step.txt"), Screen());
            }
            yield return Visit("punto.bienvenida.mito", "d_mito");
            yield return Visit("punto.personajes.castillo", "e_castillo");

            // The points' markers seen while exploring the courtyard.
            interaction.Close();
            if (characters != null)
            {
                var spectator = Spectator(camera, characters.transform.TransformPoint(new Vector3(-2, 2.2f, -characters.HalfSize.y + 1)),
                    characters.transform.position + Vector3.up * .8f);
                yield return new WaitForSecondsRealtime(1);
                Render(spectator, "f_point_markers");
                UnityEngine.Object.Destroy(spectator.gameObject);
            }

            // The automatic tour's first route from four starting places, drawn from above.
            var tour = flow.GetComponent<MuseumAutoTour>();
            var corners = typeof(MuseumAutoTour).GetField("corners", BindingFlags.Instance | BindingFlags.NonPublic);
            var starts = new List<(string name, Vector3 spot, Color colour)>();
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var maize = renderers.Where(item => item.name.Contains("Maiz") || item.name.Contains("Maize")).ToList();
            notes.Add("maize renderers: " + string.Join(", ", maize.Select(item => item.name + " " + F(item.bounds.center) + " " + F(item.bounds.size)).Distinct().Take(12)));
            // In the middle of the arrival garden's maize, where it is widest.
            var field = maize.Where(item => item.bounds.center.z < -25).OrderByDescending(item => item.bounds.size.x * item.bounds.size.z).FirstOrDefault()
                ?? maize.OrderByDescending(item => item.bounds.size.x * item.bounds.size.z).FirstOrDefault();
            if (field != null)
                starts.Add(("maiz", field.bounds.center + Vector3.right * field.bounds.extents.x * .5f, new Color(.95f, .2f, .2f)));
            var lagoon = renderers.FirstOrDefault(item => item.name.Contains("Laguna") && item.name.Contains("Agua"))
                ?? renderers.FirstOrDefault(item => item.name.Contains("Laguna"));
            notes.Add("lagoon: " + (lagoon != null ? lagoon.name + " " + F(lagoon.bounds.center) + " " + F(lagoon.bounds.size) : "-"));
            if (lagoon != null)
                starts.Add(("laguna", lagoon.bounds.center + Vector3.right * (lagoon.bounds.extents.x + .8f), new Color(.2f, .5f, 1f)));
            var instruments = runtime.FindRoom("room.instruments");
            if (instruments != null) starts.Add(("sala_instrumentos", instruments.transform.position + instruments.transform.right * 2, new Color(1f, .8f, .1f)));
            var hearth = runtime.FindRoom("room.hearth");
            if (hearth != null) starts.Add(("fogata", hearth.transform.position + hearth.transform.right * 2.5f, new Color(.7f, .2f, .9f)));
            var lines = new List<GameObject>();
            var routes = new List<string>();
            foreach (var (name, spot, colour) in starts)
            {
                tour.Stop();
                var floor = spot;
                if (UnityEngine.AI.NavMesh.SamplePosition(spot, out var hit, 4, UnityEngine.AI.NavMesh.AllAreas)) floor = hit.position;
                visitor.PlaceAt(floor + Vector3.up * .05f, Quaternion.identity);
                yield return null;
                bool begun = tour.Begin();
                var route = (Vector3[])corners.GetValue(tour);
                tour.Stop();
                routes.Add(name + " from " + F(floor) + " begun=" + begun + " to " + (tour.Stops.Count > 0 ? "?" : "-") + ": " + string.Join(" ", route.Select(F)));
                if (route.Length < 2) continue;
                var line = new GameObject("CaptureRoute_" + name).AddComponent<LineRenderer>();
                line.positionCount = route.Length + 1;
                line.SetPosition(0, floor + Vector3.up * .3f);
                for (int index = 0; index < route.Length; index++) line.SetPosition(index + 1, route[index] + Vector3.up * .3f);
                line.widthMultiplier = .45f;
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = line.endColor = colour;
                lines.Add(line.gameObject);
            }
            File.WriteAllLines(Path.Combine(Folder, "g_routes.txt"), routes);
            var bounds = new Bounds(flow.transform.position, Vector3.one);
            foreach (var room in flow.GetComponentsInChildren<RoomAnchor>())
                bounds.Encapsulate(new Bounds(room.transform.position, new Vector3(room.HalfSize.x * 2, 1, room.HalfSize.y * 2)));
            foreach (var (_, spot, _) in starts) bounds.Encapsulate(spot);
            var above = new GameObject("CaptureAbove").AddComponent<Camera>();
            above.CopyFrom(camera);
            above.enabled = false;
            above.orthographic = true;
            above.orthographicSize = Mathf.Max(bounds.extents.z, bounds.extents.x / 1.6f) + 4;
            above.farClipPlane = 400;
            above.transform.SetPositionAndRotation(bounds.center + Vector3.up * 120, Quaternion.Euler(90, 0, 0));
            Render(above, "g_routes_from_above");
            UnityEngine.Object.Destroy(above.gameObject);
            foreach (var line in lines) UnityEngine.Object.Destroy(line);
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }
#endif
    }
}
