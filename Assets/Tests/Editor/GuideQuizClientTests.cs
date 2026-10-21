using System.IO;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The hearth questions and their explanations against the canonical examples of the shared contract.</summary>
    public sealed class GuideQuizClientTests
    {
        private static string Example(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        [Test]
        public void PublishedQuestionsArriveInOrderAndAnEmptySetIsStillAnAnswer()
        {
            Assert.That(GuideQuizClient.ClassifyQuiz(UnityWebRequest.Result.Success, 200, Example("guide_quiz.json"), out var questions),
                Is.EqualTo(GuideQuizState.Ready));
            Assert.That(questions.Length, Is.EqualTo(1));
            Assert.That(questions[0].options.Length, Is.EqualTo(3));
            Assert.That(GuideQuizClient.ClassifyQuiz(UnityWebRequest.Result.Success, 200, Example("guide_quiz_empty.json"), out questions),
                Is.EqualTo(GuideQuizState.Ready));
            Assert.That(questions, Is.Empty);
            Assert.That(GuideQuizClient.ClassifyQuiz(UnityWebRequest.Result.Success, 200, "{\"schema_version\": 1}", out _),
                Is.EqualTo(GuideQuizState.Incompatible));
            Assert.That(GuideQuizClient.ClassifyQuiz(UnityWebRequest.Result.ProtocolError, 503, "", out _), Is.EqualTo(GuideQuizState.Failed));
        }

        [Test]
        public void AnExplanationMustBelongToTheQuestionAsked()
        {
            GuideQuizClient.ClassifyQuiz(UnityWebRequest.Result.Success, 200, Example("guide_quiz.json"), out var questions);
            var result = Example("guide_quiz_answer_result.json");
            Assert.That(GuideQuizClient.ClassifyAnswer(UnityWebRequest.Result.Success, 200, result, questions[0], out var explained),
                Is.EqualTo(GuideQuizAnswerState.Answered));
            Assert.That(explained.citation.quote, Is.EqualTo("Texto sintético de prueba"));
            var other = new QuizQuestionContractV1 { id = "synthetic-quiz-1", element_slug = "another", source_id = "synthetic-source" };
            Assert.That(GuideQuizClient.ClassifyAnswer(UnityWebRequest.Result.Success, 200, result, other, out _),
                Is.EqualTo(GuideQuizAnswerState.Incompatible), "Evidence from another record is not shown as the explanation.");
            Assert.That(GuideQuizClient.ClassifyAnswer(UnityWebRequest.Result.ProtocolError, 404, "", questions[0], out _),
                Is.EqualTo(GuideQuizAnswerState.Unavailable));
            Assert.That(GuideQuizClient.ClassifyAnswer(UnityWebRequest.Result.ProtocolError, 500, "", questions[0], out _),
                Is.EqualTo(GuideQuizAnswerState.Failed));
        }

        [Test]
        public void AnAnswerBodyFollowsTheContractAndUnknownChoicesAreNotSent()
        {
            Assert.That(GuideQuizAnswerContractV1.TryCreate("synthetic-quiz-1", "a", out var json), Is.True);
            Assert.That(json, Does.Contain("\"option_id\":\"a\""));
            Assert.That(GuideQuizAnswerContractV1.TryCreate("synthetic-quiz-1", "", out _), Is.False);
            var client = new GuideQuizClient(() => "http://127.0.0.1:9/api/v1", () => 0);
            Assert.That(client.Answer(new QuizQuestionContractV1 { id = "x", options = new QuizOptionContractV1[0] }, "a"), Is.False,
                "Nothing is sent before the questions are loaded.");
            client.Dispose();
        }
    }
}
