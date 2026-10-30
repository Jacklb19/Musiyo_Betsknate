using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the companion staying in sight, for review: what the visitor sees after turning away, after a jump and
    /// while walking. Explicit: it runs only when named, writing to MUSIYO_CAPTURE_DIR or TestResults/companion-in-view.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumCompanionInViewCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/companion-in-view";

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>Renders what the visitor's own camera shows, at a size for review.</summary>
        private static IEnumerator Shot(string name, Camera eye)
        {
            yield return null;
            const int width = 1440, height = 900;
            var holder = new GameObject("CaptureCamera");
            var camera = holder.AddComponent<Camera>();
            camera.CopyFrom(eye);
            camera.fieldOfView = 70;
            holder.transform.SetPositionAndRotation(eye.transform.position, eye.transform.rotation);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(holder);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheCompanionInSight()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var settings = new UnityEditor.SerializedObject(flow.GetComponent<TourLoader>());
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            var eye = visitor.GetComponentInChildren<Camera>();
            void Place(Vector3 spot) { body.enabled = false; visitor.transform.position = spot; body.enabled = true; }
            yield return null;
            flow.EnterImmediately();
            Place(new Vector3(0, .05f, -48));
            visitor.transform.rotation = Quaternion.identity;
            for (float waited = 0; waited < 3; waited += Time.deltaTime) yield return null;
            yield return Shot("1_standing", eye);
            // Turned round: a moment later it has come back into sight.
            visitor.transform.rotation = Quaternion.Euler(0, 180, 0);
            yield return Shot("2_turned_just_now", eye);
            for (float waited = 0; waited < 6; waited += Time.deltaTime) yield return null;
            yield return Shot("3_turned_back_in_sight", eye);
            // Walking north: ahead and aside.
            visitor.transform.rotation = Quaternion.identity;
            for (float z = -48; z <= -38; z += 1.4f * Mathf.Min(Time.deltaTime, .1f)) { Place(new Vector3(0, .05f, z)); yield return null; }
            yield return Shot("4_walking_ahead", eye);
            // Taken at once to the courtyard.
            Place(new Vector3(0, .05f, -6));
            for (int frame = 0; frame < 5; frame++) yield return null;
            yield return Shot("5_after_jump", eye);
            Assert.That(companion.Placed, Is.True);
        }
#endif
    }
}
