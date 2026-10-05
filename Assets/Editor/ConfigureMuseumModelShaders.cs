using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Keep material variants for models that are published after compilation.</summary>
    public sealed class ConfigureMuseumModelShaders : IPreprocessBuildWithReport
    {
        public const string AssetPath = "Assets/_Musiyo/Resources/MuseumModelShaders.shadervariants";
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath) == null)
                throw new BuildFailedException("Configure museum model shaders before building.");
        }

        [MenuItem("Musiyo/Configure Remote Model Shaders")]
        public static void Configure()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(AssetPath);
            if (collection == null)
            {
                collection = new ShaderVariantCollection();
                AssetDatabase.CreateAsset(collection, AssetPath);
            }
            collection.Clear();
            foreach (string name in new[] { "glTF-pbrMetallicRoughness", "glTF-pbrSpecularGlossiness", "glTF-unlit" })
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.cloud.gltfast/Runtime/Shader/" + name + ".shadergraph");
                if (shader == null) throw new InvalidOperationException("Missing glTFast shader: " + name);
                var keywords = new[] { "_OCCLUSION", "_EMISSIVE", "_CLEARCOAT", "_ALPHATEST_ON", "_SURFACE_TYPE_TRANSPARENT", "_ALPHAPREMULTIPLY_ON" }
                    .Where(keyword => shader.keywordSpace.keywordNames.Contains(keyword)).ToArray();
                for (int mask = 0; mask < 1 << keywords.Length; mask++)
                {
                    var enabled = new List<string>();
                    for (int index = 0; index < keywords.Length; index++)
                        if ((mask & (1 << index)) != 0) enabled.Add(keywords[index]);
                    collection.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, enabled.ToArray()));
                }
            }
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssets();
            Debug.Log("Preserved " + collection.variantCount + " remote model shader variants.");
        }
    }
}
