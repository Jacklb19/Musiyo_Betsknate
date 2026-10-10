using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumInteractionTests
    {
        private GameObject root;
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private TourPoint first;
        private TourPoint second;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("InteractionTest");
            runtime = root.AddComponent<TourRuntime>();
            interaction = root.AddComponent<MuseumInteraction>();
            var room = new GameObject("Room");
            room.transform.SetParent(root.transform);
            room.AddComponent<RoomAnchor>().Configure("room.test");
            first = CreatePoint(room.transform, "first");
            second = CreatePoint(room.transform, "second");
            Apply();
        }

        private TourPoint CreatePoint(Transform room, string key)
        {
            var location = new GameObject(key);
            location.transform.SetParent(room);
            location.AddComponent<PointAnchor>().Configure(key, 1.8f, location.transform);
            return location.AddComponent<TourPoint>();
        }

        private void Apply(bool multiple = false, bool empty = false)
        {
            var elements = empty ? new ElementSummaryContractV1[0] : multiple
                ? new[] { new ElementSummaryContractV1 { slug = "one", title = "One" }, new ElementSummaryContractV1 { slug = "two", title = "Two" } }
                : new[] { new ElementSummaryContractV1 { slug = "one", title = "One" } };
            var contract = new TourContractV1 { schema_version = 1,
                tour = new TourMetadataContractV1 { key = "museum-main", name = "Test" },
                rooms = new[] { new RoomContractV1 { key = "room.test", name = "Test", order = 0,
                    points = new[] {
                        new PointContractV1 { key = "first", name = "First", order = 0, activation = new[] { "keyboard", "gaze", "proximity" }, elements = elements },
                        new PointContractV1 { key = "second", name = "Second", order = 1, activation = new[] { "keyboard", "gaze", "proximity" }, elements = elements }
                    } } }
            };
            Assert.That(runtime.Apply(contract, out var error), Is.True, error);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [TestCase(ActivationSource.Keyboard)]
        [TestCase(ActivationSource.Proximity)]
        [TestCase(ActivationSource.Gaze)]
        public void EveryActivatorSelectsTheSameElement(ActivationSource source)
        {
            Assert.That(interaction.Activate(first, source), Is.True);
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("one"));
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            Assert.That(interaction.LastSource, Is.EqualTo(source));
        }

        [Test]
        public void SingleElementActivationNotifiesWithTheCompletedSelection()
        {
            int notifications = 0;
            interaction.Changed += () =>
            {
                notifications++;
                Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
                Assert.That(interaction.SelectedElement.slug, Is.EqualTo("one"));
            };
            Assert.That(interaction.Activate(first, ActivationSource.Keyboard), Is.True);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void EmptyPointAndDisabledMethodCannotActivate()
        {
            first.Content.activation = new[] { "keyboard" };
            Assert.That(interaction.Activate(first, ActivationSource.Gaze), Is.False);
            Apply(empty: true);
            Assert.That(interaction.Activate(first, ActivationSource.Keyboard), Is.False);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Exploration));
        }

        [Test]
        public void MultipleElementsRequireSelectionAndRejectInvalidIndex()
        {
            Apply(multiple: true);
            interaction.Activate(first, ActivationSource.Keyboard);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.PointFocus));
            Assert.That(interaction.SelectedElement, Is.Null);
            Assert.That(interaction.SelectElement(9), Is.False);
            Assert.That(interaction.SelectElement(1), Is.True);
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("two"));
        }

        [Test]
        public void ActivatingAnotherPointClearsThePreviousPoint()
        {
            interaction.Activate(first, ActivationSource.Keyboard);
            interaction.Activate(second, ActivationSource.Gaze);
            Assert.That(first.State, Is.EqualTo(PointState.Available));
            Assert.That(second.State, Is.EqualTo(PointState.Active));
            Assert.That(interaction.ActivePoint, Is.SameAs(second));
        }

        [Test]
        public void PauseRestoresReadingAndPreventsActivation()
        {
            interaction.Activate(first, ActivationSource.Keyboard);
            Assert.That(interaction.OpenDetail(), Is.True);
            Assert.That(interaction.BlocksMovement, Is.True);
            interaction.SetPaused(true);
            Assert.That(interaction.Activate(second, ActivationSource.Keyboard), Is.False);
            interaction.SetPaused(false);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Reading));
            interaction.Back();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
            interaction.Close();
            Assert.That(interaction.BlocksMovement, Is.False);
            Assert.That(interaction.SelectedElement, Is.Null);
        }

        [Test]
        public void EntryFlowBlocksActivationUntilTransitionFinishesAndPreservesReadingOnReturn()
        {
            var flow = root.AddComponent<MuseumVisitFlow>();
            Assert.That(interaction.BlocksMovement, Is.True);
            Assert.That(interaction.Activate(first, ActivationSource.Keyboard), Is.False);
            flow.BeginVisit();
            flow.Advance(MuseumExperienceConfiguration.Current.EntryFadeSeconds * .5f);
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Entering));
            Assert.That(interaction.Activate(first, ActivationSource.Proximity), Is.False);
            flow.Advance(float.NaN);
            Assert.That(flow.TransitionProgress, Is.EqualTo(.5f).Within(.001));
            flow.Advance(MuseumExperienceConfiguration.Current.EntryFadeSeconds);
            Assert.That(interaction.BlocksMovement, Is.False);
            Assert.That(interaction.Activate(first, ActivationSource.Keyboard), Is.True);
            interaction.OpenDetail();
            flow.ReturnToMenu();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Paused));
            flow.BeginVisit();
            flow.Advance(MuseumExperienceConfiguration.Current.EntryFadeSeconds);
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Reading));
            Assert.That(interaction.SelectedElement.slug, Is.EqualTo("one"));
        }

        [Test]
        public void PresenceLeavesBeyondTheMarginReturnsInsideTheRadiusAndResetsPerPoint()
        {
            var presence = root.AddComponent<MuseumPointPresence>();
            var changes = new System.Collections.Generic.List<bool>();
            presence.Changed += changes.Add;
            float radius = first.Anchor.ActivationRadius, margin = MuseumExperienceConfiguration.Current.PointLeaveMargin;
            presence.Step(Vector3.forward * (radius + margin + 1));
            Assert.That(changes, Is.Empty, "Without an active point there is nothing to leave.");
            Assert.That(interaction.Activate(first, ActivationSource.Keyboard), Is.True);
            presence.Step(Vector3.forward * (radius + margin * .5f));
            Assert.That(presence.Away, Is.False, "Between the radius and the margin the visitor is still near.");
            presence.Step(Vector3.forward * (radius + margin + .1f) + Vector3.up * 5);
            Assert.That(presence.Away, Is.True, "Height is ignored; only the floor distance counts.");
            interaction.SetPaused(true);
            presence.Step(Vector3.zero);
            Assert.That(presence.Away, Is.True, "A paused visit keeps its presence.");
            interaction.SetPaused(false);
            presence.Step(Vector3.forward * (radius + .1f));
            Assert.That(presence.Away, Is.True, "Returning halfway stays away.");
            presence.Step(Vector3.forward * (radius - .1f));
            Assert.That(changes, Is.EqualTo(new[] { true, false }));
            presence.Step(Vector3.forward * (radius + margin + 1));
            interaction.Close();
            Assert.That(interaction.Activate(second, ActivationSource.Keyboard), Is.True);
            presence.Step(Vector3.zero);
            Assert.That(presence.Away, Is.False, "A newly activated point starts near.");
            Assert.That(MuseumWebBridge.PresenceValue(true), Is.EqualTo("away"));
            Assert.That(MuseumWebBridge.PresenceValue(false), Is.EqualTo("near"));
        }

        [Test]
        public void RefreshRemovesStaleSelection()
        {
            interaction.Activate(first, ActivationSource.Keyboard);
            runtime.Clear();
            Assert.That(interaction.SelectedElement, Is.Null);
            Assert.That(interaction.ActivePoint, Is.Null);
        }

        [Test]
        public void ExaminationRetainsItsStateAcrossReadingAndPause()
        {
            Assert.That(interaction.OpenModel(), Is.False);
            interaction.Activate(first, ActivationSource.Keyboard);
            Assert.That(interaction.OpenModel(), Is.True);
            Assert.That(interaction.BlocksMovement, Is.True);
            Assert.That(interaction.OpenDetail(), Is.True);
            interaction.SetPaused(true);
            interaction.Back();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.Reading));
            interaction.Back();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ModelExamination));
            interaction.Back();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
        }

        [Test]
        public void ExpiredModelCannotBeRestoredFromReadingOrPause()
        {
            interaction.Activate(first, ActivationSource.Keyboard);
            interaction.OpenModel();
            interaction.OpenDetail();
            interaction.SetPaused(true);
            interaction.CancelModel();
            interaction.Back();
            interaction.Back();
            Assert.That(interaction.State, Is.EqualTo(InteractionState.ElementSelected));
        }
    }

    public sealed class PointActivationTests
    {
        [Test]
        public void ClosedPointCannotDwellActivateAgainUntilLeaving()
        {
            var dwell = new DwellActivation();
            dwell.SuppressUntilExit("point.test");
            Assert.That(dwell.Step("point.test", true, 2, .8f), Is.False);
            Assert.That(dwell.Step("point.test", true, 2, .8f), Is.False);
            Assert.That(dwell.Progress, Is.Zero, "A suppressed point must not show a filling indicator.");
            Assert.That(dwell.Step(null, false, 0, .8f), Is.False);
            Assert.That(dwell.Step("point.test", true, 1, .8f), Is.True);
        }

        [Test]
        public void DwellRequiresContinuousTimeAndDoesNotRepeatUntilLeaving()
        {
            var dwell = new DwellActivation();
            Assert.That(dwell.Step("one", true, .7f, .8f), Is.False);
            Assert.That(dwell.Step("one", false, .1f, .8f), Is.False);
            Assert.That(dwell.Step("one", true, .2f, .8f), Is.False);
            Assert.That(dwell.Step("one", true, .7f, .8f), Is.True);
            Assert.That(dwell.Step("one", true, 1, .8f), Is.False);
            Assert.That(dwell.Step("two", true, .2f, .8f), Is.False);
        }

        [Test]
        public void GazeCannotActivateThroughWalls()
        {
            var observer = new GameObject("Observer");
            var target = new GameObject("Target");
            target.transform.position = Vector3.forward * 3;
            target.AddComponent<PointAnchor>().Configure("point", 1.8f, target.transform);
            var point = target.AddComponent<TourPoint>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = Vector3.forward;
            try
            {
                Physics.SyncTransforms();
                Assert.That(PointActivation.IsVisible(point, observer.transform, 4, 6), Is.False);
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                Assert.That(PointActivation.IsVisible(point, observer.transform, 4, 6), Is.True);
            }
            finally
            {
                if (wall != null) Object.DestroyImmediate(wall);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(observer);
            }
        }
    }
}
