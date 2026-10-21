using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Where the visit finds the museum API. The browser build always uses the page's own origin (/api/v1). A headset or
    /// desktop build reads, in order: a <c>musiyo-service.json</c> file in the app's data folder, the same file beside an
    /// installed Windows executable, the address baked into the build (Resources/MuseumService.json, set by the build
    /// scripts with -musiyoApiBase), and finally the local development API. The file holds an address only, never a key:
    /// <c>{"schema_version": 1, "api_base": "https://museo.example.org/api/v1"}</c>.
    /// </summary>
    public static class MuseumServiceAddress
    {
        public const string FileName = "musiyo-service.json";
        public const string ResourceName = "MuseumService";
        public const string LocalDevelopment = "http://127.0.0.1:8000/api/v1";
        private static bool read;
        private static string configured;
        private static string origin = "";

        /// <summary>Address from a file or the build, or null when neither gives one; read once per run.</summary>
        public static string Configured
        {
            get
            {
                if (!read) Read();
                return configured;
            }
        }

        /// <summary>Which setting gave <see cref="Configured"/>: a file path, "build" or empty.</summary>
        public static string Origin
        {
            get
            {
                if (!read) Read();
                return origin;
            }
        }

        /// <summary>Forgets what was read, so the next use reads the file and the build again.</summary>
        public static void Reload() => read = false;

        private static void Read()
        {
            read = true;
            configured = null;
            origin = "";
            if (Application.platform == RuntimePlatform.WebGLPlayer) return;
            foreach (var folder in new[] { Application.persistentDataPath, InstallFolder() })
            {
                if (string.IsNullOrEmpty(folder)) continue;
                var path = Path.Combine(folder, FileName);
                string text;
                try
                {
                    if (!File.Exists(path)) continue;
                    text = File.ReadAllText(path);
                }
                catch (Exception) { continue; }
                if (TryParse(text, out var address)) { configured = address; origin = path; return; }
                Debug.LogWarning("Musiyo: " + path + " has no valid api_base; it is ignored.");
            }
            var built = Resources.Load<TextAsset>(ResourceName);
            if (built != null && TryParse(built.text, out var baked) && baked != null) { configured = baked; origin = "build"; }
        }

        /// <summary>Folder of an installed Windows executable; other platforms have no such place.</summary>
        private static string InstallFolder()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer) return null;
            try { return Path.GetDirectoryName(Application.dataPath); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Reads a settings document. An empty api_base is valid and means "not set"; anything else must be an absolute
        /// HTTP or HTTPS address without credentials.
        /// </summary>
        public static bool TryParse(string json, out string address)
        {
            address = null;
            try
            {
                var settings = JObject.Parse(json);
                if (settings.Value<int?>("schema_version") != 1) return false;
                var value = settings.Value<string>("api_base");
                if (string.IsNullOrWhiteSpace(value)) return true;
                address = Normalize(value);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>An absolute HTTP(S) address; an origin alone gets the API path /api/v1.</summary>
        public static string Normalize(string value)
        {
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("API base must be an HTTP or HTTPS URL");
            var text = uri.AbsoluteUri.TrimEnd('/');
            return uri.AbsolutePath == "/" ? text + "/api/v1" : text;
        }

        /// <summary>
        /// The address in force. <paramref name="scene"/> is set only by tests and development scenes; a browser page
        /// serves its own API; otherwise the configured address, the page if there is one, then the local API.
        /// </summary>
        public static string Resolve(string scene, string pageUrl, bool browser, string configuredAddress)
        {
            if (!string.IsNullOrWhiteSpace(scene)) return Normalize(scene);
            bool page = Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) && (pageUri.Scheme == "http" || pageUri.Scheme == "https");
            if (browser && page) return new Uri(pageUri, "/api/v1").AbsoluteUri.TrimEnd('/');
            if (!browser && !string.IsNullOrWhiteSpace(configuredAddress)) return configuredAddress;
            if (page) return new Uri(pageUri, "/api/v1").AbsoluteUri.TrimEnd('/');
            return LocalDevelopment;
        }
    }
}
