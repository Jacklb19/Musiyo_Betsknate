using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>The museum sky materials, and pictures of the fixed delivery views before and after the visit atmosphere.</summary>
    public static class MuseumAtmosphereTools
    {
        public const string SkyPath = "Assets/_Musiyo/Resources/" + MuseumAtmosphere.SkyMaterial + ".mat";
        public const string FallbackSkyPath = "Assets/_Musiyo/Resources/" + MuseumAtmosphere.FallbackSkyMaterial + ".mat";
        public const string SkyShader = "Musiyo/Sky Panorama";
        /// <summary>Poly Haven "Kloppenheim 03 (Pure Sky)", CC0; see ThirdParty/third_party_assets.json.</summary>
        public const string PanoramaPath = "Assets/_Musiyo/Museum/ThirdParty/PolyHaven/Kloppenheim03PureSky/museum_sky_kloppenheim_03_4k.jpg";
        public const string CaptureFolder = "TestResults/atmosphere";

        [MenuItem("Musiyo/Atmosphere/Create Sky Materials")]
        public static void CreateSky()
        {
            // The earlier procedural sky keeps its asset (and identifier) under the fallback name.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (existing != null && existing.shader.name != SkyShader && AssetDatabase.LoadAssetAtPath<Material>(FallbackSkyPath) == null)
            {
                string error = AssetDatabase.MoveAsset(SkyPath, FallbackSkyPath);
                if (!string.IsNullOrEmpty(error)) throw new IOException(error);
            }
            CreateFallback();
            ConfigurePanorama();
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            var shader = Shader.Find(SkyShader);
            if (sky == null)
            {
                sky = new Material(shader) { name = MuseumAtmosphere.SkyMaterial };
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            sky.shader = shader;
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(PanoramaPath));
            sky.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssets();
        }

        private static void CreateFallback()
        {
            var shader = Shader.Find("Skybox/Procedural");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(FallbackSkyPath);
            if (sky == null)
            {
                sky = new Material(shader) { name = MuseumAtmosphere.FallbackSkyMaterial };
                AssetDatabase.CreateAsset(sky, FallbackSkyPath);
            }
            sky.shader = shader;
            sky.name = MuseumAtmosphere.FallbackSkyMaterial;
            // A softer, warmer afternoon than the default sky: smaller sun, lighter haze toward the horizon.
            sky.SetFloat("_SunDisk", 2);
            sky.SetFloat("_SunSize", .035f);
            sky.SetFloat("_SunSizeConvergence", 6);
            sky.SetFloat("_AtmosphereThickness", .8f);
            sky.SetColor("_SkyTint", new Color(.52f, .58f, .66f));
            sky.SetColor("_GroundColor", new Color(.46f, .44f, .4f));
            sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
        }

        /// <summary>
        /// The panorama is magnified on every display (4096 px around the circle is about 11 px per degree), so it carries
        /// no mip maps, which also keeps its wrap seam invisible. Windows reads the full image; Web and the headset half.
        /// </summary>
        public static void ConfigurePanorama()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(PanoramaPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            Platform(importer, "WebGL", 2048, TextureImporterFormat.DXT1);
            Platform(importer, "Android", 2048, TextureImporterFormat.ASTC_6x6);
            importer.SaveAndReimport();
        }

        private static void Platform(TextureImporter importer, string platform, int size, TextureImporterFormat format)
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = size;
            settings.format = format;
            importer.SetPlatformTextureSettings(settings);
        }

        [MenuItem("Musiyo/Atmosphere/Capture Before And After")]
        public static void Capture()
        {
            Directory.CreateDirectory(CaptureFolder);
            EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            MuseumDeliveryCaptures.Capture(Path.Combine(CaptureFolder, "before"));
            MuseumAtmosphere.Current.Apply();
            MuseumDeliveryCaptures.Capture(Path.Combine(CaptureFolder, "after"));
        }
    }
}
