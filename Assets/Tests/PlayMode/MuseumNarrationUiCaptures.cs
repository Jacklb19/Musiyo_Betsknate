using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Pictures of the narration's subtitles and of the record on a reading stand while its narration plays, against a
    /// running museum API, for the owner's review. Explicit: it asserts only that the API answered, and writes to
    /// TestResults/narration-ui. Run with MUSIYO_LIVE_API set.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumNarrationUiCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/narration-ui";

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

        /// <summary>The camera's view with the screen's overlay canvases drawn through it, as a visitor at a screen sees it.</summary>
        private static void Shot(Camera view, string name, bool overlays)
        {
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var moved = new List<Canvas>();
            if (overlays)
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        moved.Add(canvas);
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = view;
                        canvas.planeDistance = view.nearClipPlane + .05f;
                    }
            Canvas.ForceUpdateCanvases();
            view.targetTexture = target;
            view.Render();
            view.targetTexture = null;
            foreach (var canvas in moved) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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

        private static Camera Spectator(Camera like, Vector3 position, Vector3 lookAt)
        {
            var camera = new GameObject("CaptureCamera").AddComponent<Camera>();
            camera.CopyFrom(like);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            return camera;
        }

#if UNITY_EDITOR
        [UnityTest, Timeout(600000)]
        public IEnumerator CaptureTheSubtitlesAndTheNarrationControls()
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
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var narration = flow.GetComponent<MuseumNarration>();
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            var stands = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None);

            foreach (var (key, picture) in new[] { ("punto.personajes.pedestal_1", "matachin"), ("punto.personajes.castillo", "castillo") })
            {
                var point = runtime.FindPoint(key);
                if (point == null || !point.HasContent) { notes.Add(key + ": not declared"); continue; }
                var stand = stands.FirstOrDefault(candidate => candidate.Point == point.Anchor);
                Assert.That(stand != null ? visitor.FocusPoint(point.Anchor, stand.ViewingSpot(1.5f), stand.ViewTarget) : visitor.FocusPoint(point.Anchor), Is.True);
                yield return Until(() => interaction.ActivePoint == point, 5);
                yield return Until(() => narration.State == NarrationState.Playing, 60);
                yield return Until(() => !string.IsNullOrEmpty(narration.Subtitle), 20);
                yield return Until(() => display.Openness >= 1, 3);
                yield return new WaitForSecondsRealtime(3);
                notes.Add(key + ": narration " + narration.State + ", subtitle «" + narration.Subtitle + "»");
                // The whole screen, subtitles included, as on a computer or in the browser.
                Shot(camera, picture + "_screen", true);
                if (stand != null && display.Surface != null)
                {
                    // The card on the stand as its reader sees it, while the narration plays.
                    var page = display.Surface;
                    var reader = stand.BoardCentre + stand.Facing * .7f;
                    reader.y = stand.transform.position.y + 1.6f;
                    var reading = Spectator(camera, reader, page.TransformPoint(page.rect.center));
                    Shot(reading, picture + "_card_playing", false);
                    // Paused, the toggle offers to resume.
                    narration.TogglePause();
                    yield return new WaitForSecondsRealtime(.5f);
                    Shot(reading, picture + "_card_paused", false);
                    narration.TogglePause();
                    UnityEngine.Object.Destroy(reading.gameObject);
                }
            }

            // On a headset the screen's canvas is only drawn with a menu: the subtitles need a canvas of their own.
            var spatial = typeof(MuseumHud).GetMethod("UseSpatialSubtitles");
            if (spatial != null)
            {
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.GetComponent<MuseumHud>() != null) canvas.enabled = false;
                spatial.Invoke(hud, new object[] { camera });
                yield return Until(() => !string.IsNullOrEmpty(narration.Subtitle), 20);
                yield return new WaitForSecondsRealtime(1);
                Shot(camera, "headset_subtitles", false);
                notes.Add("headset subtitles: on their own canvas");
            }
            else notes.Add("headset subtitles: none (the shared canvas is not drawn without a menu)");
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }
#endif
    }
}
