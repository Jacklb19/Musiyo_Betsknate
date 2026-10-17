using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Builds the textures and particle materials of the museum's fire from noise, so nothing is downloaded or licensed:
    /// a 4 x 4 flipbook of flame tongues, a soft smoke puff and a round spark. Materials go to Resources so every build
    /// keeps their shader variants. Batch: -executeMethod MusiyoBetsknate.Editor.MuseumFireAssets.Build.
    /// </summary>
    public static class MuseumFireAssets
    {
        public const string TextureFolder = "Assets/_Musiyo/Arte/Fire";
        public const string MaterialFolder = "Assets/_Musiyo/Resources";
        private const string GlowMaterial = MaterialFolder + "/EntryBackdropGlow.mat";

        [MenuItem("Musiyo/Fire/Build Fire Assets")]
        public static void Build()
        {
            Directory.CreateDirectory(TextureFolder);
            var flames = Save("fire_flames_4x4", FlameSheet(256, 4), true);
            var smoke = Save("fire_smoke", Puff(128, 7), false);
            var spark = Save("fire_spark", Spark(32), false);
            Material("EntryFireFlame", flames, additive: true);
            Material("EntryFireSmoke", smoke, additive: false);
            Material("EntryFireSpark", spark, additive: true);
            AssetDatabase.SaveAssets();
            Debug.Log("Fire assets built.");
        }

        private static Texture2D Save(string name, Texture2D image, bool mip)
        {
            string path = TextureFolder + "/" + name + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = mip;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A copy of the glow material, so it shares the shader the builds already carry, with its own map and blend.</summary>
        private static void Material(string name, Texture2D map, bool additive)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterial);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.CopyPropertiesFromMaterial(source);
            material.name = name;
            material.SetTexture("_BaseMap", map);
            material.SetColor("_BaseColor", Color.white);
            // URP particles: 0 alpha blend, 2 additive.
            material.SetFloat("_Blend", additive ? 2 : 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + (additive ? 10 : 0);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
        }

        /// <summary>Soft threshold: 0 below the first edge, 1 above the second, smooth between (as smoothstep in shaders).</summary>
        private static float Edge(float from, float to, float value)
        {
            float t = Mathf.Clamp01((value - from) / Mathf.Max(.0001f, to - from));
            return t * t * (3 - 2 * t);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Smooth value noise that wraps every <paramref name="period"/> cells horizontally.</summary>
        private static float Noise(float x, float y, int seed, int period = 1 << 16)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int x0 = ((ix % period) + period) % period, x1 = (x0 + 1) % period;
            float a = Hash(x0, iy, seed), b = Hash(x1, iy, seed), c = Hash(x0, iy + 1, seed), d = Hash(x1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float x, float y, int seed)
        {
            float sum = 0, amplitude = .5f, frequency = 1;
            for (int octave = 0; octave < 4; octave++)
            {
                sum += amplitude * Noise(x * frequency, y * frequency, seed + octave * 31);
                amplitude *= .5f; frequency *= 2.1f;
            }
            return sum;
        }

        /// <summary>
        /// Flame tongues: a tapering body whose centre and edges are torn by rising noise. White with alpha; the particle
        /// colour over its life paints it from yellow to deep orange.
        /// </summary>
        private static Texture2D FlameSheet(int cell, int tiles)
        {
            int size = cell * tiles;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int frame = 0; frame < tiles * tiles; frame++)
            {
                int column = frame % tiles, row = tiles - 1 - frame / tiles;
                float time = frame / (float)(tiles * tiles);
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = x / (cell - 1f), v = y / (cell - 1f);
                    // Rising, curling air bends the flame: the higher, the more it is pushed about.
                    float warpX = (Fbm(u * 3 + 5, v * 3 - time * 6, 21) - .5f) * .32f * (.15f + v);
                    float warpY = (Fbm(u * 3 + 9, v * 3 - time * 6, 37) - .5f) * .12f;
                    float wu = u + warpX, wv = Mathf.Clamp01(v + warpY);
                    // A teardrop: a rounded base that swells, then draws to a long point.
                    float radius = wv < .16f ? .27f * Mathf.Sqrt(Mathf.Max(0, wv / .16f)) : .27f * Mathf.Pow(1 - (wv - .16f) / .84f, 1.35f);
                    float across = Mathf.Abs(wu - .5f) / Mathf.Max(.001f, radius);
                    float body = Mathf.SmoothStep(1, 0, across);
                    // Licks break off higher up: the flame thins into tongues and wisps toward its tip.
                    float licks = Fbm(u * 6 + 3, v * 7 - time * 10, 53);
                    float keep = Edge(v * .75f - .12f, v * .75f + .22f, licks);
                    float alpha = Mathf.Clamp01(body * keep * 1.35f);
                    // The base fades in so no straight edge shows at the bottom.
                    alpha *= Edge(0, .1f, v);
                    float core = Mathf.Clamp01(1 - across * 1.8f) * Mathf.Clamp01(1 - v * 1.8f);
                    byte shade = (byte)(Mathf.Lerp(.78f, 1, core) * 255);
                    pixels[(row * cell + y) * size + column * cell + x] = new Color32(shade, shade, shade, (byte)(alpha * 255));
                }
            }
            image.SetPixels32(pixels);
            image.Apply();
            return image;
        }

        /// <summary>A soft, ragged puff of smoke.</summary>
        private static Texture2D Puff(int size, int seed)
        {
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (size - 1f) - .5f, v = y / (size - 1f) - .5f;
                float radius = Mathf.Sqrt(u * u + v * v) * 2;
                float cloud = Fbm(u * 4 + 3, v * 4 + 3, seed);
                float alpha = Mathf.Clamp01((1 - radius) * 1.4f) * Mathf.Clamp01(cloud * 1.8f - .25f);
                image.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0, 1, alpha)));
            }
            image.Apply();
            return image;
        }

        private static Texture2D Spark(int size)
        {
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (size - 1f) - .5f, v = y / (size - 1f) - .5f;
                float radius = Mathf.Sqrt(u * u + v * v) * 2;
                image.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2.2f)));
            }
            image.Apply();
            return image;
        }
    }
}
