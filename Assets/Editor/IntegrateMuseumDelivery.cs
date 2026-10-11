using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Puts the delivered museum where the blockout architecture stood. Anchors, rooms, the visitor, the bound
    /// reading stands and the interface are left as they are; only the stand-in geometry and its collision boxes go.
    /// </summary>
    public static class IntegrateMuseumDelivery
    {
        private const string BlockoutName = "NeutralArchitecture";

        [MenuItem("Musiyo/Museum Delivery/Replace Blockout In Museum Scene")]
        public static void Replace()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path))
                scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            if (scene.path != BuildMuseumBlockout.ScenePath || scene.isDirty)
                throw new InvalidOperationException("The saved museum scene must be active and clean.");
            if (MuseumDeliveryValidation.Find(scene, MuseumDeliveryAssets.PrefabRootName) != null)
                throw new InvalidOperationException("The museum architecture is already in the scene; nothing was replaced.");
            var blockout = MuseumDeliveryValidation.Find(scene, BlockoutName);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuseumDeliveryManifest.PrefabPath);
            if (blockout == null || prefab == null)
                throw new InvalidOperationException("Missing the blockout architecture or the museum prefab.");
            var manifest = MuseumDeliveryManifest.Load();
            var root = blockout.transform.parent;

            // Both models come from the same museum and export axes, so the delivery takes the blockout's frame.
            var architecture = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            architecture.transform.SetParent(root, false);
            architecture.transform.SetLocalPositionAndRotation(blockout.transform.localPosition, blockout.transform.localRotation);
            architecture.transform.localScale = blockout.transform.localScale;
            architecture.transform.SetSiblingIndex(blockout.transform.GetSiblingIndex());
            UnityEngine.Object.DestroyImmediate(blockout);

            // The scene already has these stands bound to their points; the delivered copies would stand inside them.
            var stands = root.GetComponentsInChildren<PointLectern>(true);
            foreach (var entry in manifest.Objects.Where(entry => entry.Role == "lectern"))
            {
                var copy = architecture.GetComponentsInChildren<Transform>(true).First(transform => transform.name == entry.Name);
                if (!stands.Any(stand => Vector3.Distance(stand.transform.position, copy.position) < .01f))
                    throw new InvalidOperationException("No bound reading stand stands where " + entry.Name + " does.");
                copy.gameObject.SetActive(false);
            }

            // The delivered floors, walls and railings collide now; the boxes that stood in for them would double them.
            var boxes = new List<GameObject>();
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
                if (!box.transform.IsChildOf(architecture.transform) && box.transform.childCount == 0
                    && box.GetComponents<Component>().Length == 2) boxes.Add(box.gameObject);
            foreach (var box in boxes) UnityEngine.Object.DestroyImmediate(box);

            var report = new JObject();
            var errors = ValidateMuseumScene.Validate(scene);
            errors.AddRange(MuseumDeliveryValidation.ValidateMuseumScene(scene, report));
            report["removed_stand_in_colliders"] = boxes.Count;
            report["errors"] = new JArray(errors);
            Directory.CreateDirectory(MuseumDeliveryValidation.OutputFolder);
            File.WriteAllText(Path.Combine(MuseumDeliveryValidation.OutputFolder, "museum-scene.json"), report.ToString());
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Museum delivery placed in the museum scene: " + boxes.Count + " stand-in colliders removed, "
                + manifest.Objects.Count(entry => entry.Role == "lectern") + " duplicate stands switched off.");
        }

        /// <summary>Batch entry: checks the saved museum scene and renders the comparison views of it.</summary>
        public static void ValidateAndCapture()
        {
            var scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            var report = new JObject();
            var errors = ValidateMuseumScene.Validate(scene);
            errors.AddRange(MuseumDeliveryValidation.ValidateMuseumScene(scene, report));
            report["errors"] = new JArray(errors);
            Directory.CreateDirectory(MuseumDeliveryValidation.OutputFolder);
            File.WriteAllText(Path.Combine(MuseumDeliveryValidation.OutputFolder, "museum-scene.json"), report.ToString());
            // The interface belongs to the visitor's screen, not to a picture of the building.
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.gameObject.SetActive(false);
            MuseumDeliveryCaptures.Capture(Path.Combine(MuseumDeliveryValidation.OutputFolder, "museum"));
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("Museum scene validated; report and captures in " + MuseumDeliveryValidation.OutputFolder + ".");
        }
    }
}
