using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class PointPresenceContractV1
    {
        public static bool TryParse(string json, out PointPresenceContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<PointPresenceContractV1>(json, ContractSchemasV1.PresenceBridge);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
