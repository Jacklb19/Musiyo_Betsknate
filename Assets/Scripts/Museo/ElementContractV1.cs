using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class ElementContractV1
    {
        public static bool TryParse(string json, out ElementContractV1 contract)
        {
            contract = null;
            try
            {
                var parsed = ContractJsonV1.Parse<ElementContractV1>(json, ContractSchemasV1.Element);
                var sourceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in parsed.sources ?? Array.Empty<SourceContractV1>())
                    if (!sourceIds.Add(source.id)) return false;
                var blockIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var block in parsed.blocks ?? Array.Empty<TextBlockContractV1>())
                    if (!blockIds.Add(block.id) || (block.source_id != null && !sourceIds.Contains(block.source_id))) return false;
                contract = parsed;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
