using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// One museum, one material set per platform. Scenes are saved with the authoring set; a build swaps in the
    /// set its platform is assigned in the profile configuration, so only that set's textures reach the player.
    /// </summary>
    public static class MuseumSurfaceProfiles
    {
        public const string ConfigurationPath = MuseumDeliveryManifest.Root + "/Delivery/surface_profiles.json";

        private sealed class Configuration
        {
            [JsonProperty("schema_version")] public int SchemaVersion = 1;
            /// <summary>Unity build target name to surface profile name.</summary>
            [JsonProperty("build_targets")] public Dictionary<string, string> BuildTargets = new Dictionary<string, string>();
        }

        /// <summary>Profile assigned to a build target, or the authoring profile when the configuration names none.</summary>
        public static string ProfileFor(BuildTarget target)
        {
            if (!File.Exists(ConfigurationPath)) throw new InvalidOperationException("Missing " + ConfigurationPath);
            var configuration = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(ConfigurationPath));
            if (configuration.SchemaVersion != 1) throw new InvalidOperationException("Unsupported surface profile configuration.");
            return configuration.BuildTargets.TryGetValue(target.ToString(), out string profile) ? profile : MuseumDeliveryAssets.AuthoringProfile;
        }

        /// <summary>Replaces every delivered museum material in the scene by its namesake of the profile.</summary>
        public static int Apply(Scene scene, string profile)
        {
            MuseumDeliveryManifest.Load().Profile(profile);
            int swapped = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        string path = materials[index] != null ? AssetDatabase.GetAssetPath(materials[index]) : "";
                        if (!path.StartsWith(MuseumDeliveryManifest.MaterialFolder + "/", StringComparison.Ordinal)) continue;
                        string wanted = MuseumDeliveryManifest.MaterialPath(profile, materials[index].name);
                        if (path == wanted) continue;
                        var replacement = AssetDatabase.LoadAssetAtPath<Material>(wanted);
                        if (replacement == null) throw new InvalidOperationException("The " + profile + " profile has no material " + materials[index].name + ".");
                        materials[index] = replacement;
                        changed = true;
                    }
                    if (!changed) continue;
                    renderer.sharedMaterials = materials;
                    swapped++;
                }
            return swapped;
        }

        [MenuItem("Musiyo/Museum Delivery/Preview Surface Profile/Web")]
        private static void PreviewWeb() => Preview("web");
        [MenuItem("Musiyo/Museum Delivery/Preview Surface Profile/Quest")]
        private static void PreviewQuest() => Preview("quest");
        [MenuItem("Musiyo/Museum Delivery/Preview Surface Profile/Windows")]
        private static void PreviewWindows() => Preview("windows");

        /// <summary>Shows a profile in the open scene. Restore the authoring profile before saving the scene.</summary>
        private static void Preview(string profile)
            => Debug.Log("Museum surface profile " + profile + " shown on " + Apply(SceneManager.GetActiveScene(), profile)
                + " renderers. Scenes are saved with the " + MuseumDeliveryAssets.AuthoringProfile + " profile.");
    }

    public sealed class MuseumSurfaceProfileBuildStep : IProcessSceneWithReport
    {
        public int callbackOrder => 10;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Entering Play mode in the Editor keeps the authoring profile.
            if (report == null) return;
            string profile = MuseumSurfaceProfiles.ProfileFor(report.summary.platform);
            int swapped = MuseumSurfaceProfiles.Apply(scene, profile);
            Debug.Log("Museum surface profile for " + report.summary.platform + ": " + profile + ", " + swapped + " renderers changed in " + scene.name + ".");
        }
    }
}
