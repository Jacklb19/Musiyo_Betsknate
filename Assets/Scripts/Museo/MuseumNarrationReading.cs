using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Where each timed fragment of a narration lies in its published transcription.</summary>
    public static class ReadingAlignment
    {
        /// <summary>
        /// Start and length, in the transcription, of every cue, found in order. Spacing and letter case may differ; the
        /// words may not. Null when any cue is missing, because a partial match would claim a precision it does not have.
        /// </summary>
        public static (int start, int length)[] Map(string transcription, IReadOnlyList<SubtitleCue> cues)
        {
            if (string.IsNullOrEmpty(transcription) || cues == null || cues.Count == 0) return null;
            var positions = new List<int>();
            string text = Normalize(transcription, positions);
            var result = new (int, int)[cues.Count];
            int from = 0;
            for (int index = 0; index < cues.Count; index++)
            {
                string cue = Normalize(cues[index].Text, null);
                if (cue.Length == 0) return null;
                int found = text.IndexOf(cue, from, StringComparison.Ordinal);
                if (found < 0) return null;
                int start = positions[found];
                result[index] = (start, positions[found + cue.Length - 1] + 1 - start);
                from = found + cue.Length;
            }
            return result;
        }

        /// <summary>Lower case with every run of spacing as one space; positions maps each kept character back to the source.</summary>
        private static string Normalize(string value, List<int> positions)
        {
            var result = new StringBuilder(value?.Length ?? 0);
            bool space = false;
            for (int index = 0; value != null && index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsWhiteSpace(character))
                {
                    if (result.Length > 0 && !space) { result.Append(' '); positions?.Add(index); space = true; }
                    continue;
                }
                result.Append(char.ToLowerInvariant(character));
                positions?.Add(index);
                space = false;
            }
            if (space) { result.Length--; positions?.RemoveAt(positions.Count - 1); }
            return result.ToString();
        }

        /// <summary>
        /// Scroll offset from the top that puts a line at the follow height of the viewport, within what can be scrolled.
        /// Heights are in the viewport's space, where up is positive.
        /// </summary>
        public static float FollowOffset(float offset, float line, float viewportTop, float viewportHeight, float followLine, float scrollable)
        {
            if (scrollable <= 0) return 0;
            float desired = viewportTop - viewportHeight * followLine;
            return Mathf.Clamp(offset + desired - line, 0, scrollable);
        }

        /// <summary>Index of the fragment being heard: the last one that has started, so a pause between fragments keeps it.</summary>
        public static int CueAt(IReadOnlyList<SubtitleCue> cues, double time)
        {
            int found = -1;
            for (int index = 0; cues != null && index < cues.Count; index++)
                if (cues[index].Start <= time) found = index;
            return found;
        }
    }

    /// <summary>
    /// Keeps the fragment of the transcription that is being heard in view on the point's screen and marks it, as the Web
    /// does, so the text read is the text heard. The panel's content scrolls; the screen itself never moves in the museum.
    /// Timing comes from the narration's playback time and its published WebVTT cues: without cues that match the
    /// transcription, nothing follows or is marked and the full transcription stays readable with manual scrolling.
    /// Scrolling by hand suspends following until the visitor asks to resume or repeats. Service text is wrapped so it is
    /// shown literally; only the mark around the heard fragment is markup.
    /// </summary>
    public sealed class MuseumNarrationReading : MonoBehaviour
    {
        private Func<string> narratedSlug;
        private Func<IReadOnlyList<SubtitleCue>> timedCues;
        private Func<float> playbackTime;
        private Func<bool> playable;
        private Func<int> starts;
        private int seenStarts;
        private ScrollRect scroll;
        private TMP_Text text;
        private Button resume;
        private string status = "", transcription = "", attribution = "";
        private string alignedTranscription;
        private IReadOnlyList<SubtitleCue> alignedCues;
        private (int start, int length)[] fragments;
        private string slug;
        private int shownCue = -2;
        private int markIndex = -1;
        private float markY;
        private bool markMeasured;
        private float velocity;
        private float lastTime;
        /// <summary>False after the visitor scrolled by hand, until they resume or repeat the narration.</summary>
        public bool Following { get; private set; } = true;
        /// <summary>True when the narration's cues were found in its transcription, in order.</summary>
        public bool Aligned => fragments != null;
        public int ActiveCue => shownCue;
        /// <summary>Index in the screen's text where the marked fragment starts, or -1 when nothing is marked.</summary>
        public int MarkedIndex => markIndex;
        /// <summary>Marking of the fragment being heard: a soft band of the accent colour and the title accent for its letters.</summary>
        public const string MarkOpen = "<mark=#E7AE4538><color=#F5CC81>", MarkClose = "</color></mark>";
        public event Action Changed;

        public void Configure(MuseumNarration source, ScrollRect panelScroll, TMP_Text transcriptText, Button resumeButton)
            => Configure(() => source.NarratedSlug, () => source.Cues, () => source.PlaybackTime, () => source.CanPlay,
                panelScroll, transcriptText, resumeButton, () => source.Starts);

        /// <summary>Follows any timed source; the museum passes its narration, tests pass synthetic timings.</summary>
        public void Configure(Func<string> slug, Func<IReadOnlyList<SubtitleCue>> cues, Func<float> time, Func<bool> canPlay,
            ScrollRect panelScroll, TMP_Text transcriptText, Button resumeButton, Func<int> playbackStarts = null)
        {
            starts = playbackStarts;
            seenStarts = starts != null ? starts() : 0;
            narratedSlug = slug;
            timedCues = cues;
            playbackTime = time;
            playable = canPlay;
            scroll = panelScroll;
            text = transcriptText;
            if (text != null) text.richText = true;
            resume = resumeButton;
            if (scroll != null)
            {
                if (!scroll.TryGetComponent<ReadingScrollWatcher>(out var watcher)) watcher = scroll.gameObject.AddComponent<ReadingScrollWatcher>();
                watcher.Reading = this;
            }
            if (resume != null) resume.gameObject.SetActive(false);
        }

        /// <summary>Shows the narration's status, transcription and credits, following the fragment being heard when there is one.</summary>
        public void Show(string statusLine, string transcript, string credits)
        {
            status = statusLine ?? "";
            transcription = transcript ?? "";
            attribution = credits ?? "";
            Align();
            Compose(true);
        }

        /// <summary>The visitor scrolled the panel: stop following until asked again.</summary>
        public void Suspend()
        {
            if (!Following || !Aligned) return;
            Following = false;
            velocity = 0;
            RefreshResume();
            Changed?.Invoke();
        }

        public void Resume()
        {
            if (Following) return;
            Following = true;
            velocity = 0;
            RefreshResume();
            Changed?.Invoke();
        }

        private void Align()
        {
            if (narratedSlug == null) return;
            if (narratedSlug() != slug)
            {
                // Another narration starts fresh; pieces that share one narration keep it, and keep its place.
                slug = narratedSlug();
                Following = true;
                shownCue = -2;
                velocity = 0;
            }
            var cues = timedCues();
            if (ReferenceEquals(cues, alignedCues) && transcription == alignedTranscription) return;
            alignedCues = cues;
            alignedTranscription = transcription;
            fragments = ReadingAlignment.Map(transcription, cues);
            shownCue = -2;
        }

        /// <summary>
        /// Shows the text as published, literally (service text is never parsed as markup), with the fragment being heard
        /// marked, and notes where that fragment starts so the panel can bring it into view.
        /// </summary>
        private void Compose(bool force)
        {
            if (text == null) return;
            int cue = Aligned && playable != null && playable() ? ReadingAlignment.CueAt(timedCues(), playbackTime()) : -1;
            if (!force && cue == shownCue) return;
            shownCue = cue;
            text.text = Markup(status, transcription, attribution, cue >= 0 ? fragments[cue] : ((int, int)?)null, out markIndex);
            markMeasured = false;
            RefreshResume();
        }

        /// <summary>
        /// The status line in small type, then the transcription with the heard fragment marked, then any credits. Returns
        /// in <paramref name="marked"/> where the fragment's first character lies in the result, or -1.
        /// </summary>
        public static string Markup(string statusLine, string transcript, string credits, (int start, int length)? fragment, out int marked)
        {
            var result = new StringBuilder();
            marked = -1;
            if (!string.IsNullOrEmpty(statusLine))
                result.Append("<size=").Append(ElementText.ReferenceSize).Append("%><color=")
                    .Append(MuseumExperienceConfiguration.Current.MutedTextColor).Append('>').Append(ElementText.Plain(statusLine))
                    .Append("</color></size>\n");
            transcript ??= "";
            if (fragment.HasValue && fragment.Value.start >= 0 && fragment.Value.start + fragment.Value.length <= transcript.Length)
            {
                var (start, length) = fragment.Value;
                result.Append(ElementText.Plain(transcript.Substring(0, start))).Append(MarkOpen);
                // The fragment is wrapped like all service text; its first letter follows the opening of that wrapping.
                marked = result.Length + "<noparse>".Length;
                result.Append(ElementText.Plain(transcript.Substring(start, length))).Append(MarkClose)
                    .Append(ElementText.Plain(transcript.Substring(start + length)));
            }
            else result.Append(ElementText.Plain(transcript));
            if (!string.IsNullOrEmpty(credits)) result.Append('\n').Append(ElementText.Plain(credits));
            return result.ToString();
        }

        private void RefreshResume()
        {
            if (resume != null) resume.gameObject.SetActive(Aligned && !Following && playable != null && playable());
        }

        private void LateUpdate()
        {
            if (narratedSlug == null || text == null || !text.gameObject.activeInHierarchy) return;
            Align();
            float time = playbackTime();
            // Repeating, or anything that sends playback back to the start, resumes following.
            int started = starts != null ? starts() : seenStarts;
            if (started != seenStarts || time < .5f && lastTime > 1) Resume();
            seenStarts = started;
            lastTime = time;
            Compose(false);
            if (!Following || markIndex < 0 || scroll == null || scroll.content == null) return;
            if (!markMeasured && !Measure()) return;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            float scrollable = scroll.content.rect.height - viewport.rect.height;
            if (scrollable <= 1) return;
            float line = viewport.InverseTransformPoint(text.rectTransform.TransformPoint(new Vector3(0, markY, 0))).y;
            float offset = (1 - scroll.verticalNormalizedPosition) * scrollable;
            float target = ReadingAlignment.FollowOffset(offset, line, viewport.rect.yMax, viewport.rect.height,
                MuseumExperienceConfiguration.Current.ReadingFollowLine, scrollable);
            float next = Mathf.SmoothDamp(offset, target, ref velocity, MuseumExperienceConfiguration.Current.ReadingFollowSeconds,
                float.PositiveInfinity, Time.unscaledDeltaTime);
            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = 1 - next / scrollable;
        }

        /// <summary>Height, in the text's own space, of the top of the line where the marked fragment starts.</summary>
        private bool Measure()
        {
            text.ForceMeshUpdate();
            var info = text.textInfo;
            for (int index = 0; index < info.characterCount; index++)
            {
                if (info.characterInfo[index].index < markIndex) continue;
                markY = info.lineInfo[info.characterInfo[index].lineNumber].ascender;
                markMeasured = true;
                return true;
            }
            return false;
        }
    }
}
