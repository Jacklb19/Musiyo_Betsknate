using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class CatalogPageContractV1
    {
        public static bool TryParse(string json, out CatalogPageContractV1 contract)
        {
            contract = null;
            try
            {
                var parsed = ContractJsonV1.Parse<CatalogPageContractV1>(json, ContractSchemasV1.Catalog);
                var slugs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in parsed.items)
                    if (!slugs.Add(item.slug)) return false;
                if (parsed.items.Length > parsed.limit || parsed.items.Length > Math.Max(0, (long)parsed.total - parsed.offset)) return false;
                contract = parsed;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
