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
                Set(headset.trackingState, 3);
                Set(headset.centerEyePosition, new Vector3(0, 1.65f, 0));
                Set(headset.centerEyeRotation, Quaternion.identity);
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
                host.PlaceMenu();
                var menuPosition = menu.transform.position;
                var originPosition = visitor.transform.position;
                Set(headset.centerEyePosition, new Vector3(.15f, 1.65f, 0));
                Set(headset.centerEyeRotation, Quaternion.Euler(0, 20, 0));
                yield return null;
                Assert.That(Vector3.Distance(visitor.transform.position, originPosition), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(menu.transform.position, menuPosition), Is.LessThan(.001f));
                Assert.That(camera.transform.localPosition.x, Is.EqualTo(.15f).Within(.001f));
                flow.BeginVisit();
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
                flow.EnterImmediately();
                yield return null;
                Assert.That(menu.GetComponent<UnityEngine.InputSystem.UI.TrackedDeviceRaycaster>().enabled, Is.False,
                    "Visit chrome cannot receive a click through an exhibit panel.");
            }
            finally { unloading = SceneManager.UnloadSceneAsync(scene); }
            if (unloading != null) yield return unloading;
        }
#endif
    }
}
