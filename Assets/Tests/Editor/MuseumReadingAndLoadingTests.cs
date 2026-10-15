using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Synthetic fixtures only: no published transcription, timing or model is used here.</summary>
    public sealed class MuseumReadingAndLoadingTests
    {
        private const string Transcript = "Primer fragmento sintético.\nSegundo   fragmento, con espacios.  Tercer fragmento final.";

        private static IReadOnlyList<SubtitleCue> Cues(params (double start, double end, string text)[] values)
        {
            var cues = new List<SubtitleCue>();
            foreach (var (start, end, text) in values) cues.Add(new SubtitleCue(start, end, text));
            return cues;
        }

        [Test]
        public void CuesAreFoundInTheTranscriptionInOrderDespiteSpacingAndCase()
        {
            var cues = Cues((0, 2, "Primer fragmento sintético."), (2.5, 5, "segundo fragmento,\ncon espacios."), (5, 8, "Tercer fragmento final."));
            var map = ReadingAlignment.Map(Transcript, cues);
            Assert.That(map, Is.Not.Null);
            Assert.That(Transcript.Substring(map[0].start, map[0].length), Is.EqualTo("Primer fragmento sintético."));
            Assert.That(Transcript.Substring(map[1].start, map[1].length), Is.EqualTo("Segundo   fragmento, con espacios."));
            Assert.That(Transcript.Substring(map[2].start, map[2].length), Is.EqualTo("Tercer fragmento final."));
        }

        [Test]
        public void CuesThatDoNotMatchTheTranscriptionAreNotFollowed()
        {
            Assert.That(ReadingAlignment.Map(Transcript, Cues((0, 2, "Otra frase que no está."))), Is.Null);
            // Out of order is not a match either: following would jump backwards.
            Assert.That(ReadingAlignment.Map(Transcript, Cues((0, 2, "Tercer fragmento final."), (2, 4, "Primer fragmento sintético."))), Is.Null);
            Assert.That(ReadingAlignment.Map(Transcript, Cues()), Is.Null, "Without timed subtitles nothing is aligned.");
            Assert.That(ReadingAlignment.Map("", Cues((0, 1, "x"))), Is.Null);
        }

        [Test]
        public void TheFragmentHeardIsTheLastOneStartedAndAGapKeepsIt()
        {
            var cues = Cues((1, 2, "a"), (2.5, 4, "b"), (4, 6, "c"));
            Assert.That(ReadingAlignment.CueAt(cues, .5), Is.EqualTo(-1));
            Assert.That(ReadingAlignment.CueAt(cues, 1.2), Is.EqualTo(0));
            Assert.That(ReadingAlignment.CueAt(cues, 2.2), Is.EqualTo(0), "Between fragments the previous one stays.");
            Assert.That(ReadingAlignment.CueAt(cues, 4), Is.EqualTo(2));
            Assert.That(ReadingAlignment.CueAt(cues, 60), Is.EqualTo(2));
        }

        [Test]
        public void FollowingPutsTheHeardLineAtItsHeightWithinTheScrollableRange()
        {
            // Viewport from 0 (top) to -400; the line should sit 30 % down, at -120.
            Assert.That(ReadingAlignment.FollowOffset(0, -300, 0, 400, .3f, 1000), Is.EqualTo(180).Within(.01f));
            Assert.That(ReadingAlignment.FollowOffset(500, -120, 0, 400, .3f, 1000), Is.EqualTo(500).Within(.01f), "Already in place.");
            Assert.That(ReadingAlignment.FollowOffset(100, 200, 0, 400, .3f, 1000), Is.EqualTo(0), "Never above the top.");
            Assert.That(ReadingAlignment.FollowOffset(900, -900, 0, 400, .3f, 1000), Is.EqualTo(1000), "Never past the end.");
            Assert.That(ReadingAlignment.FollowOffset(0, -900, 0, 400, .3f, 0), Is.EqualTo(0), "Nothing to scroll.");
        }

        [Test]
        public void ModelCacheKeepsAFewVerifiedFilesAndOnlyServesTheSameHash()
        {
            var cache = new ModelByteCache(2, 100);
            cache.Put("web/a", "AA", new byte[40]);
            cache.Put("web/b", "BB", new byte[40]);
            Assert.That(cache.TryGet("web/a", "aa", out var a), Is.True, "Hashes compare without regard to case.");
            Assert.That(a.Length, Is.EqualTo(40));
            cache.Put("web/c", "CC", new byte[40]);
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.TryGet("web/b", "BB", out _), Is.False, "The least recently used file leaves first.");
            Assert.That(cache.TryGet("web/a", "A2", out _), Is.False, "A new grant naming another file never gets the old bytes.");
            Assert.That(cache.TryGet("web/a", "AA", out _), Is.False, "A superseded file is dropped.");
            Assert.That(cache.TryGet("web/c", null, out _), Is.False, "Without a hash in the grant nothing is reused.");
            cache.Put("web/big", "XX", new byte[101]);
            Assert.That(cache.TryGet("web/big", "XX", out _), Is.False, "Files larger than the budget are not kept.");
            Assert.That(cache.Bytes, Is.LessThanOrEqualTo(100));
            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.Bytes, Is.EqualTo(0));
            var off = new ModelByteCache(0, 100);
            off.Put("web/a", "AA", new byte[1]);
            Assert.That(off.Count, Is.EqualTo(0));
        }

        [Test]
        public void LoadTimelineNamesEveryMeasuredStageAndTheWorstFrame()
        {
            var timeline = new ModelLoadTimeline("synthetic-piece", "quest");
            timeline.Begin("download");
            timeline.Begin("verify");
            timeline.Begin("import");
            timeline.End();
            timeline.ObserveFrame(.012f);
            timeline.ObserveFrame(.045f);
            timeline.ObserveFrame(float.NaN);
            timeline.Bytes = 1234;
            timeline.Outcome = "ready";
            Assert.That(timeline.Stages.Count, Is.EqualTo(3));
            Assert.That(timeline.WorstFrameMilliseconds, Is.EqualTo(45).Within(.01f));
            var line = timeline.ToString();
            Assert.That(line, Does.StartWith(ModelLoadTimeline.LogTag));
            foreach (var part in new[] { "synthetic-piece", "profile=quest", "outcome=ready", "bytes=1234", "download=", "verify=",
                         "import=", "worst_frame=45.0ms" })
                Assert.That(line, Does.Contain(part));
        }
    }
}
