using System.Collections;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The companion guide and the music in the saved museum, with no content service running.</summary>
    public sealed class MuseumCompanionPlayTests
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
        [UnityTest]
        public IEnumerator TheCompanionWalksBehindTheVisitorOnFreeFloorAndWelcomesOnce()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            // No content service: the companion and the music must work without one.
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            var companion = Object.FindAnyObjectByType<MuseumCompanion>();
            var music = Object.FindAnyObjectByType<MuseumMusic>();
            var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            Assert.That(companion, Is.Not.Null);
            Assert.That(music, Is.Not.Null);
            Assert.That(music.Track, Is.Not.Null);
            yield return null;
            Assert.That(companion.Placed, Is.False, "Nothing appears before the visit begins.");
            flow.EnterImmediately();
            Place(body, new Vector3(0, .05f, -56));
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.That(companion.Placed, Is.True);
            Assert.That(companion.GetComponentsInChildren<Collider>(true), Is.Empty, "The companion never blocks the visitor.");
            Assert.That(Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(renderer => renderer.name == MuseumCompanion.BodyPiece).All(renderer => !renderer.enabled),
                "The seated placeholder is replaced by the walking figure.");
            AssertHumanSized(companion);

            bool welcomed = false;
            var log = new System.Text.StringBuilder();
            int sample = 0;
            float nearest = float.MaxValue, farthest = 0;
            // Walk north along the arrival path at an ordinary pace.
            for (float z = -56; z <= -30; z += 1.4f * Mathf.Min(Time.deltaTime, .1f))
            {
                Place(body, new Vector3(0, .05f, z));
                yield return null;
                welcomed |= companion.RemarkText == MuseumCompanionSettings.Current.WelcomeText;
                if (sample++ % 200 == 0) log.Append(z.ToString("F1") + ": " + companion.transform.position.ToString("F2") + " " + companion.WalkState + " dt " + Time.deltaTime.ToString("F3") + "\n");
                var offset = companion.transform.position - body.transform.position;
                offset.y = 0;
                if (z > -50) { nearest = Mathf.Min(nearest, offset.magnitude); farthest = Mathf.Max(farthest, offset.magnitude); }
                Assert.That(companion.Free(companion.transform.position, companion.transform.position.y, out _), Is.True,
                    "The companion stands on free floor at " + companion.transform.position);
            }
            for (float waited = 0; waited < MuseumCompanionSettings.Current.WelcomeDelay + 3; waited += Time.unscaledDeltaTime)
            {
                yield return null;
                welcomed |= companion.RemarkText == MuseumCompanionSettings.Current.WelcomeText;
            }
            Debug.Log("Companion walk:\n" + log);
            var rest = companion.transform.position - body.transform.position;
            rest.y = 0;
            Assert.That(welcomed, Is.True, "The neutral welcome is shown once the visit starts.");
            Assert.That(rest.magnitude, Is.InRange(MuseumCompanionSettings.Current.PersonalSpace * .8f, MuseumCompanionSettings.Current.FollowDistance + 1.2f),
                "It keeps a comfortable distance once the visitor stops (" + companion.WalkState + ", at " + companion.transform.position + ").");
            Assert.That(farthest, Is.LessThan(MuseumCompanionSettings.Current.CatchUpBeyond + 1.5f), "It keeps up while the visitor walks.");
            Assert.That(nearest, Is.GreaterThan(.4f), "It never walks into the visitor.");

            // Taken at once to the courtyard, the visitor is rejoined from behind.
            Place(body, new Vector3(0, .05f, -6));
            for (float waited = 0; waited < 4; waited += Time.deltaTime) yield return null;
            var joined = companion.transform.position - body.transform.position;
            joined.y = 0;
            Assert.That(joined.magnitude, Is.LessThan(MuseumCompanionSettings.Current.FollowDistance + 1.5f), "The companion rejoins after a jump.");
        }

        /// <summary>
        /// The headset build merges the museum's static meshes before the visit; the companion's figure must not change.
        /// A copy of a merged mesh once showed the whole room as a grey sheet of 8266 × 356 m (Quest 3, 22/10/2026).
        /// </summary>
        [UnityTest]
        public IEnumerator TheCompanionKeepsAPersonsSizeWhenTheMuseumIsStaticallyBatched()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            // As the Android player does: every piece marked for batching is merged into shared meshes.
            var batched = scene.GetRootGameObjects().Where(root => root.GetComponent<MuseumCompanion>() == null).ToArray();
            foreach (var root in batched) StaticBatchingUtility.Combine(root);
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var settings = new UnityEditor.SerializedObject(flow.GetComponent<TourLoader>());
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            var companion = Object.FindAnyObjectByType<MuseumCompanion>();
            var body = Object.FindAnyObjectByType<DesktopVisitorController>().GetComponent<CharacterController>();
            yield return null;
            flow.EnterImmediately();
            Place(body, new Vector3(0, .05f, -56));
            for (int frame = 0; frame < 10; frame++) yield return null;
            Assert.That(companion.Placed, Is.True);
            AssertHumanSized(companion);
            Assert.That(companion.Figure.Renderers.Select(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh)
                .All(mesh => mesh != null && !mesh.name.StartsWith("Combined")), Is.True, "The figure never shows a merged museum mesh.");
        }

        private static void AssertHumanSized(MuseumCompanion companion)
        {
            Assert.That(companion.Figure, Is.Not.Null);
            var bounds = companion.Figure.WorldBounds();
            Assert.That(bounds.size.y, Is.InRange(1.3f, 2.1f), "The figure stands as tall as a person: " + bounds.size);
            Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.InRange(.3f, 1.4f), "The figure is as wide as a person: " + bounds.size);
            Assert.That(Vector3.Distance(new Vector3(bounds.center.x, 0, bounds.center.z),
                new Vector3(companion.transform.position.x, 0, companion.transform.position.z)), Is.LessThan(.5f), "The figure stands where the companion is.");
        }
#endif
    }
}
