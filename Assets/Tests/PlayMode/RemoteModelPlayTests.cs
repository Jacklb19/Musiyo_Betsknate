using System;
using System.Collections;
using GLTFast;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class RemoteModelPlayTests
    {
        // A neutral Draco cube generated for this test; no cultural asset is embedded.
        private const string Cube = "Z2xURgIAAABwAwAABAMAAEpTT057ImFzc2V0Ijp7InZlcnNpb24iOiIyLjAiLCJnZW5lcmF0b3IiOiJNdXNpeW8gc3ludGhldGljIHRlY2huaWNhbCBmaXh0dXJlIn0sImV4dGVuc2lvbnNVc2VkIjpbIktIUl9kcmFjb19tZXNoX2NvbXByZXNzaW9uIl0sImV4dGVuc2lvbnNSZXF1aXJlZCI6WyJLSFJfZHJhY29fbWVzaF9jb21wcmVzc2lvbiJdLCJzY2VuZSI6MCwic2NlbmVzIjpbeyJub2RlcyI6WzBdfV0sIm5vZGVzIjpbeyJtZXNoIjowfV0sIm1lc2hlcyI6W3sicHJpbWl0aXZlcyI6W3siYXR0cmlidXRlcyI6eyJQT1NJVElPTiI6MH0sImluZGljZXMiOjEsIm1hdGVyaWFsIjowLCJleHRlbnNpb25zIjp7IktIUl9kcmFjb19tZXNoX2NvbXByZXNzaW9uIjp7ImJ1ZmZlclZpZXciOjAsImF0dHJpYnV0ZXMiOnsiUE9TSVRJT04iOjB9fX19XX1dLCJtYXRlcmlhbHMiOlt7ImRvdWJsZVNpZGVkIjp0cnVlLCJwYnJNZXRhbGxpY1JvdWdobmVzcyI6eyJiYXNlQ29sb3JGYWN0b3IiOlswLjE1LDAuNSwwLjMsMV0sIm1ldGFsbGljRmFjdG9yIjowLCJyb3VnaG5lc3NGYWN0b3IiOjAuN319XSwiYWNjZXNzb3JzIjpbeyJjb21wb25lbnRUeXBlIjo1MTI2LCJjb3VudCI6OCwidHlwZSI6IlZFQzMiLCJtaW4iOlstMSwtMSwtMV0sIm1heCI6WzEsMSwxXX0seyJjb21wb25lbnRUeXBlIjo1MTIzLCJjb3VudCI6MzYsInR5cGUiOiJTQ0FMQVIifV0sImJ1ZmZlclZpZXdzIjpbeyJidWZmZXIiOjAsImJ5dGVPZmZzZXQiOjAsImJ5dGVMZW5ndGgiOjc5fV0sImJ1ZmZlcnMiOlt7ImJ5dGVMZW5ndGgiOjgwfV19UAAAAEJJTgBEUkFDTwICAQEAAAAIDAALAAADb6sUAQEQAf8AAAEACQMAAAIBAQEAAwMBMAEQAwCYlhQYAAQAAAAA/z8AAAAAgL8AAIC/AACAvwAAAEAOAA==";
        [UnityTest]
        public IEnumerator DracoModelImportsNormalizesAndReleasesNativeAssets()
        {
            var root = new GameObject("RuntimeModelTest");
            var import = new GltfImport();
            Mesh mesh = null;
            try
            {
                var bytes = Convert.FromBase64String(Cube);
                Assert.That(ModelResourcePolicy.ValidateGlb(bytes, "web"), Is.True);
                var load = import.Load(bytes);
                while (!load.IsCompleted) yield return null;
                Assert.That(load.GetAwaiter().GetResult(), Is.True);
                var instantiate = import.InstantiateMainSceneAsync(root.transform);
                while (!instantiate.IsCompleted) yield return null;
                Assert.That(instantiate.GetAwaiter().GetResult(), Is.True);
                Assert.That(ModelExaminationPose.Normalize(root.transform), Is.True);
                var filter = root.GetComponentInChildren<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                mesh = filter.sharedMesh;
                var bounds = filter.GetComponent<Renderer>().bounds;
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z), Is.EqualTo(.6f).Within(.001f));
            }
            finally { UnityEngine.Object.Destroy(root); import.Dispose(); }
            yield return null;
            Assert.That(mesh == null, Is.True, "The importer must destroy its native mesh on disposal.");
        }
    }
}
