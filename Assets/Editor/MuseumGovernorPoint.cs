using System;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// "El gobernador y los cabildantes" has no model, image or narration: the owner decided on 20/10/2026 that it stands in
    /// the characters room as a reading stand with its information only, under the new key punto.personajes.gobernador
    /// (the retired pedestal_3 key is never reused). The stand is a copy of the museum's own reading stand, with nothing
    /// on or beside it, in the open north-west corner of the courtyard between the arc of pedestals and the planters,
    /// turned like the stand of the second pedestal. It stands only while the tour declares its point.
    /// Batch: -executeMethod MusiyoBetsknate.Editor.MuseumGovernorPoint.Configure.
    /// </summary>
    public static class MuseumGovernorPoint
    {
        public const string Key = "punto.personajes.gobernador";
        public const string StandName = "Lectern_Gobernador";
        /// <summary>Floor position of the stand in the characters courtyard, in metres.</summary>
        public static readonly Vector2 Spot = new Vector2(-6.6f, 2.6f);
        /// <summary>Compass heading of the reading side: the same as the stand of the second pedestal, toward the courtyard.</summary>
        public const float Heading = 135;
        private const string TemplateStand = "Lectern_02";
        private const float Radius = 1.8f;

        [MenuItem("Musiyo/Museum Delivery/Configure Governor Point")]
        public static void Configure()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var room = roots.SelectMany(root => root.GetComponentsInChildren<RoomAnchor>(true))
                .FirstOrDefault(candidate => candidate.Key == "room.characters")
                ?? throw new InvalidOperationException("The characters room is not in the scene.");
            var template = roots.SelectMany(root => root.GetComponentsInChildren<PointLectern>(true))
                .FirstOrDefault(stand => stand.name == TemplateStand)
                ?? throw new InvalidOperationException("The reading stand " + TemplateStand + " is not in the scene.");
            foreach (var previous in roots.SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true)).Where(anchor => anchor.Key == Key).ToList())
                UnityEngine.Object.DestroyImmediate(previous.gameObject);
            Physics.SyncTransforms();

            var floorProbe = new Vector3(Spot.x, 3, Spot.y);
            if (!Physics.Raycast(floorProbe, Vector3.down, out var floor, 6, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No courtyard floor at " + floorProbe + ".");
            var point = new GameObject(Key);
            point.transform.SetParent(room.transform, false);
            point.transform.position = new Vector3(Spot.x, 0, Spot.y);

            // The stand: the museum's reading stand mesh and materials, at its own place and heading.
            var stand = new GameObject(StandName, typeof(MeshFilter), typeof(MeshRenderer));
            stand.transform.SetParent(point.transform, false);
            stand.transform.SetPositionAndRotation(new Vector3(Spot.x, floor.point.y, Spot.y), Quaternion.Euler(270, Heading, 0));
            stand.transform.localScale = template.transform.lossyScale;
            var filter = stand.GetComponent<MeshFilter>();
            filter.sharedMesh = template.GetComponent<MeshFilter>().sharedMesh;
            var renderer = stand.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = template.GetComponent<MeshRenderer>().sharedMaterials;
            renderer.shadowCastingMode = template.GetComponent<MeshRenderer>().shadowCastingMode;
            if (!ConfigureMuseumLecterns.FindBoard(filter, out var bottom, out var normal, out var up, out var size))
                throw new InvalidOperationException("No reading surface on the copied stand.");
            var board = new GameObject("Board");
            board.transform.SetParent(stand.transform, true);
            board.transform.SetPositionAndRotation(bottom, Quaternion.LookRotation(-normal, up));
            var collider = stand.AddComponent<BoxCollider>();
            collider.center = filter.sharedMesh.bounds.center;
            collider.size = filter.sharedMesh.bounds.size;

            // The board itself is what the visitor looks at; there is no exhibit to look at instead.
            var target = new GameObject("LookTarget");
            target.transform.SetParent(point.transform, false);
            target.transform.position = bottom + up * (size.y * .5f);
            var anchor = point.AddComponent<PointAnchor>();
            anchor.Configure(Key, Radius, target.transform);
            point.AddComponent<TourPoint>();
            stand.AddComponent<PointLectern>().Configure(anchor, board.transform, size, true);

            var errors = ValidateMuseumScene.Validate(scene);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(FormattableString.Invariant(
                $"Governor point {Key}: stand at {Spot.x:F2}, {floor.point.y:F3}, {Spot.y:F2} on {floor.collider.name}, board {size.x:F2} x {size.y:F2} m."));
        }
    }
}
