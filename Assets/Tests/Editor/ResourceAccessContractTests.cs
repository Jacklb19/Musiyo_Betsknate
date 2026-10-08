using System.IO;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public class ResourceAccessContractTests
    {
        [Test]
        public void TemporaryAccessIsVersionedAndRejectsExecutableUrls()
        {
            var json = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "resource_access.json"));
            Assert.IsTrue(ResourceAccessContractV1.TryParse(json, out var access));
            Assert.AreEqual("audio/wav", access.mime);
            Assert.IsFalse(ResourceAccessContractV1.TryParse(json.Replace("\"schema_version\": 1", "\"schema_version\": true"), out _));
            Assert.IsFalse(ResourceAccessContractV1.TryParse(json.Replace(access.url, "javascript:alert(1)"), out _));
        }
    }
}
