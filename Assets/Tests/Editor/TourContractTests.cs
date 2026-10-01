using System;
using System.IO;
using System.Security.Cryptography;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public class TourContractTests
    {
        private static string Example(string name)
            => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        [Test]
        public void CopiedContractsMatchSourceHashes()
        {
            var root = Path.Combine(Application.dataPath, "Contracts");
            using (var sha = SHA256.Create())
            foreach (var line in File.ReadAllLines(Path.Combine(root, "source.sha256")))
            {
                var parts = line.Split(new[] { "  " }, StringSplitOptions.None);
                Assert.AreEqual(2, parts.Length);
                var path = Path.Combine(root, parts[1].Replace('/', Path.DirectorySeparatorChar));
                var digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)))
                    .Replace("-", "").ToLowerInvariant();
                Assert.AreEqual(parts[0], digest, parts[1]);
            }
        }

        [Test]
        public void TypedElementBlocksRequireKnownSources()
        {
            Assert.IsTrue(ElementContractV1.TryParse(Example("element.json"), out var element));
            Assert.AreEqual("interpretation", element.blocks[0].kind);
            Assert.IsFalse(ElementContractV1.TryParse(Example("element.json").Replace("\"source_id\": \"source-test\"", "\"source_id\": \"missing\""), out _));
        }

        [TestCase("\"schema_version\": 1", "\"schema_version\": \"1\"")]
        [TestCase("\"keyboard\"", "\"touch\"")]
        [TestCase("\"name\": \"Sala de prueba\"", "\"unknown\": true, \"name\": \"Sala de prueba\"")]
        public void InvalidWireValuesAreRejected(string original, string replacement)
        {
            var json = Example("tour_full.json").Replace(original, replacement);
            Assert.IsFalse(TourContractV1.TryParse(json, out _, out _));
        }

        [Test]
        public void AnElementMayAppearAtTwoPoints()
        {
            Assert.IsTrue(TourContractV1.TryParse(Example("tour_full.json"), out var contract, out var error), error);
            Assert.AreEqual(2, contract.rooms[0].points.Length);
            Assert.AreEqual(contract.rooms[0].points[0].elements[0].slug,
                contract.rooms[0].points[1].elements[0].slug);
        }

        [Test]
        public void EmptyPointIsValid()
        {
            Assert.IsTrue(TourContractV1.TryParse(Example("tour_empty_point.json"), out var contract, out var error), error);
            Assert.IsEmpty(contract.rooms[0].points[0].elements);
        }

        [Test]
        public void FutureVersionIsRejected()
        {
            Assert.IsFalse(TourContractV1.TryParse(Example("tour_version_2.json"), out _, out _));
        }

        [Test]
        public void RepeatedAnchorIsRejected()
        {
            var json = Example("tour_full.json").Replace("point-02", "point-01");
            Assert.IsFalse(TourContractV1.TryParse(json, out _, out _));
        }

        [Test]
        public void BridgeOnlyResolvesAuthorizedSelections()
        {
            Assert.IsTrue(TourContractV1.TryParse(Example("tour_full.json"), out var contract, out var error), error);
            Assert.IsTrue(WebTourBridge.TryResolve(contract, "point-01", "synthetic-item", out var room));
            Assert.AreEqual("room-test", room);
            Assert.IsFalse(WebTourBridge.TryResolve(contract, "point-01", "hidden", out _));
            Assert.IsFalse(WebTourBridge.TryResolve(contract, "missing", null, out _));
        }
    }
}
