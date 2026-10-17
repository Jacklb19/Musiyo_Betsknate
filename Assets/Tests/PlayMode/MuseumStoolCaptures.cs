using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Pictures, at eye height, of each group of stools placed in the museum. Explicit: TestResults/stools.</summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumStoolCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/stools";

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
        public IEnumerator PictureTheStools()
        {
            Directory.CreateDirectory(Folder);
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            MuseumAtmosphere.Current.Apply();
            var root = GameObject.Find("MuseumStools");
            Assert.That(root, Is.Not.Null, "The stools are placed in the saved museum.");
            var groups = new Dictionary<string, List<Transform>>();
            foreach (Transform stool in root.transform)
            {
                string area = stool.name.Split('_')[0];
                if (!groups.TryGetValue(area, out var list)) groups[area] = list = new List<Transform>();
                list.Add(stool);
            }
            var camera = new GameObject("StoolCamera").AddComponent<Camera>();
            camera.fieldOfView = 70;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            foreach (var group in groups)
            {
                var centre = group.Value.Aggregate(Vector3.zero, (sum, item) => sum + item.position) / group.Value.Count;
                foreach (var (suffix, offset) in new[] { ("a", new Vector3(2.6f, 1.6f, -2.6f)), ("b", new Vector3(-2.6f, 1.6f, -2.2f)) })
                {
                    var eye = centre + offset;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre + Vector3.up * .3f - eye, Vector3.up));
                    Render(camera, group.Key + "_" + suffix);
                }
            }
            Object.Destroy(camera.gameObject);
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
