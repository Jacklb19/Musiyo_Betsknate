using System.Collections;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class PointInputPlayTests
    {
        [UnityTest]
        public IEnumerator KeyboardProximityAndGazeSelectTheSameElement()
        {
            foreach (var source in new[] { ActivationSource.Keyboard, ActivationSource.Proximity, ActivationSource.Gaze })
            {
                var root = new GameObject("PointInputPlayTest");
                root.SetActive(false);
                var runtime = root.AddComponent<TourRuntime>();
                var interaction = root.AddComponent<MuseumInteraction>();
                var input = root.AddComponent<DesktopPointInput>();
                var room = new GameObject("Room");
                room.transform.SetParent(root.transform);
                room.AddComponent<RoomAnchor>().Configure("room.test");
                var pointObject = new GameObject("Point");
                pointObject.transform.SetParent(room.transform);
                pointObject.transform.position = Vector3.forward * 3;
                pointObject.AddComponent<PointAnchor>().Configure("point.test", 1.8f, pointObject.transform);
                pointObject.AddComponent<TourPoint>();
                var observer = new GameObject("Observer");
                observer.transform.SetParent(root.transform);
                var eye = new GameObject("Eye");
                eye.transform.SetParent(root.transform);
                var camera = eye.AddComponent<Camera>();
                observer.transform.position = source == ActivationSource.Proximity ? Vector3.forward * 1.5f : Vector3.zero;
                input.Configure(observer.transform, camera);
                root.SetActive(true);
                try
                {
                    var contract = new TourContractV1 { schema_version = 1,
                        tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                        rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                            points = new[] { new PointContractV1 { key = "point.test", name = "Test", order = 0,
                                activation = new[] { "keyboard", "proximity", "gaze" },
                                elements = new[] { new ElementSummaryContractV1 { slug = "synthetic-element", title = "Test" } }
                            } } } }
                    };
                    Assert.That(runtime.Apply(contract, out var error), Is.True, error);
                    if (source == ActivationSource.Keyboard) { input.Cycle(1); input.Confirm(); }
                    float limit = Time.realtimeSinceStartup + 5;
                    while (interaction.SelectedElement == null && Time.realtimeSinceStartup < limit) yield return null;
                    Assert.That(interaction.SelectedElement, Is.Not.Null, source.ToString());
                    Assert.That(interaction.SelectedElement.slug, Is.EqualTo("synthetic-element"));
                    Assert.That(interaction.LastSource, Is.EqualTo(source));
                    const string detail = "{\"slug\":\"synthetic-element\",\"title\":\"Test\",\"description\":\"Synthetic\","
                        + "\"resources\":[{\"id\":\"test-narration\",\"kind\":\"narration\",\"mime\":\"audio/mpeg\",\"transcription\":\"TEST TRANSCRIPT\"}]}";
                    Assert.That(ElementText.TryFormat(interaction.SelectedElement.slug, detail, out var text), Is.True);
                    Assert.That(text, Does.Contain("TEST TRANSCRIPT"));
                    interaction.Close();
                    yield return new WaitForSecondsRealtime(1.5f);
                    Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration), "Closing must remain closed until leaving the point.");
                    Assert.That(interaction.SelectedElement, Is.Null);
                }
                finally { Object.Destroy(root); }
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ApproachingAnotherPointOpensItOnceTheVisitorHasLeftTheActiveOne()
        {
            var root = new GameObject("PointTakeoverPlayTest");
            root.SetActive(false);
            var runtime = root.AddComponent<TourRuntime>();
            var interaction = root.AddComponent<MuseumInteraction>();
            var input = root.AddComponent<DesktopPointInput>();
            var presence = root.AddComponent<MuseumPointPresence>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            foreach (var (key, distance) in new[] { ("point.near", 3f), ("point.far", 23f) })
            {
                var pointObject = new GameObject(key);
                pointObject.transform.SetParent(room.transform);
                pointObject.transform.position = Vector3.forward * distance;
                pointObject.AddComponent<PointAnchor>().Configure(key, 1.8f, pointObject.transform);
                pointObject.AddComponent<TourPoint>();
            }
            // The visitor carries the camera, so walking moves both.
            var observer = new GameObject("Observer");
            observer.transform.SetParent(root.transform);
            var camera = new GameObject("Eye").AddComponent<Camera>();
            camera.transform.SetParent(observer.transform, false);
            observer.transform.position = Vector3.forward * 1.5f;
            input.Configure(observer.transform, camera);
            presence.Configure(observer.transform);
            root.SetActive(true);
            try
            {
                var elements = new[] { new ElementSummaryContractV1 { slug = "synthetic-element", title = "Test" } };
                var contract = new TourContractV1 { schema_version = 1,
                    tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                    rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                        points = new[] {
                            new PointContractV1 { key = "point.near", name = "Near", order = 0, activation = new[] { "proximity" }, elements = elements },
                            new PointContractV1 { key = "point.far", name = "Far", order = 1, activation = new[] { "proximity" }, elements = elements }
                        } } }
                };
                Assert.That(runtime.Apply(contract, out var error), Is.True, error);
                var near = runtime.FindPoint("point.near");
                var far = runtime.FindPoint("point.far");
                float limit = Time.realtimeSinceStartup + 5;
                while (interaction.ActivePoint != near && Time.realtimeSinceStartup < limit) yield return null;
                Assert.That(interaction.ActivePoint, Is.SameAs(near), "Walking up to a point opens it.");

                // A complete text on the screen holds the visitor, so nothing else may replace it.
                Assert.That(interaction.OpenDetail(), Is.True);
                observer.transform.position = Vector3.forward * 21.5f;
                yield return new WaitForSecondsRealtime(1.5f);
                Assert.That(presence.Away, Is.True);
                Assert.That(interaction.ActivePoint, Is.SameAs(near));
                Assert.That(interaction.State, Is.EqualTo(InteractionState.Reading));

                interaction.Back();
                limit = Time.realtimeSinceStartup + 5;
                while (interaction.ActivePoint != far && Time.realtimeSinceStartup < limit) yield return null;
                Assert.That(interaction.ActivePoint, Is.SameAs(far), "The point the visitor walked to takes over.");
                Assert.That(interaction.LastSource, Is.EqualTo(ActivationSource.Proximity));
                Assert.That(presence.Away, Is.False);
                Assert.That(near.State, Is.EqualTo(PointState.Available));

                // Walking back and forth inside the point never reopens or resets it.
                Assert.That(interaction.OpenDetail(), Is.True);
                interaction.Back();
                observer.transform.position = Vector3.forward * 22f;
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.That(interaction.ActivePoint, Is.SameAs(far));
                Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            }
            finally { Object.Destroy(root); }
            yield return null;
        }
    }
}
