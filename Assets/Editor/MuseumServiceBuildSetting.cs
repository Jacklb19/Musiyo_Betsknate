using System;
using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Bakes the deployed API address into a headset or desktop build: -musiyoApiBase &lt;url&gt; on the command line, or the
    /// MUSIYO_API_BASE environment variable, is written to Resources/MuseumService.json for the build and the file is put
    /// back afterwards, so the repository keeps an empty address. Without either, the build keeps the file as it is.
    /// The browser build never reads it: it uses its page's origin.
    /// </summary>
    public sealed class MuseumServiceBuildSetting : IDisposable
    {
        public const string Argument = "-musiyoApiBase";
        public const string Variable = "MUSIYO_API_BASE";
        public const string ResourcePath = "Assets/_Musiyo/Resources/MuseumService.json";
        private readonly string original;

        /// <summary>Address baked into the build in progress, or empty when the build keeps the repository's setting.</summary>
        public string Address { get; }

        private MuseumServiceBuildSetting(string address)
        {
            Address = address ?? "";
            if (string.IsNullOrEmpty(address)) return;
            original = File.ReadAllText(ResourcePath);
            Write(address);
        }

        /// <summary>Reads the requested address, if any, and writes it for the build until disposed.</summary>
        public static MuseumServiceBuildSetting Apply()
        {
            string requested = null;
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, Argument);
            if (index >= 0 && index + 1 < arguments.Length) requested = arguments[index + 1];
            if (string.IsNullOrWhiteSpace(requested)) requested = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrWhiteSpace(requested)) return new MuseumServiceBuildSetting(null);
            var address = MuseumServiceAddress.Normalize(requested);
            Debug.Log("Musiyo build: API address baked into this build: " + address);
            return new MuseumServiceBuildSetting(address);
        }

        private static void Write(string address)
        {
            File.WriteAllText(ResourcePath, "{\n  \"schema_version\": 1,\n  \"api_base\": \"" + address + "\"\n}\n");
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
        }

        public void Dispose()
        {
            if (original == null) return;
            File.WriteAllText(ResourcePath, original);
            AssetDatabase.ImportAsset(ResourcePath, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
