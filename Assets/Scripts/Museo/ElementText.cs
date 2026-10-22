using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museo;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The text of a record as the museum reads it: each block with its label, its text and a small reference number, and
    /// at the end, in small type, the numbered short references and one credit line. Attributions, contexts and sources
    /// never interrupt the reading (owner's decision of 21/10/2026). The result is TextMeshPro markup: everything that
    /// comes from the service is wrapped so it is shown literally and never parsed as markup.
    /// </summary>
    public static class ElementText
    {
        /// <summary>Size of the references and credit line at the end, relative to the reading text.</summary>
        public const int ReferenceSize = 78;

        public static bool TryFormat(string selectedSlug, string json, out string text)
            => TryFormat(selectedSlug, json, out text, out _);

        /// <summary>Formats the complete detail and also returns the short description that introduces it.</summary>
        public static bool TryFormat(string selectedSlug, string json, out string text, out string description)
            => TryFormat(selectedSlug, json, true, out text, out description);

        /// <summary>
        /// Formats the detail; without <paramref name="withNarration"/> it leaves out the narration's transcription, which a
        /// screen that follows the narration shows on its own.
        /// </summary>
        public static bool TryFormat(string selectedSlug, string json, bool withNarration, out string text, out string description)
        {
            text = description = null;
            if (!ElementContractV1.TryParse(json, out var element) || element.slug != selectedSlug) return false;
            description = element.description;
            var notes = RecordReferences.BlockNotes(element);
            var output = new StringBuilder().AppendLine(Plain(element.title)).AppendLine().AppendLine(Plain(element.description));
            foreach (var block in element.blocks ?? Array.Empty<TextBlockContractV1>())
            {
                string label = block.kind == "documented_fact" ? "Información documentada"
                    : block.kind == "testimony" ? "Testimonio" : "Interpretación";
                output.AppendLine().AppendLine(label).Append(Plain(block.text));
                var note = notes.FirstOrDefault(item => item.BlockId == block.id);
                if (note != null) output.Append("<sup>").Append(note.Number).Append("</sup>");
                output.AppendLine();
            }
            foreach (var resource in element.resources ?? Array.Empty<ResourceContractV1>())
                if (withNarration && resource.kind == "narration" && !string.IsNullOrEmpty(resource.transcription))
                {
                    output.AppendLine().AppendLine("Narración · transcripción").AppendLine(Plain(resource.transcription));
                    if (!string.IsNullOrEmpty(resource.credit)) output.Append("Crédito: ").AppendLine(Plain(resource.credit));
                }
            foreach (var restriction in element.restrictions ?? Array.Empty<RestrictionContractV1>())
                output.AppendLine().AppendLine(Plain(restriction.description));
            string credits = RecordReferences.CreditLine(element);
            if (notes.Count > 0 || credits.Length > 0)
            {
                output.AppendLine().Append("<size=").Append(ReferenceSize).Append("%><color=")
                    .Append(MuseumExperienceConfiguration.Current.MutedTextColor).Append('>');
                if (notes.Count > 0)
                {
                    output.Append("<b>Referencias</b>\n");
                    foreach (var note in notes.Where((item, index) => notes.FindIndex(other => other.Number == item.Number) == index))
                        output.Append(note.Number).Append(". ").Append(Plain(note.Text)).Append('\n');
                }
                if (credits.Length > 0) output.Append(notes.Count > 0 ? "\n" : "").Append(Plain(credits)).Append('\n');
                output.Append("</color></size>");
            }
            text = output.ToString().TrimEnd() + "\n";
            return true;
        }

        /// <summary>Service text, shown literally inside markup.</summary>
        public static string Plain(string value)
            => string.IsNullOrEmpty(value) ? "" : "<noparse>" + value.Replace("</noparse>", "</nopar​se>") + "</noparse>";
    }

    /// <summary>One numbered short reference of a record: the block it follows and the line read at the end.</summary>
    public sealed class BlockNote
    {
        public string BlockId;
        public int Number;
        public string Text;
    }

    /// <summary>
    /// The short references at the end of a record, exactly as the Web builds them (src/detail-content.ts, blockNotes and
    /// shortCitation): a testimony's attribution, then the source as author and year without initials ("&amp;" read "y",
    /// institutions abbreviated), then the context. Blocks that cite exactly the same thing share one number.
    /// </summary>
    public static class RecordReferences
    {
        private static readonly (Regex pattern, string shortName)[] Abbreviations =
        {
            (new Regex("Ministerio de Cultura de Colombia"), "MinCultura"),
            (new Regex("Universidad de Antioquia"), "UdeA"),
        };
        private static readonly Regex Dated = new Regex(@"^(.+?)\.?\s*\(((?:[0-9]{4}|s\. ?f\.)[^)]*)\)");
        private static readonly Regex Initials = new Regex(@",\s*(?:[A-ZÁÉÍÓÚÑ]\.?\s*)+(?=,|\s*&|\z)");
        private static readonly Regex Ampersand = new Regex(@",?\s*&\s*");
        private static readonly Regex Year = new Regex(@" \(.*");
        private static readonly Regex FinalStop = new Regex(@"\.\z");

        public static string ShortCitation(string reference)
            => Abbreviations.Aggregate(Citation(reference ?? ""), (text, abbreviation) => abbreviation.pattern.Replace(text, abbreviation.shortName));

        private static string Citation(string reference)
        {
            var dated = Dated.Match(reference);
            if (dated.Success)
            {
                string authors = Ampersand.Replace(Initials.Replace(dated.Groups[1].Value, ""), " y ").Trim();
                return authors + " (" + dated.Groups[2].Value.Split(',')[0].Trim() + ")";
            }
            string first = Regex.Split(reference, @"\.\s")[0].Trim();
            return first.Length > 70 ? first.Substring(0, 67).TrimEnd() + "…" : first;
        }

        public static List<BlockNote> BlockNotes(ElementContractV1 element)
        {
            var notes = new List<BlockNote>();
            foreach (var block in element.blocks ?? Array.Empty<TextBlockContractV1>())
            {
                var source = element.sources?.FirstOrDefault(item => item.id == block.source_id);
                bool attributed = !string.IsNullOrEmpty(block.attribution);
                var parts = new[]
                {
                    block.kind == "testimony" && attributed ? "Testimonio: " + block.attribution : block.attribution,
                    source != null && !(attributed && block.attribution.Contains(Year.Replace(ShortCitation(source.reference), "")))
                        ? ShortCitation(source.reference) : null,
                    block.context,
                }.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();
                if (parts.Length == 0) continue;
                string text = string.Join(". ", parts.Select(part => FinalStop.Replace(part.Trim(), ""))) + ".";
                var same = notes.FirstOrDefault(note => note.Text == text);
                notes.Add(new BlockNote
                {
                    BlockId = block.id, Text = text,
                    Number = same != null ? same.Number : notes.Select(note => note.Number).Distinct().Count() + 1,
                });
            }
            return notes;
        }

        /// <summary>"Role: Name · Role: Name"; a credit without a role reads as its name alone.</summary>
        public static string CreditLine(ElementContractV1 element)
            => string.Join(" · ", (element.credits ?? Array.Empty<CreditContractV1>())
                .Where(credit => !string.IsNullOrWhiteSpace(credit.name))
                .Select(credit => string.IsNullOrWhiteSpace(credit.role) ? credit.name.Trim() : credit.role.Trim() + ": " + credit.name.Trim()));
    }
}
