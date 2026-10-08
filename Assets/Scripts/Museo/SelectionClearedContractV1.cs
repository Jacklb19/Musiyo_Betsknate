using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class SelectionClearedContractV1
    {
        public static bool TryParse(string json, out SelectionClearedContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<SelectionClearedContractV1>(json, ContractSchemasV1.ClearedBridge);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
