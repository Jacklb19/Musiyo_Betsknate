using System.Collections;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumXrPlayTests : InputTestFixture
    {
        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedMuseumKeepsHeadTrackingAndOneCameraWithAStationarySpatialMenu()
        {
            const string path = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(path);
            AsyncOperation unloading = null;
            try
            {
                yield return null;
                var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
                var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
                var menu = Object.FindAnyObjectByType<MuseumMenuHud>();
                var camera = visitor.GetComponentInChildren<Camera>();
                var input = MuseumInput.Current;
                var headset = InputSystem.AddDevice<XRHMD>();
                var host = menu.gameObject.AddComponent<MuseumXrHost>();
                host.Configure(visitor, flow.GetComponent<MuseumInteraction>(), menu, menu.GetComponent<Canvas>());
                // The visitor starts the application looking to the side.
                Set(headset.trackingState, 3);
                Set(headset.centerEyePosition, new Vector3(0, 1.65f, 0));
                Set(headset.centerEyeRotation, Quaternion.Euler(0, 90, 0));
                yield return null;
                Assert.That(host.Ready, Is.True);
                Assert.That(visitor.enabled, Is.False);
                Assert.That(input.HeadPosition.enabled, Is.True);
                Assert.That(input.Move.enabled, Is.False);
                Assert.That(flow.Entry.Staged, Is.False);
                Assert.That(camera.enabled, Is.True);
                int cameras = 0;
                foreach (var item in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    if (item.isActiveAndEnabled) cameras++;
                Assert.That(cameras, Is.EqualTo(1));
                Assert.That(menu.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.WorldSpace));
                var canvas = menu.GetComponent<Canvas>();
                for (float waited = 0; host.MenuPlacementPending && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(host.MenuPlacementPending, Is.False);
                Assert.That(canvas.enabled, Is.True, "The menu shows once it has its place.");
                AssertInFront(camera.transform, menu.transform, "The first menu opens in front of the gaze, not of the scene.");
                // A recentre moves the view in one frame; the open menu follows once, to the new forward direction.
                Set(headset.centerEyeRotation, Quaternion.Euler(0, 10, 0));
                yield return null;
                yield return null;
                Assert.That(host.MenuPlacementPending, Is.True);
                for (float waited = 0; host.MenuPlacementPending && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                AssertInFront(camera.transform, menu.transform, "After recentring the menu is in front again.");
                var menuPosition = menu.transform.position;
                var originPosition = visitor.transform.position;
                Set(headset.centerEyePosition, new Vector3(.15f, 1.65f, 0));
                Set(headset.centerEyeRotation, Quaternion.Euler(0, 20, 0));
                yield return null;
                Assert.That(Vector3.Distance(visitor.transform.position, originPosition), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(menu.transform.position, menuPosition), Is.LessThan(.001f));
                Assert.That(camera.transform.localPosition.x, Is.EqualTo(.15f).Within(.001f));
                // Reading the menu while looking around never drags it along.
                for (int frame = 0; frame < 30; frame++)
                {
                    Set(headset.centerEyeRotation, Quaternion.Euler(0, 20 + frame * .5f, 0));
                    yield return null;
                }
                Assert.That(Vector3.Distance(menu.transform.position, menuPosition), Is.LessThan(.001f));
                flow.BeginVisit();
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
                flow.EnterImmediately();
                // With a local API answering, the arrival card appears and rightly takes clicks; it is not an exhibit panel.
                flow.GetComponent<MuseumArrival>()?.Dismiss();
                yield return null;
                Assert.That(menu.GetComponent<UnityEngine.InputSystem.UI.TrackedDeviceRaycaster>().enabled, Is.False,
                    "Visit chrome cannot receive a click through an exhibit panel.");
                // Each opening of the pause menu is placed in front of where the visitor looks now.
                Set(headset.centerEyeRotation, Quaternion.Euler(0, 160, 0));
                yield return null;
                flow.GetComponent<MuseumInteraction>().SetPaused(true);
                yield return null;
                for (float waited = 0; host.MenuPlacementPending && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                AssertInFront(camera.transform, menu.transform, "The pause menu opens in front of the gaze.");
                // Gravity may settle the visitor on the floor during the visit; placing menus never moves them sideways.
                Assert.That(Vector3.Distance(Vector3.ProjectOnPlane(visitor.transform.position - originPosition, Vector3.up), Vector3.zero),
                    Is.LessThan(.001f), "Placing menus never moves the visitor.");
                flow.GetComponent<MuseumInteraction>().SetPaused(false);
            }
            finally { unloading = SceneManager.UnloadSceneAsync(scene); }
            if (unloading != null) yield return unloading;
        }
#endif

        private static void AssertInFront(Transform eye, Transform menu, string message)
        {
            var look = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            var toMenu = Vector3.ProjectOnPlane(menu.position - eye.position, Vector3.up);
            Assert.That(Vector3.Angle(look, toMenu), Is.LessThan(5), message);
            Assert.That(Vector3.Angle(look, Vector3.ProjectOnPlane(menu.forward, Vector3.up)), Is.LessThan(5), "It faces away from the visitor, readable.");
        }
    }
}
