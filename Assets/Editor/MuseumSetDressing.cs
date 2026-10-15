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
    /// Replaces the delivered stand-in lanterns and maize with licensed models (see ThirdParty/third_party_assets.json).
    /// Each replacement stands where the delivered piece stood, at its height; the delivered groups stay in the scene with
    /// their renderers switched off, so the change can be undone without touching the delivery.
    /// </summary>
    public static class MuseumSetDressing
    {
        public const string Root = "Assets/_Musiyo/Museum/ThirdParty";
        public const string LanternModel = Root + "/PolyHaven/WoodenLantern01/wooden_lantern_01_museum.fbx";
        public const string CornModel = Root + "/Quaternius/CropsPack/Corn_4.fbx";
        public const string MaterialFolder = Root + "/Materials";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string LanternPrefab = PrefabFolder + "/PathLantern.prefab";
        public const string MaizePrefab = PrefabFolder + "/MaizePlant.prefab";
        public const string SceneRoot = "MuseumSetDressing";
        public static readonly string[] ReplacedGroups =
            { "Sendero_Faroles_Luz", "Jajan_Maiz", "Jajan_Maiz_Espigas", "Patio_Maiz", "Patio_Maiz_Espigas" };
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
            Texture(folder + "/wooden_lantern_01_diff_1k.jpg", false);
            Texture(folder + "/wooden_lantern_01_glass_diff_1k.jpg", false);
            Texture(folder + "/wooden_lantern_01_nor_gl_1k.jpg", true);
            Texture(folder + "/wooden_lantern_01_glass_nor_gl_1k.jpg", true);
            ConfigureModel(LanternModel, name => name.Contains("glass") ? LanternGlass(folder) : LanternWood(folder));
            ConfigureModel(CornModel, CornMaterial);
            SavePrefab(LanternModel, LanternPrefab);
            SavePrefab(CornModel, MaizePrefab);
            AssetDatabase.SaveAssets();
        }

        private static void Texture(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.maxTextureSize = 512;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 256;
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
            importer.isReadable = false;
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
            else BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
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

        /// <summary>Flat colours as authored in the crop model; leaves are thin, so both sides are drawn.</summary>
        private static Material CornMaterial(string name)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(CornModel).OfType<Material>().FirstOrDefault(material => material.name == name);
            var built = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Maize_" + name + ".mat");
            var colour = source != null && source.HasProperty("_Color") ? source.color
                : built != null ? built.GetColor("_BaseColor") : Color.white;
            colour.a = 1;
            return Save("Maize_" + name, Shader.Find("Universal Render Pipeline/Simple Lit"), material =>
            {
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.doubleSidedGI = true;
                material.SetFloat("_Smoothness", .2f);
            });
        }

        private static void SavePrefab(string model, string prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(model));
            try
            {
                foreach (var item in instance.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(item.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
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
                    Instance(maize, root, "MaizePlant", piece.Base, (float)random.NextDouble() * 360, scale);
                }
            foreach (var name in ReplacedGroups) Group(name).enabled = false;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Museum set dressing placed: " + root.childCount + " pieces.");
        }

        private static void Instance(GameObject prefab, Transform parent, string name, Vector3 position, float yaw, float scale)
        {
            var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
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
