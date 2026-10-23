using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the hearth as delivered and as set right (<c>MuseumHearthTidy</c>), from the same cameras, for review.
    /// Explicit: it asserts nothing about the design, needs graphics, and writes to MUSIYO_CAPTURE_DIR or
    /// TestResults/hearth. The saved scene is never changed.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumHearthCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/hearth";

        [Test]
        public void CaptureTheHearthBeforeAndAfterItIsSetRight()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Single);
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToList();
            Renderer Piece(string name) => renderers.First(item => item.name == name);
            var fire = Piece("Fogon_Piedras_Shinyac").bounds.center;
            float floor = Piece("Fogon_Piso").bounds.max.y;
            var cobs = Piece("Fogon_Mazorcas").bounds.center;
            var stools = renderers.Where(item => item.name.StartsWith("Hearth_Banco_")).ToList();
            var views = new List<(string name, Vector3 eye, Vector3 target, float fov)>();
            for (int view = 0; view < 6; view++)
            {
                var direction = Quaternion.Euler(0, view * 60, 0) * Vector3.forward;
                views.Add(("hearth_" + view, new Vector3(fire.x, floor + 1.6f, fire.z) + direction * 2.2f,
                    new Vector3(fire.x, floor + .5f, fire.z) - direction * 1.5f, 80));
            }
            views.Add(("hearth_cobs", new Vector3(cobs.x, floor + 1.5f, cobs.z) + new Vector3(1.6f, 0, -1.2f), cobs + Vector3.up * .2f, 55));
            foreach (var stool in stools)
            {
                var spot = stool.bounds.center;
                var away = spot - fire;
                away.y = 0;
                views.Add(("stool_" + stool.name, spot + away.normalized * 1.3f + Vector3.up * 1.2f, spot, 60));
            }
            try
            {
                Shoot(views, Path.Combine(Folder, "antes"));
                var tidy = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.MuseumHearthTidy"))
                    .First(type => type != null);
                var changes = (List<string>)tidy.GetMethod("Apply").Invoke(null, new object[] { scene });
                Shoot(views, Path.Combine(Folder, "despues"));
                File.WriteAllLines(Path.Combine(Folder, "cambios.txt"), changes, new System.Text.UTF8Encoding(false));
            }
            finally { EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Single); }
        }

        private static void Shoot(IEnumerable<(string name, Vector3 eye, Vector3 target, float fov)> views, string folder)
        {
            Directory.CreateDirectory(folder);
            const int width = 1280, height = 800;
            var holder = new GameObject("CaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = holder.AddComponent<Camera>();
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            camera.clearFlags = CameraClearFlags.Skybox;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                foreach (var (name, eye, look, fov) in views)
                {
                    camera.fieldOfView = fov;
                    holder.transform.position = eye;
                    holder.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
                    camera.targetTexture = target;
                    camera.Render();
                    camera.targetTexture = null;
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.active = previous;
                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }
    }
}
