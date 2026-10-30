using System;
using System.Collections;
using System.IO;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the first-visit tutorial and the controller labels, for review. Explicit: it runs only when named,
    /// writing to MUSIYO_CAPTURE_DIR or TestResults/tutorial. The labels are shown on stand-in controller poses in front of
    /// the screen's camera, as a headset would hold them.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumTutorialCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/tutorial";

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator Shot(string name, Camera eye, float fov = 70)
        {
            yield return null;
            const int width = 1440, height = 900;
            var holder = new GameObject("CaptureCamera");
            var camera = holder.AddComponent<Camera>();
            camera.CopyFrom(eye);
            camera.fieldOfView = fov;
            camera.nearClipPlane = .02f;
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

        private static IEnumerator Wait(float seconds)
        {
            for (float waited = 0; waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheTutorialAndTheControllerLabels()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var settings = new UnityEditor.SerializedObject(flow.GetComponent<TourLoader>());
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            yield return null;
            flow.Menu.Preferences.SetTutorialDone(false);
            var tutorial = flow.GetComponent<MuseumFirstVisitTutorial>();
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            var eye = visitor.GetComponentInChildren<Camera>();
            flow.EnterImmediately();
            body.enabled = false;
            visitor.transform.SetPositionAndRotation(new Vector3(0, .05f, -48), Quaternion.identity);
            body.enabled = true;
            for (float waited = 0; !tutorial.Running && waited < 20; waited += Time.deltaTime) yield return null;
            yield return Wait(1.5f);
            // The visitor's own view, and a closer look at the bubble over the colibrí.
            yield return Shot("1_tutorial_step_visitor_view", eye);
            var look = eye.transform.rotation;
            eye.transform.rotation = Quaternion.LookRotation(tutorial.Bubble.position - eye.transform.position, Vector3.up);
            yield return Shot("2_tutorial_step_close", eye, 40);
            eye.transform.rotation = look;
            tutorial.Progress.MarkDone(TutorialStep.Turn);
            yield return Wait(.8f);
            eye.transform.rotation = Quaternion.LookRotation(tutorial.Bubble.position - eye.transform.position, Vector3.up);
            yield return Shot("3_tutorial_step_move", eye, 40);
            eye.transform.rotation = look;
            tutorial.Skip();
            yield return Wait(1);

            // The labels on stand-in controllers held in front of the eyes, during the tutorial and when looked at.
            var labels = flow.GetComponent<MuseumControllerLabels>();
            labels.Space = eye.transform;
            labels.Head = eye;
            labels.ForceShow = true;
            labels.Poses = right => (true, new Vector3(right ? .11f : -.11f, -.12f, .36f), Quaternion.Euler(-35, right ? -12 : 12, 0));
            yield return Wait(.6f);
            yield return Shot("4_controller_labels_both", eye, 60);
            labels.Poses = right => (true, new Vector3(right ? .05f : -.6f, -.08f, right ? .3f : -.3f), Quaternion.Euler(-40, -10, 0));
            yield return Wait(.6f);
            yield return Shot("5_controller_labels_right", eye, 50);
            labels.Poses = right => (true, new Vector3(right ? .6f : -.05f, -.08f, right ? -.3f : .3f), Quaternion.Euler(-40, 10, 0));
            yield return Wait(.6f);
            yield return Shot("6_controller_labels_left", eye, 50);
            Assert.That(companion.Placed, Is.True);
        }
#endif
    }
}
