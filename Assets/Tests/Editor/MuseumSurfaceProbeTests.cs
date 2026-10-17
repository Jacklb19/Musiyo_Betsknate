using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumSurfaceProbeTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void Clean()
        {
            foreach (var item in created) Object.DestroyImmediate(item);
            created.Clear();
        }

        private GameObject Make(string name, params System.Type[] components)
        {
            var item = new GameObject(name, components);
            created.Add(item);
            return item;
        }

        [Test]
        public void LargeCanvasesAndMovingSurfacesNearTheHeadAreNamedAndSmallOrFarOnesAreNot()
        {
            var head = Make("Head").transform;
            head.position = new Vector3(0, 1.6f, 0);
            var wall = Make("SharedCanvas", typeof(RectTransform), typeof(Canvas));
            wall.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)wall.transform;
            rect.sizeDelta = new Vector2(1024, 900);
            rect.localScale = Vector3.one * (1.4f / 1024);
            rect.position = head.position + Vector3.forward * 1.8f;
            var figure = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(figure);
            figure.name = "Companion";
            figure.transform.position = head.position + Vector3.right * 1.5f;
            figure.transform.localScale = new Vector3(.5f, 1.8f, .4f);
            var pebble = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(pebble);
            pebble.transform.position = head.position + Vector3.left;
            pebble.transform.localScale = Vector3.one * .2f;
            var far = GameObject.CreatePrimitive(PrimitiveType.Cube);
            created.Add(far);
            far.transform.position = head.position + Vector3.back * 9;
            far.transform.localScale = Vector3.one * 3;
            var follower = GameObject.CreatePrimitive(PrimitiveType.Quad);
            created.Add(follower);
            follower.name = "HeadLockedPanel";
            follower.transform.SetParent(head, false);
            follower.transform.localPosition = Vector3.forward;
            follower.transform.localScale = Vector3.one * 1.2f;

            var probe = Make("Probe").AddComponent<MuseumSurfaceProbe>();
            probe.Configure(head);
            var found = new List<MuseumSurfaceProbe.Entry>();
            probe.Collect(head, found);

            var paths = found.ConvertAll(entry => entry.Path);
            Assert.That(paths, Does.Contain("SharedCanvas"));
            Assert.That(paths, Does.Contain("Companion"));
            Assert.That(paths, Does.Contain("Head/HeadLockedPanel"));
            Assert.That(paths, Has.None.EqualTo(pebble.name), "Small things are not reported.");
            Assert.That(paths, Has.None.EqualTo(far.name), "Far things are not reported.");
            var canvas = found.Find(entry => entry.Path == "SharedCanvas");
            Assert.That(canvas.Kind, Is.EqualTo("canvas"));
            Assert.That(canvas.Size.x, Is.EqualTo(1.4f).Within(.01f));
            Assert.That(found.Find(entry => entry.Path == "Head/HeadLockedPanel").Follows, Is.True);
            Assert.That(found.Find(entry => entry.Path == "Companion").Follows, Is.False);
            for (int index = 1; index < found.Count; index++)
                Assert.That(found[index].Distance, Is.GreaterThanOrEqualTo(found[index - 1].Distance), "Nearest first.");

            // Walking on with something that keeps its place in front of the eyes marks it as following.
            head.position += Vector3.forward * 2;
            figure.transform.position += Vector3.forward * 2;
            probe.Collect(head, found);
            Assert.That(found.Find(entry => entry.Path == "Companion").Follows, Is.True);
        }
    }
}
