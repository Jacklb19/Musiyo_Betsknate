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
    /// <summary>
    /// A piece named in the piece sizes floats at its point at the size of its digitisation (owner, 28/10/2026: the masks
    /// «mucho mas grandes», as in real life), keeps that size in the hand, and is framed as before when examined.
    /// </summary>
    public sealed class MuseumPieceSizePlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Point = "punto.personajes.pedestal_1";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            MuseumPieceSizes.Replace(null);
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

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static Bounds BoundsOf(Transform model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var item in renderers) bounds.Encapsulate(item.bounds);
            return bounds;
        }

        private static float Longest(Bounds bounds) => Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator APieceAtItsScannedSizeFloatsAtItAndIsExaminedAsBefore()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            // The synthetic cube is 2 m on each side in its glTF, which is metres.
            MuseumPieceSizes.Replace(MuseumPieceSizes.Parse("{\"schema_version\":1,\"scanned_size\":[\"mask-a\"]}"));
            service = new SyntheticMuseumService();
            service.AddModel("mask-a", "Máscara sintética", Convert.FromBase64String(RemoteModelPlayTests.Cube));
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
                    Room("room.arrival", 0), Room("room.welcome", 1),
                    Room("room.characters", 2, new PointContractV1
                    {
                        key = Point, name = Point, order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "mask-a", title = "Máscara sintética", has_3d_model = true } }
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            var point = runtime.FindPoint(Point);
            Assert.That(point.Anchor.GetComponentInChildren<PointExhibitMount>(), Is.Null, "The masks float at their points.");

            yield return Until(() => presenter.ResidentCount == 1, 30);
            Assert.That(presenter.TryResident(point, out _, out var model), Is.True);
            yield return new WaitForSecondsRealtime(1);
            var standing = BoundsOf(model);
            Assert.That(Longest(standing), Is.EqualTo(2).Within(.05f), "At the size of its digitisation, not 0.6 m.");
            Assert.That(standing.center.y, Is.EqualTo(point.Anchor.LookTarget.position.y).Within(.05f), "Centred where it floated before.");
            Assert.That(standing.min.y, Is.GreaterThan(point.Anchor.transform.position.y), "Above the floor.");

            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            yield return Until(() => interaction.ActivePoint == point && presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "mask-a", 10);
            Assert.That(presenter.TryExhibitPose(out _, out _, out var scale), Is.True);
            Assert.That(scale.x * ModelExaminationPose.ExhibitSize, Is.EqualTo(2).Within(.02f),
                "Taken in the hand, it keeps the size it stands at.");

            Assert.That(interaction.OpenModel(), Is.True);
            yield return null;
            yield return null;
            Assert.That(Longest(BoundsOf(presenter.Model)), Is.LessThan(ModelExaminationPose.ExhibitSize + .01f),
                "Examined, it is framed in the view as every piece is.");
            interaction.Back();
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Longest(BoundsOf(presenter.Model)), Is.EqualTo(2).Within(.05f), "Back at its point at its size.");
        }
#endif
    }
}
