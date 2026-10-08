using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class TourRuntimeTests
    {
        private GameObject root;
        private TourRuntime runtime;
        private TourPoint point;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("RuntimeTest");
            runtime = root.AddComponent<TourRuntime>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            var location = new GameObject("Point");
            location.transform.SetParent(room.transform);
            location.AddComponent<PointAnchor>().Configure("point.test", 1.8f, location.transform);
            point = location.AddComponent<TourPoint>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        private static TourContractV1 Contract(string pointKey = "point.test", bool withContent = true)
        {
            return new TourContractV1 {
                schema_version = 1, tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                rooms = new[] { new RoomContractV1 { key = "room.test", name = "Room", order = 0,
                    points = new[] { new PointContractV1 { key = pointKey, name = "Point", order = 0,
                        activation = new[] { "keyboard", "proximity", "gaze" },
                        elements = withContent ? new[] { new ElementSummaryContractV1 { slug = "synthetic", title = "Test" } }
                            : new ElementSummaryContractV1[0] } } } }
            };
        }

        [Test]
        public void EmptyPointIsDeclaredButCannotInteract()
        {
            Assert.That(runtime.Apply(Contract(withContent: false), out var error), Is.True, error);
            Assert.That(point.State, Is.EqualTo(PointState.Empty));
            Assert.That(point.Supports("keyboard"), Is.False);
            Assert.That(runtime.OrderedPoints.Count, Is.EqualTo(1));
        }

        [Test]
        public void UnknownAnchorNeverCreatesObjects()
        {
            var count = root.GetComponentsInChildren<Transform>().Length;
            Assert.That(runtime.Apply(Contract("unknown"), out var error), Is.True, error);
            Assert.That(root.GetComponentsInChildren<Transform>().Length, Is.EqualTo(count));
            Assert.That(runtime.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(point.State, Is.EqualTo(PointState.Undeclared));
            Assert.That(runtime.FindPoint("unknown"), Is.Null);
        }

        [Test]
        public void InvalidRefreshClearsPreviousContent()
        {
            Assert.That(runtime.Apply(Contract(), out _), Is.True);
            Assert.That(point.HasContent, Is.True);
            var invalid = Contract();
            invalid.schema_version = 2;
            Assert.That(runtime.Apply(invalid, out _), Is.False);
            Assert.That(point.HasContent, Is.False);
            Assert.That(runtime.Contract, Is.Null);
        }

        [Test]
        public void PointInWrongRoomIsIgnored()
        {
            point.GetComponentInParent<RoomAnchor>().Configure("room.other");
            Assert.That(runtime.Apply(Contract(), out _), Is.True);
            Assert.That(runtime.Diagnostics.Count, Is.EqualTo(1));
            Assert.That(point.HasContent, Is.False);
        }
    }
}
