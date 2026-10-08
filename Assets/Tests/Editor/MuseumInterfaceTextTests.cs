using System;
using System.IO;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumInterfaceTextTests
    {
        [Test]
        public void ShippedCatalogResolvesEveryReferencedInterfaceKey()
        {
            var catalog = MuseumInterfaceText.Current;
            Assert.That(catalog.Language, Is.EqualTo("es"));
            Assert.That(catalog.SuggestedVisitMinutes, Is.GreaterThan(0));
            var pattern = new Regex("MuseumInterfaceText\\.(?:Get|Format)\\(\"([^\"]+)\"");
            var count = 0;
            foreach (var path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts/Museo"), "*.cs"))
                foreach (Match match in pattern.Matches(File.ReadAllText(path)))
                {
                    Assert.That(catalog.Read(match.Groups[1].Value), Is.Not.Empty, path);
                    count++;
                }
            Assert.That(count, Is.GreaterThan(0));
        }

        [TestCase(2, "es", 30)]
        [TestCase(1, "en", 30)]
        [TestCase(1, "es", 0)]
        public void InvalidCatalogMetadataIsRejected(int version, string language, int minutes)
        {
            var json = "{\"schema_version\":" + version + ",\"language\":\"" + language
                + "\",\"suggested_visit_minutes\":" + minutes + ",\"entries\":{\"pause\":\"Pausa\"}}";
            Assert.Throws<InvalidOperationException>(() => MuseumInterfaceText.Parse(json));
        }

        [Test]
        public void MissingKeysAreDiagnosedInsteadOfShowingUntranslatedFallbacks()
        {
            var catalog = MuseumInterfaceText.Parse("{\"schema_version\":1,\"language\":\"es\",\"suggested_visit_minutes\":30,\"entries\":{\"pause\":\"Pausa\"}}");
            Assert.Throws<InvalidOperationException>(() => catalog.Read("missing"));
            Assert.That(MuseumInterfaceText.Format("farewell", "Mirador"), Does.Contain("Mirador"));
        }
    }
}
