using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Places the reading stands of the museum model and binds each one to the point it stands beside.</summary>
    public static class ConfigureMuseumLecterns
    {
        public const string RootName = "MuseumLecterns";
        private const string ModelPath = "Assets/_Musiyo/Arte/Modelos/Boceto/Museum_Lecterns.fbx";
        private const string BoardMaterial = "Lectern_Board";
        private const float MaximumPointDistance = 2;

        [MenuItem("Musiyo/Configure Museum Lecterns")]
        public static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path))
                scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            if (scene.path != BuildMuseumBlockout.ScenePath || scene.isDirty)
                throw new InvalidOperationException("The saved museum scene must be active and clean.");
            var runtime = UnityEngine.Object.FindAnyObjectByType<TourRuntime>();
            if (runtime == null) throw new InvalidOperationException("Configure the museum tour before its reading stands.");
            Transform architecture = null;
            foreach (var child in runtime.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == RootName) throw new InvalidOperationException("Reading stands already exist; no objects were replaced.");
                if (child.name == "NeutralArchitecture") architecture = child;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (architecture == null || model == null)
                throw new InvalidOperationException("Missing the neutral architecture or the reading stand model: " + ModelPath);

            // Both models come from the same concept and export axes, so they share the architecture's frame.
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            Undo.RegisterCreatedObjectUndo(root, "Place museum reading stands");
            root.name = RootName;
            root.transform.SetParent(architecture.parent, false);
            root.transform.SetLocalPositionAndRotation(architecture.localPosition, architecture.localRotation);
            root.transform.localScale = architecture.localScale;

            var anchors = runtime.GetComponentsInChildren<PointAnchor>(true);
            var bound = new HashSet<PointAnchor>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var stand = filter.gameObject;
                var anchor = Nearest(anchors, stand.transform.position);
                if (anchor == null || !bound.Add(anchor))
                    throw new InvalidOperationException("No single point stands beside " + stand.name + ".");
                if (!FindBoard(filter, out var bottom, out var normal, out var up, out var size))
                    throw new InvalidOperationException("No reading surface on " + stand.name + ".");
                var board = new GameObject("Board");
                Undo.RegisterCreatedObjectUndo(board, "Place museum reading stands");
                board.transform.SetParent(stand.transform, true);
                board.transform.SetPositionAndRotation(bottom, Quaternion.LookRotation(-normal, up));
                Undo.AddComponent<PointLectern>(stand).Configure(anchor, board.transform, size);
                var collider = Undo.AddComponent<BoxCollider>(stand);
                collider.center = filter.sharedMesh.bounds.center;
                collider.size = filter.sharedMesh.bounds.size;
            }
            if (bound.Count == 0) throw new InvalidOperationException("The reading stand model is empty.");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateMuseumScene.ValidateCurrentScene();
            Debug.Log("Museum reading stands placed and bound to their points: " + bound.Count + ".");
        }

        private static PointAnchor Nearest(IEnumerable<PointAnchor> anchors, Vector3 position)
        {
            PointAnchor nearest = null;
            float best = MaximumPointDistance * MaximumPointDistance;
            foreach (var anchor in anchors)
            {
                var offset = anchor.transform.position - position;
                offset.y = 0;
                if (offset.sqrMagnitude >= best) continue;
                best = offset.sqrMagnitude;
                nearest = anchor;
            }
            return nearest;
        }

        /// <summary>Reads the surface from the board material, in world space and with outward-facing triangles.</summary>
        private static bool FindBoard(MeshFilter filter, out Vector3 bottom, out Vector3 normal, out Vector3 up, out Vector2 size)
        {
            var mesh = filter.sharedMesh;
            var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            int submesh = Array.FindIndex(materials, material => material != null && material.name == BoardMaterial);
            var triangles = submesh >= 0 && submesh < mesh.subMeshCount ? mesh.GetTriangles(submesh) : mesh.triangles;
            bool mirrored = filter.transform.localToWorldMatrix.determinant < 0;
            if (mirrored) Array.Reverse(triangles);
            var vertices = new List<Vector3>();
            foreach (var vertex in mesh.vertices) vertices.Add(filter.transform.TransformPoint(vertex));
            return PointLectern.TryFindBoard(vertices, triangles, out bottom, out normal, out up, out size);
        }
    }
}
