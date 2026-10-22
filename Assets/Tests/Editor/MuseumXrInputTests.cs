using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumXrInputTests : InputTestFixture
    {
        private MuseumInput input;
        public override void Setup()
        {
            base.Setup(); input = MuseumInput.CreateDefault(); MuseumInput.Replace(input);
        }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        [Test]
        public void HeadTrackingSurvivesPauseAndCaptureWithoutEnablingWalking()
        {
            input.SetSpatial(true);
            input.SetContext(MuseumInputContext.Menu);
            Assert.That(input.HeadPosition.enabled, Is.True);
            Assert.That(input.Move.enabled, Is.False);
            Assert.That(input.Navigate.enabled, Is.True);
            input.BeginCapture();
            Assert.That(input.HeadRotation.enabled, Is.True);
            Assert.That(input.Click.enabled, Is.False);
            Assert.That(input.Move.enabled, Is.False);
            input.EndCapture();
            input.SetSpatial(false);
            Assert.That(input.HeadPosition.enabled, Is.False);
        }

        [Test]
        public void TouchStickMovesEitherTheVisitorOrMenuAndTriggerUsesTheSharedClick()
        {
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.PoseControl>("Pose");
            InputSystem.RegisterLayout(typeof(OculusTouchControllerProfile.OculusTouchController),
                matches: new UnityEngine.InputSystem.Layouts.InputDeviceMatcher()
                    .WithInterface("XRInputV1").WithProduct("Oculus Touch Controller OpenXR"));
            // Native XR adds Axis2D metadata to the reflected profile; this descriptor exercises the same layout builder.
            var description = new UnityEngine.InputSystem.Layouts.InputDeviceDescription
            {
                interfaceName = "XRInputV1", manufacturer = "OpenXR", product = "Oculus Touch Controller OpenXR",
                capabilities = JsonUtility.ToJson(new UnityEngine.InputSystem.XR.XRDeviceDescriptor
                {
                    inputFeatures = new System.Collections.Generic.List<UnityEngine.InputSystem.XR.XRFeatureDescriptor>
                    {
                        new UnityEngine.InputSystem.XR.XRFeatureDescriptor
                        { name = "thumbstick", featureType = UnityEngine.InputSystem.XR.FeatureType.Axis2D }
                    }
                })
            };
            description.product += " Left";
            var left = (OculusTouchControllerProfile.OculusTouchController)InputSystem.AddDevice(description);
            description.product = "Oculus Touch Controller OpenXR Right";
            var right = (OculusTouchControllerProfile.OculusTouchController)InputSystem.AddDevice(description);
            InputSystem.SetDeviceUsage(left, "LeftHand"); InputSystem.SetDeviceUsage(right, "RightHand");
            input.SetSpatial(true);
            input.SetContext(MuseumInputContext.Menu);
            Set(left.thumbstick, Vector2.up);
            Assert.That(input.Navigate.ReadValue<Vector2>().y, Is.GreaterThan(.5f));
            Assert.That(input.Move.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            input.SetContext(MuseumInputContext.Visit);
            Set(left.thumbstick, Vector2.zero); Set(left.thumbstick, Vector2.up);
            Assert.That(input.Move.ReadValue<Vector2>().y, Is.GreaterThan(.5f));
            Assert.That(input.Navigate.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            Set(right.thumbstick, Vector2.up);
            Assert.That(input.ScrollPanel.ReadValue<float>(), Is.GreaterThan(.5f));
            Set(right.thumbstick, Vector2.zero);
            Press(right.triggerPressed);
            Assert.That(input.Click.WasPressedThisFrame(), Is.True);
            Press(left.menu);
            Assert.That(input.Pause.WasPressedThisFrame(), Is.True);
        }

        private (OculusTouchControllerProfile.OculusTouchController left, OculusTouchControllerProfile.OculusTouchController right) AddControllers()
        {
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.PoseControl>("Pose");
            InputSystem.RegisterLayout(typeof(OculusTouchControllerProfile.OculusTouchController),
                matches: new UnityEngine.InputSystem.Layouts.InputDeviceMatcher()
                    .WithInterface("XRInputV1").WithProduct("Oculus Touch Controller OpenXR"));
            var description = new UnityEngine.InputSystem.Layouts.InputDeviceDescription
            {
                interfaceName = "XRInputV1", manufacturer = "OpenXR", product = "Oculus Touch Controller OpenXR Left",
                capabilities = JsonUtility.ToJson(new UnityEngine.InputSystem.XR.XRDeviceDescriptor
                {
                    inputFeatures = new System.Collections.Generic.List<UnityEngine.InputSystem.XR.XRFeatureDescriptor>
                    {
                        new UnityEngine.InputSystem.XR.XRFeatureDescriptor
                        { name = "thumbstick", featureType = UnityEngine.InputSystem.XR.FeatureType.Axis2D }
                    }
                })
            };
            var left = (OculusTouchControllerProfile.OculusTouchController)InputSystem.AddDevice(description);
            description.product = "Oculus Touch Controller OpenXR Right";
            var right = (OculusTouchControllerProfile.OculusTouchController)InputSystem.AddDevice(description);
            InputSystem.SetDeviceUsage(left, "LeftHand"); InputSystem.SetDeviceUsage(right, "RightHand");
            return (left, right);
        }

        [Test]
        public void TheLeftXButtonAsksTheCompanionAndNothingElse()
        {
            var (left, right) = AddControllers();
            input.SetSpatial(true);
            input.SetContext(MuseumInputContext.Menu);
            Press(left.primaryButton);
            Assert.That(input.AskCompanion.WasPressedThisFrame(), Is.False, "A menu never opens the microphone.");
            Release(left.primaryButton);

            foreach (var context in new[] { MuseumInputContext.Visit, MuseumInputContext.Panel })
            {
                input.SetContext(context);
                Press(left.primaryButton);
                Assert.That(input.AskCompanion.WasPressedThisFrame(), Is.True, context + ": X asks the companion.");
                Assert.That(input.Back.IsPressed() || input.Teleport.IsPressed() || input.AutoTour.IsPressed() || input.Click.IsPressed()
                    || input.Pause.IsPressed() || input.Grab.IsPressed() || input.GrabLeft.IsPressed(), Is.False,
                    "X does nothing else: going back, jumping, the tour, choosing, pausing and taking stay on their own buttons.");
                Release(left.primaryButton);
                Assert.That(input.AskCompanion.WasReleasedThisFrame(), Is.True, "Releasing X ends a held question.");
            }
            Press(left.secondaryButton);
            Press(right.primaryButton);
            Assert.That(input.AskCompanion.IsPressed(), Is.False, "Y and A never ask the companion.");
            Release(left.secondaryButton); Release(right.primaryButton);
            input.SetContext(MuseumInputContext.Typing);
            Assert.That(input.AskCompanion.enabled, Is.False, "Writing silences it.");
        }

        [Test]
        public void ControllerTurningGrabbingRunningAndTheTourStaySilentInMenus()
        {
            var (left, right) = AddControllers();
            input.SetSpatial(true);
            input.SetContext(MuseumInputContext.Menu);
            Set(right.thumbstick, Vector2.right);
            Press(right.gripPressed);
            Press(right.secondaryButton);
            Press(left.thumbstickClicked);
            Assert.That(input.Turn.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero), "A menu never turns the visitor.");
            Assert.That(input.Grab.IsPressed(), Is.False, "A menu never takes a piece.");
            Assert.That(input.AutoTour.WasPressedThisFrame(), Is.False);
            Assert.That(input.Sprint.WasPressedThisFrame(), Is.False);
            Release(right.gripPressed); Release(right.secondaryButton); Release(left.thumbstickClicked);
            Set(right.thumbstick, Vector2.zero);

            input.SetContext(MuseumInputContext.Visit);
            Set(right.thumbstick, new Vector2(.6f, 0));
            Assert.That(input.Turn.ReadValue<Vector2>().x, Is.EqualTo(.6f).Within(.01f), "The right stick's sideways value reaches turning.");
            Press(right.gripPressed);
            Assert.That(input.Grab.WasPressedThisFrame(), Is.True);
            Press(right.secondaryButton);
            Assert.That(input.AutoTour.WasPressedThisFrame(), Is.True);
            Press(left.thumbstickClicked);
            Assert.That(input.Sprint.WasPressedThisFrame(), Is.True);
            Assert.That(input.Click.IsPressed(), Is.False, "The grip and the buttons never act as the trigger.");

            input.SetContext(MuseumInputContext.Typing);
            Assert.That(input.Grab.enabled, Is.False);
            Assert.That(input.Turn.enabled, Is.False);
        }

        [Test]
        public void SpatialModuleUsesTheSharedTrackedActionsAndWorldOrigin()
        {
            var go = new GameObject("Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var origin = new GameObject("Origin");
            try
            {
                var module = go.GetComponent<InputSystemUIInputModule>();
                input.BindUi(module); input.SetSpatial(true, origin.transform);
                Assert.That(module.actionsAsset, Is.SameAs(input.Asset));
                Assert.That(module.trackedDevicePosition.action, Is.SameAs(input.RightPosition));
                Assert.That(module.trackedDeviceOrientation.action, Is.SameAs(input.RightRotation));
                Assert.That(module.xrTrackingOrigin, Is.SameAs(origin.transform));
                input.SetContext(MuseumInputContext.Menu);
                Assert.That(module.move, Is.Null, "The existing menu owns focus navigation.");
                input.SetSpatial(false);
                Assert.That(module.trackedDevicePosition, Is.Null);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(origin); }
        }

        [TestCase("menu_distance", "0")]
        [TestCase("walk_speed", "10")]
        [TestCase("snap_turn_degrees", "120")]
        public void UnsafeSpatialConfigurationIsRejected(string property, string value)
        {
            var json = Resources.Load<TextAsset>("MuseumXrConfiguration").text;
            var invalid = System.Text.RegularExpressions.Regex.Replace(json,
                System.Text.RegularExpressions.Regex.Escape("\"" + property + "\"") + @"\s*:\s*[^,}]+",
                "\"" + property + "\": " + value);
            Assert.That(invalid, Is.Not.EqualTo(json));
            Assert.Throws<System.InvalidOperationException>(() => MuseumXrConfiguration.Parse(invalid));
        }
    }
}
