using System;
using System.IO;
using System.Text;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class ModelResourceTests
    {
        [Test]
        public void PlatformRequiresAnExplicitUnambiguousVariant()
        {
            var resource = new ResourceContractV1 { id = "parent", kind = "model_3d", mime = "model/gltf-binary",
                variants = new[] { new ResourceVariantContractV1 { profile = "web", id = "web-model" } } };
            var element = new ElementContractV1 { resources = new[] { resource } };
            Assert.That(ModelResourcePolicy.TrySelect(element, "web", out _, out var id), Is.True);
            Assert.That(id, Is.EqualTo("web-model"));
            Assert.That(ModelResourcePolicy.TrySelect(element, "quest", out _, out _), Is.False);
            resource.variants = new[] { resource.variants[0], resource.variants[0] };
            Assert.That(ModelResourcePolicy.TrySelect(element, "web", out _, out _), Is.False);
        }

        [TestCase("https://files.example/model.glb", "2099-01-01T00:00:00Z", 880, true)]
        [TestCase("/api/v1/files/temporary", "2099-01-01T00:00:00Z", 880, true)]
        [TestCase("https://files.example/model.glb", "2000-01-01T00:00:00Z", 880, false)]
        [TestCase("https://files.example/model.glb", "2099-01-01T00:00:00Z", 10000001, false)]
        [TestCase("http://files.example/model.glb", "2099-01-01T00:00:00Z", 880, false)]
        [TestCase("file:///private/model.glb", "2099-01-01T00:00:00Z", 880, false)]
        public void AccessRejectsExpiredOversizedAndUnsafeUrls(string url, string expiration, int size, bool accepted)
        {
            string json = "{\"schema_version\":1,\"url\":\"" + url + "\",\"expires_at\":\"" + expiration
                + "\",\"mime\":\"model/gltf-binary\",\"byte_count\":" + size + "}";
            Assert.That(ModelResourcePolicy.TryAccess("https://museum.example/api/v1", json, "web",
                DateTimeOffset.UtcNow, out _, out _), Is.EqualTo(accepted));
        }

        [Test]
        public void GlbRejectsExternalResourcesAndTampering()
        {
            var valid = Glb("{\"asset\":{\"version\":\"2.0\"}}");
            Assert.That(ModelResourcePolicy.ValidateGlb(valid, "web"), Is.True);
            Assert.That(ModelResourcePolicy.ValidateGlb(valid, "web", new string('0', 64)), Is.False);
            Assert.That(ModelResourcePolicy.ValidateGlb(Glb("{\"asset\":{\"version\":\"2.0\"},\"images\":[{\"uri\":\"texture.png\"}]}"), "web"), Is.False);
            valid[4] = 1;
            Assert.That(ModelResourcePolicy.ValidateGlb(valid, "web"), Is.False);
            Assert.That(ModelResourcePolicy.ValidateGlb(new byte[10000001], "web"), Is.False);
        }

        private static byte[] Glb(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0x46546c67u); writer.Write(2u); writer.Write((uint)(20 + bytes.Length));
            writer.Write((uint)bytes.Length); writer.Write(0x4e4f534au); writer.Write(bytes);
            return stream.ToArray();
        }

        [Test]
        public void ExaminationClampsZoomAndResetsAllControls()
        {
            var pose = new ModelExaminationPose();
            pose.Rotate(new Vector2(30, 15));
            pose.Zoom(-100);
            Assert.That(pose.Distance, Is.EqualTo(.4f));
            pose.Zoom(100);
            Assert.That(pose.Distance, Is.EqualTo(1.5f));
            Assert.That(pose.Rotation, Is.Not.EqualTo(Quaternion.identity));
            pose.Reset();
            Assert.That(pose.Distance, Is.EqualTo(.8f));
            Assert.That(pose.Rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void DefaultFrameFitsTheRotatedBoundingSphereBetweenHudPanels()
        {
            var size = Vector3.one * .6f;
            float scale = ModelExaminationPose.FrameScale(size, 70);
            Assert.That(scale, Is.GreaterThan(0).And.LessThan(1));
            float halfAngle = Mathf.Asin(size.magnitude * .5f * scale / .8f);
            float projectedFraction = Mathf.Tan(halfAngle) / Mathf.Tan(35 * Mathf.Deg2Rad);
            Assert.That(projectedFraction, Is.EqualTo(.65f).Within(.001f));
        }

        [Test]
        public void NormalizationCentersAnOffsetModelAndFitsItsLongestAxis()
        {
            var root = new GameObject("SyntheticModel");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(20, 2, -3);
            cube.transform.localScale = new Vector3(2, 4, 1);
            try
            {
                Assert.That(ModelExaminationPose.Normalize(root.transform), Is.True);
                var bounds = cube.GetComponent<Renderer>().bounds;
                Assert.That(bounds.center.magnitude, Is.LessThan(.0001f));
                Assert.That(bounds.size.y, Is.EqualTo(.6f).Within(.0001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
