using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// What was taken from an external museum delivery: files with their hashes, surface profiles, object roles and anchors.
    /// It holds no machine paths; sources are relative to the delivery folder.
    /// </summary>
    public sealed class MuseumDeliveryManifest
    {
        public const string Root = "Assets/_Musiyo/Museum";
        public const string AssetPath = Root + "/Delivery/museum_delivery.json";
        public const string ModelPath = Root + "/Models/Museum_v3.fbx";
        public const string TextureFolder = Root + "/Textures";
        public const string MaterialFolder = Root + "/Materials";
        public const string PrefabPath = Root + "/Prefabs/MuseumArchitecture.prefab";

        [JsonProperty("schema_version")] public int SchemaVersion = 1;
        [JsonProperty("package")] public string Package;
        [JsonProperty("variant")] public string Variant;
        [JsonProperty("provenance_note")] public string ProvenanceNote;
        [JsonProperty("model")] public ModelEntry Model;
        [JsonProperty("textures")] public List<TextureEntry> Textures = new List<TextureEntry>();
        [JsonProperty("profiles")] public List<ProfileEntry> Profiles = new List<ProfileEntry>();
        [JsonProperty("objects")] public List<ObjectEntry> Objects = new List<ObjectEntry>();
        [JsonProperty("anchors")] public List<AnchorEntry> Anchors = new List<AnchorEntry>();

        public static MuseumDeliveryManifest Load()
        {
            if (!File.Exists(AssetPath))
                throw new InvalidOperationException("No museum delivery was imported: " + AssetPath);
            return JsonConvert.DeserializeObject<MuseumDeliveryManifest>(File.ReadAllText(AssetPath));
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            File.WriteAllText(AssetPath, JsonConvert.SerializeObject(this, Formatting.Indented) + "\n");
        }

        public ProfileEntry Profile(string name)
        {
            var profile = Profiles.Find(candidate => candidate.Name == name);
            if (profile == null) throw new InvalidOperationException("The delivery has no surface profile named " + name + ".");
            return profile;
        }

        public static string MaterialPath(string profile, string material)
            => MaterialFolder + "/" + char.ToUpperInvariant(profile[0]) + profile.Substring(1) + "/" + material + ".mat";

        public sealed class SourceEntry
        {
            [JsonProperty("profile")] public string Profile;
            [JsonProperty("source")] public string Source;
            [JsonProperty("sha256")] public string Sha256;
            [JsonProperty("bytes")] public long Bytes;
        }

        public sealed class ModelEntry
        {
            [JsonProperty("source")] public string Source;
            [JsonProperty("sha256")] public string Sha256;
            [JsonProperty("bytes")] public long Bytes;
            [JsonProperty("objects")] public int Objects;
            [JsonProperty("meshes")] public int Meshes;
            [JsonProperty("triangles")] public int Triangles;
            /// <summary>Models of the other profiles: same geometry by the delivery's own check, not imported.</summary>
            [JsonProperty("equivalent_sources")] public List<SourceEntry> EquivalentSources = new List<SourceEntry>();
        }

        public sealed class TextureEntry
        {
            /// <summary>File name inside the texture folder.</summary>
            [JsonProperty("file")] public string File;
            [JsonProperty("sha256")] public string Sha256;
            [JsonProperty("bytes")] public long Bytes;
            /// <summary>albedo, normal or roughness.</summary>
            [JsonProperty("role")] public string Role;
            [JsonProperty("width")] public int Width;
            [JsonProperty("height")] public int Height;
            [JsonProperty("provenance")] public string Provenance;
            [JsonProperty("sources")] public List<string> Sources = new List<string>();
        }

        public sealed class ProfileEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("shader")] public string Shader;
            [JsonProperty("guide_source")] public string GuideSource;
            [JsonProperty("guide_sha256")] public string GuideSha256;
            [JsonProperty("materials")] public List<MaterialEntry> Materials = new List<MaterialEntry>();
        }

        public sealed class MaterialEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("double_sided")] public bool DoubleSided;
            /// <summary>Linear colour; ignored by the source when a colour texture is connected.</summary>
            [JsonProperty("base_color")] public float[] BaseColor;
            [JsonProperty("metallic")] public float Metallic;
            [JsonProperty("roughness")] public float Roughness;
            [JsonProperty("base_color_texture")] public string BaseColorTexture;
            [JsonProperty("normal_texture")] public string NormalTexture;
            [JsonProperty("normal_strength")] public float NormalStrength = 1;
            [JsonProperty("roughness_texture")] public string RoughnessTexture;
            [JsonProperty("emission_color")] public float[] EmissionColor;
            [JsonProperty("emission_strength")] public float EmissionStrength;
        }

        public sealed class ObjectEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("mesh")] public bool Mesh;
            [JsonProperty("room")] public string Room;
            [JsonProperty("role")] public string Role;
            /// <summary>walkable, blocking, optional or none.</summary>
            [JsonProperty("collision")] public string Collision;
            [JsonProperty("triangles")] public int Triangles;
            [JsonProperty("uv_channels")] public int UvChannels;
            /// <summary>lightmap, probes or empty.</summary>
            [JsonProperty("gi")] public string Gi;
            [JsonProperty("scale_in_lightmap")] public float ScaleInLightmap = 1;
            /// <summary>Smallest square lightmap, in pixels, that leaves two free texels between the UV islands; 0 when unknown.</summary>
            [JsonProperty("lightmap_clean_px")] public int LightmapCleanPx;
            /// <summary>World size in the scene frame, in metres.</summary>
            [JsonProperty("size")] public float[] Size;
            /// <summary>Height of the lowest point above the museum floor level.</summary>
            [JsonProperty("lowest")] public float Lowest;
        }

        public sealed class AnchorEntry
        {
            [JsonProperty("object")] public string Object;
            /// <summary>point_of_interest, spawn, lectern or root.</summary>
            [JsonProperty("kind")] public string Kind;
            /// <summary>Stable key of the scene anchor this source point stands for.</summary>
            [JsonProperty("key")] public string Key;
            /// <summary>Position in the scene frame of the technical plan.</summary>
            [JsonProperty("position")] public float[] Position;
            [JsonProperty("activation_diameter")] public float ActivationDiameter;
        }
    }
}
