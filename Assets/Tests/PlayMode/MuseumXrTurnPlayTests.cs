using System.Collections;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Owner, headset test of 23/10: turning with the right stick must turn the view in place, around the head, even when
    /// the visitor stands away from the middle of their play area; and the body that collides stands under the head.
    /// </summary>
    public sealed class MuseumXrTurnPlayTests : InputTestFixture
    {
        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator TheRightStickTurnsAroundTheHeadAndTheBodyStandsUnderIt()
        {
            const string path = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(path);
            AsyncOperation unloading = null;
            try
            {
                yield return null;
                var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
                var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
                var menu = Object.FindAnyObjectByType<MuseumMenuHud>();
                var camera = visitor.GetComponentInChildren<Camera>();
                var body = visitor.GetComponent<CharacterController>();
                var headset = InputSystem.AddDevice<XRHMD>();
                var right = AddRightController();
                var host = menu.gameObject.AddComponent<MuseumXrHost>();
                host.Configure(visitor, flow.GetComponent<MuseumInteraction>(), menu, menu.GetComponent<Canvas>());
                Set(headset.trackingState, 3);
                // The visitor stands well away from the middle of the play area, as the owner did.
                Set(headset.centerEyePosition, new Vector3(1.2f, 1.65f, .5f));
                Set(headset.centerEyeRotation, Quaternion.identity);
                yield return null;
                flow.EnterImmediately();
                yield return null;
                flow.GetComponent<MuseumArrival>()?.Dismiss();
                for (int frame = 0; frame < 10; frame++) yield return null;
                Assert.That(MuseumInput.Current.Context, Is.EqualTo(MuseumInputContext.Visit), "The visit takes the sticks.");
                var head = Flat(camera.transform.position);
                Assert.That(Vector3.Distance(Flat(visitor.transform.TransformPoint(body.center)), head), Is.LessThan(.01f),
                    "The body stands under the head, not in the middle of the play area.");
                var look = Flat(camera.transform.forward);
                // Held for a second: a continuous turn sweeps, a step turn fires once.
                Set(right.thumbstick, Vector2.right);
                for (float held = 0; held < 1; held += Time.deltaTime) yield return null;
                Set(right.thumbstick, Vector2.zero);
                yield return null;
                Assert.That(Vector3.Angle(look, Flat(camera.transform.forward)), Is.GreaterThan(15), "The view turned.");
                Assert.That(Vector3.Distance(Flat(camera.transform.position), head), Is.LessThan(.02f), "The head stays where it was.");
                Assert.That(Vector3.Distance(Flat(visitor.transform.TransformPoint(body.center)), Flat(camera.transform.position)), Is.LessThan(.01f),
                    "After turning the body still stands under the head.");
            }
            finally { unloading = SceneManager.UnloadSceneAsync(scene); }
            if (unloading != null) yield return unloading;
        }
#endif

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);

        private static OculusTouchControllerProfile.OculusTouchController AddRightController()
        {
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.PoseControl>("Pose");
            InputSystem.RegisterLayout(typeof(OculusTouchControllerProfile.OculusTouchController),
                matches: new UnityEngine.InputSystem.Layouts.InputDeviceMatcher()
                    .WithInterface("XRInputV1").WithProduct("Oculus Touch Controller OpenXR"));
            var description = new UnityEngine.InputSystem.Layouts.InputDeviceDescription
            {
                interfaceName = "XRInputV1", manufacturer = "OpenXR", product = "Oculus Touch Controller OpenXR Right",
                capabilities = JsonUtility.ToJson(new XRDeviceDescriptor
                {
                    inputFeatures = new System.Collections.Generic.List<XRFeatureDescriptor>
                    {
                        new XRFeatureDescriptor { name = "thumbstick", featureType = FeatureType.Axis2D }
                    }
                })
            };
            var right = (OculusTouchControllerProfile.OculusTouchController)InputSystem.AddDevice(description);
            InputSystem.SetDeviceUsage(right, "RightHand");
            return right;
        }
    }
}
