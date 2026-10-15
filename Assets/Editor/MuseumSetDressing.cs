using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Replaces the delivered stand-in lanterns, maize, planter plants and flower heads with licensed models (see
    /// ThirdParty/third_party_assets.json). Each replacement stands where the delivered piece stood; the delivered groups
    /// stay in the scene with their renderers switched off, so the change can be undone without touching the delivery.
    /// Plants are drawn with the museum foliage shader and combined per area when the scene is processed
    /// (<see cref="MuseumFoliageBatch"/>). Nothing here names a species or a use: the models are neutral greenery.
    /// </summary>
    public static class MuseumSetDressing
    {
        public const string Root = "Assets/_Musiyo/Museum/ThirdParty";
        public const string LanternModel = Root + "/PolyHaven/WoodenLantern01/wooden_lantern_01_museum.fbx";
        public const string CornModel = Root + "/Quaternius/CropsPack/Corn_4.fbx";
        public const string LeavesModel = Root + "/PolyHaven/Fern02/fern_02_museum.fbx";
        public const string FlowerModel = Root + "/PolyHaven/FlowerEmpodium/flower_empodium_museum.fbx";
        public const string MaterialFolder = Root + "/Materials";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string LanternPrefab = PrefabFolder + "/PathLantern.prefab";
        public const string MaizePrefab = PrefabFolder + "/MaizePlant.prefab";
        public const string SceneRoot = "MuseumSetDressing";
        public const string FoliageShader = "Musiyo/Foliage";
        /// <summary>Suffix of the material a headset build uses instead of the screen one.</summary>
        public const string HeadsetSuffix = "_Headset";
        public const string LeavesName = "PlanterLeaves";
        public const string FlowerName = "FlowerClump";
        public const string MaizeName = "MaizePlant";
        public static readonly string[] PlanterGroups = { "Jajan_Plantas_Medicinales", "Patio_Plantas_Medicinales" };
        /// <summary>Flower stand-ins by group, with the colour the delivery gives them.</summary>
        public static readonly (string group, string colour)[] FlowerGroups =
        {
            ("Jajan_Flores_amarillo", "Yellow"), ("Jajan_Flores_blanco", "White"), ("Jajan_Flores_rojo", "Red"),
            ("Patio_Flores_amarillo", "Yellow"), ("Patio_Flores_blanco", "White"), ("Patio_Flores_rojo", "Red"),
        };
        public static readonly string[] ReplacedGroups =
            new[] { "Sendero_Faroles_Luz", "Jajan_Maiz", "Jajan_Maiz_Espigas", "Patio_Maiz", "Patio_Maiz_Espigas" }
                .Concat(PlanterGroups).Concat(FlowerGroups.Select(flower => flower.group)).ToArray();
        /// <summary>Flower model variants that carry blooms; the others are leaves only.</summary>
        private static readonly int[] BloomingVariants = { 1, 2, 3 };
        /// <summary>Clear space between the lower board of a delivered lantern post and its cap.</summary>
        private const float LanternHeight = .3f;

        [MenuItem("Musiyo/Museum Delivery/Build And Place Set Dressing")]
        public static void Run()
        {
            BuildAssets();
            Place();
        }

        public static void BuildAssets()
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(PrefabFolder);
            var folder = Path.GetDirectoryName(LanternModel).Replace('\\', '/');
            Texture(folder + "/wooden_lantern_01_diff_1k.jpg", false, 512, 256);
            Texture(folder + "/wooden_lantern_01_glass_diff_1k.jpg", false, 512, 256);
            Texture(folder + "/wooden_lantern_01_nor_gl_1k.jpg", true, 512, 256);
            Texture(folder + "/wooden_lantern_01_glass_nor_gl_1k.jpg", true, 512, 256);
            ConfigureModel(LanternModel, name => name.Contains("glass") ? LanternGlass(folder) : LanternWood(folder));
            ConfigureModel(CornModel, CornMaterial);
            ConfigurePlantModel(LeavesModel);
            ConfigurePlantModel(FlowerModel);
            BuildPlantMaterials();
            SavePrefab(LanternModel, LanternPrefab, true);
            SavePrefab(CornModel, MaizePrefab, false);
            AssetDatabase.SaveAssets();
        }

        private static void Texture(string path, bool normal, int size, int headsetSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.maxTextureSize = size;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = headsetSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        /// <summary>Imports a model with nothing but meshes, and maps each of its materials to one of ours.</summary>
        private static void ConfigureModel(string path, Func<string, Material> material)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            // Plants are combined when the scene enters Play mode or a build, which reads their vertices; the processed
            // scene no longer refers to these meshes, so their CPU copy never reaches a player.
            importer.isReadable = path == CornModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();
            // Once remapped, a material is no longer embedded; the remap still names it, so a rebuild refreshes it too.
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(source => source.name)
                .Concat(importer.GetExternalObjectMap().Keys.Where(key => key.type == typeof(Material)).Select(key => key.name))
                .Distinct().ToList();
            foreach (var name in names)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), material(name));
            importer.SaveAndReimport();
        }

        /// <summary>Plant variants come one mesh per object, upright, with no materials of their own.</summary>
        private static void ConfigurePlantModel(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.isReadable = true;
            importer.bakeAxisConversion = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static Material Save(string name, Shader shader, Action<Material> setup)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            setup(material);
            if (shader.name == "Universal Render Pipeline/Lit") BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            else if (shader.name == "Universal Render Pipeline/Simple Lit") BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material LanternWood(string folder) => Save("PathLantern_Wood", Shader.Find("Universal Render Pipeline/Lit"), material =>
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/wooden_lantern_01_diff_1k.jpg"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/wooden_lantern_01_nor_gl_1k.jpg"));
            material.SetFloat("_Smoothness", .25f);
        });

        /// <summary>The glass glows warmly by day without the white glare of the delivered emissive cubes.</summary>
        private static Material LanternGlass(string folder) => Save("PathLantern_Glass", Shader.Find("Universal Render Pipeline/Lit"), material =>
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/wooden_lantern_01_glass_diff_1k.jpg"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/wooden_lantern_01_glass_nor_gl_1k.jpg"));
            material.SetFloat("_Smoothness", .7f);
            material.SetColor("_EmissionColor", new Color(1f, .55f, .22f) * 1.5f);
            // URP enables emission only for materials that declare emissive GI; the lanterns do not contribute to a bake.
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");
        });

        /// <summary>
        /// The colours the crop model was authored with, drawn by the foliage shader: darker at the foot, a little different
        /// from plant to plant, lit through the leaves against the sun and swaying slowly.
        /// </summary>
        private static Material CornMaterial(string name)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(CornModel).OfType<Material>().FirstOrDefault(material => material.name == name);
            var built = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Maize_" + name + ".mat");
            var colour = source != null && source.HasProperty("_Color") ? source.color
                : built != null && built.HasProperty("_BaseColor") ? built.GetColor("_BaseColor") : Color.white;
            colour.a = 1;
            return Save("Maize_" + name, Shader.Find(FoliageShader), material => Foliage(material, null, null, colour, name == "Yellow" ? .8f : .5f, .045f));
        }

        private static void Foliage(Material material, Texture2D colour, Texture2D normal, Color tint, float baseShade, float sway)
        {
            material.SetTexture("_BaseMap", colour);
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BumpMap", normal);
            if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            material.SetFloat("_UseNormalMap", normal != null ? 1 : 0);
            material.SetFloat("_BaseShade", baseShade);
            material.SetFloat("_Variation", .1f);
            material.SetFloat("_Translucency", .55f);
            material.SetFloat("_Smoothness", .25f);
            material.SetFloat("_WindStrength", sway);
            material.SetFloat("_WindSpeed", 1.1f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.doubleSidedGI = true;
            material.enableInstancing = true;
        }

        /// <summary>Screen and headset materials of the planter leaves and of the three flower colours.</summary>
        private static void BuildPlantMaterials()
        {
            string leaves = Path.GetDirectoryName(LeavesModel).Replace('\\', '/');
            string flowers = Path.GetDirectoryName(FlowerModel).Replace('\\', '/');
            Texture(leaves + "/fern_02_diff_1k.jpg", false, 1024, 512);
            Texture(leaves + "/fern_02_nor_gl_1k.jpg", true, 1024, 256);
            foreach (var file in new[] { "flower_empodium_diff_1k.jpg", "flower_empodium_diff_1k_red.jpg", "flower_empodium_diff_1k_white.jpg" })
                Texture(flowers + "/" + file, false, 1024, 512);
            Texture(flowers + "/flower_empodium_nor_gl_1k.jpg", true, 1024, 256);
            var shader = Shader.Find(FoliageShader);
            var leavesColour = AssetDatabase.LoadAssetAtPath<Texture2D>(leaves + "/fern_02_diff_1k.jpg");
            var leavesNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(leaves + "/fern_02_nor_gl_1k.jpg");
            // The headset sets leave the normal maps out; the leaves keep their shape from geometry and the colour map.
            Save(LeavesName, shader, material => Foliage(material, leavesColour, leavesNormal, Color.white, .6f, .02f));
            Save(LeavesName + HeadsetSuffix, shader, material => Foliage(material, leavesColour, null, Color.white, .6f, .02f));
            var flowerNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(flowers + "/flower_empodium_nor_gl_1k.jpg");
            foreach (var (colourName, file) in new[] { ("Yellow", "flower_empodium_diff_1k.jpg"), ("Red", "flower_empodium_diff_1k_red.jpg"),
                ("White", "flower_empodium_diff_1k_white.jpg") })
            {
                var colour = AssetDatabase.LoadAssetAtPath<Texture2D>(flowers + "/" + file);
                Save(FlowerName + "_" + colourName, shader, material => Foliage(material, colour, flowerNormal, Color.white, .65f, .025f));
                Save(FlowerName + "_" + colourName + HeadsetSuffix, shader, material => Foliage(material, colour, null, Color.white, .65f, .025f));
            }
        }

        private static void SavePrefab(string model, string prefab, bool batch)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(model));
            try
            {
                // Plants are combined per area by the foliage step; static batching would merge them first and lose each
                // plant's own height and tone.
                var flags = batch ? StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic : StaticEditorFlags.OccludeeStatic;
                foreach (var item in instance.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(item.gameObject, flags);
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.receiveGI = ReceiveGI.LightProbes;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        /// <summary>Height of a prefab standing upright, from its renderers.</summary>
        private static Bounds Extent(GameObject prefab)
        {
            var probe = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var renderers = probe.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        /// <summary>Variant meshes of a plant model, in the order of their names (PlanterLeaves_1, _2, ...).</summary>
        public static List<Mesh> Variants(string model, string prefix)
            => AssetDatabase.LoadAllAssetsAtPath(model).OfType<Mesh>().Where(mesh => mesh.name.StartsWith(prefix + "_", StringComparison.Ordinal))
                .OrderBy(mesh => mesh.name, StringComparer.Ordinal).ToList();

        /// <summary>Places the replacements in the saved museum scene and switches the delivered stand-ins off.</summary>
        public static void Place()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToList();
            MeshRenderer Group(string name) => all.First(renderer => renderer.name == name);
            var architecture = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .First(item => item.name == MuseumDeliveryAssets.PrefabRootName);
            var existing = architecture.parent != null ? architecture.parent.Find(SceneRoot) : null;
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            foreach (var name in ReplacedGroups) Group(name).enabled = true;
            Physics.SyncTransforms();

            var root = new GameObject(SceneRoot).transform;
            root.SetParent(architecture.parent, false);
            var random = new System.Random(20261015);

            var lantern = AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefab);
            var lanternSize = Extent(lantern).size;
            foreach (var piece in MuseumPropSurvey.Pieces(Group("Sendero_Faroles_Luz")))
            {
                // The delivered light box sits 5 cm above the board it stands on.
                var spot = piece.Base + Vector3.down * .05f;
                float yaw = spot.x > 0 ? -90 : 90;
                Instance(lantern, root, "PathLantern", spot, yaw, LanternHeight / lanternSize.y);
            }
            var maize = AssetDatabase.LoadAssetAtPath<GameObject>(MaizePrefab);
            var maizeSize = Extent(maize).size;
            foreach (var group in new[] { "Jajan_Maiz", "Patio_Maiz" })
                foreach (var piece in MuseumPropSurvey.Pieces(Group(group)).Where(piece => piece.Size.y > 1))
                {
                    float scale = piece.Size.y / maizeSize.y * (.92f + (float)random.NextDouble() * .16f);
                    Instance(maize, root, MaizeName, piece.Base, (float)random.NextDouble() * 360, scale);
                }

            var leaves = Variants(LeavesModel, LeavesName);
            var leavesMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + LeavesName + ".mat");
            foreach (var group in PlanterGroups)
                foreach (var plant in Plants(MuseumPropSurvey.Pieces(Group(group)), .3f))
                {
                    var mesh = leaves[random.Next(leaves.Count)];
                    var size = mesh.bounds.size;
                    float width = Mathf.Max(size.x, size.z);
                    float scale = Mathf.Clamp(Mathf.Min(plant.Size.y * 1.25f / size.y, Mathf.Max(plant.Size.x, plant.Size.z) * 1.6f / width), .45f, 1.3f);
                    var spot = new Vector3(plant.Base.x, Ground(plant.Base, plant.Base.y), plant.Base.z);
                    Instance(Plant(LeavesName, mesh, leavesMaterial), root, LeavesName, spot, (float)random.NextDouble() * 360, scale, true);
                }

            var flowers = Variants(FlowerModel, FlowerName);
            foreach (var (group, colour) in FlowerGroups)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + FlowerName + "_" + colour + ".mat");
                foreach (var head in MuseumPropSurvey.Pieces(Group(group)))
                {
                    var mesh = flowers[BloomingVariants[random.Next(BloomingVariants.Length)] - 1];
                    float top = head.Base.y + head.Size.y;
                    float ground = Ground(head.Base, head.Base.y);
                    // Taller than this, the thin leaves sprawl like grass; the delivered heads floated above the soil anyway.
                    float scale = Mathf.Clamp((top - ground) / mesh.bounds.size.y, .8f, 1.35f);
                    Instance(Plant(FlowerName, mesh, material), root, FlowerName, new Vector3(head.Base.x, ground, head.Base.z),
                        (float)random.NextDouble() * 360, scale, true);
                }
            }
            foreach (var name in ReplacedGroups) Group(name).enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Museum set dressing placed: " + root.childCount + " pieces.");
        }

        /// <summary>Groups leaf pieces whose bases lie within the given distance into one plant each.</summary>
        public static List<MuseumPropSurvey.Piece> Plants(IEnumerable<MuseumPropSurvey.Piece> pieces, float distance)
        {
            var plants = new List<List<MuseumPropSurvey.Piece>>();
            foreach (var piece in pieces)
            {
                var plant = plants.FirstOrDefault(found => found.Any(other =>
                    Mathf.Abs(other.Base.x - piece.Base.x) < distance && Mathf.Abs(other.Base.z - piece.Base.z) < distance));
                if (plant != null) plant.Add(piece); else plants.Add(new List<MuseumPropSurvey.Piece> { piece });
            }
            return plants.Select(plant =>
            {
                var bounds = new Bounds(plant[0].Base + Vector3.up * plant[0].Size.y / 2, plant[0].Size);
                foreach (var piece in plant) bounds.Encapsulate(new Bounds(piece.Base + Vector3.up * piece.Size.y / 2, piece.Size));
                return new MuseumPropSurvey.Piece
                {
                    Base = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Size = bounds.size,
                    Triangles = plant.Sum(piece => piece.Triangles),
                };
            }).ToList();
        }

        /// <summary>The ground or planter soil under a spot: the first collider below it, or the given height.</summary>
        private static float Ground(Vector3 spot, float fallback)
        {
            var from = new Vector3(spot.x, fallback + 1, spot.z);
            return Physics.Raycast(from, Vector3.down, out var hit, 2, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > fallback - .6f
                ? hit.point.y : fallback;
        }

        /// <summary>A temporary template of one plant variant, removed once it has been placed.</summary>
        private static GameObject Plant(string name, Mesh mesh, Material material)
        {
            var plant = new GameObject(name);
            plant.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = plant.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(plant, StaticEditorFlags.OccludeeStatic);
            return plant;
        }

        private static void Instance(GameObject template, Transform parent, string name, Vector3 position, float yaw, float scale, bool consume = false)
        {
            var item = consume ? template : PrefabUtility.IsPartOfPrefabAsset(template)
                ? (GameObject)PrefabUtility.InstantiatePrefab(template, parent) : UnityEngine.Object.Instantiate(template, parent);
            item.transform.SetParent(parent, false);
            item.name = name;
            item.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0) * item.transform.rotation);
            item.transform.localScale = item.transform.localScale * scale;
            // A model's pivot may not sit on its base: stand it on the spot.
            var renderers = item.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            item.transform.position += new Vector3(position.x - bounds.center.x, position.y - bounds.min.y, position.z - bounds.center.z);
        }
    }
}
