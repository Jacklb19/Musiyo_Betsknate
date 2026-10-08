using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class NarrationTests
    {
        [Test]
        public void WebVttKeepsSpanishTextAndUsesExclusiveEndTimes()
        {
            var cues = WebVttReader.Parse("\ufeffWEBVTT\r\n\r\nintro\r\n00:00.500 --> 00:02.000 align:start\r\n<v Guide><b>Bëtsknaté</b> &amp; prueba</v>\r\n\r\n00:01.500 --> 00:03.000\r\nSegunda línea\r\n");
            Assert.That(cues.Count, Is.EqualTo(2));
            Assert.That(WebVttReader.At(cues, .4), Is.Empty);
            Assert.That(WebVttReader.At(cues, .5), Is.EqualTo("Bëtsknaté & prueba"));
            Assert.That(WebVttReader.At(cues, 1.5), Is.EqualTo("Bëtsknaté & prueba\nSegunda línea"));
            Assert.That(WebVttReader.At(cues, 2), Is.EqualTo("Segunda línea"));
            Assert.That(WebVttReader.At(cues, 3), Is.Empty);
        }

        [Test]
        public void WebVttIgnoresNotesAndInvalidTimings()
        {
            var cues = WebVttReader.Parse("WEBVTT\n\nNOTE ignored\n00:00.000 --> 00:10.000\nThis is a note\n\n00:00.000 --> 00:00.000\nInvalid\n\n00:61.000 --> 00:62.000\nInvalid\n\n01:02:03.500 --> 01:02:04.500\nValid\n");
            Assert.That(cues.Count, Is.EqualTo(1));
            Assert.That(cues[0].Start, Is.EqualTo(3723.5));
            Assert.That(WebVttReader.Parse("00:00.000 --> 00:01.000\nNo header").Count, Is.Zero);
        }

        [Test]
        public void NarrationRequiresTranscriptAndFitsPublicationLimits()
        {
            var resource = new ResourceContractV1 { kind = "narration", mime = "audio/mpeg", transcription = "Test",
                byte_count = 3000000, duration_seconds = 180 };
            var element = new ElementContractV1 { resources = new[] { resource } };
            Assert.That(NarrationResourcePolicy.Select(element), Is.SameAs(resource));
            resource.transcription = " ";
            Assert.That(NarrationResourcePolicy.Select(element), Is.Null);
            resource.transcription = "Test";
            resource.duration_seconds = 181;
            Assert.That(NarrationResourcePolicy.Select(element), Is.Null);
            resource.duration_seconds = 180;
            resource.byte_count = 3000001;
            Assert.That(NarrationResourcePolicy.Select(element), Is.Null);
        }

        [TestCase("audio/mpeg", 100, "2099-01-01T00:00:00Z", true)]
        [TestCase("video/mp4", 100, "2099-01-01T00:00:00Z", false)]
        [TestCase("audio/mpeg", 3000001, "2099-01-01T00:00:00Z", false)]
        [TestCase("audio/mpeg", 100, "2000-01-01T00:00:00Z", false)]
        public void AccessChecksMimeSizeAndExpiration(string mime, int bytes, string expiration, bool accepted)
        {
            string json = "{\"schema_version\":1,\"url\":\"/api/v1/files/test\",\"mime\":\"" + mime
                + "\",\"byte_count\":" + bytes + ",\"expires_at\":\"" + expiration + "\"}";
            Assert.That(NarrationResourcePolicy.TryAccess("https://museum.example/api/v1", json, "audio/mpeg",
                NarrationResourcePolicy.ByteLimit, out _, out _), Is.EqualTo(accepted));
        }

        [Test]
        public void NarrationPausesBeyondTheMarginAndResumesInsideThePointRadius()
        {
            const float radius = 2, margin = 2;
            Assert.That(MuseumNarration.IsAway(3.9f, radius, margin, false), Is.False, "Stepping past the radius alone keeps playing.");
            Assert.That(MuseumNarration.IsAway(4.1f, radius, margin, false), Is.True);
            Assert.That(MuseumNarration.IsAway(3f, radius, margin, true), Is.True, "Returning halfway stays paused.");
            Assert.That(MuseumNarration.IsAway(1.9f, radius, margin, true), Is.False);
            Assert.That(MuseumExperienceConfiguration.Current.NarrationLeaveMargin, Is.GreaterThan(0));
        }
    }
}
