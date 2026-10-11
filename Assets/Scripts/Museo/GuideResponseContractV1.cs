using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class GuideResponseContractV1
    {
        /// <summary>The schema ties answer, citations and reason to the status, so a parsed response is one of its three complete outcomes.</summary>
        public static bool TryParse(string json, out GuideResponseContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideResponseContractV1>(json, ContractSchemasV1.GuideResponse);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
