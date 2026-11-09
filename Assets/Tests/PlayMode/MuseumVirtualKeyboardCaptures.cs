using System;
using System.Collections;
using System.IO;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    [Explicit, Category("Capture")]
    public sealed class MuseumVirtualKeyboardCaptures
    {
        [UnityTest]
        public IEnumerator CaptureTheSpanishKeyboard()
        {
            var root = new GameObject("KeyboardCapture");
            var eye = new GameObject("KeyboardEye").AddComponent<Camera>();
            var oldInput = MuseumInput.Current;
            oldInput.SetSpatial(true);
            var target = new RenderTexture(1440, 1000, 24);
            var image = new Texture2D(1440, 1000, TextureFormat.RGB24, false);
            try
            {
                eye.transform.position = new Vector3(0, 1.65f, 0);
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color(.035f, .045f, .04f);
                eye.fieldOfView = 55;
                var keyboard = root.AddComponent<MuseumVirtualKeyboard>();
                keyboard.Show("¿Qué es el Bëtsknaté?", eye, null, _ => { }, () => { });
                yield return null;
                Canvas.ForceUpdateCanvases();
                eye.targetTexture = target;
                eye.Render();
                eye.targetTexture = null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1440, 1000), 0, 0);
                RenderTexture.active = previous;
                var folder = Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") ?? "TestResults/keyboard";
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "teclado-quest.png"), image.EncodeToPNG());
            }
            finally
            {
                oldInput.SetSpatial(false);
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(image);
                UnityEngine.Object.Destroy(root);
                UnityEngine.Object.Destroy(eye.gameObject);
            }
        }
    }
}
