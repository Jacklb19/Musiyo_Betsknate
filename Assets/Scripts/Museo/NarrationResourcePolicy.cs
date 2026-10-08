using System;
using System.Globalization;
using System.Linq;
using MusiyoBetsknate.Museo;

namespace MusiyoBetsknate.Museum
{
    public static class NarrationResourcePolicy
    {
        public const int ByteLimit = 3000000;
        public static ResourceContractV1 Select(ElementContractV1 element)
            => element?.resources?.FirstOrDefault(resource => resource.kind == "narration"
                && resource.mime == "audio/mpeg" && !string.IsNullOrWhiteSpace(resource.transcription)
                && !(resource.byte_count > ByteLimit) && !(resource.duration_seconds > 180));

        public static bool TryAccess(string apiBase, string json, string mime, int maximumBytes,
            out ResourceAccessContractV1 access, out Uri url)
        {
            url = null;
            if (!ResourceAccessContractV1.TryParse(json, out access) || access.mime != mime
                || access.byte_count > maximumBytes
                || !DateTimeOffset.TryParse(access.expires_at, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var expiration) || expiration <= DateTimeOffset.UtcNow
                || !Uri.TryCreate(apiBase, UriKind.Absolute, out var api)
                || !Uri.TryCreate(api, access.url, out url)) return false;
            return string.IsNullOrEmpty(url.UserInfo) && (url.Scheme == "https"
                || url.Scheme == "http" && api.Scheme == "http" && url.IsLoopback);
        }
    }
}
