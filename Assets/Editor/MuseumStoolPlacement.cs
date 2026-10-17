using System;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Places the low wooden stools the owner chose (17/10, from reference photos: shape and wood only, no carved designs)
    /// around the museum's hearth and in a few rooms as neutral furnishing. Each spot is tried on a ring and kept only where
    /// the floor is free: nothing solid or delivered stands there, the guide's place and the doorways stay clear, and stools
    /// keep apart. Batch: -executeMethod MusiyoBetsknate.Editor.MuseumStoolPlacement.Place.
    /// </summary>
    public static class MuseumStoolPlacement
    {
        public const string RootName = "MuseumStools";
        private static readonly string[] Models = { "Props/Banco_Tripode", "Props/Banco_Curvo" };
        /// <summary>A stool's footprint and height, with a little room around it.</summary>
        private static readonly Vector3 Footprint = new Vector3(.55f, .4f, .55f);

        private sealed class Area
        {
            public string Name;
            public Vector3 Centre;
            public float Radius;
            public int Count;
            /// <summary>True when the stools face the centre (around a fire); otherwise they face into the room.</summary>
            public bool FaceCentre;
        }

        [MenuItem("Musiyo/Museum Delivery/Place Stools")]
        public static void Place()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var old = scene.GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToList();
            Renderer Piece(string name) => renderers.FirstOrDefault(item => item.name == name);
            var fire = Piece("Fogon_Piedras_Shinyac") ?? throw new InvalidOperationException("The hearth stones are missing.");
            var anchors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true)).ToList();
            var keep = new List<(Vector3 spot, float radius)>();
            // The guide's place and every point stay free to stand at.
            foreach (var anchor in anchors) keep.Add((anchor.transform.position, anchor.Key == "guia.principal" ? 1.4f : 1.1f));
            foreach (var name in new[] { "Fogon_Estera_Guia", "Guia_Provisional_Cuerpo" })
                if (Piece(name) != null) keep.Add((Piece(name).bounds.center, 1.1f));
            // Doorways: thresholds and passages stay clear.
            foreach (var item in renderers.Where(item => item.name.StartsWith("Umbral_") || item.name.StartsWith("Pasaje_")))
                keep.Add((item.bounds.center, 1.6f));
            var areas = new List<Area>
            {
                new Area { Name = "Hearth", Centre = Flat(fire.bounds.center), Radius = 1.55f, Count = 4, FaceCentre = true },
            };
            var welcome = Piece("Bienvenida_Piso");
            if (welcome != null) areas.Add(new Area { Name = "Welcome", Centre = Flat(welcome.bounds.center), Radius = 4.6f, Count = 2 });
            var instruments = Piece("Instrumentos_Piso");
            if (instruments != null) areas.Add(new Area { Name = "Instruments", Centre = Flat(instruments.bounds.center), Radius = 3.6f, Count = 2 });

            var root = new GameObject(RootName);
            var placed = new List<Vector3>();
            int model = 0;
            foreach (var area in areas)
            {
                int found = 0;
                for (int step = 0; step < 36 && found < area.Count; step++)
                {
                    // Spread the tries around the ring rather than filling one side first.
                    float angle = (step * 137.5f) % 360 * Mathf.Deg2Rad;
                    var spot = area.Centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * area.Radius;
                    if (!TryFloor(spot, out var floor)) continue;
                    if (keep.Any(zone => Flat(zone.spot - floor).magnitude < zone.radius)) continue;
                    if (placed.Any(other => Flat(other - floor).magnitude < 1.1f)) continue;
                    if (!Free(floor, renderers)) continue;
                    var toCentre = Flat(area.Centre - floor).normalized;
                    var facing = area.FaceCentre ? toCentre : -toCentre;
                    var prefab = Resources.Load<GameObject>(Models[model++ % Models.Length]);
                    var stool = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    stool.name = area.Name + "_" + prefab.name + "_" + found;
                    // A bench stands across the line to the fire, so whoever sits on it faces the flames.
                    var turn = Quaternion.LookRotation(facing, Vector3.up);
                    if (prefab.name == "Banco_Curvo") turn *= Quaternion.Euler(0, 90, 0);
                    stool.transform.SetPositionAndRotation(floor, turn);
                    var collider = stool.AddComponent<BoxCollider>();
                    var bounds = stool.GetComponentInChildren<Renderer>().bounds;
                    collider.center = stool.transform.InverseTransformPoint(bounds.center);
                    collider.size = Quaternion.Inverse(turn) * bounds.size;
                    collider.size = new Vector3(Mathf.Abs(collider.size.x), Mathf.Abs(collider.size.y), Mathf.Abs(collider.size.z));
                    // Not part of the baked lighting: light probes light it like the other movable pieces.
                    foreach (var mesh in stool.GetComponentsInChildren<MeshRenderer>()) mesh.receiveGI = ReceiveGI.LightProbes;
                    GameObjectUtility.SetStaticEditorFlags(stool, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                        | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                    placed.Add(floor);
                    found++;
                    Debug.Log(FormattableString.Invariant($"Stool {stool.name} at {floor.x:F2}, {floor.y:F2}, {floor.z:F2}"));
                }
                if (found < area.Count) Debug.LogWarning("Only " + found + " of " + area.Count + " stools found room in " + area.Name + ".");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Placed " + placed.Count + " stools.");
        }

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);

        private static bool TryFloor(Vector3 spot, out Vector3 floor)
        {
            floor = default;
            if (!Physics.Raycast(new Vector3(spot.x, 2.2f, spot.z), Vector3.down, out var hit, 3.5f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) || hit.normal.y < .95f || Mathf.Abs(hit.point.y) > .25f) return false;
            // Only a floor of the museum carries a stool: never water, a plinth or a step.
            if (!hit.collider.name.Contains("Piso")) return false;
            floor = hit.point;
            return true;
        }

        /// <summary>Nothing solid and no delivered piece of furnishing or decoration stands where the stool would.</summary>
        private static bool Free(Vector3 floor, List<Renderer> renderers)
        {
            var box = new Bounds(floor + Vector3.up * (Footprint.y * .5f + .03f), Footprint);
            if (Physics.CheckBox(box.center, box.extents, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            foreach (var item in renderers)
            {
                if (!item.enabled || !item.gameObject.activeInHierarchy) continue;
                var bounds = item.bounds;
                // Floors, walls, roofs and the terrain span whole rooms; only pieces of room size or smaller can be in the way.
                if (Mathf.Max(bounds.size.x, bounds.size.z) > 3.2f) continue;
                if (bounds.Intersects(box)) return false;
            }
            return true;
        }
    }
}
