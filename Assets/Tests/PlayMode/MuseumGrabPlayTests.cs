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
        private const string PointKey = "punto.personajes.pedestal_1";
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

        /// <summary>Puts the visitor on the floor this far south of a point, so a floating piece is near or left behind.</summary>
        private static void StandAt(DesktopVisitorController visitor, Vector3 point, float distance)
        {
            var body = visitor.transform;
            body.position = new Vector3(point.x, body.position.y, point.z - distance);
            Physics.SyncTransforms();
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

            // Let go away from its pedestal by a hand, it pauses only a moment where it was left, then glides slowly home.
            var dropped = piece.position;
            grab.Release();
            Assert.That(grab.Resting, Is.True);
            Assert.That(grab.Returning, Is.False);
            Assert.That(presenter.PoseHeld, Is.True);
            yield return null;
            Assert.That(Vector3.Distance(piece.position, dropped), Is.LessThan(.001f), "A piece let go does not jump back.");
            float letGo = Time.unscaledTime;
            yield return Until(() => grab.Returning, 2);
            Assert.That(grab.Returning, Is.True, "After its short pause the piece goes back by itself.");
            Assert.That(Time.unscaledTime - letGo, Is.LessThan(MuseumXrConfiguration.Current.GrabReleasePauseSeconds + .5f),
                "It never stays floating for long.");
            yield return null;
            var halfway = piece.position;
            float glideStart = Time.unscaledTime;
            yield return Until(() => !grab.Returning, 4);
            Assert.That(grab.Returning, Is.False);
            Assert.That(Time.unscaledTime - glideStart, Is.GreaterThan(MuseumXrConfiguration.Current.GrabReleaseReturnSeconds * .7f),
                "It glides home slowly.");
            AssertAt(piece, home, homeRotation, "Released, the piece is back exactly in its exhibition pose.");
            Assert.That(piece.localScale.x, Is.EqualTo(homeScale.x).Within(.0001f));
            Assert.That(presenter.PoseHeld, Is.False);
            Assert.That(presenter.TryExhibitPose(out var homeAgain, out _, out _), Is.True);
            Assert.That(Vector3.Distance(homeAgain, home), Is.LessThan(.0001f), "Moving the piece never changed its saved pose.");
            Assert.That(Vector3.Distance(halfway, home), Is.GreaterThan(.2f), "It glided rather than jumped.");
            StandAt(visitor, home, 1);

            // Let go on its pedestal, it goes back onto it at once.
            Assert.That(grab.Take(hand, Quaternion.identity, false, false), Is.True);
            grab.Follow(hand + Vector3.up * (MuseumXrConfiguration.Current.GrabSnapDistance * .5f), Quaternion.Euler(0, 20, 0), 1);
            grab.Release();
            Assert.That(grab.Resting, Is.False);
            Assert.That(grab.Returning, Is.True, "Let go on the pedestal, the piece is put back.");
            yield return Until(() => !grab.Returning, 3);
            AssertAt(piece, home, homeRotation, "Put back on its pedestal.");

            // Either hand takes it; the other hand joins, and turning both hands around it turns the piece.
            var rightHand = home + Vector3.right * .05f;
            var leftHand = home + Vector3.left * .05f;
            Assert.That(grab.Take(GrabHand.Right, rightHand, Quaternion.identity, false), Is.True);
            Assert.That(grab.Take(GrabHand.Left, leftHand, Quaternion.identity, false), Is.True);
            Assert.That(grab.HeldWithBothHands, Is.True);
            var quarter = Quaternion.Euler(0, 90, 0);
            grab.FollowHands(home + quarter * (rightHand - home), quarter, home + quarter * (leftHand - home), quarter, 1);
            Assert.That(Quaternion.Angle(piece.rotation, quarter * homeRotation), Is.LessThan(1), "Both hands turn the piece between them.");
            Assert.That(Vector3.Distance(piece.position, home), Is.LessThan(.01f), "Turning between the hands keeps it in place.");
            // The right hand lets go: the left one carries on from where the piece is, so it passes from hand to hand.
            var handedOver = piece.position;
            grab.Release(GrabHand.Right);
            Assert.That(grab.HeldBy(GrabHand.Left), Is.True);
            Assert.That(grab.HeldBy(GrabHand.Right), Is.False);
            Assert.That(Vector3.Distance(piece.position, handedOver), Is.LessThan(.001f), "Handing over does not move the piece.");
            var lifted = home + quarter * (leftHand - home) + Vector3.up * .4f;
            grab.Follow(GrabHand.Left, lifted, quarter, 1);
            Assert.That(Vector3.Distance(piece.position, handedOver + Vector3.up * .4f), Is.LessThan(.001f), "The other hand carries it.");
            // The stick turns the piece about its own centre.
            var beforeStick = piece.rotation;
            var centre = piece.position;
            grab.TurnInHands(new Vector2(30, 0), Vector3.right);
            Assert.That(Quaternion.Angle(piece.rotation, beforeStick), Is.EqualTo(30).Within(.5f));
            Assert.That(Vector3.Distance(piece.position, centre), Is.LessThan(.001f));
            grab.Follow(GrabHand.Left, lifted, quarter, 1);
            Assert.That(Quaternion.Angle(piece.rotation, beforeStick), Is.EqualTo(30).Within(.5f), "The hand keeps the turn the stick gave.");
            grab.Release(GrabHand.Left);
            Assert.That(grab.Resting, Is.True, "Let go in the air, it pauses there a moment.");
            // Caught again before it goes home, it is the same piece where it was left.
            var floating = piece.position;
            Assert.That(grab.Take(GrabHand.Right, floating + Vector3.back * .05f, Quaternion.identity, false), Is.True);
            Assert.That(Vector3.Distance(piece.position, floating), Is.LessThan(.001f));
            grab.ReturnHome();
            yield return Until(() => !grab.Returning, 3);
            AssertAt(piece, home, homeRotation, "Sent home, it is back on show.");

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
