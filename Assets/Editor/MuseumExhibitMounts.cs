using System;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The Castle stands at real size in the characters courtyard, behind the arc of pedestals (approved by the owner on
    /// 18/10/2026). The point of the former central pedestal keeps its delivered place and its reading stand under the
    /// agreed key punto.personajes.castillo; its exhibit mount stands on the courtyard floor behind the arc and carries
    /// the published model at the size it was built. The central pedestal itself is removed at import (mesh_repairs.json).
    /// Batch: -executeMethod MusiyoBetsknate.Editor.MuseumExhibitMounts.Configure.
    /// </summary>
    public static class MuseumExhibitMounts
    {
        public const string FormerKey = "punto.personajes.pedestal_3";
        public const string CastlePoint = "punto.personajes.castillo";
        public const string MountName = "ExhibitMount";
        /// <summary>Courtyard depth of the Castle's base, north of the room centre, in metres (variant approved by the owner).</summary>
        public const float CastleDepth = 3.4f;
        /// <summary>A mount that never scales the Castle beyond what fits the courtyard behind the arc.</summary>
        public const float Footprint = 12, Height = 12;

        [MenuItem("Musiyo/Museum Delivery/Configure Castle Point")]
        public static void Configure()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var anchor = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true))
                .FirstOrDefault(candidate => candidate.Key == FormerKey || candidate.Key == CastlePoint)
                ?? throw new InvalidOperationException("Neither " + FormerKey + " nor " + CastlePoint + " is in the scene.");
            var existing = anchor.transform.Find(MountName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            Physics.SyncTransforms();
            var spot = new Vector3(anchor.transform.position.x, 3, CastleDepth);
            if (!Physics.Raycast(spot, Vector3.down, out var hit, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No courtyard floor behind the arc at " + spot + ".");
            var mount = new GameObject(MountName).transform;
            mount.SetParent(anchor.transform, false);
            // Visitors come into the courtyard from the south: the front of the Castle faces them.
            mount.SetPositionAndRotation(hit.point, Quaternion.LookRotation(Vector3.back, Vector3.up));
            mount.gameObject.AddComponent<PointExhibitMount>().Configure(Footprint, Height, true);
            var target = anchor.transform.Find("CastleLookTarget");
            if (target == null)
            {
                target = new GameObject("CastleLookTarget").transform;
                target.SetParent(anchor.transform, false);
            }
            target.position = hit.point + Vector3.up * 1.9f;
            anchor.Configure(CastlePoint, anchor.ActivationRadius, target);
            EditorUtility.SetDirty(anchor);
            // The point's status marker stood on the pedestal top; with the pedestal gone it would float, so it is not drawn.
            if (anchor.TryGetComponent<TourPoint>(out var point))
            {
                var serialized = new SerializedObject(point);
                var marker = serialized.FindProperty("statusRenderer");
                if (marker.objectReferenceValue is Renderer renderer)
                {
                    if (renderer.gameObject == anchor.gameObject) renderer.enabled = false;
                    else renderer.gameObject.SetActive(false);
                }
                marker.objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(FormattableString.Invariant($"Castle point {CastlePoint}: mount at {hit.point.x:F3}, {hit.point.y:F3}, {hit.point.z:F3} on {hit.collider.name}."));
        }
    }
}
