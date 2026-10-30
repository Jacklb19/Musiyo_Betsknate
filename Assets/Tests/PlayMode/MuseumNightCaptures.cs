using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of every room by day and by night for the owner's review, the inventory of the museum's lamps by room,
    /// and what the night costs in the Editor (lights, draws, render time). Explicit: it writes to TestResults/night.
    /// MUSIYO_LIVE_API, when set, brings the exhibits onto their stands.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumNightCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/night";
        private static readonly string[] LampWords = { "farol", "lamp", "lantern", "lumin", "candil", "vela", "antorch", "torch", "bombill" };

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

        private static double Shot(Camera view, string name)
        {
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            view.targetTexture = target;
            var clock = Stopwatch.StartNew();
            view.Render();
            clock.Stop();
            view.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
            return clock.Elapsed.TotalMilliseconds;
        }

        private static string RoomOf(Vector3 position, RoomAnchor[] rooms)
            => rooms.FirstOrDefault(room => room.Contains(position))?.Key ?? "(fuera de las salas)";

        /// <summary>Where each room is seen from: inside it, at eye height, looking across it.</summary>
        private static (Vector3 eye, Vector3 target) View(RoomAnchor room)
        {
            var centre = room.transform.position;
            var size = room.HalfSize;
            switch (room.Key)
            {
                // From the path, towards the portal and the lanterns.
                case "room.arrival": return (centre + new Vector3(0, 1.6f, -size.y * .55f), centre + new Vector3(0, 1.2f, size.y * .4f));
                // From the overlook's railing out over the valley and the sky.
                case "room.overlook": return (centre + new Vector3(1.5f, 1.6f, -1.5f), centre + new Vector3(-6, 2.2f, 6));
                default:
                    var back = room.transform.TransformPoint(new Vector3(0, 0, -size.y * .7f));
                    return (back + Vector3.up * 1.6f, centre + Vector3.up * 1.1f);
            }
        }

#if UNITY_EDITOR
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureEveryRoomByDayAndByNight()
        {
            Directory.CreateDirectory(Folder);
            var notes = new List<string>();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            var api = Environment.GetEnvironmentVariable("MUSIYO_LIVE_API");
            if (!string.IsNullOrEmpty(api))
            {
                var settings = new UnityEditor.SerializedObject(loader);
                settings.FindProperty("apiBase").stringValue = api;
                settings.ApplyModifiedPropertiesWithoutUndo();
                loader.Reload();
                yield return Until(() => loader.State == TourLoadState.Ready, 240);
                notes.Add("api: " + api + " -> " + loader.State);
            }
            else notes.Add("api: none (rooms without their exhibits)");
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            var night = flow.GetComponent<MuseumNight>();
            Assert.That(night, Is.Not.Null, "The visit carries the night.");
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            var rooms = UnityEngine.Object.FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None)
                .OrderBy(room => Array.IndexOf(new[] { "room.arrival", "room.welcome", "room.characters", "room.instruments", "room.hearth", "room.overlook" }, room.Key)).ToArray();
            // The exhibits come to their stands while the visitor walks; a moment is enough for those near the eye.
            yield return new WaitForSecondsRealtime(5);

            // The lamps: by the night's materials, and by any name or material that sounds like a lamp.
            notes.Add("");
            notes.Add("== Inventario de lámparas ==");
            night.Set(false, 0);
            foreach (var (renderer, index, emission) in night.Lamps)
                notes.Add("lámpara (material " + MuseumNight.BaseName(renderer.sharedMaterials[index].name) + "): " + Hierarchy(renderer.transform)
                    + " · sala " + RoomOf(renderer.bounds.center, rooms) + " · emisión de día " + emission);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var words = (renderer.name + " " + string.Join(" ", renderer.sharedMaterials.Where(item => item != null).Select(item => item.name))).ToLowerInvariant();
                if (night.Lamps.Any(lamp => lamp.renderer == renderer) || !LampWords.Any(words.Contains)) continue;
                notes.Add("parecido a lámpara (no encendido): " + Hierarchy(renderer.transform) + " [" + words + "] activo=" + renderer.gameObject.activeInHierarchy
                    + " visible=" + renderer.enabled + " · sala " + RoomOf(renderer.bounds.center, rooms));
            }
            foreach (var room in rooms)
                notes.Add("sala " + room.Key + ": " + night.Lamps.Count(lamp => room.Contains(lamp.renderer.bounds.center)) + " lámparas");
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(light => light.isActiveAndEnabled).ToArray();
            notes.Add("luces en tiempo real activas: " + lights.Length + " (" + string.Join(", ", lights.Select(light => light.type + " " + light.name
                + (light.type == LightType.Directional ? "" : " alcance " + light.range.ToString("F1") + " · sala " + RoomOf(light.transform.position, rooms)))) + ")");

            // Each room by day and by night, with what a frame costs in the Editor.
            notes.Add("");
            notes.Add("== Capturas y coste (Editor, 1600x1000) ==");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var passes = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            for (int index = 0; index < rooms.Length; index++)
            {
                var room = rooms[index];
                var (eye, target) = View(room);
                visitor.PlaceAt(new Vector3(eye.x, room.transform.position.y, eye.z), Quaternion.LookRotation(Vector3.ProjectOnPlane(target - eye, Vector3.up)));
                var spectator = new GameObject("NightCaptureCamera").AddComponent<Camera>();
                spectator.CopyFrom(camera);
                spectator.enabled = false;
                spectator.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                foreach (var dark in new[] { false, true })
                {
                    night.Set(dark, 0);
                    // The room's own ambient settles in its blend time.
                    yield return new WaitForSecondsRealtime(2);
                    yield return null;
                    double milliseconds = 0;
                    for (int frame = 0; frame < 3; frame++) { milliseconds = Shot(spectator, (index + 1) + "_" + room.Key.Replace("room.", "") + (dark ? "_noche" : "_dia")); yield return null; }
                    notes.Add(room.Key + (dark ? " noche" : " día") + ": render " + milliseconds.ToString("F1") + " ms, draws " + draws.LastValue
                        + ", batches " + batches.LastValue + ", setpass " + passes.LastValue + ", interior " + night.Indoor.ToString("F2"));
                }
                UnityEngine.Object.Destroy(spectator.gameObject);
            }

            // The proposal, off by default: night falls by itself at the overlook at the end of the visit.
            var overlook = rooms.FirstOrDefault(room => room.Key == "room.overlook");
            if (overlook != null)
            {
                night.Set(false, 0);
                yield return null;
                var (eye, target) = View(overlook);
                var spectator = new GameObject("NightfallCamera").AddComponent<Camera>();
                spectator.CopyFrom(camera);
                spectator.enabled = false;
                spectator.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                visitor.PlaceAt(new Vector3(eye.x, overlook.transform.position.y, eye.z), Quaternion.LookRotation(Vector3.ProjectOnPlane(target - eye, Vector3.up)));
                night.AutoNightAtOverlook = true;
                var marks = new[] { 0f, .35f, .65f, 1f };
                foreach (var mark in marks)
                {
                    yield return Until(() => night.Level >= mark, 20);
                    Shot(spectator, "propuesta_anochecer_mirador_" + Mathf.RoundToInt(mark * 100));
                }
                notes.Add("");
                notes.Add("propuesta mirador: anochece sola en " + MuseumNightAtmosphere.Current.AutoNightSeconds + " s, automática=" + night.Automatic
                    + " (desactivada en MuseumNight.json: auto_night_at_overlook=false)");
                night.AutoNightAtOverlook = false;
                UnityEngine.Object.Destroy(spectator.gameObject);
            }
            night.Set(false, 0);
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }
#endif

        private static string Hierarchy(Transform item)
        {
            var parts = new List<string>();
            for (; item != null; item = item.parent) parts.Insert(0, item.name);
            return string.Join("/", parts);
        }
    }
}
