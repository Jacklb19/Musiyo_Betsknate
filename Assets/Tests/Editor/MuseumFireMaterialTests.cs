using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Owner, Web test of 23/10: a magenta disc under the logs of the fire. The fire's pieces must never rely on a
    /// primitive's own material, which a player may give the built-in Default-Material the render pipeline cannot draw.
    /// </summary>
    public sealed class MuseumFireMaterialTests
    {
        [Test]
        public void TheFireAndItsPitDrawWithAMaterialEveryBuildCarries()
        {
            var holder = new GameObject("FireMaterialTest");
            try
            {
                MuseumFirePit.Build(holder.transform, Vector3.zero);
                var fire = MuseumFire.Create(holder.transform, Vector3.zero);
                Assert.That(fire, Is.Not.Null, "The fire's materials are in Resources.");
                var plain = Resources.Load<Material>(MuseumFirePit.PlainMaterialName);
                Assert.That(plain, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(plain), Does.Contain("/Resources/"), "Resources travel with every build, shader included.");
                Assert.That(plain.shader.name, Does.StartWith("Universal Render Pipeline/"));
                Assert.That(plain.IsKeywordEnabled("_EMISSION"), Is.True, "The embers glow through it.");
                foreach (var renderer in holder.GetComponentsInChildren<MeshRenderer>())
                {
                    var material = renderer.sharedMaterial;
                    Assert.That(material, Is.Not.Null, renderer.name);
                    Assert.That(material.name, Does.Not.StartWith("Default-Material").And.Not.EqualTo("Lit"), renderer.name + " uses a primitive's material.");
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"), renderer.name);
                    Assert.That(material.shader.isSupported, Is.True, renderer.name);
                }
                Assert.That(holder.GetComponentsInChildren<MeshRenderer>().Count(renderer => renderer.sharedMaterial == plain), Is.GreaterThan(0),
                    "Away from the museum, the ash and the coals take the plain material.");
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
