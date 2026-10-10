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
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            withStand = CreatePoint(room.transform, "first", new Vector3(4, 0, 2));
            withoutStand = CreatePoint(room.transform, "second", new Vector3(-4, 0, 2));
            var elements = new[] { new ElementSummaryContractV1 { slug = "one", title = "One" } };
            var contract = new TourContractV1 { schema_version = 1,
                tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                    points = new[] {
                        new PointContractV1 { key = "first", name = "First", order = 0, activation = new[] { "keyboard" }, elements = elements },
                        new PointContractV1 { key = "second", name = "Second", order = 1, activation = new[] { "keyboard" }, elements = elements }
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
            display = root.AddComponent<MuseumLecternDisplay>();
            display.Configure(panel, null);
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
        public void ThePanelUnfoldsFromTheStandOfItsPointAndFoldsBackOntoIt()
        {
            float seconds = MuseumExperienceConfiguration.Current.LecternUnfoldSeconds;
            display.Show(withStand);
            Assert.That(display.Host, Is.SameAs(stand));
            Assert.That(panel.parent, Is.SameAs(display.Surface), "The panel is carried by the screen of the stand.");
            Assert.That(display.Surface.gameObject.activeSelf, Is.False, "It starts folded on the board.");
            display.Advance(seconds * .5f);
            Assert.That(display.Openness, Is.EqualTo(.5f).Within(.001f));
            Assert.That(display.Surface.gameObject.activeSelf, Is.True);
            Assert.That(display.Surface.localScale.y, Is.LessThan(display.Surface.localScale.x), "It rises from the board instead of popping in.");
            display.Advance(seconds);
            Assert.That(display.Openness, Is.EqualTo(1));
            float width = display.Surface.sizeDelta.x * display.Surface.localScale.x;
            Assert.That(width, Is.EqualTo(MuseumExperienceConfiguration.Current.LecternScreenWidth).Within(.001f));
            Assert.That(Quaternion.Angle(display.Surface.rotation, stand.Board.rotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(display.Surface.position, stand.Board.position), Is.LessThan(.05f));

            display.Hide();
            Assert.That(panel.gameObject.activeSelf, Is.True, "The folding screen keeps its content until it has closed.");
            display.Advance(seconds * 2);
            Assert.That(display.Openness, Is.EqualTo(0));
            Assert.That(display.Surface.gameObject.activeSelf, Is.False);
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
