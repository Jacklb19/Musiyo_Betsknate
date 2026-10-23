using System;
using System.Collections;
using System.IO;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// What a visitor sees in the headset: the title menu over the fire of the entry set, its settings and controls pages,
    /// the fire seen close, and the closing credits by that fire, from a simulated head. Explicit: it asserts nothing
    /// about the design and writes pictures to MUSIYO_CAPTURE_DIR (or TestResults/headset), each named with
    /// MUSIYO_CAPTURE_LABEL, so the same views can be taken before and after a change.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumHeadsetMenuCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/headset";
        private static string Label => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_LABEL") is string label && label.Length > 0
            ? label : "now";
        /// <summary>A view about as wide as the headset's: 70° high on a 16:9 picture is about 102° across.</summary>
        private const float HeadsetField = 70;
        private SyntheticMuseumService service;
        private XRHMD headset;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        private sealed class Visit
        {
            public MuseumVisitFlow Flow;
            public MuseumMenuHud Menu;
            public MuseumXrHost Host;
            public Camera Head;
        }

        private IEnumerator Open(Visit visit)
        {
            service = new SyntheticMuseumService { Credits = SyntheticMuseumService.ExampleCredits };
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var menu = UnityEngine.Object.FindAnyObjectByType<MuseumMenuHud>();
            headset = InputSystem.AddDevice<XRHMD>();
            var host = menu.gameObject.AddComponent<MuseumXrHost>();
            host.Configure(visitor, flow.GetComponent<MuseumInteraction>(), menu, menu.GetComponent<Canvas>());
            Set(headset.trackingState, 3);
            Set(headset.centerEyePosition, new Vector3(0, 1.65f, 0));
            Set(headset.centerEyeRotation, Quaternion.identity);
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            for (float waited = 0; host.MenuPlacementPending && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
            visit.Flow = flow;
            visit.Menu = menu;
            visit.Host = host;
            visit.Head = visitor.GetComponentInChildren<Camera>();
            yield return new WaitForSecondsRealtime(1.2f);
        }

        /// <summary>Turns the simulated head to a direction, in degrees, without moving it.</summary>
        private IEnumerator Look(float pitch, float yaw)
        {
            Set(headset.centerEyeRotation, Quaternion.Euler(pitch, yaw, 0));
            yield return null;
            yield return null;
        }

        /// <summary>Stands the simulated head at a point of the world and looks at another, through the tracking space.</summary>
        private IEnumerator Stand(Visit visit, Vector3 eye, Vector3 target)
        {
            var space = visit.Host.TrackingOrigin;
            Set(headset.centerEyePosition, space.InverseTransformPoint(eye));
            Set(headset.centerEyeRotation, Quaternion.Inverse(space.rotation) * Quaternion.LookRotation(target - eye, Vector3.up));
            yield return null;
            yield return null;
        }

        private static void Shot(Camera head, string name)
        {
            Directory.CreateDirectory(Folder);
            const int width = 1600, height = 900;
            float field = head.fieldOfView;
            head.fieldOfView = HeadsetField;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = head.targetTexture;
            head.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            head.Render();
            head.targetTexture = previousTarget;
            head.fieldOfView = field;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, Label + "_" + name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator CaptureTheHeadsetMenusAndTheFire()
        {
            var visit = new Visit();
            yield return Open(visit);
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumMenuView>();
            var canvas = visit.Menu.GetComponent<Canvas>();
            Shot(visit.Head, "vr_1_title");
            yield return Look(8, 22);
            Shot(visit.Head, "vr_1b_title_toward_fire");
            yield return Look(0, 0);
            view.Main.Settings.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.2f);
            Shot(visit.Head, "vr_2_settings_top");
            foreach (var (place, name) in new[] { (.55f, "vr_2b_settings_reading"), (0f, "vr_2c_settings_end") })
            {
                view.Settings.Scroll.verticalNormalizedPosition = place;
                yield return new WaitForSecondsRealtime(.4f);
                Shot(visit.Head, name);
            }
            view.Settings.Back.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.6f);
            view.Main.Controls.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.6f);
            Shot(visit.Head, "vr_3_controls");
            view.Controls.Scroll.verticalNormalizedPosition = 0;
            yield return new WaitForSecondsRealtime(.4f);
            Shot(visit.Head, "vr_3b_controls_end");
            view.Controls.Back.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.6f);
            // The fire on its own: the menu is set aside for these pictures.
            var backdrop = visit.Flow.GetComponent<MuseumEntryBackdrop>();
            Assert.That(backdrop != null && backdrop.Shown, Is.True, "The title menu stands over the fire of the entry set.");
            var fire = backdrop.Root.TransformPoint(MuseumEntryBackdrop.Focus);
            canvas.enabled = false;
            yield return Look(6, 18);
            Shot(visit.Head, "vr_4_fire_from_menu");
            var across = backdrop.Root.TransformDirection(new Vector3(-.35f, 0, -1).normalized);
            yield return Stand(visit, fire + across * 1.6f + Vector3.up * 1.45f, fire + Vector3.up * .35f);
            Shot(visit.Head, "vr_4b_fire_close");
            var side = backdrop.Root.TransformDirection(new Vector3(1, 0, -.4f).normalized);
            yield return Stand(visit, fire + side * 1.4f + Vector3.up * 1.05f, fire + Vector3.up * .5f);
            Shot(visit.Head, "vr_4c_fire_side");
            yield return Stand(visit, fire + across * 3.2f + Vector3.up * 1.6f, fire + Vector3.up * 1.2f);
            Shot(visit.Head, "vr_4d_fire_above");
            canvas.enabled = true;
        }

        [UnityTest]
        public IEnumerator CaptureTheHeadsetCredits()
        {
            var visit = new Visit();
            yield return Open(visit);
            var flow = visit.Flow;
            var runtime = flow.GetComponent<TourRuntime>();
            RoomContractV1 Room(string key, int order) => new RoomContractV1 { key = key, name = key, order = order, points = Array.Empty<PointContractV1>() };
            var tour = new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            };
            Assert.That(runtime.Apply(tour, out var error), Is.True, error);
            flow.EnterImmediately();
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            yield return null;
            var ending = flow.GetComponent<MuseumEnding>();
            // The visit asks for the credits on reaching the overlook; the picture skips the walk there.
            ending.Client.Load();
            for (float waited = 0; !ending.Client.Ready && waited < 10; waited += Time.unscaledDeltaTime) yield return null;
            ending.Finish();
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Closing),
                "credits ready " + ending.Client.Ready + ", phase " + flow.Phase + ", stage " + ending.Stage);
            for (float waited = 0; ending.Roll == null && waited < 6; waited += Time.unscaledDeltaTime) yield return null;
            for (float waited = 0; ending.Darkness > 0 && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
            Assert.That(ending.Roll, Is.Not.Null);
            yield return new WaitForSecondsRealtime(.5f);
            Shot(visit.Head, "vr_5_credits_opening");
            yield return new WaitForSecondsRealtime(MuseumCreditsRoll.OpeningSeconds + 6);
            Shot(visit.Head, "vr_5b_credits_rolling");
            var head = visit.Head.transform;
            var flat = Vector3.ProjectOnPlane(ending.Roll.transform.position - head.position, Vector3.up);
            Debug.Log(FormattableString.Invariant(
                $"[Capture] credits roll {flat.magnitude:F2} m from the eyes, {Vector3.SignedAngle(Vector3.ProjectOnPlane(head.forward, Vector3.up), flat, Vector3.up):F1}° from the gaze"));
        }
#endif
    }
}
