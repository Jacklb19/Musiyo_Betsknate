using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumWebBridgeTests
    {
        [Test]
        public void QueryParametersDecodeEscapesWithoutConfusingEmbeddedSeparators()
        {
            Assert.That(MuseumWebBridge.Parameter("?point=one%26two&element=plus%2Bsign", "point"), Is.EqualTo("one&two"));
            Assert.That(MuseumWebBridge.Parameter("point=one&element=plus%2Bsign", "element"), Is.EqualTo("plus+sign"));
            Assert.That(MuseumWebBridge.Parameter(null, "point"), Is.Null);
        }

        [Test]
        public void ClosingSelectionUsesTheSharedContract()
        {
            const string json = "{\"source\":\"musiyo-unity\",\"type\":\"selection_cleared\",\"version\":1,\"data\":{\"tour_key\":\"museum-main\"}}";
            Assert.That(SelectionClearedContractV1.TryParse(json, out var message), Is.True);
            Assert.That(message.data.tour_key, Is.EqualTo("museum-main"));
            Assert.That(SelectionClearedContractV1.TryParse(json.Replace("\"version\":1", "\"version\":true"), out _), Is.False);
            Assert.That(SelectionClearedContractV1.TryParse(json.Replace("\"museum-main\"", "\"museum-main\",\"point_key\":\"stale\""), out _), Is.False);
        }

        [Test]
        public void ReturningToTheCatalogueUsesItsOwnVersionedMessage()
        {
            const string json = "{\"source\":\"musiyo-unity\",\"type\":\"return_to_catalog\",\"version\":1,\"data\":{\"tour_key\":\"museum-main\"}}";
            Assert.That(ReturnToCatalogContractV1.TryParse(json, out var message), Is.True);
            Assert.That(message.data.tour_key, Is.EqualTo("museum-main"));
            Assert.That(SelectionClearedContractV1.TryParse(json, out _), Is.False, "A navigation request is not a cleared selection.");
            Assert.That(ReturnToCatalogContractV1.TryParse(json.Replace("\"version\":1", "\"version\":\"1\""), out _), Is.False);
            Assert.That(ReturnToCatalogContractV1.TryParse(json.Replace("\"museum-main\"", "\"museum-main\",\"point_key\":\"stale\""), out _), Is.False);
        }

        [Test]
        public void PointPresenceUsesItsOwnVersionedMessage()
        {
            const string json = "{\"source\":\"musiyo-unity\",\"type\":\"point_presence\",\"version\":1,\"data\":"
                + "{\"tour_key\":\"tour-test\",\"point_key\":\"point-01\",\"element_slug\":null,\"presence\":\"away\"}}";
            Assert.That(PointPresenceContractV1.TryParse(json, out var message), Is.True);
            Assert.That(message.data.presence, Is.EqualTo("away"));
            Assert.That(PointPresenceContractV1.TryParse(json.Replace("\"away\"", "\"near\""), out _), Is.True);
            Assert.That(PointPresenceContractV1.TryParse(json.Replace("\"away\"", "\"nearby\""), out _), Is.False);
            Assert.That(PointPresenceContractV1.TryParse(json.Replace("\"version\":1", "\"version\":true"), out _), Is.False);
            Assert.That(SelectionClearedContractV1.TryParse(json, out _), Is.False, "Presence is not a cleared selection.");
        }

        [TestCase(null)]
        [TestCase("")]
        public void EmptyNetworkResponsesAreRejected(string json)
        {
            Assert.That(SelectionClearedContractV1.TryParse(json, out _), Is.False);
            Assert.That(ElementContractV1.TryParse(json, out _), Is.False);
        }
    }
}
