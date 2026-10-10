using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumLecternTests
    {
        private GameObject root;
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform eye;
        private TourPoint withStand;
        private TourPoint withoutStand;
        private PointLectern stand;
        private RectTransform screenParent;
        private RectTransform panel;
        private MuseumLecternDisplay display;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("LecternTest");
            runtime = root.AddComponent<TourRuntime>();
            interaction = root.AddComponent<MuseumInteraction>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            withStand = CreatePoint(room.transform, "first", new Vector3(4, 0, 2));
            withoutStand = CreatePoint(room.transform, "second", new Vector3(-4, 0, 2));
            var elements = new[] { new ElementSummaryContractV1 { slug = "one", title = "One" },
                new ElementSummaryContractV1 { slug = "two", title = "Two" } };
            var contract = new TourContractV1 { schema_version = 1,
                tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                    points = new[] {
                        new PointContractV1 { key = "first", name = "First", order = 0, activation = new[] { "keyboard", "proximity" }, elements = elements },
                        new PointContractV1 { key = "second", name = "Second", order = 1, activation = new[] { "keyboard", "proximity" }, elements = elements }
                    } } }
            };
            Assert.That(runtime.Apply(contract, out var error), Is.True, error);

            var standObject = new GameObject("Lectern");
            standObject.transform.SetParent(root.transform);
            standObject.transform.position = new Vector3(5, 0, 1);
            var board = new GameObject("Board");
            board.transform.SetParent(standObject.transform);
            // The board faces south (toward -Z) and leans back like a lectern.
            board.transform.SetPositionAndRotation(new Vector3(5, .8f, 1), Quaternion.Euler(25, 0, 0));
            stand = standObject.AddComponent<PointLectern>();
            stand.Configure(withStand.Anchor, board.transform, new Vector2(.58f, .74f));

            screenParent = new GameObject("Screen", typeof(RectTransform)).GetComponent<RectTransform>();
            screenParent.SetParent(root.transform);
            panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(screenParent, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.one;
            panel.anchoredPosition = new Vector2(-24, -124);
            panel.sizeDelta = new Vector2(540, 740);
            panel.gameObject.SetActive(false);
            // The visitor starts on the reading side of the board, two metres from the stand.
            eye = new GameObject("Eye", typeof(Camera)).transform;
            eye.SetParent(root.transform);
            eye.position = new Vector3(5, 1.65f, -1);
            display = root.AddComponent<MuseumLecternDisplay>();
            display.Configure(panel, eye.GetComponent<Camera>());
        }

        private static TourPoint CreatePoint(Transform room, string key, Vector3 position)
        {
            var location = new GameObject(key);
            location.transform.SetParent(room);
            location.transform.position = position;
            var target = new GameObject("LookTarget");
            target.transform.SetParent(location.transform, false);
            target.transform.localPosition = Vector3.up * 1.6f;
            location.AddComponent<PointAnchor>().Configure(key, 1.8f, target.transform);
            return location.AddComponent<TourPoint>();
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void ThePanelUnfoldsInFrontOfTheStandOfItsPointAndFoldsAway()
        {
            var settings = MuseumExperienceConfiguration.Current;
            float seconds = settings.LecternUnfoldSeconds;
            display.Show(withStand);
            Assert.That(display.Host, Is.SameAs(stand));
            Assert.That(panel.parent, Is.SameAs(display.Surface), "The panel is carried by the screen of the stand.");
            Assert.That(display.Surface.gameObject.activeSelf, Is.False, "It starts folded away.");
            display.Advance(seconds * .5f);
            Assert.That(display.Openness, Is.EqualTo(.5f).Within(.001f));
            Assert.That(display.Surface.gameObject.activeSelf, Is.True);
            Assert.That(display.Surface.localScale.y, Is.LessThan(display.Surface.localScale.x), "It rises instead of popping in.");
            display.Advance(seconds);
            Assert.That(display.Openness, Is.EqualTo(1));
            float width = display.Surface.sizeDelta.x * display.Surface.localScale.x;
            Assert.That(width, Is.EqualTo(settings.LecternScreenWidth).Within(.001f));
            Assert.That(Vector3.Angle(display.Surface.up, Vector3.up), Is.LessThan(.01f), "The floating screen is upright.");
            Assert.That(Vector3.Angle(display.Surface.forward, Vector3.forward), Is.LessThan(.01f), "It is read from the visitor's side.");
            var expected = stand.BoardCentre + Vector3.back * settings.LecternFloatDistance;
            expected.y = stand.transform.position.y + settings.LecternFloatHeight;
            Assert.That(Vector3.Distance(display.Surface.position, expected), Is.LessThan(.001f), "It floats in front of the stand.");

            display.Hide();
            Assert.That(panel.gameObject.activeSelf, Is.True, "The folding screen keeps its content until it has closed.");
            display.Advance(seconds * 2);
            Assert.That(display.Openness, Is.EqualTo(0));
            Assert.That(display.Surface.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TheFloatingScreenKeepsTurningTowardTheVisitor()
        {
            var settings = MuseumExperienceConfiguration.Current;
            display.Show(withStand);
            display.Advance(1);
            foreach (var position in new[] { new Vector3(8, 1.65f, 1), new Vector3(5, 1.65f, 4), new Vector3(3, 1.65f, -1) })
            {
                eye.position = position;
                display.Advance(settings.LecternFollowSeconds * .5f);
                var toVisitor = position - display.Surface.position;
                toVisitor.y = 0;
                Assert.That(Vector3.Angle(-display.Surface.forward, toVisitor), Is.GreaterThan(1), "It turns smoothly, not in one frame.");
                display.Advance(settings.LecternFollowSeconds * 40);
                var axis = stand.BoardCentre;
                var fromStand = position - axis;
                fromStand.y = 0;
                Assert.That(Vector3.Angle(-display.Surface.forward, fromStand), Is.LessThan(.5f), "The screen faces the visitor.");
                var offset = display.Surface.position - axis;
                offset.y = 0;
                Assert.That(offset.magnitude, Is.EqualTo(settings.LecternFloatDistance).Within(.001f));
                Assert.That(Vector3.Angle(offset, fromStand), Is.LessThan(.5f), "It stays on the visitor's side of the stand.");
                Assert.That(Vector3.Angle(display.Surface.up, Vector3.up), Is.LessThan(.01f));
            }
            eye.position = stand.BoardCentre + Vector3.up;
            var before = display.Surface.rotation;
            display.Advance(1);
            Assert.That(Quaternion.Angle(display.Surface.rotation, before), Is.LessThan(.01f), "Right above the stand it keeps its heading.");
        }

        [Test]
        public void ADetailByAStandLeavesTheVisitorFreeToWalk()
        {
            Assert.That(interaction.Activate(withStand, ActivationSource.Proximity), Is.True);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.PointFocus));
            Assert.That(interaction.DetailInWorld, Is.True);
            Assert.That(interaction.HoldsVisitor, Is.False, "Choosing an element by the stand does not stop the visitor.");
            Assert.That(interaction.SelectElement(1), Is.True);
            Assert.That(interaction.OpenDetail(), Is.True);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Reading));
            Assert.That(interaction.BlocksMovement, Is.False, "The complete text floats in the museum, so it is read while walking.");
            interaction.SetPaused(true);
            Assert.That(interaction.BlocksMovement, Is.True);
            interaction.SetPaused(false);

            Assert.That(interaction.Activate(withoutStand, ActivationSource.Proximity), Is.True);
            Assert.That(interaction.DetailInWorld, Is.False);
            Assert.That(interaction.HoldsVisitor, Is.True, "A list on the screen holds the visitor until an element is chosen.");
            interaction.SelectElement(0);
            interaction.OpenDetail();
            Assert.That(interaction.BlocksMovement, Is.True, "A complete text on the screen stops the visitor.");
        }

        [Test]
        public void StandingByTheStandIsBeingAtThePoint()
        {
            float radius = withStand.Anchor.ActivationRadius;
            // In front of the stand, out of reach of the exhibit itself.
            var byTheStand = stand.transform.position + new Vector3(1.2f, 0, -.6f);
            Assert.That(Vector3.Distance(byTheStand, withStand.Anchor.transform.position), Is.GreaterThan(radius));
            Assert.That(PointActivation.IsInRange(withStand, byTheStand), Is.True);
            Assert.That(withStand.Anchor.DistanceTo(byTheStand + Vector3.up * 3), Is.EqualTo(withStand.Anchor.DistanceTo(byTheStand)).Within(.001f));
            Assert.That(PointActivation.IsInRange(withoutStand, withoutStand.Anchor.transform.position + Vector3.right * (radius + .1f)), Is.False);
            Assert.That(PointActivation.IsInRange(withoutStand, withoutStand.Anchor.transform.position + Vector3.right * (radius - .1f)), Is.True);

            // Between the exhibit and the stand, looking at the stand with the exhibit behind.
            eye.position = new Vector3(4.6f, 1.65f, 1.4f);
            eye.rotation = Quaternion.LookRotation(new Vector3(1, 0, -1));
            Assert.That(PointActivation.IsVisible(withStand, eye, 4, 60), Is.False);
            Assert.That(PointActivation.IsFacing(withStand, eye, 4, 60), Is.True, "Looking at the stand counts as facing the point.");
            eye.rotation = Quaternion.LookRotation(new Vector3(1, 0, 1));
            Assert.That(PointActivation.IsFacing(withStand, eye, 4, 60), Is.False);

            var presence = root.AddComponent<MuseumPointPresence>();
            interaction.Activate(withStand, ActivationSource.Proximity);
            float margin = MuseumExperienceConfiguration.Current.PointLeaveMargin;
            presence.Step(stand.transform.position + Vector3.right * (radius + margin - .1f));
            Assert.That(presence.Away, Is.False, "The stand side of the point is still the point.");
            presence.Step(stand.transform.position + Vector3.right * (radius + margin + .1f));
            Assert.That(presence.Away, Is.True);
            presence.Step(byTheStand);
            Assert.That(presence.Away, Is.False, "Coming back to the stand is coming back to the point.");
        }

        [Test]
        public void APointWithoutAStandKeepsItsPanelOnTheScreen()
        {
            display.Show(withStand);
            display.Advance(1);
            display.Show(withoutStand);
            Assert.That(display.InWorld, Is.False);
            Assert.That(panel.parent, Is.SameAs(screenParent));
            Assert.That(panel.anchoredPosition, Is.EqualTo(new Vector2(-24, -124)));
            Assert.That(panel.pivot, Is.EqualTo(Vector2.one));
            Assert.That(panel.gameObject.activeSelf, Is.True);
            Assert.That(display.Surface.gameObject.activeSelf, Is.False);
            display.Hide();
            Assert.That(panel.gameObject.activeSelf, Is.False);
            Assert.That(display.Find(null), Is.Null);
        }

        [Test]
        public void TheViewingSpotFacesTheBoardWithTheExhibitAndTheStandOnEitherSide()
        {
            Assert.That(Vector3.Angle(stand.Facing, Vector3.back), Is.LessThan(.01f), "A reader stands on the side the board leans toward.");
            var spot = stand.ViewingSpot(1.4f);
            Assert.That(spot, Is.EqualTo(new Vector3(4.5f, 0, .1f)).Using(Vector3EqualityComparer.Instance));
            var toExhibit = withStand.Anchor.transform.position - spot;
            var toStand = stand.transform.position - spot;
            Assert.That(Vector3.Cross(toExhibit, toStand).y, Is.GreaterThan(0), "The exhibit is to the left and the stand to the right.");
            Assert.That(stand.ViewTarget.y, Is.InRange(1, 1.6f));
        }

        [Test]
        public void TheReadingSurfaceIsTheLargestFaceThatLeansTowardTheReader()
        {
            // A thin board leaning back 25 degrees, 0.6 wide and 0.8 long, plus a larger vertical back plate.
            var rotation = Quaternion.Euler(25, 0, 0);
            var origin = new Vector3(2, .75f, -3);
            Vector3 P(float x, float y, float z) => origin + rotation * new Vector3(x, y, z);
            var vertices = new[] {
                P(-.3f, 0, 0), P(.3f, 0, 0), P(.3f, .8f, 0), P(-.3f, .8f, 0),          // reading face, looks toward -Z and up
                P(-.3f, 0, .02f), P(.3f, 0, .02f), P(.3f, .8f, .02f), P(-.3f, .8f, .02f), // its back
                new Vector3(0, 0, 0), new Vector3(3, 0, 0), new Vector3(3, 3, 0), new Vector3(0, 3, 0) // vertical plate
            };
            var triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 8, 10, 9, 8, 11, 10 };
            Assert.That(PointLectern.TryFindBoard(vertices, triangles, out var bottom, out var normal, out var up, out var size), Is.True);
            Assert.That(Vector3.Angle(normal, rotation * Vector3.back), Is.LessThan(.5f));
            Assert.That(Vector3.Angle(up, rotation * Vector3.up), Is.LessThan(.5f));
            Assert.That(size.x, Is.EqualTo(.6f).Within(.001f));
            Assert.That(size.y, Is.EqualTo(.8f).Within(.001f));
            Assert.That(Vector3.Distance(bottom, origin), Is.LessThan(.001f));
            Assert.That(PointLectern.TryFindBoard(vertices, new[] { 8, 10, 9, 8, 11, 10 }, out _, out _, out _, out _), Is.False,
                "A vertical wall is not a reading surface.");
        }

        private sealed class Vector3EqualityComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public static readonly Vector3EqualityComparer Instance = new Vector3EqualityComparer();
            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < .001f;
            public int GetHashCode(Vector3 value) => 0;
        }
    }
}
