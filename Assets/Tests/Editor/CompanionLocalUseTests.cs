using System;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using System.Text.RegularExpressions;
using System.Globalization;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class CompanionLocalUseTests
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-16T18:00:00Z");
        private static string Document() => "{\"schema_version\":1,\"scope\":\"local-rehearsal-companion-appearance\",\"approved\":true,"
            + "\"responsible\":\"Synthetic owner\",\"decision_reference\":\"synthetic-local-decision\",\"approved_at\":\"2026-10-16T00:00:00Z\","
            + "\"element_slug\":\"synthetic-mask\",\"web_resource_id\":\"synthetic-web\",\"web_sha256\":\"" + new string('a',64)
            + "\",\"quest_resource_id\":\"synthetic-quest\",\"quest_sha256\":\"" + new string('b',64) + "\"}";

        [Test]
        public void LocalPermissionNeverRemovesTheGlobalReuseRestriction()
        {
            var record = new ElementContractV1 { slug = "synthetic-mask", restrictions = new[] { new RestrictionContractV1 { kind="no_reuse" } } };
            var permission = CompanionLocalUse.Parse(Document(), Now);
            Assert.That(permission, Is.Not.Null);
            Assert.That(MuseumCompanionMask.MayWear(record), Is.False);
            Assert.That(permission.Matches(record.slug,"quest","synthetic-quest",new string('b',64),"http://127.0.0.1:8000/api/v1", ""), Is.True);
            Assert.That(record.restrictions[0].kind, Is.EqualTo("no_reuse"));
        }

        [TestCase("https://museum.example/api/v1", "")]
        [TestCase("http://127.0.0.1:8000/api/v1", "https://museum.example/recorrido")]
        [TestCase("http://localhost.example:8000/api/v1", "")]
        public void PublicOriginsCannotUseALocalPermission(string api, string page)
            => Assert.That(CompanionLocalUse.Parse(Document(),Now).Matches("synthetic-mask","web","synthetic-web",new string('a',64),api,page), Is.False);

        [TestCase("different-mask", "web", "synthetic-web", 'a')]
        [TestCase("synthetic-mask", "quest", "synthetic-web", 'a')]
        [TestCase("synthetic-mask", "web", "synthetic-web", 'b')]
        [TestCase("synthetic-mask", "unknown", "synthetic-web", 'a')]
        public void PermissionIsBoundToThePieceVariantAndHash(string slug,string profile,string resource,char hash)
            => Assert.That(CompanionLocalUse.Parse(Document(),Now).Matches(slug,profile,resource,new string(hash,64),"http://localhost:5174/api/v1","http://localhost:5174/recorrido"),Is.False);

        [TestCase("approved", "false")]
        [TestCase("schema_version", "2")]
        [TestCase("schema_version", "1.1")]
        [TestCase("scope", "\"global-reuse\"")]
        [TestCase("approved_at", "\"2027-01-01T00:00:00Z\"")]
        [TestCase("web_sha256", "\"invalid\"")]
        [TestCase("responsible", "\"\"")]
        public void IncompleteOrUnapprovedPermissionsFailClosed(string field,string replacement)
        {
            string pattern = "\"" + field + "\"\\s*:\\s*(?:\"[^\"]*\"|true|false|[0-9.]+)";
            string value = Regex.Replace(Document(), pattern, "\"" + field + "\":" + replacement);
            Assert.That(CompanionLocalUse.Parse(value,Now),Is.Null);
        }

        [Test]
        public void MissingMalformedAndUnknownFieldsFailClosed()
        {
            Assert.That(CompanionLocalUse.Parse(null,Now),Is.Null);
            Assert.That(CompanionLocalUse.Parse("broken",Now),Is.Null);
            string value=Document().TrimEnd('}') + ",\"unexpected\":true}";
            Assert.That(CompanionLocalUse.Parse(value,Now),Is.Null);
        }

        [TestCase("es-CO")]
        [TestCase("en-US")]
        public void ApprovalDatesDoNotDependOnTheDeviceLanguage(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.That(CompanionLocalUse.Parse(Document(),Now),Is.Not.Null);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
