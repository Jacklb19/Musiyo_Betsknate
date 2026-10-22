using System;
using System.Collections;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The pieces of the points stand on their pedestals from the start of the visit, turn their face to the visitor at
    /// their point, and a light on the floor marks each point instead of the old coloured spheres. Synthetic models only.
    /// </summary>
    public sealed class MuseumResidentPiecesPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string First = "punto.personajes.pedestal_1", Second = "punto.personajes.pedestal_4";
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

        private static float Planar(Vector3 value) { value.y = 0; return value.magnitude; }

        [UnityTest]
        public IEnumerator PiecesStandFromTheStartAndFaceTheVisitorAtTheirPoint()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            var cube = Convert.FromBase64String(RemoteModelPlayTests.Cube);
            foreach (var (slug, title) in new[] { ("piece-a", "Pieza sintética A"), ("piece-b", "Pieza sintética B"), ("piece-c", "Pieza sintética C") })
                service.AddModel(slug, title, cube);
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
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var glows = flow.GetComponent<MuseumPointGlow>();
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var eye = visitor.GetComponentInChildren<Camera>().transform;
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            Assert.That(presenter.PreloadMode, Is.EqualTo(MuseumExperienceConfiguration.Current.ModelPreload));
            Assert.That(presenter.PreloadMode, Is.EqualTo("visit"), "The museum ships with the pieces present from the start.");
            Assert.That(glows, Is.Not.Null, "The visit installs the lights on the floor.");
            yield return Until(() => loader.State != TourLoadState.Loading, 20);
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            PointContractV1 Point(string key, int order, params ElementSummaryContractV1[] elements)
                => new PointContractV1 { key = key, name = key, order = order, activation = new[] { "proximity", "keyboard" }, elements = elements };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1),
                    Room("room.characters", 2,
                        Point(First, 0, new ElementSummaryContractV1 { slug = "piece-common", title = "Registro común" },
                            new ElementSummaryContractV1 { slug = "piece-a", title = "Pieza sintética A", has_3d_model = true },
                            new ElementSummaryContractV1 { slug = "piece-b", title = "Pieza sintética B", has_3d_model = true }),
                        Point(Second, 1, new ElementSummaryContractV1 { slug = "piece-c", title = "Pieza sintética C", has_3d_model = true })),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            var first = runtime.FindPoint(First);
            var second = runtime.FindPoint(Second);

            // Before the visitor goes near them, both points' first pieces stand at their points.
            yield return Until(() => presenter.ResidentCount == 2, 30);
            Assert.That(presenter.ResidentCount, Is.EqualTo(2), "Each point's first piece is brought at the start of the visit.");
            Assert.That(presenter.TryResident(first, out var firstSlug, out var firstModel), Is.True);
            Assert.That(firstSlug, Is.EqualTo("piece-a"));
            Assert.That(presenter.TryResident(second, out var secondSlug, out var secondModel), Is.True);
            Assert.That(secondSlug, Is.EqualTo("piece-c"));
            Assert.That(firstModel.gameObject.activeInHierarchy && secondModel.gameObject.activeInHierarchy, Is.True);
            Assert.That(Planar(firstModel.position - first.Anchor.LookTarget.position), Is.LessThan(.5f), "It stands at its own point.");
            Assert.That(Planar(secondModel.position - second.Anchor.LookTarget.position), Is.LessThan(.5f));
            Assert.That(interaction.ActivePoint, Is.Null, "Nothing was opened to bring them.");
            Assert.That(service.Downloads("piece-b"), Is.EqualTo(0), "Only each point's first piece is brought in advance.");

            // The old state spheres are gone and a light on the floor marks the points.
            foreach (var point in new[] { first, second })
            {
                Assert.That(point.Indicator == null || !point.Indicator.enabled || !point.Indicator.gameObject.activeInHierarchy, Is.True,
                    "The coloured sphere is no longer drawn.");
                var light = glows.LightOf(point);
                Assert.That(light, Is.Not.Null);
                Assert.That(light.transform.position.y, Is.LessThan(point.Anchor.transform.position.y + .2f), "The light lies on the floor.");
            }
            yield return new WaitForSecondsRealtime(MuseumPointGlow.FadeSeconds + .3f);
            Assert.That(glows.AlphaOf(second), Is.EqualTo(MuseumPointGlow.WaitingAlpha).Within(.01f), "A point to visit is softly lit.");

            // At the first point its resident piece is the one on show at once: nothing is downloaded again.
            int downloads = service.Downloads("piece-a");
            Assert.That(visitor.FocusPoint(first.Anchor), Is.True);
            yield return Until(() => interaction.ActivePoint == first, 5);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-a", 5);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-a"));
            Assert.That(presenter.Model, Is.SameAs(firstModel), "The piece on the pedestal is the one on show.");
            Assert.That(service.Downloads("piece-a"), Is.EqualTo(downloads));
            yield return new WaitForSecondsRealtime(MuseumPointGlow.FadeSeconds + .3f);
            Assert.That(glows.AlphaOf(first), Is.LessThan(.01f), "The light gives way to the screen while the visitor is there.");

            // Its face (the piece's -Z side) turns to the visitor.
            yield return new WaitForSecondsRealtime(MuseumModelPresenter.FacingSeconds * 4);
            var toVisitor = eye.position - firstModel.position;
            toVisitor.y = 0;
            var face = -firstModel.forward;
            face.y = 0;
            Assert.That(Vector3.Angle(face, toVisitor), Is.LessThan(12), "The piece looks at the visitor.");

            // Another piece of the point takes the resident's place while it is read, and gives it back afterwards.
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-common"), "By a stand the point opens on its own record.");
            Assert.That(hud.StepCarousel(1), Is.True);
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-b", 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-b"));
            Assert.That(firstModel.gameObject.activeSelf, Is.False, "The resident piece steps aside.");
            Assert.That(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Count(item => item.name == "RemoteModelPivot" && item.gameObject.activeInHierarchy && Planar(item.position - first.Anchor.LookTarget.position) < 1),
                Is.EqualTo(1), "One piece stands at the point.");
            interaction.Close();
            visitor.PlaceAt(first.Anchor.transform.position + Vector3.back * 6 + Vector3.up * .05f, Quaternion.identity);
            yield return Until(() => interaction.ActivePoint == null, 5);
            yield return Until(() => firstModel != null && firstModel.gameObject.activeSelf, 5);
            Assert.That(firstModel != null && firstModel.gameObject.activeSelf, Is.True, "The resident piece comes back to its pedestal.");
            Assert.That(presenter.ResidentCount, Is.EqualTo(2));
            yield return new WaitForSecondsRealtime(MuseumPointGlow.FadeSeconds + .3f);
            Assert.That(glows.AlphaOf(first), Is.EqualTo(MuseumPointGlow.VisitedAlpha).Within(.01f), "A visited point stays softly lit.");
        }
#endif
    }
}
