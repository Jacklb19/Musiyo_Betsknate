using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Arrival card, guide presence, and end of the suggested route; none of them carries cultural content.</summary>
    public sealed class MuseumStationsTests
    {
        private GameObject root;
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform visitor;
        private RoomAnchor arrivalRoom;
        private RoomAnchor hearthRoom;
        private RoomAnchor overlookRoom;
        private TourPoint guidePoint;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("StationsTest");
            root.SetActive(false);
            runtime = root.AddComponent<TourRuntime>();
            interaction = root.AddComponent<MuseumInteraction>();
            arrivalRoom = CreateRoom("room.arrival", new Vector3(0, 0, -43), new Vector2(1.25f, 17));
            hearthRoom = CreateRoom("room.hearth", new Vector3(0, 0, 18.5f), new Vector2(4, 4));
            overlookRoom = CreateRoom("room.overlook", new Vector3(-11.5f, 0, 18.5f), new Vector2(5.5f, 2));
            CreatePoint(arrivalRoom.transform, "zona.llegada.tutorial", new Vector3(0, 0, -40), 2.5f);
            guidePoint = CreatePoint(hearthRoom.transform, "guia.principal", new Vector3(1, 0, 20.3f), 2.5f);
            CreatePoint(overlookRoom.transform, "punto.mirador.creditos", new Vector3(-11.5f, 0, 18.5f), 2);
            visitor = new GameObject("Visitor").transform;
            visitor.SetParent(root.transform);
            visitor.position = new Vector3(0, 1, -58);
            root.SetActive(true);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        private RoomAnchor CreateRoom(string key, Vector3 center, Vector2 halfSize)
        {
            var room = new GameObject(key);
            room.transform.SetParent(root.transform);
            room.transform.position = center;
            var anchor = room.AddComponent<RoomAnchor>();
            anchor.Configure(key);
            anchor.ConfigureBounds(halfSize);
            return anchor;
        }

        private static TourPoint CreatePoint(Transform room, string key, Vector3 position, float radius)
        {
            var location = new GameObject(key);
            location.transform.SetParent(room);
            location.transform.position = position;
            location.AddComponent<PointAnchor>().Configure(key, radius, location.transform);
            return location.AddComponent<TourPoint>();
        }

        private static TourContractV1 Contract(bool withGuide = true, bool available = false)
        {
            PointContractV1 Point(string key) => new PointContractV1 { key = key, name = "Punto", order = 0,
                activation = new[] { "keyboard", "proximity", "gaze" }, elements = new ElementSummaryContractV1[0] };
            return new TourContractV1
            {
                schema_version = 1,
                tour = new TourMetadataContractV1 { key = "museum-main", name = "Museo de prueba" },
                rooms = new[]
                {
                    new RoomContractV1 { key = "room.arrival", name = "Llegada", order = 0, points = new[] { Point("zona.llegada.tutorial") } },
                    new RoomContractV1 { key = "room.hearth", name = "Fogón", order = 1, points = new[] { Point("guia.principal") } },
                    new RoomContractV1 { key = "room.overlook", name = "Mirador", order = 2, points = new[] { Point("punto.mirador.creditos") } }
                },
                guide = withGuide ? new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = available } : null
            };
        }

        [Test]
        public void RuntimeBindsThePublishedGuideToItsAnchorAndClearsIt()
        {
            Assert.That(runtime.Apply(Contract(), out var error), Is.True, error);
            Assert.That(runtime.GuidePoint, Is.SameAs(guidePoint));
            Assert.That(guidePoint.IsGuide, Is.True);
            Assert.That(guidePoint.HasContent, Is.False, "The guide is not a cultural element list.");
            Assert.That(runtime.FindAnchor("zona.llegada.tutorial"), Is.Not.Null);
            Assert.That(runtime.RoomAt(new Vector3(0, 1, 18.5f)).key, Is.EqualTo("room.hearth"));
            Assert.That(runtime.RoomAt(new Vector3(0, 1, -13)), Is.Null);
            runtime.Clear();
            Assert.That(runtime.GuidePoint, Is.Null);
            Assert.That(guidePoint.IsGuide, Is.False);
        }

        [Test]
        public void UnknownGuideAnchorIsDiagnosedWithoutCreatingObjects()
        {
            var contract = Contract();
            contract.guide.key = "guia.desconocida";
            var count = root.GetComponentsInChildren<Transform>().Length;
            Assert.That(runtime.Apply(contract, out _), Is.True);
            Assert.That(runtime.GuidePoint, Is.Null);
            Assert.That(runtime.Diagnostics, Does.Contain("Unknown guide anchor: guia.desconocida"));
            Assert.That(root.GetComponentsInChildren<Transform>().Length, Is.EqualTo(count));
        }

        [Test]
        public void ArrivalCardShowsAtSpawnUntilDismissedOrLeftBehind()
        {
            var arrival = root.AddComponent<MuseumArrival>();
            arrival.Configure(visitor);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.False, "No card before the tour is available.");
            Assert.That(runtime.Apply(Contract(), out var error), Is.True, error);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.True);
            Assert.That(arrival.Title, Is.EqualTo("Museo de prueba"));
            Assert.That(arrival.Route, Is.EqualTo("Llegada · Fogón · Mirador"));
            Assert.That(arrival.Body, Does.Contain("30 minutos"));
            interaction.SetPaused(true);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.False);
            interaction.SetPaused(false);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.True);
            arrival.Dismiss();
            Assert.That(arrival.Visible, Is.False);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.False, "Dismissing is remembered while the visitor stays.");
            Assert.That(runtime.Apply(Contract(), out error), Is.True, error);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.True, "A reloaded tour shows the card again.");
            visitor.position = new Vector3(0, 1, -20);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.False);
            visitor.position = new Vector3(0, 1, -58);
            arrival.Refresh();
            Assert.That(arrival.Visible, Is.False, "Walking out of the arrival area dismisses the card.");
        }

        [Test]
        public void GuideAnswersOnlyInRangeAndStatesItsLimitation()
        {
            var guide = root.AddComponent<MuseumGuide>();
            guide.Configure(visitor);
            Assert.That(guide.Present, Is.False);
            Assert.That(runtime.Apply(Contract(available: false), out var error), Is.True, error);
            Assert.That(guide.Present, Is.True);
            guide.Refresh();
            Assert.That(guide.InRange, Is.False);
            Assert.That(guide.Prompt, Is.Empty);
            Assert.That(guide.Ask(), Is.False);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
            visitor.position = new Vector3(0, 1, 19);
            guide.Refresh();
            Assert.That(guide.InRange, Is.True);
            Assert.That(guide.Prompt, Does.Contain("G"));
            Assert.That(guide.Message, Does.Contain("no está disponible"));
            Assert.That(guide.Ask(), Is.True);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion));
            Assert.That(interaction.BlocksMovement, Is.True);
            Assert.That(guide.Open, Is.True);
            Assert.That(guide.Prompt, Is.Empty, "The prompt disappears while the conversation panel is open.");
            Assert.That(guide.Ask(), Is.False, "Opening twice is not possible.");
            interaction.SetPaused(true);
            interaction.SetPaused(false);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.GuideQuestion));
            guide.Close();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
            Assert.That(runtime.Apply(Contract(available: true), out error), Is.True, error);
            Assert.That(guide.Message, Does.Contain("todavía no puede enviar preguntas"));
            Assert.That(runtime.Apply(Contract(withGuide: false), out error), Is.True, error);
            Assert.That(guide.Present, Is.False);
            Assert.That(interaction.OpenGuide(), Is.False, "Without a published guide there is nobody to ask.");
        }

        [Test]
        public void FarewellRecognisesTheLastRoomOfTheSuggestedRoute()
        {
            var farewell = root.AddComponent<MuseumFarewell>();
            farewell.Configure(visitor);
            Assert.That(runtime.Apply(Contract(), out var error), Is.True, error);
            farewell.Refresh();
            Assert.That(farewell.AtEnd, Is.False);
            Assert.That(farewell.Message, Is.Empty);
            visitor.position = new Vector3(-11.5f, 1, 18.5f);
            farewell.Refresh();
            Assert.That(farewell.AtEnd, Is.True);
            Assert.That(farewell.RoomName, Is.EqualTo("Mirador"));
            Assert.That(farewell.Message, Does.Contain("Mirador"));
            Assert.That(farewell.CanReturn, Is.False, "Outside the Web build nothing can navigate the page.");
            farewell.ReturnToCatalog();
            runtime.Clear();
            Assert.That(farewell.AtEnd, Is.False);
        }
    }
}
