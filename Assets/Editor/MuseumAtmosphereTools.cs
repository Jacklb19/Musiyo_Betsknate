using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>The museum sky material, and pictures of the fixed delivery views before and after the visit atmosphere.</summary>
    public static class MuseumAtmosphereTools
    {
        public const string SkyPath = "Assets/_Musiyo/Resources/" + MuseumAtmosphere.SkyMaterial + ".mat";
        public const string CaptureFolder = "TestResults/atmosphere";

        [MenuItem("Musiyo/Atmosphere/Create Sky Material")]
        public static void CreateSky()
        {
            var shader = Shader.Find("Skybox/Procedural");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null)
            {
                sky = new Material(shader) { name = MuseumAtmosphere.SkyMaterial };
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            sky.shader = shader;
            // A softer, warmer afternoon than the default sky: smaller sun, lighter haze toward the horizon.
            sky.SetFloat("_SunDisk", 2);
            sky.SetFloat("_SunSize", .035f);
            sky.SetFloat("_SunSizeConvergence", 6);
            sky.SetFloat("_AtmosphereThickness", .8f);
            sky.SetColor("_SkyTint", new Color(.52f, .58f, .66f));
            sky.SetColor("_GroundColor", new Color(.46f, .44f, .4f));
            sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
            AssetDatabase.SaveAssets();
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
