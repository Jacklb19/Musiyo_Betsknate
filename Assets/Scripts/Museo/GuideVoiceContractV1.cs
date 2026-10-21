using Newtonsoft.Json;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class VoiceCapabilitiesContractV1
    {
        public static bool TryParse(string json, out VoiceCapabilitiesContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<VoiceCapabilitiesContractV1>(json, ContractSchemasV1.VoiceCapabilities);
                contract.providers ??= new string[0];
                return true;
            }
            catch (JsonException) { return false; }
        }

        /// <summary>The service can turn a recorded question into text.</summary>
        public bool Listens => transcription == "configured";
        /// <summary>The service can speak an answer.</summary>
        public bool Speaks => synthesis == "configured";
        /// <summary>Recordings or answers leave the museum's server for the named providers.</summary>
        public bool External => processing == "external_providers";
    }

    public sealed partial class GuideTranscriptionContractV1
    {
        public static bool TryParse(string json, out GuideTranscriptionContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideTranscriptionContractV1>(json, ContractSchemasV1.GuideTranscription);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    public sealed partial class GuideVoiceAnswerContractV1
    {
        /// <summary>The schema ties the inner response to its status, as for a written question.</summary>
        public static bool TryParse(string json, out GuideVoiceAnswerContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<GuideVoiceAnswerContractV1>(json, ContractSchemasV1.GuideVoiceAnswer);
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
