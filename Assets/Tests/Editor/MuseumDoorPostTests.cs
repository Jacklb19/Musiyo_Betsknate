using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumDoorPostTests
    {
        private const string ModelPath = "Assets/_Musiyo/Museum/Models/Museum_v3.fbx";
        private const string Structure = "Bienvenida_Estructura_Madera.001";

        /// <summary>
        /// The posts framing both doorways of the welcome room were delivered standing on a plinth that the doorways lack;
        /// the import lowers them to the floor, so no door frame hangs in the air at the entrance or after the tree of life.
        /// </summary>
        [TestCase(-26.35f, TestName = "PostsOfTheEntranceDoorReachTheFloor")]
        [TestCase(-13.65f, TestName = "PostsOfThePassageAfterTheTreeReachTheFloor")]
        public void DoorPostsStandOnTheFloor(float z)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(root, Is.Not.Null);
            var filter = root.GetComponentsInChildren<MeshFilter>(true).First(item => item.name == Structure);
            var matrix = filter.transform.localToWorldMatrix;
            var points = filter.sharedMesh.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToArray();
            foreach (float x in new[] { -.84f, .84f })
            {
                // The importer may mirror the plan, so the doorway is found by the size of its coordinates.
                var post = points.Where(point => Mathf.Abs(Mathf.Abs(point.x) - Mathf.Abs(x)) < .12f && Mathf.Sign(point.x) == Mathf.Sign(x)
                    && Mathf.Abs(Mathf.Abs(point.z) - Mathf.Abs(z)) < .12f && point.y < 3).ToArray();
                Assert.That(post, Is.Not.Empty, "A post frames the doorway at x " + x + ", z " + z + ".");
                Assert.That(post.Min(point => point.y), Is.EqualTo(0).Within(.01f), "The post at x " + x + ", z " + z + " reaches the floor.");
            }
        }
    }
}
