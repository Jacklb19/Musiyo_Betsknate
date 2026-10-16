using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumTeleportTests
    {
        private GameObject visitor;
        private GameObject floor;
        private GameObject pedestal;
        private MuseumTeleport teleport;

        [SetUp]
        public void SetUp()
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(40, .2f, 40);
            floor.transform.position = new Vector3(0, -.1f, 0);
            pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.transform.localScale = new Vector3(.8f, 1, .8f);
            pedestal.transform.position = new Vector3(0, .5f, 4);
            visitor = new GameObject("Visitor");
            var body = visitor.AddComponent<CharacterController>();
            body.height = 1.8f; body.radius = .25f; body.center = Vector3.up * .9f;
            var pointer = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            teleport = new MuseumTeleport(visitor.transform, body, pointer, Color.cyan, Color.red);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(visitor);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(pedestal);
        }

        [Test]
        public void OnlyLevelFloorNearTheVisitorsHeightAndWithinReachIsAccepted()
        {
            var feet = Vector3.zero;
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(3, 0, 2), Vector3.up), Is.True);
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(3, .3f, 2), Vector3.up), Is.True, "A low step is fine.");
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(0, 1, 4), Vector3.up), Is.False, "Pedestal tops are too high.");
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(0, -1, 4), Vector3.up), Is.False, "No dropping off a ledge.");
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(2, 0, 0), Vector3.forward), Is.False, "Walls are not floor.");
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(2, 0, 0), new Vector3(0, .5f, .87f)), Is.False, "Steep slopes are not floor.");
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(MuseumTeleport.MaxDistance + .1f, 0, 0), Vector3.up), Is.False);
            Assert.That(MuseumTeleport.Acceptable(feet, new Vector3(float.NaN, 0, 0), Vector3.up), Is.False);
        }

        [Test]
        public void TheArcFallsUnderGravityFromTheController()
        {
            var origin = new Vector3(0, 1.2f, 0);
            var velocity = new Vector3(0, 0, 5);
            Assert.That(MuseumTeleport.ArcPoint(origin, velocity, 0), Is.EqualTo(origin));
            var later = MuseumTeleport.ArcPoint(origin, velocity, .5f);
            Assert.That(later.z, Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(later.y, Is.LessThan(origin.y));
        }

        [Test]
        public void AimingAtTheFloorJumpsThereAndAimingAtAPedestalDoesNot()
        {
            var feet = Vector3.zero;
            var hand = new Vector3(.2f, 1.2f, 0);
            // Slightly down and to the side, away from the pedestal: lands on open floor a few metres ahead.
            teleport.Aim(hand, new Vector3(.6f, -.25f, .4f), feet, Vector3.forward);
            Assert.That(teleport.Aiming, Is.True);
            Assert.That(teleport.Valid, Is.True);
            var landing = teleport.Landing;
            Assert.That(landing.y, Is.EqualTo(0).Within(.01f));
            Assert.That(teleport.Jump(feet), Is.True);
            Assert.That(visitor.transform.position.x, Is.EqualTo(landing.x).Within(1e-3f));
            Assert.That(visitor.transform.position.z, Is.EqualTo(landing.z).Within(1e-3f));
            Assert.That(teleport.Aiming, Is.False);

            visitor.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            // Slightly up toward the pedestal four metres ahead: the arc meets its side, which is not floor.
            teleport.Aim(new Vector3(0, 1.2f, 0), Quaternion.Euler(-10, 0, 0) * Vector3.forward, feet, Vector3.forward);
            Assert.That(teleport.Valid, Is.False);
            Assert.That(teleport.Jump(feet), Is.False);
            Assert.That(visitor.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void TheHeadsetBindsTheJumpToTheRightAButton()
        {
            var input = MuseumInput.CreateDefault();
            try
            {
                Assert.That(input.Teleport.bindings.Select(binding => binding.path),
                    Does.Contain("<OculusTouchController>{RightHand}/primaryButton"));
                Assert.That(input.Asset.FindActionMap("Locomotion").FindAction("Teleport"), Is.SameAs(input.Teleport));
            }
            finally { input.Dispose(); }
        }
    }
}
