using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    public static class ValidateMuseumScene
    {
        private static readonly HashSet<string> ExpectedKeys = new HashSet<string>
        {
            "zona.llegada.tutorial",
            "punto.bienvenida.mito", "punto.bienvenida.significados",
            "punto.personajes.pedestal_1", "punto.personajes.pedestal_2",
            "punto.personajes.pedestal_3", "punto.personajes.pedestal_4",
            "punto.personajes.pedestal_5", "punto.instrumentos.pedestal_1",
            "punto.instrumentos.pedestal_2", "punto.instrumentos.pedestal_3",
            "punto.instrumentos.pedestal_4", "punto.instrumentos.pedestal_5",
            "punto.instrumentos.danza_ocho", "guia.principal", "punto.mirador.creditos"
        };

        [MenuItem("Musiyo/Validate Museum Scene")]
        public static void ValidateCurrentScene()
        {
            var errors = Validate(SceneManager.GetActiveScene());
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("Museum scene is valid: 16 unique technical anchors, expected positions, and colliders.");
        }

        [MenuItem("Musiyo/Validate Saved Blockout")]
        public static void ValidateSavedBlockout()
        {
            var previousScene = SceneManager.GetActiveScene();
            var useSingleScene = Application.isBatchMode && string.IsNullOrEmpty(previousScene.path);
            if (!useSingleScene && string.IsNullOrEmpty(previousScene.path))
                throw new InvalidOperationException("Save the current scene before validating the blockout.");

            var scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath,
                useSingleScene ? OpenSceneMode.Single : OpenSceneMode.Additive);
            try
            {
                var errors = Validate(scene);
                if (errors.Count > 0)
                    throw new InvalidOperationException(string.Join("\n", errors));
                var root = Find(scene, "MuseumBlockout");
                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Debug.Log($"Saved blockout valid: {renderers.Length} mesh renderers, bounds {bounds}.");
            }
            finally
            {
                if (!useSingleScene)
                {
                    SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        public static List<string> Validate(Scene scene)
        {
            var errors = new List<string>();
            if (!scene.IsValid())
            {
                errors.Add("No valid scene is open.");
                return errors;
            }

            var root = Find(scene, "MuseumBlockout");
            if (root == null)
            {
                errors.Add("Missing MuseumBlockout root.");
                return errors;
            }
            var architecture = Find(scene, "NeutralArchitecture");
            if (architecture == null)
                errors.Add("Missing imported neutral architecture.");
            else
            {
                var renderers = architecture.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length < 50)
                    errors.Add("Neutral architecture has too few mesh renderers.");
                else
                {
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    if (bounds.min.x > -15 || bounds.max.x < 24 ||
                        bounds.min.z > -55 || bounds.max.z < 20)
                        errors.Add("Neutral architecture does not align with the planned room coordinates.");
                }
            }
            CheckPosition(scene, "VisitorSpawn", new Vector3(0, 0, -58), errors);
            CheckPosition(scene, "HearthRoom", new Vector3(0, 0, 18.5f), errors);

            var keys = new HashSet<string>();
            foreach (var anchor in root.GetComponentsInChildren<PointAnchor>(true))
            {
                if (string.IsNullOrWhiteSpace(anchor.Key))
                    errors.Add("Anchor has no key: " + anchor.name);
                else if (!keys.Add(anchor.Key))
                    errors.Add("Duplicate anchor: " + anchor.Key);
                if (anchor.LookTarget == null || anchor.ActivationRadius <= 0)
                    errors.Add("Anchor has no look target or activation radius: " + anchor.Key);
            }
            foreach (var key in ExpectedKeys)
                if (!keys.Contains(key)) errors.Add("Missing anchor: " + key);
            if (keys.Count != ExpectedKeys.Count)
                errors.Add("Expected 16 technical anchors, found " + keys.Count + ".");

            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length < 30)
                errors.Add("Missing blockout collision geometry.");
            return errors;
        }

        private static void CheckPosition(Scene scene, string name, Vector3 expected,
            List<string> errors)
        {
            var objectFound = Find(scene, name);
            if (objectFound == null)
                errors.Add("Missing " + name + ".");
            else if (Vector3.Distance(objectFound.transform.position, expected) > 0.05f)
                errors.Add(name + " does not match the planned position.");
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var childTransform in root.GetComponentsInChildren<Transform>(true))
                    if (childTransform.name == name) return childTransform.gameObject;
            }
            return null;
        }
    }
}
