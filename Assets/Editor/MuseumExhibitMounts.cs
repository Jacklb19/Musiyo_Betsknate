using System;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Puts an exhibit mount on the top of the existing pedestal of a point, so a published model stands on it. The mount
    /// belongs to the scene; which model stands there is decided by what the API publishes for the point.
    /// </summary>
    public static class MuseumExhibitMounts
    {
        public const string CentralPedestal = "punto.personajes.pedestal_3";
        public const string MountName = "ExhibitMount";
        /// <summary>The five pedestals of the characters room are 0.92 m across; the mount keeps a margin from the edge.</summary>
        public const float Footprint = .84f;
        public const float Height = 1.2f;

        [MenuItem("Musiyo/Museum Delivery/Configure Central Pedestal Mount")]
        public static void Configure()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var anchor = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true))
                .First(candidate => candidate.Key == CentralPedestal);
            var existing = anchor.transform.Find(MountName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            Physics.SyncTransforms();
            var above = anchor.LookTarget.position;
            above.y += 3;
            if (!Physics.Raycast(above, Vector3.down, out var hit, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No pedestal under " + CentralPedestal + ".");
            var mount = new GameObject(MountName).transform;
            mount.SetParent(anchor.transform, false);
            // Visitors come into the courtyard from the south: the front of what stands here faces them.
            mount.SetPositionAndRotation(hit.point, Quaternion.LookRotation(Vector3.back, Vector3.up));
            mount.gameObject.AddComponent<PointExhibitMount>().Configure(Footprint, Height);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(FormattableString.Invariant($"Exhibit mount on {hit.collider.name} at {hit.point.x:F3}, {hit.point.y:F3}, {hit.point.z:F3}; look target {anchor.LookTarget.position}."));
        }
    }
}
