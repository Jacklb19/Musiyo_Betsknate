using MusiyoBetsknate.Museum;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class ElementTextTests
    {
        [Test]
        public void PublicTextKeepsLabelsSourcesAndTranscription()
        {
            const string json = "{\"slug\":\"test\",\"title\":\"Test\",\"description\":\"Synthetic\","
                + "\"blocks\":[{\"id\":\"b\",\"kind\":\"testimony\",\"text\":\"Testimony\",\"attribution\":\"Test speaker\"}],"
                + "\"sources\":[{\"id\":\"s\",\"kind\":\"other\",\"reference\":\"Test source\"}],"
                + "\"resources\":[{\"id\":\"r\",\"kind\":\"narration\",\"mime\":\"audio/mpeg\",\"transcription\":\"TEST TRANSCRIPT\"}]}";
            Assert.That(ElementText.TryFormat("test", json, out var text), Is.True);
            Assert.That(text, Does.Contain("Testimonio").And.Contain("Test speaker").And.Contain("Test source").And.Contain("TEST TRANSCRIPT"));
            Assert.That(ElementText.TryFormat("another", json, out var rejected), Is.False);
            Assert.That(rejected, Is.Null);
        }
    }
}
