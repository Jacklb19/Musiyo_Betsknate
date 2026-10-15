using System;
using System.Collections;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Several pieces of one point shown one at a time on its pedestal, with synthetic models and records.</summary>
    public sealed class MuseumCarouselPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string PointKey = "punto.personajes.pedestal_4";
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

        [UnityTest]
        public IEnumerator PiecesOfOnePointAreShownOneAtATimeAndStayInStepWithTheirRecord()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService { ModelDelayMilliseconds = 150 };
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
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            yield return Until(() => loader.State != TourLoadState.Loading, 20);
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2, new PointContractV1
                    {
                        key = PointKey, name = "Punto compartido", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[]
                        {
                            new ElementSummaryContractV1 { slug = "piece-common", title = "Registro común" },
                            new ElementSummaryContractV1 { slug = "piece-a", title = "Pieza sintética A", has_3d_model = true },
                            new ElementSummaryContractV1 { slug = "piece-b", title = "Pieza sintética B", has_3d_model = true },
                            new ElementSummaryContractV1 { slug = "piece-c", title = "Pieza sintética C", has_3d_model = true },
                        }
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
            var point = runtime.FindPoint(PointKey);
            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            yield return Until(() => interaction.ActivePoint == point, 5);
            Assert.That(interaction.SelectElement(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-a"));
            // The point panel floats in the museum, away from the screen interface.
            var carousel = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(item => item.name == "PieceCarousel").gameObject;
            var label = carousel.transform.Find("CarouselPosition").GetComponent<TMP_Text>();
            yield return null;
            Assert.That(carousel.activeInHierarchy, Is.True, "A point with several pieces shows the carousel.");
            Assert.That(label.text, Does.Contain("1 de 3").And.Contain("Pieza sintética A"));

            // Quick changes settle on the last piece; the ones passed over are never downloaded.
            int requests = presenter.RequestCount;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return null;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-c", 15);
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-c"), "The record follows the piece.");
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-c"));
            Assert.That(presenter.RequestCount - requests, Is.EqualTo(1), "Only the piece the visitor stopped on is requested.");
            Assert.That(service.Downloads("piece-b"), Is.EqualTo(0));
            yield return null;
            Assert.That(label.text, Does.Contain("3 de 3"));
            Assert.That(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Count(item => item.name == "RemoteModelPivot" && item.gameObject.activeInHierarchy), Is.EqualTo(1), "One piece is on show.");

            // Aim and press on the floating screen, as on a headset or with the view centre.
            yield return Until(() => display.Openness >= 1, 3);
            var next = carousel.transform.Find("CarouselNext").GetComponent<Selectable>();
            var rect = (RectTransform)next.transform;
            var spot = rect.TransformPoint(rect.rect.center);
            display.Aim(new Ray(spot - display.Surface.forward, display.Surface.forward));
            Assert.That(display.Aimed, Is.SameAs(next));
            Assert.That(display.Press(), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-a", 15);
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-a"), "Next wraps around to the first piece.");

            // A failing piece says so and can be retried by hand.
            service.Failing["piece-b"] = true;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Unavailable, 15);
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Unavailable));
            service.Failing.TryRemove("piece-b", out _);
            presenter.Retry();
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-b"));

            // Examining keeps the piece: nothing changes by itself and the carousel steps aside.
            Assert.That(presenter.Examine(), Is.True);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ModelExamination));
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-b"));
            Assert.That(carousel.activeInHierarchy, Is.False);
            Assert.That(hud.StepCarousel(1), Is.False);
            interaction.Back();
            yield return null;
            Assert.That(carousel.activeInHierarchy, Is.True);
        }
#endif
    }
}
