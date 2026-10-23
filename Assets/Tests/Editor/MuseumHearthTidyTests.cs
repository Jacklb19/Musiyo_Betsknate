using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumHearthTidyTests
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static readonly Type Tidy = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.MuseumHearthTidy")).First(type => type != null);

        private static List<string> Apply(Scene scene) => (List<string>)Tidy.GetMethod("Apply").Invoke(null, new object[] { scene });
        private static List<Vector3> PotPoints(Renderer pots) => (List<Vector3>)Tidy.GetMethod("PotPoints").Invoke(null, new object[] { pots });
        private static bool Crowded(Bounds stool, IEnumerable<Vector3> pots) => (bool)Tidy.GetMethod("Crowded").Invoke(null, new object[] { stool, pots });
        private static bool Blocked(Bounds stool, Transform own) => (bool)Tidy.GetMethod("Blocked").Invoke(null, new object[] { stool, own });

        /// <summary>The cobs hang from the rack over the fire and no stool stands inside the pots; the scene file is untouched.</summary>
        [Test]
        public void TheHearthCobsHangFromTheRackAndNoStoolStandsInThePots()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToList();
                Renderer Piece(string name) => renderers.First(item => item.name == name);
                var changes = Apply(scene);
                Physics.SyncTransforms();
                var rack = Piece("Fogon_Bichasha").bounds;
                var cobs = Piece("Fogon_Mazorcas").bounds;
                Assert.That(rack.min.y - cobs.max.y, Is.InRange(-.02f, .03f), "The cobs touch the rack's underside: " + string.Join("; ", changes));
                var pots = PotPoints(Piece("Fogon_Ollas_Barro"));
                Assert.That(pots, Is.Not.Empty);
                foreach (var stool in renderers.Where(item => item.name.StartsWith("Hearth_Banco_") && item.gameObject.activeInHierarchy))
                {
                    Assert.That(Crowded(stool.bounds, pots), Is.False, stool.name + " stands clear of the pots.");
                    Assert.That(Blocked(stool.bounds, stool.transform), Is.False, stool.name + " stands clear of the benches.");
                }
                Assert.That(Apply(scene), Is.Empty, "A second pass finds nothing left to set right.");
                TestContext.WriteLine(string.Join("\n", changes));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
