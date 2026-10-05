using System.Collections;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class WayfindingPlayTests
    {
        [UnityTest]
        public IEnumerator RouteGoesAroundWallsAndRejectsAnIsolatedRoom()
        {
            var root = new GameObject("WayfindingPlayTest");
            root.SetActive(false);
            var origin = new Vector3(200, 0, 200);
            root.transform.position = origin;
            var runtime = root.AddComponent<TourRuntime>();
            root.AddComponent<MuseumInteraction>();
            var guide = root.AddComponent<MuseumWayfinding>();
            var visitor = new GameObject("Visitor");
            visitor.transform.SetParent(root.transform, false);
            visitor.transform.localPosition = Vector3.up;
            guide.Configure(visitor.transform);
            var rooms = new RoomContractV1[3];
            for (int index = 0; index < 3; index++)
            {
                var room = new GameObject("Room" + index);
                room.transform.SetParent(root.transform, false);
                room.transform.localPosition = new Vector3(index == 2 ? 30 : index * 6, 0, 0);
                var anchor = room.AddComponent<RoomAnchor>();
                anchor.Configure("room." + index);
                anchor.ConfigureBounds(Vector2.one * 2);
                rooms[index] = new RoomContractV1 { key = anchor.Key, name = "Room " + index, order = index,
                    points = System.Array.Empty<PointContractV1>() };
            }
            Box(root.transform, "Floor", new Vector3(3, -.15f, 0), new Vector3(20, .3f, 20));
            Box(root.transform, "Wall", new Vector3(3, 1.5f, 0), new Vector3(.4f, 3, 6));
            Box(root.transform, "IsolatedFloor", new Vector3(30, -.15f, 0), new Vector3(4, .3f, 4));
            root.SetActive(true);
            try
            {
                Assert.That(runtime.Apply(new TourContractV1 { schema_version = 1,
                    tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" }, rooms = rooms }, out var error), Is.True, error);
                yield return null;
                Assert.That(guide.TryPath(origin, "room.1", out var corners), Is.True);
                Assert.That(corners.Length, Is.GreaterThanOrEqualTo(4));
                for (int index = 1; index < corners.Length; index++)
                    Assert.That(Physics.Linecast(corners[index - 1] + Vector3.up, corners[index] + Vector3.up), Is.False,
                        "The guidance must not cross a wall.");
                Assert.That(guide.TryPath(origin, "room.2", out _), Is.False);
                guide.FollowSuggested();
                Assert.That(guide.TargetRoomKey, Is.EqualTo("room.1"));
                visitor.transform.position = origin + Vector3.right * 6 + Vector3.up;
                yield return null;
                Assert.That(guide.TargetRoomKey, Is.EqualTo("room.2"));
                Assert.That(guide.CornerCount, Is.Zero, "A partial route must never be displayed.");
                guide.ToggleMenu();
                Assert.That(guide.MenuOpen, Is.True);
                runtime.Clear();
                Assert.That(guide.TargetRoomKey, Is.Null);
                Assert.That(guide.MenuOpen, Is.False);
                guide.enabled = false;
                Assert.That(guide.NavigationReady, Is.False);
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
        private static void Box(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.AddComponent<BoxCollider>().size = size;
        }
    }
}
