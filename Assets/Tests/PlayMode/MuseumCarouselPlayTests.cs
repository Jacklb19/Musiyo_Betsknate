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
    /// <summary>
    /// Several records of one point shown one at a time on its pedestal, with synthetic models: arrows beside the piece step
    /// through them and the screen by the stand says which one is on show.
    /// </summary>
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
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected), "By a stand the point opens on its first record, with no list.");
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-common"));
            // The point's own record stands the point's first piece on the pedestal, and the arrows are there from the
            // start, before any piece has arrived (they used to wait for a piece chosen some other way).
            var carousel = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .First(item => item.name == "PieceCarousel").gameObject;
            yield return null;
            yield return null;
            Assert.That(carousel.activeInHierarchy, Is.True, "The arrows stand by the pedestal on the point's own record.");
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-a"), "The point's own record brings its first piece.");
            bool Says(string text) => UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)
                .Any(item => item.text.Contains(text));
            // One card per point (owner, 25/10/2026): the card stays on the point's own record; the arrows beside the
            // piece change only the piece, they name the pieces and count nothing.
            var cues = flow.GetComponent<MuseumPieceCues>();
            yield return null;
            Assert.That(carousel.activeInHierarchy, Is.True, "A point with several pieces shows the arrows beside its piece.");
            Assert.That(cues.PositionLabel.text, Is.Empty, "No «1 de 4»: the pieces are not records on the card.");
            Assert.That(cues.NextLabel.text, Is.EqualTo("Pieza sintética B"));
            Assert.That(cues.PreviousLabel.text, Is.EqualTo("Pieza sintética C"));
            Assert.That(Says("de 4"), Is.False, "The card counts nothing.");
            Assert.That(cues.Surface.IsChildOf(display.Surface), Is.False, "The arrows stand apart from the screen.");
            Assert.That(UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
                .Any(item => item.name == "ElementChoice" || item.name == "ModelAction" && item.gameObject.activeInHierarchy
                    && item.GetComponentInChildren<TMP_Text>().text.StartsWith("Examinar")), Is.False,
                "The screen by the stand has no list of records and no examination button.");
            Assert.That(flow.GetComponent<MuseumRecordTabs>() == null || !UnityEngine.Object.FindAnyObjectByType<MuseumRecordTabs>().Row.gameObject.activeInHierarchy,
                Is.True, "A single record on the card needs no tabs.");

            // A piece record chosen some other way (a number key, a link from the Web) puts its piece on the pedestal and
            // leaves the card on the point's record.
            Assert.That(interaction.SelectElement(2), Is.True);
            yield return null;
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-common"), "The card stays on the point's record.");
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-b", 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-b"));
            Assert.That(presenter.ShownPiece(point).slug, Is.EqualTo("piece-b"));

            // Quick changes settle on the last piece; the ones passed over are never downloaded.
            int requests = presenter.RequestCount;
            // Changing the piece leaves the point's narration alone: it neither stops nor starts again.
            var narration = flow.GetComponent<MuseumNarration>();
            int starts = narration.Starts;
            var narrated = narration.NarratedSlug;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return null;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-a", 15);
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-common"), "The card does not follow the piece.");
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-a"), "Two steps from B wrap around to A.");
            Assert.That(presenter.RequestCount - requests, Is.LessThanOrEqualTo(1), "Only the piece the visitor stopped on is requested.");
            Assert.That(narration.Starts, Is.EqualTo(starts), "Stepping the masks does not restart the narration.");
            Assert.That(narration.NarratedSlug, Is.EqualTo(narrated));
            Assert.That(service.Downloads("piece-c"), Is.EqualTo(0), "The piece passed over is never downloaded.");
            Assert.That(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Count(item => item.name == "RemoteModelPivot" && item.gameObject.activeInHierarchy), Is.EqualTo(1), "One piece is on show.");

            // Aim and press on the arrow beside the piece, as on a headset or with the view centre.
            yield return null;
            var next = carousel.transform.Find("CarouselNext").GetComponent<Selectable>();
            var cue = (RectTransform)carousel.transform;
            void PressNext()
            {
                var rect = (RectTransform)next.transform;
                var spot = rect.TransformPoint(rect.rect.center);
                display.Aim(new Ray(spot - cue.forward, cue.forward));
                Assert.That(display.Aimed, Is.SameAs(next), "The pointer reaches the arrow in the museum.");
                Assert.That(display.Press(), Is.True);
            }
            PressNext();
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-b", 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-b"));
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("piece-common"));

            // A failing piece says so and can be retried by hand.
            service.Failing["piece-c"] = true;
            Assert.That(hud.StepCarousel(1), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Unavailable, 15);
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Unavailable));
            service.Failing.TryRemove("piece-c", out _);
            presenter.Retry();
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-c"), "The retry brings the piece shown, not the card's record.");

            // The piece's small label names it from its own record when the visitor looks at it.
            var label = flow.GetComponent<MuseumPieceLabel>();
            Assert.That(label, Is.Not.Null);
            Assert.That(visitor.FocusPoint(point.Anchor, visitor.transform.position, presenter.Model.position), Is.True);
            yield return new WaitForSecondsRealtime(MuseumPieceLabel.FadeSeconds + .3f);
            Assert.That(label.Wanted, Is.True, "Looking at the piece shows its label.");
            Assert.That(label.Title.text, Is.EqualTo("Pieza sintética C"));
            Assert.That(label.Surface.gameObject.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator APieceTheVisitorStaysWithIsGrantedAgainAndLeavesWhenRevoked()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            // Eight-second grants stand in for the real five minutes: renewal comes a quarter of the life before the end.
            service = new SyntheticMuseumService { AccessSeconds = 8 };
            service.AddModel("piece-a", "Pieza sintética A", Convert.FromBase64String(RemoteModelPlayTests.Cube));
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
                        key = PointKey, name = "Punto", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "piece-a", title = "Pieza sintética A", has_3d_model = true } }
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
            Assert.That(visitor.FocusPoint(runtime.FindPoint(PointKey).Anchor), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Ready));
            // Past its first grant the piece stays: the access was asked for again, the file was not downloaded again.
            yield return new WaitForSecondsRealtime(14);
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Ready), presenter.Status);
            Assert.That(presenter.ModelSlug, Is.EqualTo("piece-a"));
            Assert.That(service.Grants("piece-a"), Is.GreaterThanOrEqualTo(3));
            Assert.That(service.Downloads("piece-a"), Is.EqualTo(1));
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            // Once the service no longer grants it, the piece goes at the next renewal and says so.
            service.Revoked["piece-a"] = true;
            yield return Until(() => presenter.State != ModelLoadState.Ready, 12);
            Assert.That(presenter.State, Is.EqualTo(ModelLoadState.Unavailable));
            Assert.That(presenter.Model, Is.Null);
        }
#endif
    }
}
