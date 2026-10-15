using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Checks the imported museum delivery against its own map before it replaces anything: files, import settings,
    /// materials, scale, orientation, UV channels, collisions and the floor a visitor can actually reach.
    /// </summary>
    public static class MuseumDeliveryValidation
    {
        public const string ScenePath = "Assets/_Musiyo/Scenes/90_Museum_Delivery_Validation.unity";
        public const string OutputFolder = "TestResults/museum-delivery";
        /// <summary>Turn about the vertical axis that brings the exported model into the scene frame of the plan.</summary>
        public const float SceneYaw = 180;
        private const float PositionTolerance = .002f;
        private const float SizeTolerance = .005f;

        /// <summary>Batch entry: validates the assets, builds the validation scene, probes it and saves captures.</summary>
        public static void Run()
        {
            Directory.CreateDirectory(OutputFolder);
            var report = new JObject();
            var errors = ValidateAssets(report);
            var scene = BuildValidationScene();
            var architecture = Find(scene, MuseumDeliveryAssets.PrefabRootName).transform;
            var manifest = MuseumDeliveryManifest.Load();
            errors.AddRange(ValidatePlacement(architecture, manifest, report));

            var points = manifest.Anchors.Where(anchor => anchor.Kind == "point_of_interest").ToList();
            var spawn = manifest.Anchors.First(anchor => anchor.Kind == "spawn");
            var probe = MuseumWalkProbe.Run(scene.GetPhysicsScene(), Position(spawn), .3f, 1.75f, .3f);
            var reach = new JObject();
            foreach (var point in points)
            {
                float distance = probe.DistanceToReached(Position(point));
                reach[point.Key] = Math.Round(distance, 2);
                // The smallest activation radius in the accepted scene is 1.5 m.
                if (distance > 1.5f - probe.Cell) errors.Add("A visitor cannot come within reach of " + point.Object + ".");
            }
            report["walk"] = new JObject
            {
                ["cell_m"] = probe.Cell, ["reached_m2"] = Math.Round(probe.ReachedArea, 1),
                ["start_ground_m"] = Math.Round(probe.StartGround, 3), ["distance_to_points_m"] = reach,
            };
            probe.SaveImage(Path.Combine(OutputFolder, "validation-walk-map.png"), points.Select(Position));
            MuseumDeliveryCaptures.Capture(Path.Combine(OutputFolder, "validation"));

            report["errors"] = new JArray(errors);
            File.WriteAllText(Path.Combine(OutputFolder, "validation.json"), report.ToString());
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("Museum delivery validated; report and captures in " + OutputFolder + ".");
        }

        public static Vector3 Position(MuseumDeliveryManifest.AnchorEntry anchor)
            => new Vector3(anchor.Position[0], anchor.Position[1], anchor.Position[2]);

        /// <summary>A scene that holds only the delivered museum, in the frame of the plan, for checks and captures.</summary>
        [MenuItem("Musiyo/Museum Delivery/Build Validation Scene")]
        public static Scene BuildValidationScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuseumDeliveryManifest.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Build the museum delivery assets first.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var architecture = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            architecture.transform.rotation = Quaternion.Euler(0, SceneYaw, 0);
            // Same neutral daylight as the museum scene, so captures of both can be compared.
            var lightObject = new GameObject("NeutralDaylight");
            lightObject.transform.rotation = Quaternion.Euler(55, -30, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Physics.SyncTransforms();
            return scene;
        }

        public static List<string> ValidateAssets() => ValidateAssets(new JObject());
        public static List<string> ValidateMuseumScene(Scene scene) => ValidateMuseumScene(scene, new JObject());

        /// <summary>Problems in the imported files, their import settings, the material sets and the model contents.</summary>
        public static List<string> ValidateAssets(JObject report)
        {
            var errors = new List<string>();
            var manifest = MuseumDeliveryManifest.Load();
            if (MuseumDeliveryImporter.Sha256(MuseumDeliveryManifest.ModelPath) != manifest.Model.Sha256)
                errors.Add("The model file differs from the delivered one.");

            var listed = new HashSet<string>();
            foreach (var texture in manifest.Textures)
            {
                listed.Add(texture.File);
                string path = MuseumDeliveryAssets.TexturePath(texture.File);
                var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (asset == null || MuseumDeliveryImporter.Sha256(path) != texture.Sha256)
                {
                    errors.Add("Texture is missing or differs from the delivered one: " + texture.File);
                    continue;
                }
                var settings = (TextureImporter)AssetImporter.GetAtPath(path);
                if (asset.width != texture.Width || asset.height != texture.Height)
                    errors.Add(texture.File + " is imported at " + asset.width + "x" + asset.height + ", not at its delivered size.");
                if ((settings.textureType == TextureImporterType.NormalMap) != (texture.Role == "normal")
                    || settings.sRGBTexture != (texture.Role == "albedo") || settings.wrapMode != TextureWrapMode.Repeat)
                    errors.Add(texture.File + " has import settings that do not match its role.");
            }
            foreach (string file in Directory.GetFiles(MuseumDeliveryManifest.TextureFolder))
                if (!file.EndsWith(".meta", StringComparison.Ordinal) && !listed.Contains(Path.GetFileName(file)))
                    errors.Add("Texture outside the delivery list: " + Path.GetFileName(file));

            var importer = (ModelImporter)AssetImporter.GetAtPath(MuseumDeliveryManifest.ModelPath);
            if (importer.generateSecondaryUV || importer.meshCompression != ModelImporterMeshCompression.Off || importer.swapUVChannels)
                errors.Add("The model import would alter the delivered lightmap UV channel.");

            var profiles = new JObject();
            var repairs = MuseumMeshRepairs.Load();
            foreach (var profile in manifest.Profiles)
            {
                int doubleSided = 0;
                var used = new HashSet<string>();
                foreach (var entry in profile.Materials)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(MuseumDeliveryManifest.MaterialPath(profile.Name, entry.Name));
                    if (material == null) { errors.Add("Missing " + profile.Name + " material " + entry.Name + "."); continue; }
                    if (material.shader.name != profile.Shader) errors.Add(profile.Name + "/" + entry.Name + " uses " + material.shader.name + ".");
                    bool both = Mathf.Approximately(material.GetFloat("_Cull"), (float)CullMode.Off);
                    if (both != (entry.DoubleSided && !repairs.DrawsOneSide(entry.Name)))
                        errors.Add(profile.Name + "/" + entry.Name + " has the wrong render face.");
                    if (both) doubleSided++;
                    Check(material, "_BaseMap", entry.BaseColorTexture, profile.Name, used, errors);
                    if (material.HasProperty("_BumpMap")) Check(material, "_BumpMap", entry.NormalTexture, profile.Name, used, errors);
                    if (material.HasProperty("_MetallicGlossMap"))
                        Check(material, "_MetallicGlossMap", entry.RoughnessTexture, profile.Name, used, errors);
                }
                profiles[profile.Name] = new JObject
                {
                    ["shader"] = profile.Shader, ["materials"] = profile.Materials.Count, ["double_sided"] = doubleSided,
                    ["textures"] = used.Count,
                    ["texture_source_bytes"] = manifest.Textures.Where(texture => used.Contains(texture.File)).Sum(texture => texture.Bytes),
                };
            }
            report["profiles"] = profiles;
            errors.AddRange(ValidateSmoothnessMaps(manifest, report));

            var root = PrefabUtility.LoadPrefabContents(MuseumDeliveryManifest.PrefabPath);
            try
            {
                root.transform.rotation = Quaternion.Euler(0, SceneYaw, 0);
                errors.AddRange(ValidateModel(root.transform, manifest, report));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return errors;
        }

        private static void Check(Material material, string property, string expected, string profile, HashSet<string> used, List<string> errors)
        {
            var texture = material.GetTexture(property);
            string file = texture != null ? Path.GetFileName(AssetDatabase.GetAssetPath(texture)) : null;
            if (file != expected) errors.Add(profile + "/" + material.name + " " + property + " is " + (file ?? "empty") + ", expected " + (expected ?? "empty") + ".");
            else if (file != null) used.Add(file);
        }

        /// <summary>
        /// Each smoothness map must average what the delivery measured for its roughness map: the lighter profiles
        /// carry that mean as a constant, so it checks the conversion without a second copy of the images.
        /// </summary>
        private static List<string> ValidateSmoothnessMaps(MuseumDeliveryManifest manifest, JObject report)
        {
            var errors = new List<string>();
            var measured = new JObject();
            // Reading a texture back needs a graphics device, which a batch run may be started without.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                report["smoothness_maps"] = "not measured without a graphics device";
                return errors;
            }
            var reference = manifest.Profile(MuseumDeliveryAssets.AuthoringProfile).Materials.ToDictionary(entry => entry.Name);
            var seen = new HashSet<string>();
            foreach (var profile in manifest.Profiles)
                foreach (var entry in profile.Materials)
                {
                    if (entry.RoughnessTexture == null || !seen.Add(entry.RoughnessTexture)) continue;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MuseumDeliveryAssets.TexturePath(entry.RoughnessTexture));
                    var target = RenderTexture.GetTemporary(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    var previous = RenderTexture.active;
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    var copy = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
                    copy.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    var pixels = copy.GetPixels32();
                    UnityEngine.Object.DestroyImmediate(copy);
                    float smoothness = (float)pixels.Average(pixel => pixel.a / 255.0), red = (float)pixels.Average(pixel => pixel.r / 255.0);
                    float expected = 1 - reference[entry.Name].Roughness;
                    measured[entry.RoughnessTexture] = new JObject { ["mean_smoothness"] = Math.Round(smoothness, 3), ["expected"] = Math.Round(expected, 3) };
                    if (Mathf.Abs(smoothness - expected) > .03f || red > .02f)
                        errors.Add(entry.RoughnessTexture + " does not hold the smoothness of its roughness map.");
                }
            report["smoothness_maps"] = measured;
            return errors;
        }

        /// <summary>Compares every object of the model, turned into the scene frame, with the delivery map.</summary>
        private static List<string> ValidateModel(Transform root, MuseumDeliveryManifest manifest, JObject report)
        {
            var errors = new List<string>();
            var transforms = new Dictionary<string, Transform>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform != root) transforms[transform.name] = transform;
            int triangles = 0, meshes = 0, lightmapChannels = 0, boxes = 0, meshColliders = 0, materialPairs = 0, expectedTotal = 0;
            var repairs = MuseumMeshRepairs.Load();
            float largestStray = 0;
            var withoutLightmapChannel = new List<string>();
            foreach (var entry in manifest.Objects)
            {
                if (!transforms.TryGetValue(entry.Name, out var transform)) { errors.Add("Missing object: " + entry.Name); continue; }
                if (!entry.Mesh) continue;
                var mesh = transform.GetComponent<MeshFilter>().sharedMesh;
                var renderer = transform.GetComponent<MeshRenderer>();
                meshes++;
                int count = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) count += (int)mesh.GetIndexCount(submesh) / 3;
                triangles += count;
                int expectedTriangles = repairs.ExpectedTriangles(entry.Name, entry.Triangles);
                expectedTotal += expectedTriangles;
                if (count != expectedTriangles) errors.Add(entry.Name + " has " + count + " triangles, the delivery and its repairs give " + expectedTriangles + ".");

                var size = renderer.bounds.size;
                var expected = new Vector3(entry.Size[0], entry.Size[1], entry.Size[2]);
                float stray = Mathf.Max(Mathf.Abs(size.x - expected.x), Mathf.Abs(size.y - expected.y), Mathf.Abs(size.z - expected.z),
                    Mathf.Abs(renderer.bounds.min.y - entry.Lowest));
                largestStray = Mathf.Max(largestStray, stray);
                if (stray > SizeTolerance) errors.Add(entry.Name + " measures " + size.ToString("F3") + " from " + renderer.bounds.min.y.ToString("F3")
                    + " m, the delivery lists " + expected.ToString("F3") + " from " + entry.Lowest + " m.");

                bool colour = mesh.uv.Length == mesh.vertexCount, lightmap = mesh.uv2.Length == mesh.vertexCount;
                if (colour != entry.UvChannels > 0 || lightmap != entry.UvChannels > 1)
                    errors.Add(entry.Name + " does not carry the UV channels the delivery lists.");
                if (lightmap)
                {
                    lightmapChannels++;
                    foreach (var uv in mesh.uv2)
                        if (uv.x < -1e-4f || uv.x > 1.0001f || uv.y < -1e-4f || uv.y > 1.0001f)
                        {
                            errors.Add(entry.Name + " has lightmap UVs outside the unit square.");
                            break;
                        }
                }
                else withoutLightmapChannel.Add(entry.Name);
                if (renderer.receiveGI == ReceiveGI.Lightmaps && !lightmap) errors.Add(entry.Name + " is lightmapped without a lightmap channel.");

                foreach (var material in renderer.sharedMaterials)
                {
                    materialPairs++;
                    string path = material != null ? AssetDatabase.GetAssetPath(material) : null;
                    if (material == null || path != MuseumDeliveryManifest.MaterialPath(MuseumDeliveryAssets.AuthoringProfile, material.name))
                        errors.Add(entry.Name + " carries a material outside the delivered set: " + (material != null ? material.name : "none"));
                }

                var colliders = transform.GetComponents<Collider>();
                bool solid = entry.Collision == "walkable" || entry.Collision == "blocking";
                if (colliders.Length != (solid ? 1 : 0)) errors.Add(entry.Name + " has " + colliders.Length + " colliders for collision role " + entry.Collision + ".");
                boxes += colliders.Count(collider => collider is BoxCollider);
                meshColliders += colliders.Count(collider => collider is MeshCollider);
            }
            int repairedObjects = manifest.Objects.Count(entry => entry.Mesh && repairs.Cards.Objects.ContainsKey(entry.Name));
            if (repairedObjects != repairs.Cards.Objects.Count) errors.Add("A repaired object is missing from the delivery map.");
            if (manifest.Objects.Sum(entry => entry.Triangles) != manifest.Model.Triangles)
                errors.Add("The delivery map does not add up to the delivered " + manifest.Model.Triangles + " triangles.");
            if (triangles != expectedTotal)
                errors.Add("The model has " + triangles + " triangles; the delivery and its repairs give " + expectedTotal + ".");
            report["model"] = new JObject
            {
                ["objects"] = transforms.Count, ["meshes"] = meshes, ["triangles"] = triangles, ["delivered_triangles"] = manifest.Model.Triangles,
                ["repaired_objects"] = repairedObjects, ["mesh_material_pairs"] = materialPairs,
                ["lightmap_channels"] = lightmapChannels, ["without_lightmap_channel"] = new JArray(withoutLightmapChannel),
                ["largest_size_or_height_difference_m"] = Math.Round(largestStray, 5),
                ["box_colliders"] = boxes, ["mesh_colliders"] = meshColliders,
            };
            errors.AddRange(ValidatePlacement(root, manifest, report));
            return errors;
        }

        /// <summary>Every delivered anchor must sit where the plan puts it once the model is in the scene frame.</summary>
        public static List<string> ValidatePlacement(Transform architecture, MuseumDeliveryManifest manifest, JObject report)
        {
            var errors = new List<string>();
            var transforms = architecture.GetComponentsInChildren<Transform>(true).ToDictionary(transform => transform.name);
            float largest = 0;
            int mirrored = 0;
            foreach (var anchor in manifest.Anchors)
            {
                if (!transforms.TryGetValue(anchor.Object, out var transform)) { errors.Add("Missing anchor object: " + anchor.Object); continue; }
                var expected = Position(anchor);
                float distance = Vector3.Distance(transform.position, expected);
                largest = Mathf.Max(largest, distance);
                if (distance > PositionTolerance) errors.Add(anchor.Object + " is " + distance.ToString("F3") + " m from its planned position.");
                // Without the turn the museum would be the mirror image through its centre.
                var unturned = architecture.InverseTransformPoint(transform.position);
                if (Vector3.Distance(unturned, expected) <= PositionTolerance && expected.sqrMagnitude > 1) mirrored++;
            }
            report["placement"] = new JObject
            {
                ["scene_yaw_degrees"] = architecture.eulerAngles.y, ["anchors"] = manifest.Anchors.Count,
                ["largest_anchor_distance_m"] = Math.Round(largest, 5), ["anchors_also_matching_without_turn"] = mirrored,
            };
            return errors;
        }

        /// <summary>Problems of the museum scene once the delivery replaced the blockout architecture.</summary>
        public static List<string> ValidateMuseumScene(Scene scene, JObject report)
        {
            var errors = new List<string>();
            var manifest = MuseumDeliveryManifest.Load();
            var architectureObject = Find(scene, MuseumDeliveryAssets.PrefabRootName);
            if (architectureObject == null) { errors.Add("The museum architecture is not in the scene."); return errors; }
            errors.AddRange(ValidatePlacement(architectureObject.transform, manifest, report));

            var anchors = new Dictionary<string, PointAnchor>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var anchor in root.GetComponentsInChildren<PointAnchor>(true)) anchors[anchor.Key] = anchor;
            var reconciled = new JArray();
            foreach (var source in manifest.Anchors.Where(anchor => anchor.Kind == "point_of_interest"))
            {
                if (!anchors.TryGetValue(source.Key, out var anchor)) { errors.Add("No scene anchor keeps the key " + source.Key + "."); continue; }
                float distance = Vector3.Distance(anchor.transform.position, Position(source));
                if (distance > .02f) errors.Add(source.Key + " is " + distance.ToString("F3") + " m from " + source.Object + ".");
                reconciled.Add(new JObject
                {
                    ["key"] = source.Key, ["source"] = source.Object, ["distance_m"] = Math.Round(distance, 4),
                    ["scene_radius_m"] = anchor.ActivationRadius, ["source_radius_m"] = source.ActivationDiameter / 2,
                });
            }
            var sourceKeys = new HashSet<string>(manifest.Anchors.Where(anchor => anchor.Key != null).Select(anchor => anchor.Key));
            report["anchors"] = new JObject
            {
                ["scene_keys"] = anchors.Count, ["matched_to_source_points"] = reconciled,
                ["scene_only_keys"] = new JArray(anchors.Keys.Where(key => !sourceKeys.Contains(key)).OrderBy(key => key)),
            };

            // Each solid of the map collides exactly once: its own collider, or the bound reading stand that stands in its place.
            var doubled = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var collider in root.GetComponentsInChildren<Collider>(false))
                {
                    if (collider is CharacterController || collider.transform.IsChildOf(architectureObject.transform)) continue;
                    if (collider.GetComponent<PointLectern>() == null) doubled.Add(collider.name);
                }
            if (doubled.Count > 0) errors.Add("Colliders left over from the blockout: " + string.Join(", ", doubled.Distinct()));
            foreach (var stand in architectureObject.GetComponentsInChildren<Transform>(false))
                if (manifest.Objects.Any(entry => entry.Name == stand.name && entry.Role == "lectern"))
                    errors.Add(stand.name + " repeats a reading stand the scene already binds to its point.");

            Physics.SyncTransforms();
            var body = Find(scene, "DesktopVisitor").GetComponent<CharacterController>();
            var probe = MuseumWalkProbe.Run(scene.GetPhysicsScene(), body.transform.position, body.radius, body.height, body.stepOffset);
            var reach = new JObject();
            foreach (var anchor in anchors.Values.OrderBy(anchor => anchor.Key))
            {
                // Reading stands count as the point, as they do for the visitor's proximity.
                float distance = probe.DistanceToReached(anchor.transform.position);
                foreach (var stand in architectureObject.transform.parent.GetComponentsInChildren<PointLectern>(false))
                    if (stand.Point == anchor) distance = Mathf.Min(distance, probe.DistanceToReached(stand.transform.position));
                reach[anchor.Key] = Math.Round(distance, 2);
                if (distance > anchor.ActivationRadius - probe.Cell)
                    errors.Add("A visitor cannot come within the activation radius of " + anchor.Key + ".");
            }
            if (Mathf.Abs(probe.StartGround - body.transform.position.y) > body.stepOffset)
                errors.Add("The visitor does not start on the floor: ground at " + probe.StartGround.ToString("F3") + " m.");
            report["walk"] = new JObject
            {
                ["cell_m"] = probe.Cell, ["reached_m2"] = Math.Round(probe.ReachedArea, 1),
                ["start_ground_m"] = Math.Round(probe.StartGround, 3), ["distance_to_points_m"] = reach,
            };
            Directory.CreateDirectory(OutputFolder);
            probe.SaveImage(Path.Combine(OutputFolder, "museum-walk-map.png"), anchors.Values.Select(anchor => anchor.transform.position));
            return errors;
        }

        public static GameObject Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform.name == name) return transform.gameObject;
            return null;
        }
    }

    /// <summary>
    /// Floods the floor a visitor's capsule can reach from a start position, over the colliders of a scene:
    /// it stands where nothing solid overlaps it and moves where the floor changes by no more than one step.
    /// </summary>
    public sealed class MuseumWalkProbe
    {
        private static readonly Rect Area = Rect.MinMaxRect(-24, -64, 34, 28);
        private const float ProbeHeight = 1;
        public readonly float Cell = .25f;
        private readonly int columns, rows;
        private readonly float[] ground;
        private readonly bool[] standable, reached;

        public float StartGround { get; private set; } = float.NaN;
        public float ReachedArea => reached.Count(cell => cell) * Cell * Cell;

        private MuseumWalkProbe()
        {
            columns = Mathf.CeilToInt(Area.width / Cell);
            rows = Mathf.CeilToInt(Area.height / Cell);
            ground = new float[columns * rows];
            standable = new bool[columns * rows];
            reached = new bool[columns * rows];
        }

        private Vector3 Centre(int column, int row, float height)
            => new Vector3(Area.xMin + (column + .5f) * Cell, height, Area.yMin + (row + .5f) * Cell);

        public static MuseumWalkProbe Run(PhysicsScene physics, Vector3 start, float radius, float height, float step)
        {
            var probe = new MuseumWalkProbe();
            var hits = new RaycastHit[16];
            var overlaps = new Collider[16];
            for (int row = 0; row < probe.rows; row++)
                for (int column = 0; column < probe.columns; column++)
                {
                    int index = row * probe.columns + column;
                    probe.ground[index] = float.NaN;
                    int count = physics.Raycast(probe.Centre(column, row, ProbeHeight), Vector3.down, hits, ProbeHeight + 2, ~0, QueryTriggerInteraction.Ignore);
                    float nearest = float.PositiveInfinity;
                    for (int hit = 0; hit < count; hit++)
                        if (!(hits[hit].collider is CharacterController) && hits[hit].distance < nearest) nearest = hits[hit].distance;
                    if (float.IsPositiveInfinity(nearest)) continue;
                    float floor = ProbeHeight - nearest;
                    probe.ground[index] = floor;
                    // The capsule is tested from one step above the floor: anything lower is climbed, not an obstacle.
                    int solid = physics.OverlapCapsule(probe.Centre(column, row, floor + step + radius), probe.Centre(column, row, floor + height - radius),
                        radius - .02f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    for (int overlap = 0; overlap < solid; overlap++) blocked |= !(overlaps[overlap] is CharacterController);
                    probe.standable[index] = !blocked;
                }

            int startColumn = Mathf.FloorToInt((start.x - Area.xMin) / probe.Cell), startRow = Mathf.FloorToInt((start.z - Area.yMin) / probe.Cell);
            int first = startRow * probe.columns + startColumn;
            if (startColumn < 0 || startColumn >= probe.columns || startRow < 0 || startRow >= probe.rows || !probe.standable[first]) return probe;
            probe.StartGround = probe.ground[first];
            var queue = new Queue<int>();
            probe.reached[first] = true;
            queue.Enqueue(first);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int column = index % probe.columns, row = index / probe.columns;
                foreach (var (nextColumn, nextRow) in new[] { (column + 1, row), (column - 1, row), (column, row + 1), (column, row - 1) })
                {
                    if (nextColumn < 0 || nextColumn >= probe.columns || nextRow < 0 || nextRow >= probe.rows) continue;
                    int next = nextRow * probe.columns + nextColumn;
                    if (probe.reached[next] || !probe.standable[next] || Mathf.Abs(probe.ground[next] - probe.ground[index]) > step) continue;
                    probe.reached[next] = true;
                    queue.Enqueue(next);
                }
            }
            return probe;
        }

        /// <summary>Floor distance from a position to the nearest place the visitor can stand.</summary>
        public float DistanceToReached(Vector3 position)
        {
            float best = float.PositiveInfinity;
            for (int index = 0; index < reached.Length; index++)
            {
                if (!reached[index]) continue;
                var offset = Centre(index % columns, index / columns, position.y) - position;
                best = Mathf.Min(best, offset.sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Plan view, north up: reached floor in green, other standable floor in ochre, obstacles in grey, points in red.</summary>
        public void SaveImage(string path, IEnumerable<Vector3> points)
        {
            const int scale = 3;
            var image = new Texture2D(columns * scale, rows * scale, TextureFormat.RGB24, false);
            var pixels = new Color32[columns * scale * rows * scale];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    var colour = reached[index] ? new Color32(70, 160, 90, 255) : standable[index] ? new Color32(190, 160, 80, 255)
                        : float.IsNaN(ground[index]) ? new Color32(15, 15, 15, 255) : new Color32(90, 90, 95, 255);
                    for (int y = 0; y < scale; y++)
                        for (int x = 0; x < scale; x++) pixels[(row * scale + y) * columns * scale + column * scale + x] = colour;
                }
            foreach (var point in points)
            {
                int centreX = Mathf.RoundToInt((point.x - Area.xMin) / Cell * scale), centreY = Mathf.RoundToInt((point.z - Area.yMin) / Cell * scale);
                for (int y = -3; y <= 3; y++)
                    for (int x = -3; x <= 3; x++)
                    {
                        int pixelX = centreX + x, pixelY = centreY + y;
                        if (pixelX >= 0 && pixelX < columns * scale && pixelY >= 0 && pixelY < rows * scale)
                            pixels[pixelY * columns * scale + pixelX] = new Color32(220, 40, 40, 255);
                    }
            }
            image.SetPixels32(pixels);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    /// <summary>Renders fixed views of the open scene, placed like the delivery's own comparison cameras.</summary>
    public static class MuseumDeliveryCaptures
    {
        /// <summary>Name, eye and target in the scene frame, and lens in millimetres on a 36 mm sensor.</summary>
        private static readonly (string name, Vector3 eye, Vector3 target, float lens)[] Views =
        {
            ("llegada_eye", new Vector3(0, 1.6f, -50), new Vector3(0, 2.6f, -28), 24),
            ("portada_close", new Vector3(0, 1.6f, -33), new Vector3(0, 3.2f, -26.5f), 24),
            ("bienvenida_inside", new Vector3(0, 1.6f, -24.5f), new Vector3(0, 2.6f, -20), 18),
            ("personajes_patio_north", new Vector3(0, 1.6f, -10.5f), new Vector3(0, 1.6f, 2), 24),
            ("personajes_pedestals", new Vector3(0, 1.6f, -6), new Vector3(0, 1.2f, 1), 20),
            ("personajes_lectern_close", new Vector3(-5.2f, 1.5f, -5), new Vector3(-7, 1, -6), 28),
            ("personajes_corridor_east", new Vector3(11.75f, 1.6f, -9.75f), new Vector3(11.75f, 1.8f, 10), 22),
            ("instrumentos_inside", new Vector3(16, 1.6f, -4.5f), new Vector3(23, 1.6f, -4.5f), 18),
            ("fogon_inside", new Vector3(0, 1.6f, 15.3f), new Vector3(.5f, 1.2f, 21), 18),
            ("mirador_exit", new Vector3(-6.8f, 1.6f, 18.5f), new Vector3(-16, 1.3f, 18.5f), 20),
            ("exterior_southeast", new Vector3(34, 9, -34), new Vector3(3, 2, -2), 24),
        };

        public static void Capture(string prefix, params string[] only)
        {
            const int width = 960, height = 540;
            var cameraObject = new GameObject("DeliveryCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 600;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            // A shader still compiling in the background would leave its placeholder in the first pictures.
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.transform.SetPositionAndRotation(Views[0].eye, Quaternion.identity);
                camera.Render();
                foreach (var view in Views)
                {
                    if (only.Length > 0 && Array.IndexOf(only, view.name) < 0) continue;
                    camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.target - view.eye, Vector3.up));
                    // The delivery states the horizontal field; Unity takes the vertical one.
                    camera.fieldOfView = 2 * Mathf.Atan(18 / view.lens * height / width) * Mathf.Rad2Deg;
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.active = previous;
                    File.WriteAllBytes(prefix + "__" + view.name + ".png", image.EncodeToPNG());
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asynchronous;
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
