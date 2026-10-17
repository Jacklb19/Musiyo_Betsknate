using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Imports the hand visualizer sample of XR Hands (Unity Companion License, shipped with the package), switches on the
    /// OpenXR hand tracking features for Android and points the headset's hand presence at the sample's hands.
    /// Batch: -executeMethod MusiyoBetsknate.Editor.MuseumHandsSetup.Setup.
    /// </summary>
    public static class MuseumHandsSetup
    {
        public static void Setup()
        {
            var samples = Sample.FindByPackage("com.unity.xr.hands", "").ToList();
            Debug.Log("Hands samples: " + string.Join(", ", samples.Select(item => item.displayName + " -> " + item.importPath)));
            var visualizer = samples.First(item => item.displayName.Replace(" ", "") == "HandVisualizer");
            if (!visualizer.isImported && !visualizer.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow))
                throw new InvalidOperationException("The hand visualizer sample did not import.");
            AssetDatabase.Refresh();
            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            foreach (var feature in settings.GetFeatures())
            {
                string type = feature.GetType().Name;
                if (type == "HandTracking" || type == "MetaHandTrackingAim") { feature.enabled = true; EditorUtility.SetDirty(feature); }
                Debug.Log("Hands feature " + type + " enabled=" + feature.enabled);
            }
            AssetDatabase.SaveAssets();
            foreach (var path in Directory.GetFiles(visualizer.importPath, "*.prefab", SearchOption.AllDirectories))
                Debug.Log("Hands prefab " + path.Replace('\\', '/'));
            foreach (var path in Directory.GetFiles(visualizer.importPath, "*.*", SearchOption.AllDirectories).Where(item => !item.EndsWith(".meta")))
                Debug.Log("Hands file " + path.Replace('\\', '/') + " " + new FileInfo(path).Length);
            CreateAssets();
        }

        /// <summary>Points the headset's hand presence at the sample's tracked hands and hand models.</summary>
        public static void CreateAssets()
        {
            const string root = "Assets/Samples/XR Hands/1.9.0/HandVisualizer/";
            const string path = "Assets/Scripts/Hands/Resources/" + Museum.MuseumHandAssets.ResourceName + ".asset";
            var assets = AssetDatabase.LoadAssetAtPath<Museum.MuseumHandAssets>(path);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<Museum.MuseumHandAssets>();
                AssetDatabase.CreateAsset(assets, path);
            }
            assets.LeftTracked = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Left Hand Tracking.prefab");
            assets.RightTracked = AssetDatabase.LoadAssetAtPath<GameObject>(root + "Prefabs/Right Hand Tracking.prefab");
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log("Hands assets " + (assets.LeftTracked != null) + " " + (assets.RightTracked != null));
        }

        /// <summary>Batch: prepares the hands and builds the Quest APK in one run.</summary>
        public static void SetupAndBuild()
        {
            CreateAssets();
            BuildMuseumQuest.Build();
        }
    }
}
