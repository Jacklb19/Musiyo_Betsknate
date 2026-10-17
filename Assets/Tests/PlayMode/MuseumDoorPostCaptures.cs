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
    /// Pictures, at eye height, of both doorways of the welcome room, whose frame posts the import lowers to the floor.
    /// Explicit: written to TestResults/door-posts.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumDoorPostCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/door-posts";

        private static void Render(Camera camera, string name)
        {
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator PictureBothDoorwaysOfTheWelcomeRoom()
        {
            Directory.CreateDirectory(Folder);
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            MuseumAtmosphere.Current.Apply();
            // The model may be mirrored on import: find the doorways where the welcome room's thresholds are.
            float sign = 1;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (renderer.name == "Umbral_Bienvenida_Sur") sign = Mathf.Sign(renderer.bounds.center.z);
            var camera = new GameObject("DoorCamera").AddComponent<Camera>();
            camera.fieldOfView = 70;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            foreach (var (name, eye, look) in new[]
            {
                ("1_entrance_from_path", new Vector3(.5f, 1.6f, 30.5f * sign), new Vector3(0, 1.4f, 26.35f * sign)),
                ("2_entrance_from_inside", new Vector3(-.4f, 1.6f, 23f * sign), new Vector3(0, 1.2f, 26.35f * sign)),
                ("3_passage_after_tree", new Vector3(.6f, 1.6f, 17.5f * sign), new Vector3(0, 1.3f, 13.65f * sign)),
            })
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                Render(camera, name);
            }
            Object.Destroy(camera.gameObject);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
