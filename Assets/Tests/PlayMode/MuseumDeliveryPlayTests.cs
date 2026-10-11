using System.Collections;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The accepted visit behaviours, exercised against the colliders and stands of the delivered museum.</summary>
    public sealed class MuseumDeliveryPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        // A failed check must not leave the museum loaded for the tests that follow.
        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static Vector2 Floor(Transform target) => new Vector2(target.position.x, target.position.z);

        private static void Place(CharacterController body, Vector3 position)
        {
            body.enabled = false;
            body.transform.position = position;
            body.enabled = true;
        }

        private static PointContractV1 Point(string key, int order) => new PointContractV1
        {
            key = key, name = key, order = order, activation = new[] { "proximity", "keyboard", "gaze" },
            elements = new[] { new ElementSummaryContractV1 { slug = "synthetic-element", title = "Test" } },
        };

        private static RoomContractV1 Room(string key, int order, params PointContractV1[] points)
            => new RoomContractV1 { key = key, name = key, order = order, points = points };

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedMuseumShowsPointPanelsInsideTheBuildingAndGuidesBetweenRooms()
        {
            bool hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            try
            {
                yield return null;
                var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
                var runtime = flow.GetComponent<TourRuntime>();
                var loader = flow.GetComponent<TourLoader>();
                var interaction = flow.GetComponent<MuseumInteraction>();
                var guide = flow.GetComponent<MuseumWayfinding>();
                var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
                var body = visitor.GetComponent<CharacterController>();
                flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
                // The content comes from this test whether or not a local service answers.
                for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(runtime.Apply(new TourContractV1
                {
                    schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                    rooms = new[]
                    {
                        Room("room.arrival", 0), Room("room.welcome", 1, Point("punto.bienvenida.significados", 0)),
                        Room("room.characters", 2, Point("punto.personajes.pedestal_1", 0)),
                        Room("room.instruments", 3, Point("punto.instrumentos.pedestal_1", 0)),
                        Room("room.hearth", 4), Room("room.overlook", 5, Point("punto.mirador.creditos", 0)),
                    },
                }, out var error), Is.True, error);
                flow.EnterImmediately();
                yield return null;
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));

                var start = visitor.transform.position;
                Press(keyboard.wKey);
                for (float waited = 0; waited < .8f; waited += Time.deltaTime) yield return null;
                Release(keyboard.wKey);
                yield return null;
                Assert.That(visitor.transform.position.z, Is.GreaterThan(start.z + .3f), "The visitor walks up the arrival path.");
                Assert.That(visitor.transform.position.y, Is.InRange(start.y - .05f, start.y + .15f), "The path carries the visitor.");
                Assert.That(body.isGrounded, Is.True);

                var display = flow.GetComponent<MuseumLecternDisplay>();
                foreach (var (key, withStand) in new[] { ("punto.personajes.pedestal_1", true), ("punto.instrumentos.pedestal_1", false),
                    ("punto.bienvenida.significados", false), ("punto.mirador.creditos", false) })
                {
                    var point = runtime.FindPoint(key);
                    Assert.That(visitor.FocusPoint(point.Anchor), Is.True, "There is room to stand by " + key + ".");
                    for (float waited = 0; interaction.ActivePoint != point && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
                    Assert.That(interaction.ActivePoint, Is.SameAs(point), "Standing by " + key + " opens it.");
                    Assert.That(interaction.DetailInWorld, Is.True, key);
                    for (float waited = 0; display.Openness < 1 && waited < 3; waited += Time.unscaledDeltaTime) yield return null;
                    Assert.That(display.Open && display.InWorld && display.Openness >= 1, Is.True, "The panel of " + key + " unfolds inside the museum.");
                    Assert.That(display.Host != null, Is.EqualTo(withStand), key);
                    Assert.That(display.Anchor, Is.SameAs(point.Anchor));
                    Assert.That(interaction.BlocksMovement, Is.False, "Reading " + key + " never stops the visitor.");

                    // Pointing at an option from the reader's side and pressing chooses it.
                    var control = display.Surface.GetComponentsInChildren<Selectable>(false).First(candidate => candidate.IsInteractable());
                    var rect = (RectTransform)control.transform;
                    var spot = rect.TransformPoint(rect.rect.center);
                    display.Aim(new Ray(spot - display.Surface.forward, display.Surface.forward));
                    Assert.That(display.Aimed, Is.Not.Null, "An option of " + key + " can be aimed at.");
                    Assert.That(display.Press(), Is.True);
                    yield return null;

                    var reading = Floor(visitor.transform);
                    Press(keyboard.sKey);
                    for (float waited = 0; waited < .4f; waited += Time.deltaTime) yield return null;
                    Release(keyboard.sKey);
                    yield return null;
                    Assert.That(Vector2.Distance(Floor(visitor.transform), reading), Is.GreaterThan(.1f), "The visitor walks while the panel of " + key + " is open.");

                    interaction.Close();
                    Place(body, start);
                    yield return new WaitForSecondsRealtime(.6f);
                }

                // The optional route reaches every room over the delivered floors without crossing a wall.
                body.enabled = false;
                foreach (string room in new[] { "room.welcome", "room.characters", "room.instruments", "room.hearth", "room.overlook" })
                {
                    Assert.That(guide.TryPath(start, room, out var corners), Is.True, "A route leads to " + room + ".");
                    for (int index = 1; index < corners.Length; index++)
                        Assert.That(Physics.Linecast(corners[index - 1] + Vector3.up, corners[index] + Vector3.up, out var hit), Is.False,
                            "The route to " + room + " crosses " + (hit.collider != null ? hit.collider.name : ""));
                }
                body.enabled = true;
            }
            finally
            {
                if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }
#endif
    }
}
