using System;
using System.Linq;
using System.Globalization;
using MusiyoBetsknate.Museo;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// An operator-supplied permission for one local rehearsal appearance, separate from publication and global reuse.
    /// The file lives beside the delivered player, outside source control. Public origins never use this exception.
    /// </summary>
    [Preserve]
    public sealed class CompanionLocalUse
    {
        public const string FileName = "museum-companion-local-use.json";
        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("scope")] public string Scope;
        [JsonProperty("approved")] public bool Approved;
        [JsonProperty("responsible")] public string Responsible;
        [JsonProperty("decision_reference")] public string DecisionReference;
        [JsonProperty("approved_at")] public string ApprovedAt;
        [JsonProperty("element_slug")] public string ElementSlug;
        [JsonProperty("web_resource_id")] public string WebResourceId;
        [JsonProperty("web_sha256")] public string WebSha256;
        [JsonProperty("quest_resource_id")] public string QuestResourceId;
        [JsonProperty("quest_sha256")] public string QuestSha256;

        public static bool IsLocalContext(string apiBase, string pageUrl)
        {
            if (!IsLoopback(apiBase)) return false;
            return string.IsNullOrEmpty(pageUrl) || IsLoopback(pageUrl);
        }

        private static bool IsLoopback(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(uri.UserInfo)
                && (uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "[::1]" || uri.Host == "::1");

        public static CompanionLocalUse Parse(string json, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 8192) return null;
            try
            {
                var value = JObject.Parse(json);
                if (value["schema_version"]?.Type != JTokenType.Integer || value["approved"]?.Type != JTokenType.Boolean) return null;
                var result = JsonConvert.DeserializeObject<CompanionLocalUse>(json, new JsonSerializerSettings
                    { MissingMemberHandling = MissingMemberHandling.Error, DateParseHandling = DateParseHandling.None });
                if (result == null || result.SchemaVersion != 1 || !result.Approved || result.Scope != "local-rehearsal-companion-appearance"
                    || string.IsNullOrWhiteSpace(result.Responsible) || string.IsNullOrWhiteSpace(result.DecisionReference)
                    || !DateTimeOffset.TryParse(result.ApprovedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var approved) || approved > now
                    || !Identifier(result.ElementSlug) || !Identifier(result.WebResourceId) || !Identifier(result.QuestResourceId)
                    || !Hash(result.WebSha256) || !Hash(result.QuestSha256)) return null;
                return result;
            }
            catch (JsonException) { return null; }
        }

        private static bool Identifier(string value)
            => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && !value.Any(char.IsWhiteSpace);
        private static bool Hash(string value)
            => value != null && value.Length == 64 && value.All(character => character >= '0' && character <= '9' || character >= 'a' && character <= 'f');

        public bool Matches(string slug, string profile, string resourceId, string sha256, string apiBase, string pageUrl)
        {
            if (!IsLocalContext(apiBase, pageUrl) || slug != ElementSlug) return false;
            if (profile == "web") return resourceId == WebResourceId && sha256 == WebSha256;
            if (profile == "quest") return resourceId == QuestResourceId && sha256 == QuestSha256;
            return false;
        }

        public bool Selects(string slug, string profile, string resourceId, string apiBase, string pageUrl)
            => IsLocalContext(apiBase, pageUrl) && slug == ElementSlug
                && (profile == "web" && resourceId == WebResourceId || profile == "quest" && resourceId == QuestResourceId);
    }
}
