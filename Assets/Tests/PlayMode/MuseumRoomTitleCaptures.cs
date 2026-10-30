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
    /// Pictures of the room's title against the museum service, for the owner's review: on the screen (computer and
    /// browser), smaller beside a narration's subtitles, and on a simulated headset; and whether the orientation's arrival
    /// line gave way to it. Explicit: it writes to TestResults/room-title. Run with MUSIYO_LIVE_API set.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumRoomTitleCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/room-title";

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

        private static void Shot(Camera view, string name, bool overlays, int width = 1600, int height = 1000)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var moved = new List<Canvas>();
            if (overlays)
                foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.enabled)
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

        private static IEnumerator Enter(DesktopVisitorController visitor, RoomAnchor room, Vector3 offset)
        {
            var spot = room.transform.position + offset;
            var look = Vector3.ProjectOnPlane(room.transform.position - spot, Vector3.up);
            visitor.PlaceAt(spot, Quaternion.LookRotation(look.sqrMagnitude > .01f ? look : Vector3.forward));
            yield return null;
        }

#if UNITY_EDITOR
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureTheRoomTitleOnScreenBesideSubtitlesAndOnAHeadset()
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
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var title = hud.RoomTitle;
            Assert.That(title, Is.Not.Null, "The visit carries the room's title.");
            var wayfinding = flow.GetComponent<MuseumWayfinding>();
            var orientation = hud.GetComponent<MuseumWayfindingHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            var anchors = UnityEngine.Object.FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None).ToDictionary(anchor => anchor.Key);
            notes.Add("rooms from the service: " + string.Join(" · ", wayfinding.Rooms.Select((room, index) => (index + 1) + " " + room.key + " «" + room.name + "»")));
            var announced = new List<string>();
            title.Announced += key => announced.Add(key);

            // The menu: no title over it, though the visitor stands in the arrival.
            yield return new WaitForSecondsRealtime(3);
            notes.Add("entry menu: title shown = " + title.Showing + " (room " + wayfinding.CurrentRoomKey + ")");
            flow.EnterImmediately();
            yield return null;
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            yield return Until(() => title.Showing, 6);
            yield return new WaitForSecondsRealtime(MuseumRoomTitle.FadeInSeconds + .3f);
            Shot(camera, "1_llegada_pantalla", true);
            notes.Add("arrival: «" + title.NameText.text + "» / «" + title.PositionText.text + "»");
            yield return Until(() => !title.Showing, 8);

            // Guided by the orientation to the welcome room: the title says it, the arrival line does not repeat it.
            wayfinding.SelectRoom("room.welcome");
            yield return Enter(visitor, anchors["room.welcome"], new Vector3(0, 0, -2));
            yield return Until(() => title.Showing, 6);
            yield return new WaitForSecondsRealtime(MuseumRoomTitle.FadeInSeconds + .3f);
            Shot(camera, "2_bienvenida_pantalla", true);
            notes.Add("welcome: «" + title.NameText.text + "» / «" + title.PositionText.text + "»; orientation status «" + wayfinding.Status
                + "», arrival line visible = " + orientation.StatusVisible);
            yield return Until(() => !title.Showing, 8);

            // A narration plays by a stand: the title waits, then shows smaller at the top while the subtitles run below.
            var runtime = flow.GetComponent<TourRuntime>();
            var narration = flow.GetComponent<MuseumNarration>();
            var stands = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None);
            var point = runtime.FindPoint("punto.personajes.pedestal_1");
            if (point != null && point.HasContent)
            {
                var stand = stands.FirstOrDefault(candidate => candidate.Point == point.Anchor);
                visitor.FocusPoint(point.Anchor, stand != null ? stand.ViewingSpot(1.5f) : (Vector3?)null, stand != null ? stand.ViewTarget : point.Anchor.transform.position);
                yield return Until(() => !string.IsNullOrEmpty(narration.Subtitle), 60);
                yield return Until(() => title.Showing, MuseumRoomTitle.BusyWaitSeconds + 4);
                yield return new WaitForSecondsRealtime(MuseumRoomTitle.FadeInSeconds + .3f);
                Shot(camera, "3_personajes_con_subtitulos", true);
                notes.Add("characters with narration: shown = " + title.Showing + ", compact = " + title.Compact + ", subtitle «" + narration.Subtitle + "»");
                yield return Until(() => !title.Showing, 8);
                flow.GetComponent<MuseumInteraction>().Back();
                yield return new WaitForSecondsRealtime(1);
            }
            else notes.Add("characters: pedestal_1 has no content in the service");

            // A simulated headset: the title on its own canvas ahead of the eyes; the screen's canvas is not drawn.
            hud.UseSpatialSubtitles(camera);
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.GetComponent<MuseumHud>() != null) canvas.enabled = false;
            yield return Enter(visitor, anchors["room.instruments"], new Vector3(-3, 0, 0));
            yield return Until(() => title.Showing, 8);
            yield return new WaitForSecondsRealtime(MuseumRoomTitle.FadeInSeconds + .5f);
            Shot(camera, "4_instrumentos_visor", false, 1440, 1584);
            notes.Add("instruments on a headset: «" + title.NameText.text + "», canvas " + (title.SpatialCanvas != null ? title.SpatialCanvas.transform.position.ToString("F2") : "none")
                + ", eyes " + camera.transform.position.ToString("F2"));
            yield return Until(() => !title.Showing, 8);
            yield return Enter(visitor, anchors["room.hearth"], new Vector3(0, 0, -2.5f));
            yield return Until(() => title.Showing, 8);
            yield return new WaitForSecondsRealtime(MuseumRoomTitle.FadeInSeconds + .5f);
            Shot(camera, "5_fogon_visor", false, 1440, 1584);
            yield return Until(() => !title.Showing, 8);
            // Entering a room again says nothing.
            yield return Enter(visitor, anchors["room.instruments"], new Vector3(-3, 0, 0));
            yield return new WaitForSecondsRealtime(3);
            notes.Add("instruments again: shown = " + title.Showing);
            notes.Add("announced in order: " + string.Join(", ", announced));
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }
#endif
    }
}
