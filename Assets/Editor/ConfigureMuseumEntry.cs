using System;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    public static class ConfigureMuseumEntry
    {
        public const string ViewpointName = "EntryViewpoint";
        // Relative to the visitor's starting spot: beside the path and above eye level, so the path leads the eye to the museum.
        private static readonly Vector3 Offset = new Vector3(-3, 2.4f, -2);
        private static readonly Vector3 Angles = new Vector3(-1, -8, 0);
        private const float FieldOfView = 45;

        [MenuItem("Musiyo/Configure Museum Entry Viewpoint")]
        public static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path))
                scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            if (scene.path != BuildMuseumBlockout.ScenePath || scene.isDirty)
                throw new InvalidOperationException("The saved museum scene must be active and clean.");
            if (UnityEngine.Object.FindAnyObjectByType<MuseumEntryViewpoint>(FindObjectsInactive.Include) != null)
            {
                Debug.Log("The entry viewpoint already exists; it was left where it is.");
                return;
            }
            Transform spawn = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "VisitorSpawn") spawn = child;
            if (spawn == null) throw new InvalidOperationException("The visitor's starting spot is missing.");
            Undo.RegisterCreatedObjectUndo(Add(spawn).gameObject, "Add museum entry viewpoint");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateMuseumScene.ValidateCurrentScene();
        }

        /// <summary>Adds the viewpoint beside the starting spot. Adjust it afterwards in the scene; it is not moved again.</summary>
        public static MuseumEntryViewpoint Add(Transform spawn)
        {
            var anchor = new GameObject(ViewpointName);
            anchor.transform.SetParent(spawn.parent, false);
            anchor.transform.SetPositionAndRotation(spawn.TransformPoint(Offset), spawn.rotation * Quaternion.Euler(Angles));
            var viewpoint = anchor.AddComponent<MuseumEntryViewpoint>();
            viewpoint.Configure(FieldOfView);
            return viewpoint;
        }
    }
}
