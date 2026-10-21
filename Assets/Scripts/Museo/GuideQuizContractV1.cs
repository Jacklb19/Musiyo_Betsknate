using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class GuideQuizContractV1
    {
        /// <summary>The hearth questions as published; the order of the array is the order they are asked in.</summary>
        public static bool TryParse(string json, out GuideQuizContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideQuizContractV1>(json, ContractSchemasV1.GuideQuiz);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    public sealed partial class GuideQuizAnswerContractV1
    {
        /// <summary>Request body for one chosen option; fails when the canonical schema would reject it.</summary>
        public static bool TryCreate(string questionId, string optionId, out string json)
        {
            json = JsonConvert.SerializeObject(new GuideQuizAnswerContractV1 { schema_version = 1, id = questionId, option_id = optionId });
            try
            {
                ContractJsonV1.Parse<GuideQuizAnswerContractV1>(json, ContractSchemasV1.GuideQuizAnswer);
                return true;
            }
            catch (JsonException) { json = null; return false; }
        }
    }

    public sealed partial class GuideQuizAnswerResultContractV1
    {
        public static bool TryParse(string json, out GuideQuizAnswerResultContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideQuizAnswerResultContractV1>(json, ContractSchemasV1.GuideQuizAnswerResult);
                return true;
            }
            catch (JsonException) { return false; }
        }

        /// <summary>The explanation belongs to the question asked: same question, record and source.</summary>
        public bool Explains(QuizQuestionContractV1 question)
            => question != null && id == question.id && citation?.element_slug == question.element_slug && source?.id == question.source_id;
    }
}
