using System.Collections;
using System.IO;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of both hands held at stand-in controllers, open and closed, as a visitor would see them at chest height.
    /// Explicit: TestResults/hands. The stand-ins are plain sticks along each pointer, not controller models.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumHandCaptures
    {
        private const string Folder = "TestResults/hands";

        private static void Render(Camera camera, string name)
        {
            const int width = 1200, height = 900;
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

        [UnityTest]
        public IEnumerator PictureTheHandsOnTheControllers()
        {
            Directory.CreateDirectory(Folder);
            var assets = Resources.Load<MuseumHandAssets>(MuseumHandAssets.ResourceName);
            Assert.That(assets, Is.Not.Null, "The hand assets are set up.");
            var stage = new GameObject("HandStage").transform;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40, -30, 0);
            var camera = new GameObject("Eyes").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(30, 0, 0));
            camera.nearClipPlane = .02f;
            camera.fieldOfView = 70;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .19f, .21f);
            var left = MuseumControllerHand.Create(assets.LeftTracked, false, stage);
            var right = MuseumControllerHand.Create(assets.RightTracked, true, stage);
            Assert.That(left, Is.Not.Null);
            Assert.That(right, Is.Not.Null);
            // Pointers held forward at chest height, tipped a little down, as when walking with the controllers.
            var pose = Quaternion.Euler(20, 0, 0);
            foreach (var (hand, x) in new[] { (left, -.17f), (right, .17f) })
            {
                var pointer = new Vector3(x, 1.15f, .42f);
                var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stick.transform.SetParent(stage, false);
                stick.transform.SetPositionAndRotation(pointer + pose * Vector3.back * .07f, pose * Quaternion.Euler(90, 0, 0));
                stick.transform.localScale = new Vector3(.035f, .08f, .035f);
                hand.Place(pointer, pose);
            }
            foreach (var (name, amount) in new[] { ("1_open", 0f), ("2_closed", 1f) })
            {
                left.Pose(amount, amount);
                right.Pose(amount, amount);
                yield return null;
                camera.transform.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(30, 0, 0));
                Render(camera, name);
                camera.transform.SetPositionAndRotation(new Vector3(.55f, 1.2f, .35f), Quaternion.LookRotation(new Vector3(-.55f, -.05f, .05f)));
                Render(camera, name + "_side");
            }
            Object.Destroy(stage.gameObject);
            Object.Destroy(sun.gameObject);
            Object.Destroy(camera.gameObject);
        }
    }
}
