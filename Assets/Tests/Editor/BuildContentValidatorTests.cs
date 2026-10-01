using System;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Dominio;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public class BuildContentValidatorTests
    {
        private static void Invoke(string method, object argument)
        {
            // The validator lives in Unity's predefined editor assembly.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.BuildContentValidator"))
                .First(candidate => candidate != null);
            try { type.GetMethod(method).Invoke(null, new[] { argument }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void SceneDependenciesDoNotUseTheAssetObjectLoader()
        {
            Invoke("ValidateDependencies", new[] { "Assets/Scenes/Recorrido_Prueba.unity" });
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ActualSceneValidationBlocksInlineUnapprovedElements()
        {
            var scene = SceneManager.GetActiveScene();
            var element = ScriptableObject.CreateInstance<Mascara>();
            var root = new GameObject("SyntheticBuildGuardTest");
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                root.AddComponent<PuntoInteres>().AsignarElemento(element);
                Assert.Throws<BuildFailedException>(() => Invoke("ValidateScene", scene));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(element);
            }
        }
    }
}
