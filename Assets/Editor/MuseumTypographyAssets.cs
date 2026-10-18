using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Builds the TextMesh Pro fonts of the shared visual language chosen for the Web on 18/10/2026
    /// (coordination/design-tokens-20261018.json): Bodoni Moda for titles and Instrument Sans for reading and controls,
    /// both under the SIL Open Font License (texts beside the files). They are dynamic, fall back to MuseumSans for any
    /// missing glyph, and live in Resources so the visit loads them at runtime.
    /// Batch: -executeMethod MusiyoBetsknate.Editor.MuseumTypographyAssets.Build.
    /// </summary>
    public static class MuseumTypographyAssets
    {
        private const string Folder = "Assets/_Musiyo/UI/Resources";

        [MenuItem("Musiyo/UI/Build Typography")]
        public static void Build()
        {
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Musiyo/UI/MuseumSans.asset");
            Create("Assets/_Musiyo/UI/Fonts/BodoniModa.ttf", MuseumTypography.DisplayFont, fallback);
            Create("Assets/_Musiyo/UI/Fonts/InstrumentSans.ttf", MuseumTypography.BodyFont, fallback);
            AssetDatabase.SaveAssets();
            Debug.Log("Typography built.");
        }

        private static void Create(string source, string name, TMP_FontAsset fallback)
        {
            string path = Folder + "/" + name + ".asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset != null) AssetDatabase.DeleteAsset(path);
            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            asset.name = name;
            asset.fallbackFontAssetTable = fallback != null ? new List<TMP_FontAsset> { fallback } : new List<TMP_FontAsset>();
            // Glyphs met at runtime are not kept in the asset, so builds leave it unchanged.
            asset.ClearFontAssetData(true);
            AssetDatabase.CreateAsset(asset, path);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var atlas in asset.atlasTextures)
            {
                atlas.name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            EditorUtility.SetDirty(asset);
        }
    }
}
