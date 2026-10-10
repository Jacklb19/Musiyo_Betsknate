using System;
using System.Text;
using MusiyoBetsknate.Museo;

namespace MusiyoBetsknate.Museum
{
    public static class ElementText
    {
        public static bool TryFormat(string selectedSlug, string json, out string text)
            => TryFormat(selectedSlug, json, out text, out _);

        /// <summary>Formats the complete detail and also returns the short description that introduces it.</summary>
        public static bool TryFormat(string selectedSlug, string json, out string text, out string description)
        {
            text = description = null;
            if (!ElementContractV1.TryParse(json, out var element) || element.slug != selectedSlug) return false;
            description = element.description;
            var output = new StringBuilder().AppendLine(element.title).AppendLine().AppendLine(element.description);
            foreach (var block in element.blocks ?? Array.Empty<TextBlockContractV1>())
            {
                string label = block.kind == "documented_fact" ? "Información documentada"
                    : block.kind == "testimony" ? "Testimonio" : "Interpretación";
                output.AppendLine().AppendLine(label).AppendLine(block.text);
                if (!string.IsNullOrEmpty(block.attribution)) output.AppendLine(block.attribution);
                if (!string.IsNullOrEmpty(block.context)) output.AppendLine(block.context);
            }
            foreach (var source in element.sources ?? Array.Empty<SourceContractV1>())
                output.AppendLine().Append("Fuente: ").AppendLine(source.reference);
            foreach (var credit in element.credits ?? Array.Empty<CreditContractV1>())
                output.AppendLine().Append("Crédito: ").AppendLine(credit.name);
            foreach (var resource in element.resources ?? Array.Empty<ResourceContractV1>())
                if (resource.kind == "narration" && !string.IsNullOrEmpty(resource.transcription))
                {
                    output.AppendLine().AppendLine("Narración · transcripción").AppendLine(resource.transcription);
                    if (!string.IsNullOrEmpty(resource.credit)) output.Append("Crédito: ").AppendLine(resource.credit);
                }
            foreach (var restriction in element.restrictions ?? Array.Empty<RestrictionContractV1>())
                output.AppendLine().AppendLine(restriction.description);
            text = output.ToString();
            return true;
        }
    }
}
