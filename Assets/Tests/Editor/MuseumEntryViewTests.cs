using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumEntryViewTests
    {
        private GameObject root;
        private GameObject visitor;
        private GameObject anchor;
        private Camera visitorCamera;
        private MuseumEntryViewpoint viewpoint;
        private MuseumVisitFlow flow;
        private MuseumEntryView entry;
        private MuseumExperienceConfiguration configuration;

        [SetUp]
        public void SetUp()
        {
            configuration = MuseumExperienceConfiguration.Current;
            root = new GameObject("EntryTestRuntime");
            flow = root.AddComponent<MuseumVisitFlow>();
            visitor = new GameObject("EntryTestVisitor", typeof(CharacterController));
            var body = visitor.GetComponent<CharacterController>();
            body.height = 1.75f;
            body.center = Vector3.up * .875f;
            var view = new GameObject("EntryTestCamera", typeof(Camera), typeof(AudioListener));
            view.transform.SetParent(visitor.transform, false);
            view.transform.localPosition = Vector3.up * 1.65f;
            visitorCamera = view.GetComponent<Camera>();
            visitorCamera.fieldOfView = 70;
            anchor = new GameObject("EntryTestViewpoint");
            anchor.transform.SetPositionAndRotation(new Vector3(-1.5f, 2.4f, -4), Quaternion.Euler(8, 20, 0));
            viewpoint = anchor.AddComponent<MuseumEntryViewpoint>();
            viewpoint.Configure(50);
            entry = root.AddComponent<MuseumEntryView>();
            entry.HeadsetActive = () => false;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(visitor);
            Object.DestroyImmediate(anchor);
        }

        private void Configure() => entry.Configure(visitorCamera, visitor.transform, viewpoint);
        private void Advance(float share)
        {
            flow.Advance(flow.TransitionSeconds * share);
            entry.Apply();
        }

        [Test]
        public void TheMenuLooksAtTheMuseumFromTheViewpointAndLeavesTheVisitorCameraInPlace()
        {
            var restPosition = visitorCamera.transform.localPosition;
            var restRotation = visitorCamera.transform.localRotation;
            Configure();
            Assert.That(entry.Staged, Is.True);
            Assert.That(visitorCamera.enabled, Is.False, "Only one camera renders the entry.");
            var stage = entry.StageCamera;
            Assert.That(Vector3.Distance(stage.transform.position, anchor.transform.position), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(stage.transform.rotation, anchor.transform.rotation), Is.LessThan(.01f));
            Assert.That(stage.fieldOfView, Is.EqualTo(50).Within(.001f));
            Assert.That(stage.GetComponent<AudioListener>(), Is.Null, "The visitor keeps the only listener.");
            Assert.That(stage.CompareTag("MainCamera"), Is.False);
            Assert.That(visitorCamera.transform.localPosition, Is.EqualTo(restPosition));
            Assert.That(visitorCamera.transform.localRotation, Is.EqualTo(restRotation));
        }

        [Test]
        public void ANearClearEntryGlidesToTheVisitorsEyesAndHandsTheViewBack()
        {
            Configure();
            flow.BeginVisit();
            Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Glide), "The visitor's own body does not count as an obstacle.");
            Assert.That(flow.TransitionSeconds, Is.EqualTo(configuration.EntryGlideSeconds));
            Advance(.5f);
            var stage = entry.StageCamera;
            Assert.That(entry.Staged, Is.True);
            var halfway = Vector3.Lerp(anchor.transform.position, visitorCamera.transform.position, .5f);
            Assert.That(Vector3.Distance(stage.transform.position, halfway), Is.LessThan(.001f));
            Assert.That(stage.fieldOfView, Is.EqualTo(60).Within(.001f));
            Advance(.49f);
            Assert.That(Vector3.Distance(stage.transform.position, visitorCamera.transform.position), Is.LessThan(.05f),
                "The glide arrives at the visitor's eyes before the view is handed over.");
            Advance(.02f);
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
            Assert.That(entry.Staged, Is.False);
            Assert.That(visitorCamera.enabled, Is.True);
        }

        [Test]
        public void AFarOrBlockedEntryFadesAndChangesViewInTheDark()
        {
            Configure();
            visitor.transform.position = new Vector3(0, 0, configuration.EntryGlideMaxDistance + 10);
            flow.BeginVisit();
            Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
            Assert.That(flow.TransitionSeconds, Is.EqualTo(configuration.EntryFadeSeconds));
            Advance(configuration.EntryMenuFadeShare * .5f);
            Assert.That(entry.Staged, Is.True);
            Assert.That(Vector3.Distance(entry.StageCamera.transform.position, anchor.transform.position), Is.LessThan(.001f),
                "A fading entry never moves the camera through the museum.");
            Advance(configuration.EntryMenuFadeShare * .6f);
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Entering));
            Assert.That(entry.Staged, Is.False, "The view changes once the screen is dark.");
            Assert.That(visitorCamera.enabled, Is.True);

            flow.EnterImmediately();
            flow.ReturnToMenu();
            visitor.transform.position = Vector3.zero;
            var wall = new GameObject("EntryTestWall", typeof(BoxCollider));
            try
            {
                wall.transform.position = Vector3.Lerp(anchor.transform.position, visitorCamera.transform.position, .5f);
                wall.GetComponent<BoxCollider>().size = new Vector3(6, 6, .2f);
                flow.BeginVisit();
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade), "The camera does not glide through a wall.");
            }
            finally { Object.DestroyImmediate(wall); }
        }

        [Test]
        public void ReturningToTheMenuStagesTheEntryAgain()
        {
            Configure();
            flow.EnterImmediately();
            entry.Apply();
            Assert.That(entry.Staged, Is.False);
            flow.ReturnToMenu();
            entry.Apply();
            Assert.That(entry.Staged, Is.True);
            Assert.That(visitorCamera.enabled, Is.False);
            Assert.That(Vector3.Distance(entry.StageCamera.transform.position, anchor.transform.position), Is.LessThan(.001f));
        }

        [Test]
        public void AHeadsetKeepsItsOwnViewAndEntersThroughTheFade()
        {
            entry.HeadsetActive = () => true;
            var pose = visitorCamera.transform.position;
            Configure();
            Assert.That(entry.Staged, Is.False);
            Assert.That(visitorCamera.enabled, Is.True);
            flow.BeginVisit();
            Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
            Advance(.5f);
            Assert.That(entry.Staged, Is.False);
            Assert.That(visitorCamera.transform.position, Is.EqualTo(pose), "Nothing moves the view of a headset.");
        }

        [Test]
        public void WithoutAViewpointTheEntryStaysAsAFade()
        {
            entry.Configure(visitorCamera, visitor.transform, null);
            Assert.That(entry.Staged, Is.False);
            Assert.That(visitorCamera.enabled, Is.True);
            flow.BeginVisit();
            Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
        }

        [Test]
        public void RemovingTheEntryViewReturnsTheVisitorCamera()
        {
            Configure();
            var stage = entry.StageCamera.gameObject;
            typeof(MuseumEntryView).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(entry, null);
            Assert.That(visitorCamera.enabled, Is.True);
            Assert.That(flow.Entry, Is.Null);
            Assert.That(stage == null, Is.True);
        }
    }
}
