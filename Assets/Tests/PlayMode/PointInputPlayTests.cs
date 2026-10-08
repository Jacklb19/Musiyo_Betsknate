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
    }
}
