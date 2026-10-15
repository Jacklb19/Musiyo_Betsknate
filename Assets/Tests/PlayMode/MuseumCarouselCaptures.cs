using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the piece carousel against the API the scene is configured for. Explicit: it needs a running local
    /// API with published pieces, reads only, and writes to TestResults/carousel, which Git ignores.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumCarouselCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/carousel";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        private static void Render(Camera camera, string name)
        {
            var target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheCarouselOfTheSharedPoint()
        {
            Directory.CreateDirectory(Folder);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var hud = Object.FindAnyObjectByType<MuseumHud>();
            var visitor = Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; (loader.State == TourLoadState.Loading || loader.State == TourLoadState.Idle) && waited < 20;
                waited += Time.unscaledDeltaTime) yield return null;
            if (loader.State != TourLoadState.Ready) Assert.Ignore("The local API is not answering: " + loader.Status);
            flow.EnterImmediately();
            var point = runtime.OrderedPoints.FirstOrDefault(candidate => candidate.HasContent
                && candidate.Content.elements.Count(element => element.has_3d_model) >= 2);
            if (point == null) Assert.Ignore("No point publishes several pieces with models.");
            Assert.That(visitor.FocusPoint(point.Anchor), Is.True);
            for (float waited = 0; interaction.ActivePoint != point && waited < 5; waited += Time.unscaledDeltaTime) yield return null;
            int first = System.Array.FindIndex(point.Content.elements, element => element.has_3d_model);
            Assert.That(interaction.SelectElement(first), Is.True);
            // A view from a few steps in front of the pedestal, toward the visitor, shows piece, pedestal and screen together.
            var camera = new GameObject("CarouselCaptureCamera").AddComponent<Camera>();
            camera.fieldOfView = 55;
            camera.nearClipPlane = .05f;
            var look = point.Anchor.LookTarget.position;
            var head = visitor.GetComponentInChildren<Camera>().transform.position;
            var back = head - look;
            back.y = 0;
            var eye = head + back.normalized * 1.2f + Vector3.up * .15f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look + Vector3.down * .2f - eye, Vector3.up));
            for (int step = 0; step < 3; step++)
            {
                for (float waited = 0; presenter.State != ModelLoadState.Ready && waited < 30; waited += Time.unscaledDeltaTime) yield return null;
                yield return new WaitForSecondsRealtime(.6f);
                Render(camera, "piece_" + (step + 1) + "_" + presenter.State);
                hud.StepCarousel(1);
                yield return null;
            }
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
