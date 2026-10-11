using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace MusiyoBetsknate.Editor
{
    public static class BuildMuseumQuest
    {
        [MenuItem("Musiyo/Configure Quest 2 Profile")]
        public static void Configure()
        {
            const string path = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            Directory.CreateDirectory("Assets/XR"); AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            settings.SettingsForBuildTarget(BuildTargetGroup.Android).InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(settings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android),
                    "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android))
                throw new InvalidOperationException("Cannot assign Android OpenXR loader.");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (xr == null) throw new InvalidOperationException("OpenXR Android settings are missing.");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            xr.GetFeature<OculusTouchControllerProfile>().enabled = true;
            xr.GetFeature<MetaQuestFeature>().enabled = true;
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.musiyo.museum");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.forceInternetPermission = true;
            const string pointerPath = "Assets/_Musiyo/Resources/MuseumXrPointer.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(pointerPath) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")), pointerPath);
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(settings.SettingsForBuildTarget(BuildTargetGroup.Android));
            foreach (var feature in xr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>()) EditorUtility.SetDirty(feature);
            AssetDatabase.SaveAssets();
            Debug.Log("Quest 2 profile configured with OpenXR, Touch, ARM64 and single-pass rendering.");
        }

        [MenuItem("Musiyo/Build Quest 2 Prototype")]
        public static void Build()
        {
            Configure();
            Directory.CreateDirectory("Build/MuseumQuest");
            var httpPolicy = PlayerSettings.insecureHttpOption;
            try
            {
                // A USB-reversed local API is available only in this development APK; release policy is preserved.
                PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BuildMuseumBlockout.ScenePath },
                    locationPathName = "Build/MuseumQuest/Musiyo-Quest2-prototype.apk",
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Quest prototype build failed: " + report.summary.result);
            }
            finally { PlayerSettings.insecureHttpOption = httpPolicy; }
            Debug.Log("Quest prototype built. Physical headset acceptance is still required.");
        }
    }
}
