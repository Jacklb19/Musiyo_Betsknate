using System.Collections;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class VisitorMovementPlayTests : InputTestFixture
    {
        private const float Walk = .5f;
        private int simulatedFrames;
        private float simulatedSeconds;

        /// <summary>Distance covered in Walk seconds of game time while easing in at the configured acceleration, with margin.</summary>
        private static float MinimumWalk
        {
            get
            {
                var settings = MuseumExperienceConfiguration.Current;
                float ramp = Mathf.Min(Walk, settings.WalkSpeed / settings.Acceleration);
                return .8f * (settings.Acceleration * ramp * ramp * .5f + settings.WalkSpeed * (Walk - ramp));
            }
        }

        /// <summary>Movement integrates Time.deltaTime, so wait on game time rather than wall-clock time.</summary>
        private IEnumerator Simulate(float seconds)
        {
            simulatedFrames = 0;
            float start = Time.time;
            while (Time.time - start < seconds) { simulatedFrames++; yield return null; }
            simulatedSeconds = Time.time - start;
        }

        private string Diagnostics() => "frames=" + simulatedFrames + " simulated=" + simulatedSeconds
            + " maximumDeltaTime=" + Time.maximumDeltaTime + " timeScale=" + Time.timeScale;

        [UnityTest]
        public IEnumerator SelectedElementAllowsWalkingWhileReadingStopsLocomotion()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var root = new GameObject("MovementPlayTest");
            root.SetActive(false);
            var runtime = root.AddComponent<TourRuntime>();
            var interaction = root.AddComponent<MuseumInteraction>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            var pointObject = new GameObject("Point");
            pointObject.transform.SetParent(room.transform);
            pointObject.transform.position = Vector3.forward * 3;
            pointObject.AddComponent<PointAnchor>().Configure("point.test", 1.8f, pointObject.transform);
            var point = pointObject.AddComponent<TourPoint>();
            var visitor = new GameObject("Visitor");
            visitor.transform.SetParent(root.transform);
            var body = visitor.AddComponent<CharacterController>();
            body.height = 1.75f;
            body.center = Vector3.up * .875f;
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(visitor.transform);
            cameraObject.transform.localPosition = Vector3.up * 1.65f;
            var camera = cameraObject.AddComponent<Camera>();
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = actions.AddActionMap("Desktop");
            map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w");
            map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            map.AddAction("KeyboardLook", InputActionType.Value).AddCompositeBinding("2DVector").With("Up", "<Keyboard>/i");
            map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            map.AddAction("CapturePointer", InputActionType.Button, "<Mouse>/leftButton");
            visitor.AddComponent<DesktopVisitorController>().Configure(actions, camera, interaction);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.SetParent(root.transform);
            floor.transform.position = new Vector3(0, -.1f, 0);
            floor.transform.localScale = new Vector3(20, .2f, 20);
            root.SetActive(true);
            try
            {
                var contract = new TourContractV1 { schema_version = 1,
                    tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                    rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                        points = new[] { new PointContractV1 { key = "point.test", name = "Test", order = 0,
                            activation = new[] { "keyboard" }, elements = new[] { new ElementSummaryContractV1 { slug = "test", title = "Test" } } } } } } };
                Assert.That(runtime.Apply(contract, out var error), Is.True, error);
                var flow = root.AddComponent<MuseumVisitFlow>();
                var controller = visitor.GetComponent<DesktopVisitorController>();
                Press(keyboard.wKey);
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.EqualTo(0).Within(.01f), "The entry menu must block locomotion.");
                flow.EnterImmediately();
                interaction.Activate(point, ActivationSource.Keyboard);
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.GreaterThan(MinimumWalk), Diagnostics());
                interaction.OpenDetail();
                yield return null;
                float stopped = visitor.transform.position.z;
                Assert.That(controller.PlanarVelocity, Is.EqualTo(Vector3.zero), "Reading must stop at once, without gliding.");
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.EqualTo(stopped).Within(.01f));
                interaction.Back();
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.GreaterThan(stopped + MinimumWalk), Diagnostics());
                interaction.SetPaused(true);
                yield return null;
                float paused = visitor.transform.position.z;
                Assert.That(controller.PlanarVelocity, Is.EqualTo(Vector3.zero), "Pause must stop at once.");
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.EqualTo(paused).Within(.01f));
                interaction.SetPaused(false);
                var guidance = root.AddComponent<MuseumWayfinding>();
                guidance.Configure(visitor.transform);
                guidance.ToggleMenu();
                yield return null;
                float menuPosition = visitor.transform.position.z;
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.EqualTo(menuPosition).Within(.01f));
                guidance.CloseMenu();
                yield return Simulate(Walk);
                Assert.That(visitor.transform.position.z, Is.GreaterThan(menuPosition + MinimumWalk), Diagnostics());
                Release(keyboard.wKey);
            }
            finally { Object.Destroy(root); Object.Destroy(actions); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator MenusConsumeKeyboardWithoutActivatingPoints()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var root = new GameObject("MenuKeyboardPlayTest");
            root.SetActive(false);
            var runtime = root.AddComponent<TourRuntime>();
            var interaction = root.AddComponent<MuseumInteraction>();
            var input = root.AddComponent<DesktopPointInput>();
            var flow = root.AddComponent<MuseumVisitFlow>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            var pointObject = new GameObject("Point");
            pointObject.transform.SetParent(room.transform);
            pointObject.transform.position = Vector3.forward * 3;
            pointObject.AddComponent<PointAnchor>().Configure("point.test", 1.8f, pointObject.transform);
            pointObject.AddComponent<TourPoint>();
            var observer = new GameObject("Observer");
            observer.transform.SetParent(root.transform);
            var camera = new GameObject("Eye").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = Vector3.forward * 1.5f + Vector3.up * 2;
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up);
            observer.transform.position = Vector3.forward * 1.5f;
            input.Configure(observer.transform, camera);
            root.SetActive(true);
            try
            {
                var contract = new TourContractV1 { schema_version = 1,
                    tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                    rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                        points = new[] { new PointContractV1 { key = "point.test", name = "Test", order = 0,
                            activation = new[] { "keyboard", "proximity" },
                            elements = new[] { new ElementSummaryContractV1 { slug = "one", title = "One" },
                                new ElementSummaryContractV1 { slug = "two", title = "Two" } } } } } } };
                Assert.That(runtime.Apply(contract, out var error), Is.True, error);
                foreach (var phase in new[] { "menu", "pause" })
                {
                    if (phase == "pause") { flow.EnterImmediately(); interaction.SetPaused(true); }
                    foreach (var key in new[] { keyboard.tabKey, keyboard.enterKey, keyboard.eKey, keyboard.hKey, keyboard.backspaceKey })
                    {
                        PressAndRelease(key);
                        yield return null;
                    }
                    Assert.That(input.FocusedPoint, Is.Null, phase);
                    Assert.That(interaction.ActivePoint, Is.Null, phase);
                    Assert.That(root.GetComponent<MuseumWayfinding>(), Is.Null, phase);
                }
                Assert.That(interaction.State, Is.EqualTo(InteractionState.Paused), "Backspace on the pause menu must not leave the pause.");
                interaction.SetPaused(false);
                yield return null;
                PressAndRelease(keyboard.eKey);
                yield return null;
                Assert.That(interaction.ActivePoint, Is.Not.Null, "Control: the same key activates a nearby point while exploring.");
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
    }
}
