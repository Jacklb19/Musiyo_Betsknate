using System;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Dominio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Blocks unapproved cultural data in scene dependencies and Resources.</summary>
    public sealed class BuildContentValidator : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var entryPaths = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToList();
            entryPaths.AddRange(AssetDatabase.GetAllAssetPaths().Where(path => path.Contains("/Resources/")));
            ValidateDependencies(entryPaths);
        }

        // Explicit BuildPlayer scene lists may differ from EditorBuildSettings.
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            ValidateScene(scene);
        }

        public static void ValidateDependencies(IEnumerable<string> entryPaths)
        {
            foreach (var path in AssetDatabase.GetDependencies(entryPaths.ToArray(), true))
            {
                // Scene objects must be inspected through the loaded scene API.
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(path)) continue;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    ValidateElement(asset, path);
            }
        }

        public static void ValidateScene(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path)) ValidateDependencies(new[] { scene.path });
            var visited = new HashSet<UnityEngine.Object>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component != null) ValidateReferences(component, scene.path, visited);
        }

        private static void ValidateReferences(UnityEngine.Object owner, string location, HashSet<UnityEngine.Object> visited)
        {
            if (!visited.Add(owner)) return;
            ValidateElement(owner, location);
            using (var serialized = new SerializedObject(owner))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is ScriptableObject child)
                        ValidateReferences(child, location, visited);
            }
        }

        private static void ValidateElement(UnityEngine.Object asset, string location)
        {
            if (asset is ElementoCultural element && !element.EsDivulgablePublicamente)
                throw new BuildFailedException("Unapproved cultural data in build: " + location
                    + ". Remove scene/Resources references and request authorized data from the backend.");
        }
    }
}
