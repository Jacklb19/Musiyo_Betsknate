using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MusiyoBetsknate.Museo;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Accept only published platform variants and self-contained GLB files.</summary>
    public static class ModelResourcePolicy
    {
        public static int ByteLimit(string profile) => profile == "quest" ? 6000000 : 10000000;
        public static int TriangleLimit(string profile) => profile == "quest" ? 40000 : 130000;

        public static bool TrySelect(ElementContractV1 element, string profile,
            out ResourceContractV1 resource, out string variantId)
        {
            resource = null;
            variantId = null;
            if (profile != "web" && profile != "quest") return false;
            foreach (var candidate in element?.resources ?? Array.Empty<ResourceContractV1>())
            {
                if (candidate.kind != "model_3d" || candidate.mime != "model/gltf-binary") continue;
                var variants = (candidate.variants ?? Array.Empty<ResourceVariantContractV1>())
                    .Where(variant => variant.profile == profile && !string.IsNullOrWhiteSpace(variant.id)).ToArray();
                if (variants.Length != 1) continue;
                resource = candidate;
                variantId = variants[0].id;
                return true;
            }
            return false;
        }

        public static bool TryAccess(string apiBase, string json, string profile, DateTimeOffset now,
            out ResourceAccessContractV1 access, out Uri url)
        {
            url = null;
            if (!ResourceAccessContractV1.TryParse(json, out access)
                || access.mime != "model/gltf-binary"
                || access.byte_count > ByteLimit(profile)
                || !DateTimeOffset.TryParse(access.expires_at, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var expiration) || expiration <= now
                || !Uri.TryCreate(apiBase, UriKind.Absolute, out var api)
                || !Uri.TryCreate(api, access.url, out url)) return false;
            return string.IsNullOrEmpty(url.UserInfo) && (url.Scheme == "https"
                || url.Scheme == "http" && api.Scheme == "http" && url.IsLoopback);
        }

        public static bool ValidateGlb(byte[] data, string profile, string expectedHash = null)
        {
            if (data == null || data.Length < 20 || data.Length > ByteLimit(profile)) return false;
            try
            {
                if (!string.IsNullOrEmpty(expectedHash))
                {
                    using var sha = SHA256.Create();
                    var actual = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
                }
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2
                    || reader.ReadUInt32() != data.Length) return false;
                var jsonLength = reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x4e4f534a || jsonLength == 0 || jsonLength % 4 != 0
                    || jsonLength > data.Length - 20) return false;
                var document = JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes((int)jsonLength)));
                if ((string)document["asset"]?["version"] != "2.0") return false;
                // Remote subordinate files would bypass the resource access and size checks.
                foreach (var property in document.Descendants().OfType<JProperty>())
                    if (property.Name == "uri" && property.Value.Type == JTokenType.String
                        && !property.Value.Value<string>().StartsWith("data:", StringComparison.Ordinal)) return false;
                return true;
            }
            catch (JsonException) { return false; }
            catch (EndOfStreamException) { return false; }
        }
    }
}
