using System;
using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class ResourceAccessContractV1
    {
        public static bool TryParse(string json, out ResourceAccessContractV1 contract)
        {
            contract = null;
            try
            {
                var parsed = ContractJsonV1.Parse<ResourceAccessContractV1>(json, ContractSchemasV1.ResourceAccess);
                if (!Uri.TryCreate(new Uri("https://api.invalid"), parsed.url, out var url)
                    || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
                    || !string.IsNullOrEmpty(url.UserInfo)) return false;
                contract = parsed;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
