using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace MusiyoBetsknate.Museum
{
    public sealed class SubtitleCue
    {
        public double Start { get; }
        public double End { get; }
        public string Text { get; }
        public SubtitleCue(double start, double end, string text) { Start = start; End = end; Text = text; }
    }

    public static class WebVttReader
    {
        public static IReadOnlyList<SubtitleCue> Parse(string source)
        {
            var cues = new List<SubtitleCue>();
            if (string.IsNullOrEmpty(source)) return cues;
            var lines = source.TrimStart('\ufeff').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (!Regex.IsMatch(lines[0], "^WEBVTT(?:[ \\t].*)?$")) return cues;
            for (int index = 1; index < lines.Length; index++)
            {
                if (lines[index] == "STYLE" || lines[index] == "REGION" || lines[index] == "NOTE"
                    || lines[index].StartsWith("NOTE ", StringComparison.Ordinal) || lines[index].StartsWith("NOTE\t", StringComparison.Ordinal))
                { while (++index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) { } continue; }
                var timing = lines[index].Split(new[] { "-->" }, StringSplitOptions.None);
                if (timing.Length != 2 || !TryTime(timing[0].Trim(), out var start)
                    || !TryTime(timing[1].Trim().Split(' ', '\t')[0], out var end) || end <= start) continue;
                var text = new List<string>();
                while (++index < lines.Length && !string.IsNullOrWhiteSpace(lines[index])) text.Add(lines[index]);
                string plain = WebUtility.HtmlDecode(Regex.Replace(string.Join("\n", text), "<[^>]*>", ""));
                if (!string.IsNullOrWhiteSpace(plain)) cues.Add(new SubtitleCue(start, end, plain));
            }
            return cues.OrderBy(cue => cue.Start).ToArray();
        }

        public static string At(IReadOnlyList<SubtitleCue> cues, double time)
            => string.Join("\n", cues.Where(cue => cue.Start <= time && time < cue.End).Select(cue => cue.Text));

        private static bool TryTime(string value, out double seconds)
        {
            seconds = 0;
            if (!Regex.IsMatch(value, "^(?:[0-9]{2,}:)?[0-5][0-9]:[0-5][0-9]\\.[0-9]{3}$")) return false;
            var parts = value.Split(':');
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var component)) return false;
                seconds = seconds * 60 + component;
            }
            return true;
        }
    }
}
