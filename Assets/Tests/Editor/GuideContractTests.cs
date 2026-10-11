using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public class GuideContractTests
    {
        private static string Example(string name)
            => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        [Test]
        public void QuestionsFollowTheSharedSchema()
        {
            Assert.IsTrue(GuideQuestionContractV1.TryParse(Example("guide_question.json"), out var example));
            Assert.AreEqual(1, example.schema_version);
            Assert.IsNull(example.element_slug);

            Assert.IsTrue(GuideQuestionContractV1.TryCreate("  ¿Qué es esta pieza?  ", null, out var json));
            Assert.IsTrue(GuideQuestionContractV1.TryParse(json, out var question));
            Assert.AreEqual("¿Qué es esta pieza?", question.question, "Surrounding spaces do not count toward the length.");
            Assert.IsNull(question.element_slug);
            Assert.IsTrue(GuideQuestionContractV1.TryCreate("¿Qué es esta pieza?", "synthetic-item", out json));
            Assert.IsTrue(GuideQuestionContractV1.TryParse(json, out question));
            Assert.AreEqual("synthetic-item", question.element_slug);

            Assert.IsFalse(GuideQuestionContractV1.TryCreate(null, null, out _));
            Assert.IsFalse(GuideQuestionContractV1.TryCreate("  ab  ", null, out _));
            Assert.IsFalse(GuideQuestionContractV1.TryCreate(new string('a', 501), null, out _));
            Assert.IsTrue(GuideQuestionContractV1.TryCreate(new string('a', 500), null, out _));
            Assert.IsFalse(GuideQuestionContractV1.TryCreate("¿Qué es esta pieza?", "two words", out _));
            Assert.IsFalse(GuideQuestionContractV1.TryParse(
                Example("guide_question.json").Replace("\"schema_version\": 1", "\"schema_version\": 2"), out _));
        }

        [Test]
        public void AnAnswerCitesElementsOfThePublishedTour()
        {
            Assert.IsTrue(GuideResponseContractV1.TryParse(Example("guide_answered.json"), out var response));
            Assert.AreEqual("answered", response.status);
            Assert.IsNotEmpty(response.answer);
            Assert.IsNull(response.reason);
            Assert.IsNotEmpty(response.citations);

            Assert.IsTrue(TourContractV1.TryParse(Example("tour_full.json"), out var tour, out var error), error);
            var published = new HashSet<string>();
            foreach (var room in tour.rooms)
                foreach (var point in room.points)
                    foreach (var element in point.elements) published.Add(element.slug);
            foreach (var citation in response.citations)
            {
                Assert.IsNotEmpty(citation.title);
                Assert.IsTrue(published.Contains(citation.element_slug), citation.element_slug);
            }
        }

        [TestCase("guide_abstained.json", "abstained", "insufficient_evidence")]
        [TestCase("guide_unavailable.json", "provider_unavailable", "provider_unavailable")]
        public void WithoutAnAnswerTheReasonIsStated(string example, string status, string reason)
        {
            Assert.IsTrue(GuideResponseContractV1.TryParse(Example(example), out var response));
            Assert.AreEqual(status, response.status);
            Assert.AreEqual(reason, response.reason);
            Assert.IsNull(response.answer);
            Assert.IsEmpty(response.citations);
        }

        [TestCase("guide_answered.json", "\"citations\": \\[[^\\]]*\\]", "\"citations\": []")]
        [TestCase("guide_answered.json", "\"answer\": \"[^\"]*\"", "\"answer\": null")]
        [TestCase("guide_answered.json", "\"answer\": \"[^\"]*\"", "\"answer\": \"   \"")]
        [TestCase("guide_answered.json", "\"reason\": null", "\"reason\": \"insufficient_evidence\"")]
        [TestCase("guide_answered.json", "\"reason\": null", "\"reason\": null, \"provider\": \"local\"")]
        [TestCase("guide_answered.json", "\"status\": \"answered\"", "\"status\": \"pending\"")]
        [TestCase("guide_answered.json", "\"schema_version\": 1", "\"schema_version\": 2")]
        [TestCase("guide_answered.json", ",\\s*\"title\": \"[^\"]*\"", "")]
        [TestCase("guide_answered.json", "\"synthetic-item\"", "\"synthetic item\"")]
        [TestCase("guide_abstained.json", "\"answer\": null", "\"answer\": \"Texto sin respaldo.\"")]
        [TestCase("guide_abstained.json", "\"reason\": \"insufficient_evidence\"", "\"reason\": null")]
        [TestCase("guide_abstained.json", "\"reason\": \"insufficient_evidence\"", "\"reason\": \"provider_unavailable\"")]
        [TestCase("guide_abstained.json", "\"citations\": \\[\\]",
            "\"citations\": [{\"element_slug\": \"synthetic-item\", \"title\": \"Ficha sintética de prueba\"}]")]
        [TestCase("guide_unavailable.json", "\"reason\": \"provider_unavailable\"", "\"reason\": \"insufficient_evidence\"")]
        [TestCase("guide_unavailable.json", "\"answer\": null", "\"answer\": \"Texto sin respaldo.\"")]
        public void MixedOutcomesAreRejected(string example, string pattern, string replacement)
        {
            var original = Example(example);
            var changed = Regex.Replace(original, pattern, replacement);
            Assert.AreNotEqual(original, changed, "The example no longer contains the part this case changes.");
            Assert.IsFalse(GuideResponseContractV1.TryParse(changed, out _));
        }
    }
}
