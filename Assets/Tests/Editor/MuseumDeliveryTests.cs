using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumDeliveryTests
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string MaterialFolder = "Assets/_Musiyo/Museum/Materials/";

        private static object Invoke(string type, string method, params object[] arguments)
        {
            // The delivery tools live in Unity's predefined editor assembly.
            var found = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor." + type))
                .First(candidate => candidate != null);
            var target = found.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length);
            try { return target.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void ImportedFilesMaterialsAndModelMatchTheDelivery()
        {
            var errors = (List<string>)Invoke("MuseumDeliveryValidation", "ValidateAssets");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void SavedMuseumKeepsItsAnchorsAndLetsAVisitorReachEveryPoint()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var errors = (List<string>)Invoke("ValidateMuseumScene", "Validate", scene);
                errors.AddRange((List<string>)Invoke("MuseumDeliveryValidation", "ValidateMuseumScene", scene));
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void EachPlatformBuildsTheMuseumWithItsOwnSurfaceProfile()
        {
            var expected = new[]
            {
                (target: BuildTarget.Android, profile: "quest", folder: "Quest", shader: "Universal Render Pipeline/Simple Lit"),
                (target: BuildTarget.StandaloneWindows64, profile: "windows", folder: "Windows", shader: "Universal Render Pipeline/Lit"),
                (target: BuildTarget.WebGL, profile: "web", folder: "Web", shader: "Universal Render Pipeline/Lit"),
            };
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var architecture = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .First(transform => transform.name == "MuseumArchitecture");
                foreach (var platform in expected)
                {
                    Assert.That(Invoke("MuseumSurfaceProfiles", "ProfileFor", platform.target), Is.EqualTo(platform.profile));
                    Invoke("MuseumSurfaceProfiles", "Apply", scene, platform.profile);
                    int materials = 0;
                    foreach (var renderer in architecture.GetComponentsInChildren<MeshRenderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                        {
                            materials++;
                            Assert.That(AssetDatabase.GetAssetPath(material), Does.StartWith(MaterialFolder + platform.folder + "/"), renderer.name);
                            // The lagoon has its own shader on every profile.
                            Assert.That(material.shader.name, Is.EqualTo(material.name == "M_Agua_Laguna" ? "Musiyo/Lagoon Water" : platform.shader),
                                material.name);
                        }
                    Assert.That(materials, Is.GreaterThan(150), "Every mesh of the museum carries a profile material.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
