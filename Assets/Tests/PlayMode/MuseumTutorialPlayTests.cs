using System.Collections;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The first-visit tutorial and the controller labels in the saved museum, with no content service running.</summary>
    public sealed class MuseumTutorialPlayTests
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static void Place(CharacterController body, Vector3 position)
        {
            body.enabled = false;
            body.transform.position = position;
            body.enabled = true;
        }

#if UNITY_EDITOR
        private static IEnumerator OpenMuseum(bool firstVisit)
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var settings = new UnityEditor.SerializedObject(flow.GetComponent<TourLoader>());
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            yield return null;
            Assert.That(flow.Menu, Is.Not.Null);
            flow.Menu.Preferences.SetTutorialDone(!firstVisit);
        }

        /// <summary>
        /// On a first visit the colibrí proposes the steps in the arrival, one at a time, in its own bubble; each is done by
        /// doing it, holding the back control skips the rest, and the visit remembers that the tutorial ran.
        /// </summary>
        [UnityTest]
        public IEnumerator OnAFirstVisitTheColibriProposesOneStepAtATimeUntilSkipped()
        {
            yield return OpenMuseum(true);
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var tutorial = interaction.GetComponent<MuseumFirstVisitTutorial>();
            var companion = Object.FindAnyObjectByType<MuseumCompanion>();
            var body = Object.FindAnyObjectByType<DesktopVisitorController>().GetComponent<CharacterController>();
            Assert.That(tutorial, Is.Not.Null, "The menu adds the tutorial to the visit.");
            yield return null;
            yield return null;
            Assert.That(MuseumFirstVisitTutorial.Pending, Is.True, "A first visit has the tutorial ahead.");
            bool announced = false;
            void Heard(bool active) => announced |= active;
            MuseumFirstVisitTutorial.ActiveChanged += Heard;
            try
            {
                flow.EnterImmediately();
                Place(body, new Vector3(0, .05f, -48));
                body.transform.rotation = Quaternion.identity;
                var settings = MuseumTutorialSettings.Current;
                for (float waited = 0; !tutorial.Running && waited < settings.StartAtLatest + 3; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Running, Is.True, "It starts in the arrival (zone " + companion.Zone + ").");
                Assert.That(MuseumFirstVisitTutorial.Active && announced, Is.True, "Others hear that it speaks (room names wait for it).");
                Assert.That(flow.Menu.Preferences.TutorialDone, Is.False, "It is remembered only once it ends or is skipped.");
                Assert.That(MuseumFirstVisitTutorial.Pending, Is.False, "Started, it is no longer pending.");
                Assert.That(companion.Muted, Is.True, "The companion's own remarks keep quiet meanwhile.");
                for (float waited = 0; !tutorial.Showing && waited < 2; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Text, Does.Contain(MuseumInterfaceText.Get("tutorial_turn")), "Step one, in the companion's bubble: " + tutorial.Text);
                Assert.That(tutorial.Text, Does.Contain("1 de 6"));
                Assert.That(tutorial.Bubble.parent, Is.EqualTo(companion.transform), "The words come from the colibrí, not from a panel.");
                Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(canvas => canvas.name == "TutorialRemark"), Is.EqualTo(1));

                // Turning the view does step one; the next one is proposed.
                for (int frame = 0; frame < 30; frame++)
                {
                    body.transform.rotation = Quaternion.Euler(0, frame * 4, 0);
                    yield return null;
                }
                Assert.That(tutorial.Progress.Done, Does.Contain(TutorialStep.Turn));
                for (float waited = 0; waited < settings.DoneSeconds + .6f; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Text, Does.Contain(MuseumInterfaceText.Get("tutorial_move")), tutorial.Text);
                // A few steps forward do the second.
                for (float z = -48; z < -46; z += .05f) { Place(body, new Vector3(0, .05f, z)); yield return null; }
                Assert.That(tutorial.Progress.Done, Does.Contain(TutorialStep.Move));
                // Opening the menu does the third, and the bubble keeps quiet over it.
                interaction.SetPaused(true);
                for (float waited = 0; waited < .6f; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(tutorial.Progress.Done, Does.Contain(TutorialStep.Menu));
                Assert.That(tutorial.Showing, Is.False, "Nothing is said over the menu.");
                Assert.That(flow.Menu.TutorialButton.gameObject.activeInHierarchy, Is.True, "The pause menu offers the tutorial.");
                interaction.SetPaused(false);
                for (float waited = 0; waited < settings.DoneSeconds + .6f; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Text, Does.Contain(MuseumInterfaceText.Get("tutorial_approach")), tutorial.Text);

                // The visitor skips the rest.
                tutorial.Skip();
                for (float waited = 0; waited < .5f; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Text, Does.Contain(MuseumInterfaceText.Get("tutorial_skipped")));
                Assert.That(flow.Menu.Preferences.TutorialDone, Is.True, "Skipped, it is remembered: it never starts by itself again.");
                Assert.That(MuseumFirstVisitTutorial.Active && !MuseumFirstVisitTutorial.Pending, Is.True,
                    "Its last word still holds back other notices (the room's name comes after it).");
                for (float waited = 0; tutorial.Running && waited < settings.EndSeconds + 2; waited += Time.deltaTime) yield return null;
                Assert.That(tutorial.Running || MuseumFirstVisitTutorial.Active || MuseumFirstVisitTutorial.Pending, Is.False);
                Assert.That(companion.Muted, Is.False, "The companion speaks again.");

                // Asked again from the pause menu, it starts over, the menu already known.
                interaction.SetPaused(true);
                yield return null;
                flow.Menu.TutorialButton.onClick.Invoke();
                yield return null;
                Assert.That(tutorial.Running, Is.True);
                Assert.That(interaction.State, Is.Not.EqualTo(InteractionState.Paused), "Asking for it returns to the visit.");
                Assert.That(tutorial.Progress.Done, Does.Contain(TutorialStep.Menu));
                tutorial.Skip();
            }
            finally { MuseumFirstVisitTutorial.ActiveChanged -= Heard; }
        }

        /// <summary>
        /// A tutorial left halfway (the visit went back to the menu) is not remembered and starts again on the next entry
        /// into the arrival; one finished to its last step is remembered and does not.
        /// </summary>
        [UnityTest]
        public IEnumerator ATutorialLeftHalfwayStartsAgainButAFinishedOneDoesNot()
        {
            yield return OpenMuseum(true);
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var tutorial = flow.GetComponent<MuseumFirstVisitTutorial>();
            var body = Object.FindAnyObjectByType<DesktopVisitorController>().GetComponent<CharacterController>();
            var settings = MuseumTutorialSettings.Current;
            IEnumerator Enter()
            {
                flow.EnterImmediately();
                Place(body, new Vector3(0, .05f, -48));
                body.transform.rotation = Quaternion.identity;
                for (float waited = 0; !tutorial.Running && waited < settings.StartAtLatest + 3; waited += Time.deltaTime) yield return null;
            }
            yield return Enter();
            Assert.That(tutorial.Running, Is.True);
            tutorial.Progress.MarkDone(TutorialStep.Turn);
            yield return null;

            // Back to the menu halfway: nothing is remembered and it waits for the next entry.
            flow.ReturnToMenu();
            yield return null;
            yield return null;
            Assert.That(tutorial.Running || MuseumFirstVisitTutorial.Active, Is.False, "Leaving the museum ends it quietly.");
            Assert.That(flow.Menu.Preferences.TutorialDone, Is.False, "Left halfway, it is not remembered.");
            Assert.That(MuseumFirstVisitTutorial.Pending, Is.True, "It is about to speak again.");
            yield return Enter();
            Assert.That(tutorial.Running, Is.True, "It starts again in the arrival.");
            Assert.That(tutorial.Progress.CurrentNumber, Is.EqualTo(1), "From its first step.");

            // Done to its last step: remembered, with its last word.
            foreach (var step in new[] { TutorialStep.Turn, TutorialStep.Move, TutorialStep.Menu, TutorialStep.Approach, TutorialStep.Grab, TutorialStep.Companion })
                tutorial.Progress.MarkDone(step);
            for (float waited = 0; !flow.Menu.Preferences.TutorialDone && waited < settings.DoneSeconds + 2; waited += Time.deltaTime) yield return null;
            Assert.That(flow.Menu.Preferences.TutorialDone, Is.True, "Finished, it is remembered.");
            Assert.That(MuseumFirstVisitTutorial.Active, Is.True, "Its last word is still being said.");
            for (float waited = 0; tutorial.Running && waited < settings.EndSeconds + 2; waited += Time.deltaTime) yield return null;
            Assert.That(MuseumFirstVisitTutorial.Active || MuseumFirstVisitTutorial.Pending, Is.False);
            flow.ReturnToMenu();
            yield return null;
            yield return Enter();
            Assert.That(tutorial.Running, Is.False, "Finished once, it does not start by itself again.");
        }

        [UnityTest]
        public IEnumerator AVisitorWhoHadTheTutorialIsNotShownItAgain()
        {
            yield return OpenMuseum(false);
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var tutorial = flow.GetComponent<MuseumFirstVisitTutorial>();
            var body = Object.FindAnyObjectByType<DesktopVisitorController>().GetComponent<CharacterController>();
            flow.EnterImmediately();
            Place(body, new Vector3(0, .05f, -48));
            for (float waited = 0; waited < MuseumTutorialSettings.Current.StartAtLatest + 2; waited += Time.deltaTime) yield return null;
            Assert.That(tutorial.Running, Is.False);
            Assert.That(MuseumFirstVisitTutorial.Pending, Is.False);
        }

        /// <summary>
        /// The labels show beside a controller the visitor looks at, and during the tutorial; never with the option off or
        /// for a hand without its controller.
        /// </summary>
        [UnityTest]
        public IEnumerator ControllerLabelsShowWhenTheVisitorLooksAtAController()
        {
            yield return OpenMuseum(false);
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var labels = flow.GetComponent<MuseumControllerLabels>();
            Assert.That(labels, Is.Not.Null, "The menu adds the labels to the visit.");
            var eye = Object.FindAnyObjectByType<DesktopVisitorController>().GetComponentInChildren<Camera>();
            flow.EnterImmediately();
            yield return null;
            bool rightTracked = true;
            Vector3 rightAt = new Vector3(.08f, -.15f, .4f);
            // Poses in the eye's own space: the right controller low in front, the left one out of sight.
            labels.Space = eye.transform;
            labels.Head = eye;
            labels.Poses = right => right ? (rightTracked, rightAt, Quaternion.Euler(-30, 0, 0)) : (true, new Vector3(-.6f, -.5f, -.2f), Quaternion.identity);
            yield return Wait(.5f);
            Assert.That(labels.Showing(true), Is.True, "Looked at, the right controller shows its labels.");
            Assert.That(labels.Showing(false), Is.False, "The left one, out of sight, does not.");
            Assert.That(labels.Texts(true), Does.Contain(MuseumInterfaceText.Get("controller_label_grab")));
            Assert.That(labels.Texts(false), Does.Contain(MuseumInterfaceText.Get("controller_label_ask")).And.Contain(MuseumInterfaceText.Get("controller_label_menu")));
            var boxes = labels.Boxes(true).ToList();
            Assert.That(boxes.Count, Is.EqualTo(5));
            for (int one = 0; one < boxes.Count; one++)
                for (int other = one + 1; other < boxes.Count; other++)
                    Assert.That(boxes[one].Overlaps(boxes[other]), Is.False, "Held tilted, no label covers another.");
            rightAt = new Vector3(.6f, -.6f, .1f);
            yield return Wait(.5f);
            Assert.That(labels.Showing(true), Is.False, "Looked away, they fade.");
            labels.ForceShow = true;
            yield return Wait(.5f);
            Assert.That(labels.Showing(true) && labels.Showing(false), Is.True);
            rightTracked = false;
            yield return Wait(.5f);
            Assert.That(labels.Showing(true), Is.False, "A hand without its controller has no labels.");
            flow.Menu.Preferences.SetControllerLabels(false);
            yield return Wait(.5f);
            Assert.That(labels.Showing(false), Is.False, "The visitor turned them off.");
            flow.Menu.Preferences.SetControllerLabels(true);
        }

        private static IEnumerator Wait(float seconds)
        {
            for (float waited = 0; waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }
#endif
    }
}
