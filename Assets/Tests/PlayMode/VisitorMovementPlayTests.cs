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
                interaction.Activate(point, ActivationSource.Keyboard);
                Press(keyboard.wKey);
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(visitor.transform.position.z, Is.GreaterThan(.1f));
                interaction.OpenDetail();
                float stopped = visitor.transform.position.z;
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(visitor.transform.position.z, Is.EqualTo(stopped).Within(.01f));
                interaction.Back();
                yield return new WaitForSecondsRealtime(.25f);
                Assert.That(visitor.transform.position.z, Is.GreaterThan(stopped + .1f));
                Release(keyboard.wKey);
            }
            finally { Object.Destroy(root); Object.Destroy(actions); }
            yield return null;
        }
    }
}
