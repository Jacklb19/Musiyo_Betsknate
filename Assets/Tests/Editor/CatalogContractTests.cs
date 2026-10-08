using System.IO;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public class CatalogContractTests
    {
        [Test]
        public void CanonicalPageParsesAndRejectsImpossibleBounds()
        {
            var json = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "catalog.json"));
            Assert.IsTrue(CatalogPageContractV1.TryParse(json, out var page));
            Assert.AreEqual("synthetic-item", page.items[0].slug);
            Assert.IsFalse(CatalogPageContractV1.TryParse(json.Replace("\"limit\": 24", "\"limit\": 101"), out _));
            Assert.IsFalse(CatalogPageContractV1.TryParse(json.Replace("\"total\": 1", "\"total\": 0"), out _));
            Assert.IsFalse(CatalogPageContractV1.TryParse(json.Replace("\"schema_version\": 1", "\"schema_version\": true"), out _));
        }
    }
}
