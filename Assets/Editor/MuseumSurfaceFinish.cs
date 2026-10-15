using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Finishing touches the importer adds to the delivered materials, read from <c>Delivery/surface_finish.json</c>.
    /// Macro variation lays a slow, large light-and-dark drift over the textures that repeat most, so their tiling stops
    /// reading as a grid. It uses the Lit detail map, so only the profiles whose shader has one receive it.
    /// </summary>
    public sealed class MuseumSurfaceFinish
    {
        public const string AssetPath = MuseumDeliveryManifest.Root + "/Delivery/surface_finish.json";
        public const string TexturePath = MuseumDeliveryManifest.Root + "/Finish/macro_variation.png";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("macro_variation")] public MacroVariation Macro;
        [JsonProperty("water")] public WaterSurface Water;

        /// <summary>The lagoon's own shader and its values per profile; the mesh and colliders stay as delivered.</summary>
        public sealed class WaterSurface
        {
            [JsonProperty("material")] public string Material;
            [JsonProperty("shader")] public string Shader;
            [JsonProperty("profiles")] public Dictionary<string, WaterProfile> Profiles = new Dictionary<string, WaterProfile>();
        }

        public sealed class WaterProfile
        {
            [JsonProperty("deep_color")] public string DeepColor;
            [JsonProperty("facing_color")] public string FacingColor;
            [JsonProperty("reflection")] public float Reflection;
            [JsonProperty("wave_length_m")] public float WaveLength;
            [JsonProperty("wave_slope")] public float WaveSlope;
            [JsonProperty("wave_speed")] public float WaveSpeed;
            [JsonProperty("glint")] public float Glint;
        }

        public sealed class MacroVariation
        {
            [JsonProperty("profiles")] public List<string> Profiles = new List<string>();
            /// <summary>Size of one drift pattern in metres.</summary>
            [JsonProperty("period_m")] public float Period;
            /// <summary>Metres one repeat of each material's own texture covers, measured by the surface audit.</summary>
            [JsonProperty("texture_period_m")] public Dictionary<string, float> TexturePeriods = new Dictionary<string, float>();
            /// <summary>Larger drift for the widest surfaces, such as the valley around the museum.</summary>
            [JsonProperty("period_overrides_m")] public Dictionary<string, float> PeriodOverrides = new Dictionary<string, float>();
        }

        public static MuseumSurfaceFinish Load()
        {
            var finish = JsonConvert.DeserializeObject<MuseumSurfaceFinish>(File.ReadAllText(AssetPath));
            if (finish?.SchemaVersion != 1 || finish.Macro == null || !(finish.Macro.Period > 0))
                throw new InvalidOperationException("Invalid museum surface finish: " + AssetPath);
            foreach (var period in finish.Macro.TexturePeriods.Values)
                if (!(period > 0)) throw new InvalidOperationException("Invalid texture period in " + AssetPath);
            if (finish.Water != null)
            {
                if (string.IsNullOrEmpty(finish.Water.Material) || string.IsNullOrEmpty(finish.Water.Shader))
                    throw new InvalidOperationException("Invalid water surface in " + AssetPath);
                foreach (var water in finish.Water.Profiles.Values)
                    if (water == null || !(water.WaveLength > 0) || water.Reflection < 0 || water.Reflection > 1 || water.WaveSlope < 0
                        || water.WaveSpeed < 0 || water.Glint < 0 || !ColorUtility.TryParseHtmlString(water.DeepColor, out _)
                        || !ColorUtility.TryParseHtmlString(water.FacingColor, out _))
                        throw new InvalidOperationException("Invalid water profile in " + AssetPath);
            }
            return finish;
        }

        /// <summary>Water values for a profile's material, or null when the material is not the lagoon's.</summary>
        public WaterProfile WaterFor(string profile, string material)
            => Water != null && Water.Material == material && Water.Profiles.TryGetValue(profile, out var water) ? water : null;

        /// <summary>Gives a delivered material the lagoon shader with the profile's values.</summary>
        public void ApplyWater(Material material, WaterProfile water)
        {
            var shader = UnityEngine.Shader.Find(Water.Shader);
            if (shader == null) throw new InvalidOperationException("Missing shader: " + Water.Shader);
            var defaults = new Material(shader);
            material.shader = shader;
            material.CopyPropertiesFromMaterial(defaults);
            UnityEngine.Object.DestroyImmediate(defaults);
            material.shaderKeywords = new string[0];
            ColorUtility.TryParseHtmlString(water.DeepColor, out var deep);
            ColorUtility.TryParseHtmlString(water.FacingColor, out var facing);
            material.SetColor("_DeepColor", deep);
            material.SetColor("_ShallowColor", facing);
            material.SetTexture("_SkyTex", AssetDatabase.LoadAssetAtPath<Texture2D>(MuseumAtmosphereTools.PanoramaPath));
            material.SetFloat("_Reflection", water.Reflection);
            material.SetFloat("_WaveLength", water.WaveLength);
            material.SetFloat("_WaveSlope", water.WaveSlope);
            material.SetFloat("_WaveSpeed", water.WaveSpeed);
            material.SetFloat("_Glint", water.Glint);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        public bool Varies(string profile, string material)
            => Macro.Profiles.Contains(profile) && Macro.TexturePeriods.ContainsKey(material);

        /// <summary>Detail tiling over the material's own UVs that gives one drift pattern per period.</summary>
        public float Tiling(string material) => Macro.TexturePeriods[material]
            / (Macro.PeriodOverrides.TryGetValue(material, out var period) && period > 0 ? period : Macro.Period);

        public static Texture2D Texture() => AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

        /// <summary>
        /// Writes the drift texture: tileable value noise in four octaves around mid grey, which the detail map reads as
        /// no change, within about fourteen percent either way. Drawn here, deterministic, and owned by the project.
        /// </summary>
        [MenuItem("Musiyo/Museum Delivery/Create Macro Variation Texture")]
        public static void CreateTexture()
        {
            const int size = 256;
            var random = new System.Random(20261015);
            var lattices = new List<float[,]>();
            int[] cells = { 4, 8, 16, 32 };
            foreach (int count in cells)
            {
                var lattice = new float[count, count];
                for (int y = 0; y < count; y++)
                    for (int x = 0; x < count; x++) lattice[x, y] = (float)random.NextDouble();
                lattices.Add(lattice);
            }
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            float[] weights = { .55f, .25f, .13f, .07f };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float value = 0;
                    for (int octave = 0; octave < cells.Length; octave++)
                        value += weights[octave] * Sample(lattices[octave], cells[octave], x / (float)size, y / (float)size);
                    float level = .5f + (value - .5f) * .28f;
                    image.SetPixel(x, y, new Color(level, level, level, 1));
                }
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            // The Lit detail map reads linear values: mid grey must stay 0.5.
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        private static float Sample(float[,] lattice, int count, float u, float v)
        {
            float x = u * count, y = v * count;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);
            float At(int i, int j) => lattice[((i % count) + count) % count, ((j % count) + count) % count];
            return Mathf.Lerp(Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), fx), Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), fx), fy);
        }

        private static float Smooth(float t) => t * t * (3 - 2 * t);
    }
}
