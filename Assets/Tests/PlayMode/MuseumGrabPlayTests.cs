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
    /// <summary>Taking a synthetic piece from its point in the saved museum, and its way back to the exhibition pose.</summary>
    public sealed class MuseumGrabPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string PointKey = "punto.personajes.pedestal_3";
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

        private static void AssertAt(Transform piece, Vector3 position, Quaternion rotation, string message)
        {
            Assert.That(Vector3.Distance(piece.position, position), Is.LessThan(.001f), message);
            Assert.That(Quaternion.Angle(piece.rotation, rotation), Is.LessThan(.1f), message);
        }

        [UnityTest]
        public IEnumerator ATakenPieceGoesBackToItsExhibitionPoseHoweverItIsLetGo()
        {
            InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            var cube = Convert.FromBase64String(RemoteModelPlayTests.Cube);
            service.AddModel("piece-a", "Pieza sintética A", cube);
            service.AddModel("piece-b", "Pieza sintética B", cube);
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
            var grab = flow.GetComponent<MuseumModelGrab>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var view = visitor.GetComponentInChildren<Camera>().transform;
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            Assert.That(grab, Is.Not.Null, "The visit installs direct handling of pieces.");
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
                        key = PointKey, name = "Punto sintético", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[]
                        {
                            new ElementSummaryContractV1 { slug = "piece-a", title = "Pieza sintética A", has_3d_model = true },
                            new ElementSummaryContractV1 { slug = "piece-b", title = "Pieza sintética B", has_3d_model = true },
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
            Assert.That(interaction.SelectElement(0), Is.True);
            yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
            yield return new WaitForSecondsRealtime(MuseumModelPresenter.RevealSeconds + .2f);
            var piece = presenter.Model;
            Assert.That(piece, Is.Not.Null);
            Assert.That(presenter.TryExhibitPose(out var home, out var homeRotation, out var homeScale), Is.True);
            AssertAt(piece, home, homeRotation, "The piece stands in its exhibition pose before anyone touches it.");
            Assert.That(piece.GetComponentsInChildren<Collider>().Length, Is.EqualTo(0), "A held piece cannot push the visitor.");

            // A hand close to the piece takes it, moves it and turns it.
            var hand = home + Vector3.back * .05f;
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.True);
            Assert.That(grab.Held, Is.True);
            Assert.That(presenter.PoseHeld, Is.True);
            var moved = Quaternion.Euler(10, 70, 0);
            grab.Follow(hand + new Vector3(.3f, .2f, -.2f), moved, 1);
            Assert.That(Vector3.Distance(piece.position, home), Is.GreaterThan(.3f));
            Assert.That(Quaternion.Angle(piece.rotation, homeRotation), Is.GreaterThan(60));
            presenter.RefreshPose();
            Assert.That(Vector3.Distance(piece.position, home), Is.GreaterThan(.3f), "The exhibition pose does not snatch a held piece.");
            yield return null;
            Assert.That(grab.Held, Is.True, "Nothing lets go on its own while the visit carries on.");

            // Letting go: it glides back, not to a pose derived from where it was dropped.
            grab.Release();
            Assert.That(grab.Returning, Is.True);
            var halfway = piece.position;
            yield return Until(() => !grab.Returning, 3);
            Assert.That(grab.Returning, Is.False);
            AssertAt(piece, home, homeRotation, "Released, the piece is back exactly in its exhibition pose.");
            Assert.That(piece.localScale.x, Is.EqualTo(homeScale.x).Within(.0001f));
            Assert.That(presenter.PoseHeld, Is.False);
            Assert.That(presenter.TryExhibitPose(out var homeAgain, out _, out _), Is.True);
            Assert.That(Vector3.Distance(homeAgain, home), Is.LessThan(.0001f), "Moving the piece never changed its saved pose.");
            Assert.That(Vector3.Distance(halfway, home), Is.GreaterThan(.2f), "It glided rather than jumped.");

            // Pausing the visit sends a held piece back.
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.True);
            grab.Follow(hand + Vector3.up * .3f, Quaternion.Euler(0, 40, 0), 1);
            interaction.SetPaused(true);
            yield return null;
            Assert.That(grab.Held, Is.False, "A pause lets go.");
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.False, "Nothing is taken during a pause.");
            interaction.SetPaused(false);
            yield return Until(() => !grab.Returning, 3);
            AssertAt(piece, home, homeRotation, "After a pause the piece is back.");

            // Carried too far from its pedestal, it goes back by itself.
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.True);
            grab.Follow(hand + Vector3.right * (MuseumXrConfiguration.Current.GrabMaxDistance + .5f), Quaternion.identity, 1);
            Assert.That(grab.Held, Is.False);
            yield return Until(() => !grab.Returning, 3);
            AssertAt(piece, home, homeRotation, "A piece carried away returns.");

            // On a desktop, the right button holds the piece; it turns with the mouse while the view stays still.
            Press(mouse.rightButton);
            Assert.That(grab.Take(view.position, view.rotation, true, true), Is.True);
            Assert.That(visitor.LookSuspended, Is.True);
            var viewBefore = view.rotation;
            grab.Turn(new Vector2(30, 0));
            grab.Follow(view.position, view.rotation, 1);
            Assert.That(Vector3.Distance(piece.position, view.position + view.forward * MuseumExperienceConfiguration.Current.GrabDesktopDistance),
                Is.LessThan(.01f));
            yield return null;
            Assert.That(grab.Held, Is.True, "Held while the button is down.");
            Assert.That(Quaternion.Angle(view.rotation, viewBefore), Is.LessThan(.01f));
            Release(mouse.rightButton);
            yield return null;
            Assert.That(grab.Held, Is.False, "Releasing the button lets go.");
            Assert.That(visitor.LookSuspended, Is.False);
            yield return Until(() => !grab.Returning, 3);
            AssertAt(piece, home, homeRotation, "Released on a desktop, the piece is back too.");

            // Changing piece while one is held drops the hold; the new piece takes its own pose.
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.True);
            Assert.That(interaction.SelectElement(1), Is.True);
            yield return null;
            Assert.That(grab.Held, Is.False);
            Assert.That(presenter.PoseHeld, Is.False);
            yield return Until(() => presenter.State == ModelLoadState.Ready && presenter.ModelSlug == "piece-b", 15);
            yield return new WaitForSecondsRealtime(MuseumModelPresenter.RevealSeconds + .2f);
            Assert.That(presenter.TryExhibitPose(out var otherHome, out var otherRotation, out _), Is.True);
            AssertAt(presenter.Model, otherHome, otherRotation, "The next piece stands in its own exhibition pose.");

            // Examining keeps working and takes the piece out of the hand.
            Assert.That(grab.Take(otherHome, Quaternion.identity, false, false), Is.True);
            Assert.That(presenter.Examine(), Is.True);
            yield return null;
            Assert.That(grab.Held, Is.False, "Examining takes the piece out of the hand.");
            Assert.That(presenter.PoseHeld, Is.False);
            Assert.That(grab.Take(otherHome, Quaternion.identity, false, false), Is.False, "Nothing is taken while examining.");
            interaction.Back();
            yield return null;
            AssertAt(presenter.Model, otherHome, otherRotation, "After the examination the piece is back on show.");
        }
#endif
    }
}
