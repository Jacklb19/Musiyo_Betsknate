using System;
using System.Collections;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The optional automatic tour in the saved museum, with a synthetic tour and no published content.</summary>
    public sealed class MuseumAutoTourPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static float Planar(Vector3 from, Vector3 to) { var offset = to - from; offset.y = 0; return offset.magnitude; }

        [UnityTest]
        public IEnumerator TheTourWalksToThePointsWithoutTurningTheVisitorAndHandsControlBack()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var tour = flow.GetComponent<MuseumAutoTour>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            var view = visitor.GetComponentInChildren<Camera>().transform;
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            Assert.That(tour, Is.Not.Null, "The visit installs the optional tour.");
            yield return Until(() => loader.State != TourLoadState.Loading, 20);
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            PointContractV1 Point(string key, int order, string slug) => new PointContractV1
            {
                key = key, name = slug, order = order, activation = new[] { "proximity", "keyboard" },
                elements = new[] { new ElementSummaryContractV1 { slug = slug, title = slug } }
            };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1),
                    Room("room.characters", 2, Point("punto.personajes.pedestal_1", 0, "synthetic-one"),
                        Point("punto.personajes.pedestal_2", 1, "synthetic-two")),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
            // Start across the room from the first stop, facing away from it.
            var first = runtime.FindPoint("punto.personajes.pedestal_1");
            Assert.That(visitor.FocusPoint(runtime.FindPoint("punto.personajes.pedestal_5").Anchor), Is.True);
            interaction.Close();
            yield return null;
            var heading = visitor.transform.rotation;
            var look = view.localRotation;

            Assert.That(tour.Begin(), Is.True, tour.Status);
            Assert.That(tour.Phase, Is.EqualTo(AutoTourPhase.Walking));
            Assert.That(tour.CurrentStop, Is.SameAs(first), "It continues from the visitor's room, in the tour's order.");
            Assert.That(tour.Status, Is.Not.Empty);
            var start = visitor.transform.position;
            yield return new WaitForSecondsRealtime(.6f);
            Assert.That(Planar(start, visitor.transform.position), Is.GreaterThan(.05f), "It really moves the visitor.");
            Assert.That(Planar(start, visitor.transform.position), Is.LessThan(MuseumExperienceConfiguration.Current.AutoTourSpeed * .8f),
                "Slowly.");
            // Simulated time keeps the test short; the same steps run every frame in a visit.
            for (int step = 0; tour.Phase == AutoTourPhase.Walking && step < 4000; step++)
            {
                tour.Step(.05f);
                if (step % 20 == 0) yield return null;
            }
            Assert.That(tour.Phase, Is.EqualTo(AutoTourPhase.AtStop));
            Assert.That(first.Anchor.DistanceTo(view.position), Is.LessThanOrEqualTo(first.Anchor.ActivationRadius),
                "It stops inside the point's radius.");
            Assert.That(Quaternion.Angle(visitor.transform.rotation, heading), Is.LessThan(.01f), "It never turns the visitor.");
            Assert.That(Quaternion.Angle(view.localRotation, look), Is.LessThan(.01f), "It never moves the gaze.");
            yield return Until(() => interaction.ActivePoint == first, 3);
            Assert.That(interaction.ActivePoint, Is.SameAs(first), "Arriving opens the point as walking there would.");

            // The shared action pauses and resumes it.
            Press(keyboard.oKey);
            yield return null;
            Release(keyboard.oKey);
            Assert.That(tour.Phase, Is.EqualTo(AutoTourPhase.Paused));
            yield return null;
            Press(keyboard.oKey);
            yield return null;
            Release(keyboard.oKey);
            Assert.That(tour.Phase, Is.EqualTo(AutoTourPhase.AtStop));

            // A pause of the visit holds it where it is.
            for (int step = 0; tour.Phase == AutoTourPhase.AtStop && step < 400; step++) tour.Step(.05f);
            Assert.That(tour.Phase, Is.EqualTo(AutoTourPhase.Walking), "After its stop it heads for the next point.");
            interaction.SetPaused(true);
            var held = visitor.transform.position;
            for (int step = 0; step < 40; step++) tour.Step(.05f);
            Assert.That(Planar(held, visitor.transform.position), Is.LessThan(.001f), "Nothing moves during a pause.");
            Assert.That(tour.Active, Is.True, "A pause does not end the tour.");
            interaction.SetPaused(false);
            yield return null;

            // Walking by hand takes over at once.
            Press(keyboard.wKey);
            yield return null;
            Assert.That(tour.Active, Is.False);
            Release(keyboard.wKey);
            var after = visitor.transform.position;
            for (int step = 0; step < 20; step++) tour.Step(.05f);
            Assert.That(Planar(after, visitor.transform.position), Is.LessThan(.001f), "A stopped tour moves nothing.");

            // The screen has no permanent bars; pausing, orientation and the tour are reached from the pause menu.
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var bars = System.Array.FindAll(hud.GetComponentsInChildren<Transform>(true),
                item => (item.name == "Header" || item.name == "Controls") && item.GetComponentInParent<MuseumMenuView>(true) == null);
            Assert.That(bars.Length, Is.GreaterThanOrEqualTo(2), "The saved museum still holds the old bars.");
            foreach (var bar in bars)
                Assert.That(bar.gameObject.activeInHierarchy, Is.False, "No permanent " + bar.name + " bar during the visit.");
            foreach (var button in hud.GetComponentsInChildren<UnityEngine.UI.Button>())
                Assert.That(button.name != "WayfindingAction" || button.transform.parent.name != hud.name, Is.True,
                    "No permanent orientation button.");
            var menu = UnityEngine.Object.FindAnyObjectByType<MuseumMenuHud>();
            interaction.SetPaused(true);
            yield return null;
            Assert.That(menu.OrientationButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(menu.AutoTourButton.gameObject.activeInHierarchy, Is.True);
            menu.OrientationButton.onClick.Invoke();
            yield return null;
            Assert.That(interaction.State, Is.Not.EqualTo(InteractionState.Paused), "Choosing orientation resumes the visit.");
            Assert.That(flow.GetComponent<MuseumWayfinding>().MenuOpen, Is.True, "and opens the orientation menu.");
            flow.GetComponent<MuseumWayfinding>().CloseMenu();
        }
#endif
    }
}
