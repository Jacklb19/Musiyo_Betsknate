using System.Collections;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumEntryPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";

        // The shipped actions with their default keys, bound to this fixture's devices and never to saved preferences.
        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        private static int RenderingCameras()
        {
            int count = 0;
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.isActiveAndEnabled) count++;
            return count;
        }

        private static Vector2 Floor(Transform target) => new Vector2(target.position.x, target.position.z);

        /// <summary>
        /// Without a local service the loader retries now and then; a press that falls in a retry would not begin the
        /// visit, so each press waits until the loader has settled.
        /// </summary>
        private static IEnumerator Settled(TourLoader loader)
        {
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>A failed check must not leave the museum loaded under the next test.</summary>
        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SavedMuseumStagesTheEntryGlidesInAndReturns()
        {
            // The menu saves the visitor's preferences as it is used; this test puts them back afterwards.
            bool hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            try
            {
                yield return null;
                var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
                var entry = flow.GetComponent<MuseumEntryView>();
                var loader = flow.GetComponent<TourLoader>();
                var interaction = flow.GetComponent<MuseumInteraction>();
                var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
                var eyes = visitor.GetComponentInChildren<Camera>(true);
                var viewpoint = Object.FindAnyObjectByType<MuseumEntryViewpoint>();
                entry.HeadsetActive = () => false;
                yield return null;

                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
                Assert.That(entry.Staged, Is.True, "The entry menu opens in front of the museum.");
                Assert.That(eyes.enabled, Is.False);
                Assert.That(RenderingCameras(), Is.EqualTo(1));
                Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
                // The entry opens on the scenic set when one is chosen (the hearth since 2026-10-17), else at the museum viewpoint.
                var opening = entry.BackdropActive ? entry.Backdrop.CameraPose.position : viewpoint.transform.position;
                Assert.That(Vector3.Distance(entry.StageCamera.transform.position, opening), Is.LessThan(.001f));

                // With or without a local service the entry settles; a failed load still lets the visitor walk in.
                for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                yield return null;
                var menu = flow.Menu.View;
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.Main.Primary.gameObject));

                var start = Floor(visitor.transform);
                Press(keyboard.wKey);
                for (int frame = 0; frame < 10; frame++) yield return null;
                Assert.That(Vector2.Distance(Floor(visitor.transform), start), Is.LessThan(.001f), "A walking key in the entry menu moves nobody.");
                Assert.That(EventSystem.current.currentSelectedGameObject.transform.IsChildOf(menu.Main.transform), Is.True);
                Release(keyboard.wKey);
                yield return null;
                EventSystem.current.SetSelectedGameObject(menu.Main.Primary.gameObject);
                yield return Settled(loader);

                Press(keyboard.enterKey);
                yield return null;
                Release(keyboard.enterKey);
                yield return null;
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Entering));
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Glide), "The saved viewpoint is near and in clear sight of the start.");
                // From the museum viewpoint the glide never darkens the screen; from a scenic set it first fades out of the set.
                if (!entry.BackdropActive) Assert.That(menu.Transition.gameObject.activeSelf, Is.False, "A glide never darkens the screen.");

                Press(keyboard.wKey);
                float travelled = 0;
                var previous = entry.StageCamera.transform.position;
                for (float waited = 0; flow.Phase == VisitPhase.Entering && waited < 10; waited += Time.unscaledDeltaTime)
                {
                    yield return null;
                    if (flow.Phase != VisitPhase.Entering) break;
                    Assert.That(entry.Staged, Is.True);
                    Assert.That(Vector2.Distance(Floor(visitor.transform), start), Is.LessThan(.001f), "The visitor stands still while the camera glides.");
                    travelled += Vector3.Distance(entry.StageCamera.transform.position, previous);
                    previous = entry.StageCamera.transform.position;
                }
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
                Assert.That(travelled, Is.GreaterThan(1), "The camera travelled from the viewpoint toward the visitor.");
                Release(keyboard.wKey);
                yield return null;
                Assert.That(entry.Staged, Is.False);
                Assert.That(eyes.enabled, Is.True);
                Assert.That(RenderingCameras(), Is.EqualTo(1));

                Press(keyboard.wKey);
                for (float waited = 0; waited < .6f; waited += Time.deltaTime) yield return null;
                Assert.That(visitor.transform.position.z, Is.GreaterThan(start.y + .2f), "Once inside, the same key walks.");
                Release(keyboard.wKey);
                yield return null;

                interaction.SetPaused(true);
                yield return null;
                Assert.That(menu.Pause.gameObject.activeInHierarchy, Is.True);
                Assert.That(entry.Staged, Is.False, "A pause stays where the visitor is.");
                menu.Pause.MainMenu.onClick.Invoke();
                yield return null;
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
                Assert.That(entry.Staged, Is.True, "Returning to the menu stages the entry again.");
                Assert.That(eyes.enabled, Is.False);

                // With a headset the entry is a fade and its view is left alone.
                entry.HeadsetActive = () => true;
                yield return null;
                Assert.That(entry.Staged, Is.False);
                Assert.That(eyes.enabled, Is.True);
                var pose = eyes.transform.position;
                yield return Settled(loader);
                menu.Main.Primary.onClick.Invoke();
                yield return null;
                Assert.That(flow.Transition, Is.EqualTo(EntryTransition.Fade));
                Assert.That(menu.Transition.gameObject.activeSelf, Is.True);
                for (float waited = 0; flow.Phase == VisitPhase.Entering && waited < 10; waited += Time.unscaledDeltaTime) yield return null;
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
                Assert.That(Vector3.Distance(eyes.transform.position, pose), Is.LessThan(.01f));
            }
            finally
            {
                if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
            if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
