using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The closing conversation's opinion questions against the published contract (owner decision D6): the survey read
    /// from the API, sent only with consent, and an API that refuses or does not answer never blocking the end of the visit.
    /// </summary>
    public sealed class MuseumClosingSurveyTests
    {
        private static string Example(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts/examples", name));

        /// <summary>A survey source that keeps what it is sent and answers as it is told to.</summary>
        private sealed class FakeSource : IClosingSurveySource
        {
            public readonly List<IReadOnlyList<ClosingSurveyAnswer>> Sent = new List<IReadOnlyList<ClosingSurveyAnswer>>();
            public ClosingSurveySubmit Outcome = ClosingSurveySubmit.Stored;
            public ClosingSurveyLoad LoadState { get; set; } = ClosingSurveyLoad.Ready;
            public ClosingSurveyDefinition Survey { get; set; }
            public ClosingSurveySubmit SubmitState { get; private set; }
            public event Action Changed;
            public void Load(string tourKey) { }
            public void Submit(ClosingSurveyDefinition survey, IReadOnlyList<ClosingSurveyAnswer> answers)
            {
                Sent.Add(answers);
                SubmitState = Outcome;
                Changed?.Invoke();
            }
            public void Poll() { }
            public void Dispose() { }
        }

        private GameObject root;
        private MuseumClosingSurvey survey;
        private FakeSource source;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SurveyTest");
            var interaction = root.AddComponent<MuseumInteraction>();
            root.AddComponent<TourRuntime>();
            survey = root.AddComponent<MuseumClosingSurvey>();
            typeof(MuseumClosingSurvey).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(survey, null);
            // The conversation with the hearth guide is open.
            typeof(MuseumInteraction).GetProperty(nameof(MuseumInteraction.State)).SetValue(interaction, InteractionState.GuideQuestion);
            Assert.That(MuseumSurveyClient.ClassifyLoad(UnityWebRequest.Result.Success, 200, Example("guide_survey.json"), out var published),
                Is.EqualTo(ClosingSurveyLoad.Ready));
            source = new FakeSource { Survey = published };
            survey.Source = source;
        }

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

        [Test]
        public void ThePublishedSurveyIsReadWithItsNoticeRepliesAndClosing()
        {
            var published = source.Survey;
            Assert.That(published.Key, Is.EqualTo("synthetic-closing"));
            Assert.That(published.Intro, Is.EqualTo("¿Me cuentas cómo te fue?"));
            Assert.That(new[] { published.AcceptLabel, published.LocalLabel, published.DeclineLabel, published.MoreLabel },
                Is.EqualTo(new[] { "Claro, te cuento", "Te cuento, pero no lo guardes", "Ahora no", "¿Para qué es esto?" }));
            Assert.That(published.Closing, Is.EqualTo("Gracias por la charla."));
            Assert.That(published.Questions.Select(question => question.Id), Is.EqualTo(new[] { "liked", "noticed" }));
            Assert.That(published.Questions[0].Reply, Is.EqualTo("Gracias por contarme."));
            Assert.That(published.Questions[0].Optional, Is.False);
            Assert.That(published.Questions[1].Optional, Is.True);
            Assert.That(published.Questions[1].Options.Select(option => option.Id), Is.EqualTo(new[] { "masks", "music", "other" }));
        }

        [TestCase(404, ClosingSurveyLoad.None)]
        [TestCase(503, ClosingSurveyLoad.Failed)]
        [TestCase(429, ClosingSurveyLoad.Failed)]
        [TestCase(0, ClosingSurveyLoad.Failed)]
        public void ASurveyThatIsNotThereIsNeverOffered(int code, ClosingSurveyLoad expected)
        {
            var result = code == 0 ? UnityWebRequest.Result.ConnectionError : UnityWebRequest.Result.ProtocolError;
            Assert.That(MuseumSurveyClient.ClassifyLoad(result, code, "{}", out var none), Is.EqualTo(expected));
            Assert.That(none, Is.Null);
            Assert.That(MuseumSurveyClient.ClassifyLoad(UnityWebRequest.Result.Success, 200, "{\"schema_version\":1}", out _), Is.EqualTo(ClosingSurveyLoad.Failed),
                "A body that is not the contract is not a survey.");
        }

        [Test]
        public void OnlyTheServicesOwnConfirmationCountsAsStored()
        {
            Assert.That(MuseumSurveyClient.ClassifySubmit(UnityWebRequest.Result.Success, 201, Example("guide_survey_stored.json")), Is.EqualTo(ClosingSurveySubmit.Stored));
            Assert.That(MuseumSurveyClient.ClassifySubmit(UnityWebRequest.Result.ProtocolError, 503, "{}"), Is.EqualTo(ClosingSurveySubmit.Failed));
            Assert.That(MuseumSurveyClient.ClassifySubmit(UnityWebRequest.Result.ProtocolError, 403, "{}"), Is.EqualTo(ClosingSurveySubmit.Failed));
            Assert.That(MuseumSurveyClient.ClassifySubmit(UnityWebRequest.Result.ProtocolError, 429, "{}"), Is.EqualTo(ClosingSurveySubmit.RateLimited));
            Assert.That(MuseumSurveyClient.ClassifySubmit(UnityWebRequest.Result.ConnectionError, 0, null), Is.EqualTo(ClosingSurveySubmit.Failed));
            Assert.That(MuseumSurveyClient.Platform(RuntimePlatform.Android), Is.EqualTo("quest"));
            Assert.That(MuseumSurveyClient.Platform(RuntimePlatform.WebGLPlayer), Is.EqualTo("web"));
            Assert.That(MuseumSurveyClient.Platform(RuntimePlatform.WindowsPlayer), Is.EqualTo("desktop"));
        }

        [Test]
        public void AcceptedAnswersAreSentAndTheGuideRepliesThenCloses()
        {
            Assert.That(survey.Offer(), Is.True);
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Notice));
            survey.ToggleMore();
            Assert.That(survey.MoreShown, Is.True, "«¿Para qué es esto?» shows why the questions are asked.");
            Assert.That(survey.AcceptAndAnswer(), Is.True);
            Assert.That(survey.Skip(), Is.False, "A question that is not optional cannot be passed.");
            Assert.That(survey.Choose(0), Is.True);
            Assert.That(survey.Reply, Is.EqualTo("Gracias por contarme."), "The guide replies before the next question.");
            Assert.That(survey.Current.Id, Is.EqualTo("noticed"));
            Assert.That(survey.Skip(), Is.True, "An optional question can be passed.");
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Done));
            Assert.That(source.Sent, Has.Count.EqualTo(1));
            Assert.That(source.Sent[0].Select(answer => answer.QuestionId + ":" + answer.OptionId), Is.EqualTo(new[] { "liked:a-lot" }),
                "Only the question and the chosen option; a passed question is not sent.");
            Assert.That(survey.Message, Is.EqualTo("Gracias por la charla."), "The conversation ends on the service's closing words.");
        }

        [Test]
        public void WithoutConsentNothingIsSentAndARefusalNeverBlocksTheEnd()
        {
            // Answering without storing: nothing leaves the museum.
            survey.Offer();
            Assert.That(survey.AnswerWithoutStoring(), Is.True);
            survey.Choose(1);
            survey.Choose(2);
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Done));
            Assert.That(source.Sent, Is.Empty);
            Assert.That(survey.Message, Is.EqualTo("Gracias por la charla."));
            // Not now: nothing either.
            survey.Offer();
            survey.Decline();
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Declined));
            Assert.That(source.Sent, Is.Empty);
            // With consent but a service that cannot store (503, or no answer): the visit closes all the same, with a note.
            source.Outcome = ClosingSurveySubmit.Failed;
            survey.Offer();
            survey.AcceptAndAnswer();
            survey.Choose(0);
            survey.Finish();
            Assert.That(source.Sent, Has.Count.EqualTo(1));
            Assert.That(survey.Stage, Is.EqualTo(ClosingSurveyStage.Done), "A refusal never leaves the conversation waiting.");
            Assert.That(survey.Message, Does.StartWith("Gracias por la charla.").And.Contain(MuseumInterfaceText.Get("survey_not_stored_note")));
        }

        [Test]
        public void TheRealClientIsAttachedToTheGuideAndNothingAsksBeforeTheTour()
        {
            var client = new MuseumSurveyClient(() => "http://127.0.0.1:9/api/v1", () => 0, "quest");
            client.Load("");
            Assert.That(client.LoadState, Is.EqualTo(ClosingSurveyLoad.Idle), "Without a tour key nothing is asked.");
            client.Submit(source.Survey, Array.Empty<ClosingSurveyAnswer>());
            Assert.That(client.SubmitState, Is.EqualTo(ClosingSurveySubmit.Failed), "No answers, nothing sent.");
            client.Dispose();
        }
    }
}
