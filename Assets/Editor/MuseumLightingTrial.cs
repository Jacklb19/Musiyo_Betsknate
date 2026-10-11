using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Bakes two representative rooms of the delivered museum in a throwaway scene and measures the result, so the
    /// lightmap resolution and the per-object decisions can be made before a bake of the whole museum.
    /// Nothing it bakes stays in the project.
    /// </summary>
    public static class MuseumLightingTrial
    {
        private const string Folder = "Assets/_MuseumLightingTrial";
        private const float TexelsPerMetre = 15;
        private const int Padding = 2;
        /// <summary>A round room open to the sky through its roof and a closed room lit from inside.</summary>
        private static readonly string[] Rooms = { "Sala_Bienvenida", "Sala_Fogon" };

        [MenuItem("Musiyo/Museum Delivery/Run Lighting Trial")]
        public static void Run()
        {
            var manifest = MuseumDeliveryManifest.Load();
            var entries = manifest.Objects.ToDictionary(entry => entry.Name);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MuseumDeliveryManifest.PrefabPath);
            var architecture = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            architecture.transform.rotation = Quaternion.Euler(0, MuseumDeliveryValidation.SceneYaw, 0);
            foreach (var filter in architecture.GetComponentsInChildren<MeshFilter>(true))
            {
                var entry = entries[filter.name];
                // The ground stays so no light reaches the rooms from below.
                if (!Rooms.Contains(entry.Room) && entry.Role != "provisional_ground") filter.gameObject.SetActive(false);
            }

            // The three lights of the delivery master, which its exports do not carry. Directions and positions are
            // the master's; intensities are a first translation of its units and still have to be judged by eye.
            var sun = AddLight("Sun", LightType.Directional, new Color(1, .96f, .9f), 1.2f);
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(.439f, -.643f, .628f));
            sun.shadows = LightShadows.Soft;
            sun.shadowAngle = 25;
            var oculus = AddLight("OculusLight", LightType.Spot, new Color(1, .97f, .9f), 30);
            oculus.transform.SetPositionAndRotation(new Vector3(0, 7.5f, -20), Quaternion.LookRotation(Vector3.down));
            oculus.spotAngle = 35;
            oculus.range = 12;
            var fire = AddLight("FireLight", LightType.Point, new Color(1, .55f, .25f), 3);
            fire.transform.position = new Vector3(1, .8f, 20.3f);
            fire.range = 6;

            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            string scenePath = Folder + "/LightingTrial.unity";
            var settings = new LightingSettings
            {
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                bakedGI = true, realtimeGI = false,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
                lightmapResolution = TexelsPerMetre, lightmapPadding = Padding, lightmapMaxSize = 1024,
                lightmapCompression = LightmapCompression.None, directionalityMode = LightmapsMode.NonDirectional,
                directSampleCount = 16, indirectSampleCount = 128, environmentSampleCount = 64, maxBounces = 2,
                ao = false,
            };
            AssetDatabase.CreateAsset(settings, Folder + "/LightingTrial.lighting");
            Lightmapping.lightingSettings = settings;
            EditorSceneManager.SaveScene(scene, scenePath);

            var report = new JObject();
            try
            {
                var watch = Stopwatch.StartNew();
                bool baked = Lightmapping.Bake();
                watch.Stop();
                var lightmaps = LightmapSettings.lightmaps;
                var sizes = new JArray();
                Directory.CreateDirectory(MuseumDeliveryValidation.OutputFolder);
                for (int index = 0; index < lightmaps.Length; index++)
                {
                    var texture = lightmaps[index].lightmapColor;
                    sizes.Add(texture.width + "x" + texture.height);
                    SavePreview(texture, Path.Combine(MuseumDeliveryValidation.OutputFolder, "lighting-trial-lightmap-" + index + ".png"));
                }
                var tight = new JArray();
                int lightmapped = 0;
                foreach (var renderer in architecture.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (renderer.lightmapIndex < 0 || renderer.lightmapIndex >= lightmaps.Length) continue;
                    lightmapped++;
                    var atlas = lightmaps[renderer.lightmapIndex].lightmapColor;
                    int pixels = Mathf.RoundToInt(Mathf.Min(renderer.lightmapScaleOffset.x * atlas.width, renderer.lightmapScaleOffset.y * atlas.height));
                    int clean = entries[renderer.name].LightmapCleanPx;
                    // Fewer pixels than the delivery measured as clean means neighbouring islands can bleed into each other.
                    if (clean > 0 && pixels < clean)
                        tight.Add(new JObject { ["object"] = renderer.name, ["baked_px"] = pixels, ["clean_px"] = clean });
                }
                report["baked"] = baked;
                report["rooms"] = new JArray(Rooms);
                report["lightmapper"] = settings.lightmapper.ToString();
                report["texels_per_metre"] = TexelsPerMetre;
                report["padding_texels"] = Padding;
                report["seconds"] = Math.Round(watch.Elapsed.TotalSeconds, 1);
                report["lightmaps"] = sizes;
                report["lightmapped_objects"] = lightmapped;
                report["objects_below_clean_resolution"] = tight;
                report["light_probes"] = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
                MuseumDeliveryCaptures.Capture(Path.Combine(MuseumDeliveryValidation.OutputFolder, "lighting-trial"), "bienvenida_inside", "fogon_inside");
                File.WriteAllText(Path.Combine(MuseumDeliveryValidation.OutputFolder, "lighting-trial.json"), report.ToString());
                if (!baked) throw new InvalidOperationException("The lighting trial did not bake.");
                Debug.Log("Museum lighting trial baked " + lightmapped + " objects into " + lightmaps.Length + " lightmaps in "
                    + report["seconds"] + " s; " + tight.Count + " are below their clean resolution.");
            }
            finally
            {
                Lightmapping.Clear();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        private static Light AddLight(string name, LightType type, Color colour, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = type;
            light.color = colour;
            light.intensity = intensity;
            light.lightmapBakeType = LightmapBakeType.Baked;
            return light;
        }

        private static void SavePreview(Texture2D lightmap, string path)
        {
            var target = RenderTexture.GetTemporary(lightmap.width, lightmap.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(lightmap, target);
            RenderTexture.active = target;
            var copy = new Texture2D(lightmap.width, lightmap.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, lightmap.width, lightmap.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, copy.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
