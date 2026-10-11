using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class GuideQuestionContractV1
    {
        /// <summary>Request body for a visitor's question; fails when the canonical schema would reject it.</summary>
        public static bool TryCreate(string question, string elementSlug, out string json)
        {
            json = null;
            var body = JsonConvert.SerializeObject(new GuideQuestionContractV1
            {
                schema_version = 1,
                question = question?.Trim(),
                element_slug = string.IsNullOrEmpty(elementSlug) ? null : elementSlug
            });
            if (!TryParse(body, out _)) return false;
            json = body;
            return true;
        }

        public static bool TryParse(string json, out GuideQuestionContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideQuestionContractV1>(json, ContractSchemasV1.GuideQuestion);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
