using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Builds the project assets of the imported museum delivery: texture settings, one material set per surface
    /// profile, the model import settings and the architecture prefab with its colliders and lighting roles.
    /// </summary>
    public static class MuseumDeliveryAssets
    {
        public const string PrefabRootName = "MuseumArchitecture";
        /// <summary>Profile whose materials the model and the saved scenes carry; builds swap it for their own.</summary>
        public const string AuthoringProfile = "web";
        public const string SmoothnessMarker = "museum:roughness-to-smoothness";
        private const string LitShader = "Universal Render Pipeline/Lit";

        [MenuItem("Musiyo/Museum Delivery/Rebuild Materials And Prefab")]
        public static void Build()
        {
            var manifest = MuseumDeliveryManifest.Load();
            ConfigureTextures(manifest);
            BuildMaterials(manifest);
            ConfigureModel(manifest);
            BuildPrefab(manifest);
            AssetDatabase.SaveAssets();
            Debug.Log("Museum delivery assets built: " + manifest.Profiles.Count + " material sets and " + MuseumDeliveryManifest.PrefabPath + ".");
        }

        /// <summary>
        /// Reimports the model so its mesh repairs apply, and rebuilds only the materials those repairs change.
        /// </summary>
        [MenuItem("Musiyo/Museum Delivery/Apply Mesh Repairs")]
        public static void ApplyMeshRepairs()
        {
            var manifest = MuseumDeliveryManifest.Load();
            var repairs = MuseumMeshRepairs.Load();
            AssetDatabase.ImportAsset(MuseumDeliveryManifest.ModelPath, ImportAssetOptions.ForceUpdate);
            foreach (var profile in manifest.Profiles)
            {
                var shader = Shader.Find(profile.Shader);
                foreach (var entry in profile.Materials.Where(entry => repairs.DrawsOneSide(entry.Name)))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(MuseumDeliveryManifest.MaterialPath(profile.Name, entry.Name));
                    Apply(material, shader, entry, profile.Name);
                    EditorUtility.SetDirty(material);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Museum mesh repairs applied to " + repairs.Cards.Objects.Count + " objects.");
        }

        /// <summary>Rebuilds only the materials the surface finish touches, in every profile.</summary>
        [MenuItem("Musiyo/Museum Delivery/Apply Surface Finish")]
        public static void ApplySurfaceFinish()
        {
            var manifest = MuseumDeliveryManifest.Load();
            var finish = MuseumSurfaceFinish.Load();
            if (MuseumSurfaceFinish.Texture() == null) MuseumSurfaceFinish.CreateTexture();
            int count = 0;
            foreach (var profile in manifest.Profiles)
            {
                var shader = Shader.Find(profile.Shader);
                foreach (var entry in profile.Materials.Where(entry => finish.Macro.TexturePeriods.ContainsKey(entry.Name)
                    || finish.WaterFor(profile.Name, entry.Name) != null))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(MuseumDeliveryManifest.MaterialPath(profile.Name, entry.Name));
                    Apply(material, shader, entry, profile.Name);
                    EditorUtility.SetDirty(material);
                    count++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Museum surface finish applied to " + count + " materials.");
        }

        public static string TexturePath(string file) => MuseumDeliveryManifest.TextureFolder + "/" + file;

        private static void ConfigureTextures(MuseumDeliveryManifest manifest)
        {
            foreach (var texture in manifest.Textures)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath(texture.File));
                if (importer == null) throw new InvalidOperationException("Missing texture: " + texture.File);
                importer.textureType = texture.Role == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = texture.Role == "albedo";
                // Unity reads smoothness from an alpha channel; the postprocessor turns the delivered roughness into it.
                importer.alphaSource = texture.Role == "roughness" ? TextureImporterAlphaSource.FromGrayScale : TextureImporterAlphaSource.None;
                importer.userData = texture.Role == "roughness" ? SmoothnessMarker : "";
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Math.Max(texture.Width, texture.Height);
                importer.textureCompression = TextureImporterCompression.Compressed;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = importer.maxTextureSize;
                android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
        }

        private static void BuildMaterials(MuseumDeliveryManifest manifest)
        {
            foreach (var profile in manifest.Profiles)
            {
                var shader = Shader.Find(profile.Shader);
                if (shader == null) throw new InvalidOperationException("Missing shader: " + profile.Shader);
                foreach (var entry in profile.Materials)
                {
                    string path = MuseumDeliveryManifest.MaterialPath(profile.Name, entry.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(shader) { name = entry.Name };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    Apply(material, shader, entry, profile.Name);
                    EditorUtility.SetDirty(material);
                }
            }
        }

        private static void Apply(Material material, Shader shader, MuseumDeliveryManifest.MaterialEntry entry, string profile)
        {
            // The lagoon has its own shader; see the water section of surface_finish.json.
            var surface = MuseumSurfaceFinish.Load();
            var water = surface.WaterFor(profile, entry.Name);
            if (water != null) { surface.ApplyWater(material, water); return; }
            // Start from the shader defaults so a rebuilt material keeps nothing from an earlier delivery.
            var defaults = new Material(shader);
            material.shader = shader;
            material.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);

            bool textured = entry.BaseColorTexture != null;
            // A connected colour texture replaces the colour value in the source; the leftover value must not tint it.
            material.SetColor("_BaseColor", textured ? Color.white : Linear(entry.BaseColor).gamma);
            if (textured) material.SetTexture("_BaseMap", Texture(entry.BaseColorTexture));
            // Pieces repaired with explicit back faces draw one side; see mesh_repairs.json.
            bool doubleSided = entry.DoubleSided && !MuseumMeshRepairs.Load().DrawsOneSide(entry.Name);
            material.SetFloat("_Cull", (float)(doubleSided ? CullMode.Off : CullMode.Back));
            material.doubleSidedGI = doubleSided;
            material.SetFloat("_Smoothness", 1 - entry.Roughness);
            if (shader.name == LitShader)
            {
                material.SetFloat("_Metallic", entry.Metallic);
                if (entry.RoughnessTexture != null)
                {
                    material.SetTexture("_MetallicGlossMap", Texture(entry.RoughnessTexture));
                    material.SetFloat("_Smoothness", 1);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (entry.NormalTexture != null)
                {
                    material.SetTexture("_BumpMap", Texture(entry.NormalTexture));
                    material.SetFloat("_BumpScale", entry.NormalStrength);
                    material.EnableKeyword("_NORMALMAP");
                }
                // Large, slow variation over the most repeated textures; see surface_finish.json.
                var finish = MuseumSurfaceFinish.Load();
                if (finish.Varies(profile, entry.Name))
                {
                    material.SetTexture("_DetailAlbedoMap", MuseumSurfaceFinish.Texture());
                    material.SetTextureScale("_DetailAlbedoMap", Vector2.one * finish.Tiling(entry.Name));
                    material.SetFloat("_DetailAlbedoMapScale", 1);
                }
            }
            // The simple shader keeps its default of no specular highlights, which suits the light profile.
            else if (entry.NormalTexture != null || entry.RoughnessTexture != null)
                throw new InvalidOperationException(entry.Name + " carries detail maps its profile shader does not use.");
            if (entry.EmissionColor != null && entry.EmissionStrength > 0)
            {
                material.SetColor("_EmissionColor", Linear(entry.EmissionColor) * entry.EmissionStrength);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            // Finish as the pipeline's own inspector does, so Unity finds nothing to rewrite in the saved material.
            if (shader.name == LitShader) BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, SetDetailKeywords);
            else BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
        }

        /// <summary>The rule the Lit inspector applies to its detail maps (its helper is internal to the pipeline).</summary>
        private static void SetDetailKeywords(Material material)
        {
            if (!material.HasProperty("_DetailAlbedoMap") || !material.HasProperty("_DetailNormalMap")
                || !material.HasProperty("_DetailAlbedoMapScale")) return;
            bool scaled = material.GetFloat("_DetailAlbedoMapScale") != 1f;
            bool detail = material.GetTexture("_DetailAlbedoMap") || material.GetTexture("_DetailNormalMap");
            if (!scaled && detail) material.EnableKeyword("_DETAIL_MULX2"); else material.DisableKeyword("_DETAIL_MULX2");
            if (scaled && detail) material.EnableKeyword("_DETAIL_SCALED"); else material.DisableKeyword("_DETAIL_SCALED");
        }

        private static Color Linear(float[] value) => new Color(value[0], value[1], value[2], 1);

        private static Texture2D Texture(string file)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(file));
            if (texture == null) throw new InvalidOperationException("Missing texture: " + file);
            return texture;
        }

        private static void ConfigureModel(MuseumDeliveryManifest manifest)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(MuseumDeliveryManifest.ModelPath);
            if (importer == null) throw new InvalidOperationException("Missing model: " + MuseumDeliveryManifest.ModelPath);
            // Same axes and units as the earlier exports of this museum, so both share one frame in the scene.
            importer.bakeAxisConversion = false;
            importer.useFileScale = true;
            importer.globalScale = 1;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            // The second UV channel is authored for lightmaps: keep it as delivered.
            importer.generateSecondaryUV = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.swapUVChannels = false;
            // The route guidance builds its navigation mesh from the collider meshes in the player.
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var entry in manifest.Profile(AuthoringProfile).Materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Name),
                    AssetDatabase.LoadAssetAtPath<Material>(MuseumDeliveryManifest.MaterialPath(AuthoringProfile, entry.Name)));
            importer.SaveAndReimport();
        }

        private static void BuildPrefab(MuseumDeliveryManifest manifest)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MuseumDeliveryManifest.ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = PrefabRootName;
                var entries = manifest.Objects.ToDictionary(entry => entry.Name);
                var found = new HashSet<string>();
                foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (transform == instance.transform) continue;
                    if (!entries.TryGetValue(transform.name, out var entry))
                        throw new InvalidOperationException("The model has an object the delivery map does not describe: " + transform.name);
                    found.Add(entry.Name);
                    var filter = transform.GetComponent<MeshFilter>();
                    if (entry.Mesh != (filter != null))
                        throw new InvalidOperationException("The delivery map and the model disagree about " + entry.Name + ".");
                    if (filter == null) continue;
                    var renderer = transform.GetComponent<MeshRenderer>();
                    GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.ContributeGI
                        | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                        | StaticEditorFlags.ReflectionProbeStatic);
                    renderer.receiveGI = entry.Gi == "lightmap" ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                    renderer.scaleInLightmap = entry.Gi == "lightmap" ? entry.ScaleInLightmap : 1;
                    if (entry.Collision == "walkable" || entry.Collision == "blocking") AddCollider(filter);
                }
                var missing = entries.Keys.Where(name => !found.Contains(name)).ToList();
                if (missing.Count > 0)
                    throw new InvalidOperationException("The model lacks objects the delivery map lists: " + string.Join(", ", missing));
                Directory.CreateDirectory(Path.GetDirectoryName(MuseumDeliveryManifest.PrefabPath));
                PrefabUtility.SaveAsPrefabAsset(instance, MuseumDeliveryManifest.PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        /// <summary>
        /// A box where the mesh is one, so simple floors and walls stay cheap; the exact mesh otherwise. A delivered box whose
        /// covered faces the import trims (mesh_repairs.json) keeps its box: the walking surface does not change.
        /// </summary>
        private static void AddCollider(MeshFilter filter)
        {
            var mesh = filter.sharedMesh;
            if (IsBox(mesh, MuseumMeshRepairs.Load().Trimmed(filter.name)))
            {
                var box = filter.gameObject.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
            else filter.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>
        /// Whether every vertex sits on a corner of the mesh bounds, which only an axis-aligned box does; with
        /// <paramref name="trimmed"/>, the corners are all there and the other vertices lie on the faces of the box.
        /// </summary>
        public static bool IsBox(Mesh mesh, bool trimmed = false)
        {
            var bounds = mesh.bounds;
            float tolerance = bounds.size.magnitude * 1e-4f;
            if (bounds.size.x <= tolerance || bounds.size.y <= tolerance || bounds.size.z <= tolerance) return false;
            var corners = new HashSet<int>();
            foreach (var vertex in mesh.vertices)
            {
                int corner = 0, extremes = 0;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Mathf.Abs(vertex[axis] - bounds.max[axis]) <= tolerance) { corner |= 1 << axis; extremes++; }
                    else if (Mathf.Abs(vertex[axis] - bounds.min[axis]) <= tolerance) extremes++;
                }
                if (extremes == 3) corners.Add(corner);
                else if (!trimmed || extremes == 0) return false;
            }
            return corners.Count == 8;
        }
    }

    /// <summary>Turns a delivered roughness map into the smoothness alpha channel the lit shader reads.</summary>
    public sealed class MuseumSmoothnessMapPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private void OnPostprocessTexture(Texture2D texture)
        {
            if (assetImporter.userData != MuseumDeliveryAssets.SmoothnessMarker) return;
            var pixels = texture.GetPixels32(0);
            for (int index = 0; index < pixels.Length; index++)
                // Red stays empty because the same texture supplies the metallic value there.
                pixels[index] = new Color32(0, 0, 0, (byte)(255 - pixels[index].a));
            texture.SetPixels32(pixels, 0);
            texture.Apply(true);
        }
    }
}
