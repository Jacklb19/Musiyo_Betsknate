using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public static class BuildMuseumWeb
    {
        [MenuItem("Musiyo/Build Museum Web")]
        public static void Build()
        {
            const string output = "Build/MuseumWeb";
            var compression = PlayerSettings.WebGL.compressionFormat;
            try
            {
                // Uncompressed files work with ordinary local static servers.
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BuildMuseumBlockout.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Museum Web build failed: " + report.summary.result);
                File.WriteAllText(Path.Combine(output, "unity-build.json"), JsonUtility.ToJson(new Manifest
                {
                    loaderUrl = Find(output, "*.loader.js"),
                    dataUrl = Find(output, "*.data"),
                    frameworkUrl = Find(output, "*.framework.js"),
                    codeUrl = Find(output, "*.wasm"),
                    productName = "Musiyo Bëtsknaté",
                    productVersion = Application.version
                }, true));
                Debug.Log("Museum Web build and same-document loader manifest saved to " + output);
            }
            finally { PlayerSettings.WebGL.compressionFormat = compression; }
        }

        private static string Find(string output, string pattern)
        {
            var files = Directory.GetFiles(Path.Combine(output, "Build"), pattern);
            if (files.Length != 1) throw new InvalidOperationException("Expected one build file: " + pattern);
            return "Build/" + Path.GetFileName(files[0]);
        }

        [Serializable]
        private sealed class Manifest
        {
            public int version = 1;
            public string loaderUrl;
            public string dataUrl;
            public string frameworkUrl;
            public string codeUrl;
            public string streamingAssetsUrl = "StreamingAssets";
            public string companyName = "Musiyo";
            public string productName;
            public string productVersion;
        }
    }
}
