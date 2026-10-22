using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The steady light that marks the points and the frame log of the visit, without the museum scene.</summary>
    public sealed class ExhibitsPresentTests
    {
        [Test]
        public void ThePointLightIsSteadyAndGivesWayAtThePoint()
        {
            Assert.That(MuseumPointGlow.Target(PointState.Undeclared, false, false), Is.EqualTo(0));
            Assert.That(MuseumPointGlow.Target(PointState.Empty, false, false), Is.EqualTo(0), "A point with nothing published is not lit.");
            Assert.That(MuseumPointGlow.Target(PointState.Available, false, false), Is.EqualTo(MuseumPointGlow.WaitingAlpha));
            Assert.That(MuseumPointGlow.Target(PointState.Focused, false, false), Is.EqualTo(MuseumPointGlow.WaitingAlpha),
                "Looking at a point does not change its light: nothing blinks while the visitor looks around.");
            Assert.That(MuseumPointGlow.Target(PointState.Available, false, true), Is.EqualTo(MuseumPointGlow.VisitedAlpha));
            Assert.That(MuseumPointGlow.Target(PointState.Active, true, true), Is.EqualTo(MuseumPointGlow.PresentAlpha));
            Assert.That(MuseumPointGlow.VisitedAlpha, Is.LessThan(MuseumPointGlow.WaitingAlpha));
        }

        [Test]
        public void TheLightLiesBeforeTheStandOrTowardTheMiddleOfTheRoom()
        {
            var root = new GameObject("GlowTest");
            try
            {
                var room = new GameObject("Room");
                room.transform.SetParent(root.transform);
                room.transform.position = new Vector3(10, 0, 0);
                room.AddComponent<RoomAnchor>().Configure("room.test");
                var location = new GameObject("Point");
                location.transform.SetParent(room.transform);
                location.transform.position = new Vector3(4, 0, 0);
                location.AddComponent<PointAnchor>().Configure("point", 1.8f, null);
                var point = location.AddComponent<TourPoint>();
                var spot = MuseumPointGlow.Spot(point, null);
                Assert.That(spot.x, Is.GreaterThan(4).And.LessThan(5.1f), "Without a stand it lies on the room's side of the point.");
                Assert.That(spot.z, Is.EqualTo(0).Within(.001f));

                var standObject = new GameObject("Lectern");
                standObject.transform.SetParent(root.transform);
                standObject.transform.position = new Vector3(4, 0, 1);
                var board = new GameObject("Board");
                board.transform.SetParent(standObject.transform);
                board.transform.SetPositionAndRotation(new Vector3(4, .8f, 1), Quaternion.Euler(25, 0, 0));
                var stand = standObject.AddComponent<PointLectern>();
                stand.Configure(point.Anchor, board.transform, new Vector2(.58f, .74f));
                Assert.That(Vector3.Distance(MuseumPointGlow.Spot(point, stand), stand.ViewingSpot(.85f)), Is.LessThan(.001f),
                    "By a stand it lies where the visitor stands to read.");

                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(location.transform);
                point.Configure(sphere.GetComponent<Renderer>());
                point.HideIndicator();
                Assert.That(sphere.activeSelf, Is.False, "The old state sphere is no longer drawn.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheFrameLogSaysHowSmoothlyTheMuseumDraws()
        {
            var frames = new List<float>();
            for (int index = 0; index < 99; index++) frames.Add(1 / 72f);
            frames.Add(.05f);
            string line = MuseumFrameLog.Summary(frames, "room.characters", 3);
            StringAssert.StartsWith(MuseumFrameLog.LogTag, line);
            StringAssert.Contains("room=room.characters", line);
            StringAssert.Contains("residents=3", line);
            StringAssert.Contains("low1%=20.0", line);
            StringAssert.Contains("worst_ms=50.0", line);
            StringAssert.Contains("frames=100", line);
            Assert.That(MuseumFrameLog.Summary(new List<float>(), "-", 0), Does.Contain("no frames"));
        }
    }
}
