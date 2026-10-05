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
        public void RefreshRemovesStaleSelection()
        {
            interaction.Activate(first, ActivationSource.Keyboard);
            runtime.Clear();
            Assert.That(interaction.SelectedElement, Is.Null);
            Assert.That(interaction.ActivePoint, Is.Null);
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
