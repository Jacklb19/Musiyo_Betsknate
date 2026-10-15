using System.Reflection;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumTourAndServiceTests
    {
        private GameObject root;
        private TourRuntime runtime;

        [SetUp]
        public void Build()
        {
            MuseumInput.Replace(MuseumInput.CreateDefault());
            root = new GameObject("TourAndService");
            root.SetActive(false);
            runtime = root.AddComponent<TourRuntime>();
            root.AddComponent<MuseumInteraction>();
            var loader = root.AddComponent<TourLoader>();
            typeof(TourLoader).GetField("loadOnStart", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, false);
            foreach (var (room, x) in new[] { ("room.welcome", 0f), ("room.characters", 20f) })
            {
                var anchor = new GameObject(room).AddComponent<RoomAnchor>();
                anchor.transform.SetParent(root.transform);
                anchor.transform.position = new Vector3(x, 0, 0);
                anchor.Configure(room);
                anchor.ConfigureBounds(new Vector2(5, 5));
                for (int index = 0; index < 2; index++)
                {
                    var point = new GameObject(room + index);
                    point.transform.SetParent(anchor.transform);
                    point.transform.position = new Vector3(x + index * 3, 0, 0);
                    point.AddComponent<PointAnchor>().Configure(room.Replace("room", "point") + "." + index, 1.5f, point.transform);
                    point.AddComponent<TourPoint>();
                }
            }
            // Rooms are found from their points only among active objects, as in a running visit.
            root.SetActive(true);
        }

        [TearDown]
        public void Release()
        {
            Object.DestroyImmediate(root);
            MuseumInput.Replace(null);
        }

        private static ElementSummaryContractV1[] Elements(string slug)
            => new[] { new ElementSummaryContractV1 { slug = slug, title = slug } };

        private void Apply()
        {
            var contract = new TourContractV1 { schema_version = 1,
                tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    new RoomContractV1 { key = "room.welcome", name = "Welcome", order = 0, points = new[]
                    {
                        new PointContractV1 { key = "point.welcome.0", name = "A", order = 0, activation = new[] { "proximity" }, elements = Elements("a") },
                        new PointContractV1 { key = "point.welcome.1", name = "B", order = 1, activation = new[] { "keyboard" }, elements = Elements("b") },
                    } },
                    new RoomContractV1 { key = "room.characters", name = "Characters", order = 1, points = new[]
                    {
                        new PointContractV1 { key = "point.characters.0", name = "C", order = 0, activation = new[] { "proximity" }, elements = Elements("c") },
                        new PointContractV1 { key = "point.characters.1", name = "D", order = 1, activation = new[] { "proximity" },
                            elements = new ElementSummaryContractV1[0] },
                    } },
                } };
            Assert.That(runtime.Apply(contract, out var error), Is.True, error);
        }

        [Test]
        public void TheAutomaticTourVisitsPublishedPointsInTourOrderFromTheVisitorsRoom()
        {
            Apply();
            var stops = MuseumAutoTour.PlanStops(runtime.OrderedPoints);
            Assert.That(stops.ConvertAll(point => point.Anchor.Key), Is.EqualTo(new[] { "point.welcome.0", "point.characters.0" }),
                "Points without content or that do not open by approach are not stops.");
            var order = new[] { "room.welcome", "room.characters" };
            Assert.That(MuseumAutoTour.FirstStop(stops, order, null), Is.EqualTo(0), "Between rooms it starts at the beginning.");
            Assert.That(MuseumAutoTour.FirstStop(stops, order, "room.characters"), Is.EqualTo(1), "It continues from the visitor's room.");
            Assert.That(MuseumAutoTour.FirstStop(stops, order, "room.unknown"), Is.EqualTo(0));
        }

        [Test]
        public void WithoutTheServiceThePointWhereTheVisitorStandsSaysSoAndNothingElse()
        {
            var loader = root.GetComponent<TourLoader>();
            var notice = root.AddComponent<MuseumServiceNotice>();
            var change = typeof(TourLoader).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);
            change.Invoke(loader, new object[] { TourLoadState.Unavailable, "Unavailable fixture" });
            notice.Step(new Vector3(10, 0, 0));
            Assert.That(notice.Point, Is.Null, "Away from every point nothing is shown.");
            notice.Step(new Vector3(.5f, 0, 0));
            Assert.That(notice.Point?.Anchor.Key, Is.EqualTo("point.welcome.0"));
            notice.Step(new Vector3(2.5f, 0, 0));
            Assert.That(notice.Point?.Anchor.Key, Is.EqualTo("point.welcome.0"), "The leave margin keeps the notice steady between close points.");
            notice.Step(new Vector3(10, 0, 0));
            Assert.That(notice.Point, Is.Null);
            notice.Step(new Vector3(20, 0, 0));
            Assert.That(notice.Point, Is.Not.Null);
            // Once the tour is there, points speak for themselves and the notice disappears.
            Apply();
            change.Invoke(loader, new object[] { TourLoadState.Ready, "Ready fixture" });
            notice.Step(new Vector3(20, 0, 0));
            Assert.That(notice.Point, Is.Null);
        }
    }
}
