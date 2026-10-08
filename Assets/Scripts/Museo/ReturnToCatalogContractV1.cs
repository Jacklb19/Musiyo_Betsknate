using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class ReturnToCatalogContractV1
    {
        public static bool TryParse(string json, out ReturnToCatalogContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<ReturnToCatalogContractV1>(json, ContractSchemasV1.ReturnBridge);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
