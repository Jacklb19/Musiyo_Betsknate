using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The browser build. Uncompressed by default, which any local static server serves; with the environment
    /// variable MUSIYO_WEB_COMPRESSION set to "brotli" or "gzip" the player files are compressed and the loader's
    /// decompression fallback is switched on, so a host that sends no Content-Encoding (a public bucket behind a
    /// static page) still works: the loader decompresses in the browser. The manifest then names the compressed files.
    /// </summary>
    public static class BuildMuseumWeb
    {
        public const string CompressionVariable = "MUSIYO_WEB_COMPRESSION";

        /// <summary>The compression asked for by the environment: brotli, gzip, or none.</summary>
        public static WebGLCompressionFormat RequestedCompression()
        {
            switch ((Environment.GetEnvironmentVariable(CompressionVariable) ?? "").Trim().ToLowerInvariant())
            {
                case "brotli": case "br": return WebGLCompressionFormat.Brotli;
                case "gzip": case "gz": return WebGLCompressionFormat.Gzip;
                case "": case "none": case "disabled": return WebGLCompressionFormat.Disabled;
                default: throw new InvalidOperationException("Unknown " + CompressionVariable + "; use brotli, gzip or none.");
            }
        }

        [MenuItem("Musiyo/Build Museum Web")]
        public static void Build()
        {
            const string output = "Build/MuseumWeb";
            var compression = PlayerSettings.WebGL.compressionFormat;
            var fallback = PlayerSettings.WebGL.decompressionFallback;
            var requested = RequestedCompression();
            try
            {
                PlayerSettings.WebGL.compressionFormat = requested;
                // A compressed player is only safe to publish when the loader can decompress it itself.
                PlayerSettings.WebGL.decompressionFallback = requested != WebGLCompressionFormat.Disabled;
                Debug.Log("Musiyo Web build: compression " + requested + ", decompression fallback "
                    + PlayerSettings.WebGL.decompressionFallback + ".");
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
            finally
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.decompressionFallback = fallback;
            }
        }

        /// <summary>The one player file of a kind, compressed (.br or .gz) or not.</summary>
        private static string Find(string output, string pattern)
        {
            var files = new System.Collections.Generic.List<string>(Directory.GetFiles(Path.Combine(output, "Build"), pattern));
            files.AddRange(Directory.GetFiles(Path.Combine(output, "Build"), pattern + ".br"));
            files.AddRange(Directory.GetFiles(Path.Combine(output, "Build"), pattern + ".gz"));
            if (files.Count != 1) throw new InvalidOperationException("Expected one build file: " + pattern);
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
