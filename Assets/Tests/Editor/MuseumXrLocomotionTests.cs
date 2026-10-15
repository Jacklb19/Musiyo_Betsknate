using System.Collections.Generic;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumXrLocomotionTests
    {
        [Test]
        public void ContinuousTurningIsSilentInsideTheDeadZoneAndGrowsSmoothlyPastIt()
        {
            const float deadzone = .2f, speed = 90, frame = 1f / 72;
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(.19f, 0), deadzone, speed, frame), Is.EqualTo(0));
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(.2f, 0), deadzone, speed, frame), Is.EqualTo(0));
            // No jump at the edge of the dead zone: just past it the rate is close to zero.
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(.21f, 0), deadzone, speed, frame), Is.GreaterThan(0).And.LessThan(.05f));
            float previous = 0;
            for (float x = .21f; x <= 1.0001f; x += .01f)
            {
                float degrees = MuseumTurning.ContinuousDegrees(new Vector2(x, 0), deadzone, speed, frame);
                Assert.That(degrees, Is.GreaterThanOrEqualTo(previous), "Turning rate never drops as the stick goes further.");
                Assert.That(degrees - previous, Is.LessThan(.05f), "No step in the turning rate.");
                previous = degrees;
            }
            Assert.That(MuseumTurning.ContinuousDegrees(Vector2.right, deadzone, speed, 1), Is.EqualTo(speed).Within(.001f));
            Assert.That(MuseumTurning.ContinuousDegrees(Vector2.left, deadzone, speed, 1), Is.EqualTo(-speed).Within(.001f));
            // Across frames of any length the total turn depends only on time.
            float sixty = 0, ninety = 0;
            for (int index = 0; index < 60; index++) sixty += MuseumTurning.ContinuousDegrees(Vector2.right * .6f, deadzone, speed, 1f / 60);
            for (int index = 0; index < 90; index++) ninety += MuseumTurning.ContinuousDegrees(Vector2.right * .6f, deadzone, speed, 1f / 90);
            Assert.That(sixty, Is.EqualTo(ninety).Within(.01f));
        }

        [Test]
        public void PushingTheRightStickUpOrDownScrollsWithoutTurning()
        {
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(.5f, .9f), .2f, 90, .1f), Is.EqualTo(0));
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(.9f, .5f), .2f, 90, .1f), Is.GreaterThan(0));
            Assert.That(MuseumTurning.ContinuousDegrees(new Vector2(float.NaN, 0), .2f, 90, .1f), Is.EqualTo(0));
            Assert.That(MuseumTurning.ContinuousDegrees(Vector2.right, .2f, 90, -1), Is.EqualTo(0));
        }

        [Test]
        public void StepTurningFiresOncePerPushAndOnlyAfterTheStickRested()
        {
            var gate = new SnapTurnGate();
            Assert.That(gate.Step(1, 30), Is.EqualTo(0), "A stick already pushed when turning becomes possible does not fire.");
            Assert.That(gate.Step(0, 30), Is.EqualTo(0));
            Assert.That(gate.Step(.8f, 30), Is.EqualTo(30));
            Assert.That(gate.Step(1, 30), Is.EqualTo(0), "Holding the stick does not repeat the step.");
            Assert.That(gate.Step(.5f, 30), Is.EqualTo(0));
            Assert.That(gate.Step(-.9f, 30), Is.EqualTo(0), "The stick must come back to rest first.");
            Assert.That(gate.Step(.1f, 30), Is.EqualTo(0));
            Assert.That(gate.Step(-.9f, 30), Is.EqualTo(-30));
        }

        [Test]
        public void AMenuIsPlacedOnceTheHeadIsTrackedAndSteadyNotWhileItTurns()
        {
            const float settle = .25f, steadySpeed = 40, timeout = 1.5f, frame = 1f / 72;
            var gate = new MenuPlacementGate();
            Assert.That(gate.Step(true, 0, frame, settle, steadySpeed, timeout), Is.False, "Nothing was requested.");
            gate.Request();
            for (int index = 0; index < 200; index++)
                Assert.That(gate.Step(false, 0, frame, settle, steadySpeed, timeout), Is.False, "Untracked frames never place the menu.");
            float yaw = 90;
            bool placed = false;
            float elapsed = 0;
            // The visitor is still turning their head: the menu waits.
            for (int index = 0; index < 10; index++)
            {
                yaw += 2;
                placed |= gate.Step(true, yaw, frame, settle, steadySpeed, timeout);
                elapsed += frame;
            }
            Assert.That(placed, Is.False);
            while (!placed && elapsed < 2)
            {
                placed = gate.Step(true, yaw, frame, settle, steadySpeed, timeout);
                elapsed += frame;
            }
            Assert.That(placed, Is.True);
            Assert.That(gate.Pending, Is.False);
            Assert.That(gate.Step(true, yaw + 90, frame, settle, steadySpeed, timeout), Is.False, "It is placed once; reading it never drags it.");
        }

        [Test]
        public void AHeadThatNeverHoldsStillStillGetsItsMenu()
        {
            var gate = new MenuPlacementGate();
            gate.Request();
            float yaw = 0, elapsed = 0;
            bool placed = false;
            while (!placed && elapsed < 3)
            {
                yaw += 3;
                placed = gate.Step(true, yaw, 1f / 72, .25f, 40, 1.5f);
                elapsed += 1f / 72;
            }
            Assert.That(placed, Is.True);
            Assert.That(elapsed, Is.EqualTo(1.5f).Within(.05f));
        }

        [Test]
        public void ShippedHeadsetSettingsTurnContinuouslyAndOfferStepsAsAnOption()
        {
            var settings = MuseumXrConfiguration.Load();
            Assert.That(settings.DefaultTurn, Is.EqualTo(MuseumTurnMode.Continuous), "The owner's preference is continuous turning.");
            Assert.That(settings.ContinuousTurnSpeeds, Does.Contain(settings.DefaultContinuousTurnSpeed));
            Assert.That(settings.TurnSpeed(9999), Is.EqualTo(settings.DefaultContinuousTurnSpeed));
            float speed = settings.DefaultContinuousTurnSpeed;
            var seen = new HashSet<float>();
            for (int index = 0; index < settings.ContinuousTurnSpeeds.Length; index++) { seen.Add(speed); speed = settings.NextTurnSpeed(speed); }
            Assert.That(seen.Count, Is.EqualTo(settings.ContinuousTurnSpeeds.Length), "Cycling visits every offered speed.");
            Assert.That(settings.RunSpeed, Is.GreaterThan(settings.WalkSpeed));
            Assert.That(MuseumTurning.ParseMode("snap"), Is.EqualTo(MuseumTurnMode.Snap));
            Assert.That(MuseumTurning.ParseMode("spin"), Is.Null);
        }

        [TestCase("schema_version", "1")]
        [TestCase("default_turn_mode", "\"spin\"")]
        [TestCase("turn_deadzone", "0.9")]
        [TestCase("default_continuous_turn_speed", "33")]
        [TestCase("run_speed", "0.5")]
        [TestCase("menu_placement_timeout", "0")]
        [TestCase("grab_max_distance", "9")]
        public void InvalidHeadsetSettingsFail(string property, string value)
        {
            var json = Resources.Load<TextAsset>("MuseumXrConfiguration").text;
            var pattern = "\"" + property + "\"\\s*:\\s*(\"[^\"]*\"|[^,}\\s]+)";
            Assert.That(Regex.IsMatch(json, pattern), Is.True);
            var invalid = Regex.Replace(json, pattern, "\"" + property + "\": " + value);
            Assert.Throws<System.InvalidOperationException>(() => MuseumXrConfiguration.Parse(invalid));
        }

        [Test]
        public void RunningIsOnlyOutdoorsOnTheDeliveredGroundAndOpenAirRooms()
        {
            var root = new GameObject("OutdoorsTest");
            try
            {
                var environment = new GameObject("Entorno");
                environment.transform.SetParent(root.transform);
                var terrain = GameObject.CreatePrimitive(PrimitiveType.Cube);
                terrain.name = "Terreno_Valle_Provisional";
                terrain.transform.SetParent(environment.transform);
                terrain.transform.position = new Vector3(0, -.1f, 0);
                terrain.transform.localScale = new Vector3(100, .2f, 100);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Personajes_Piso_Patio";
                floor.transform.SetParent(root.transform);
                floor.transform.position = new Vector3(20, .05f, 0);
                floor.transform.localScale = new Vector3(6, .1f, 6);
                var room = new GameObject("Characters").AddComponent<RoomAnchor>();
                room.transform.SetParent(root.transform);
                room.transform.position = new Vector3(20, 0, 0);
                room.Configure("room.characters");
                room.ConfigureBounds(new Vector2(5, 5));
                var arrival = new GameObject("Arrival").AddComponent<RoomAnchor>();
                arrival.transform.SetParent(root.transform);
                arrival.transform.position = new Vector3(-20, 0, 0);
                arrival.Configure("room.arrival");
                arrival.ConfigureBounds(new Vector2(2, 10));
                Physics.SyncTransforms();
                var rooms = new[] { room, arrival };
                var openAir = new HashSet<string> { "room.arrival" };
                var ground = new HashSet<string> { "Terreno_Valle_Provisional", "Sendero_Jajan" };
                Assert.That(MuseumOutdoors.Contains(new Vector3(0, 0, 0), rooms, openAir, ground, null), Is.True, "Open valley.");
                Assert.That(MuseumOutdoors.Contains(new Vector3(20, .1f, 0), rooms, openAir, ground, null), Is.False, "Museum floor.");
                Assert.That(MuseumOutdoors.Contains(new Vector3(16, 0, 4), rooms, openAir, ground, null), Is.False,
                    "Inside a museum room even where the floor collider ends.");
                Assert.That(MuseumOutdoors.Contains(new Vector3(-20, 0, 5), rooms, openAir, ground, null), Is.True, "The arrival path is open air.");
                Assert.That(MuseumOutdoors.Contains(new Vector3(0, 30, 0), rooms, openAir, ground, null), Is.False, "No ground below.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
