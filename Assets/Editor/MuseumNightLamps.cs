using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Lamps for the rooms that had none (owner, 30/10/2026, approving U3's proposal: «me parece súper bien lo de las
    /// lámparas»): a standing lamp by the loom in the welcome room, two low ones by the drums in the instruments room and
    /// one by the overlook's bench. Each is the path's own lantern (<see cref="MuseumSetDressing.LanternPrefab"/>) on a
    /// plain post of the same wood, with no carving, colour or figure. A lamp whose post would cross something solid (a wall,
    /// a bench, a decoration's box) is left out rather than built into it. By day the glass glows as the path's does; at night
    /// <see cref="MuseumNight"/> finds them by that glass, lights them brighter and lays their pools of light on the floor
    /// and a nearby wall. No light is added and nothing has a collider, so no path, stand or guide is ever blocked. Built
    /// with the rest of <see cref="MuseumWorldDressing"/> when the scene is processed; the saved scene is not changed.
    /// </summary>
    public static class MuseumNightLamps
    {
        public const string RootName = "MuseumNightLamps";
        /// <summary>Height of the lantern itself, as on the path.</summary>
        public const float LanternHeight = .32f;
        /// <summary>No lamp stands closer than this to a point of the visit, where its stand and its visitor go.</summary>
        public const float PointClearance = 1.2f;
        private const float PostWidth = .07f;

        /// <summary>Where each lamp goes and how tall its post is; the overlook's stands beside its bench.</summary>
        public static IEnumerable<(string name, Vector2 spot, float post)> Spots(Transform world)
        {
            // Beside the loom (MuseumCulturalDressing, by the myth), on the side away from the myth's point.
            var loom = new Vector2(-2.53f, -15.23f);
            var facing = (new Vector2(0, -20) - loom).normalized;
            var right = new Vector2(facing.y, -facing.x);
            yield return ("NightLamp_Welcome_Loom", loom + right * 1.05f - facing * .1f, 1.2f);
            // By the drum beside the instruments' bench, and by the shelf of small drums on the north wall.
            yield return ("NightLamp_Instruments_Drum", new Vector2(22.55f, -9.55f), .62f);
            yield return ("NightLamp_Instruments_Shelf", new Vector2(21.85f, .25f), .62f);
            // At the south end of the overlook's bench, so the view over the valley stays open.
            var bench = world.GetComponentsInChildren<BoxCollider>(true).Where(item => item.name == "Bench")
                .OrderBy(item => Vector2.Distance(new Vector2(item.transform.position.x, item.transform.position.z), new Vector2(-16.3f, 19.35f)))
                .FirstOrDefault();
            var seat = bench != null ? new Vector2(bench.transform.position.x, bench.transform.position.z) : new Vector2(-16.3f, 19.35f);
            yield return ("NightLamp_Overlook_Bench", seat + new Vector2(0, -1.3f), 1.0f);
        }

        public static void Apply(Transform world)
        {
            var lantern = AssetDatabase.LoadAssetAtPath<GameObject>(MuseumSetDressing.LanternPrefab);
            var wood = AssetDatabase.LoadAssetAtPath<Material>(MuseumSetDressing.MaterialFolder + "/PathLantern_Wood.mat");
            if (lantern == null || wood == null)
            {
                Debug.LogWarning("Museum night lamps: the path's lantern is missing (Musiyo > Museum Delivery > Build And Place Set Dressing); none built.");
                return;
            }
            Physics.SyncTransforms();
            var root = new GameObject(RootName).transform;
            root.SetParent(world, false);
            var points = Object.FindObjectsByType<PointAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(point => new Vector2(point.transform.position.x, point.transform.position.z)).ToList();
            foreach (var (name, spot, post) in Spots(world))
            {
                if (points.Any(point => Vector2.Distance(point, spot) < PointClearance))
                {
                    Debug.LogWarning("Museum night lamps: " + name + " would stand too close to a point of the visit; left out.");
                    continue;
                }
                if (Crossed(spot, post))
                {
                    Debug.LogWarning("Museum night lamps: " + name + " would cross something solid; left out.");
                    continue;
                }
                Build(root, name, spot, post, lantern, wood);
            }
            Debug.Log("Museum night lamps: " + root.childCount + " built.");
        }

        /// <summary>One lamp: a post with a foot and a small plate on the floor, and the lantern on top.</summary>
        private static void Build(Transform root, string name, Vector2 spot, float post, GameObject lantern, Material wood)
        {
            var lamp = new GameObject(name).transform;
            lamp.SetParent(root, false);
            var foot = new Vector3(spot.x, Floor(spot), spot.y);
            lamp.position = foot;
            Block(lamp, "Foot", foot + Vector3.up * .02f, new Vector3(.2f, .04f, .2f), wood);
            Block(lamp, "Post", foot + Vector3.up * post * .5f, new Vector3(PostWidth, post, PostWidth), wood);
            Block(lamp, "Plate", foot + Vector3.up * (post + .0125f), new Vector3(.17f, .025f, .17f), wood);
            float top = foot.y + post + .025f;
            var item = Object.Instantiate(lantern, lamp);
            item.name = "Lantern";
            foreach (var collider in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            var renderers = item.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            item.transform.localScale *= LanternHeight / bounds.size.y;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            item.transform.position += new Vector3(spot.x - bounds.center.x, top - bounds.min.y, spot.y - bounds.center.z);
            foreach (var part in lamp.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(part.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        /// <summary>The floor under a spot: the highest surface facing up below a raised floor's height, beams above not counted.</summary>
        public static float Floor(Vector2 spot)
        {
            float best = 0;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(new Vector3(spot.x, 4, spot.y), Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .7f && hit.point.y < 1.5f && (!found || hit.point.y > best)) { best = hit.point.y; found = true; }
            return found ? best : 0;
        }

        /// <summary>True when something solid stands where the post and its lantern would rise from the floor.</summary>
        public static bool Crossed(Vector2 spot, float post)
        {
            float floor = Floor(spot);
            float height = post + LanternHeight;
            var centre = new Vector3(spot.x, floor + .05f + height * .5f, spot.y);
            return Physics.OverlapBox(centre, new Vector3(.12f, height * .5f, .12f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0;
        }

        private static void Block(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.SetPositionAndRotation(centre, Quaternion.identity);
            block.transform.localScale = size;
            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveGI = ReceiveGI.LightProbes;
        }
    }
}
