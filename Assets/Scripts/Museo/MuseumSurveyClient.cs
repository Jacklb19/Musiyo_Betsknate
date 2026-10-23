using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusiyoBetsknate.Museo;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The closing conversation's opinion questions from the museum API (owner decision D6): <c>GET /guide/survey</c> reads
    /// the published survey of the tour, and <c>POST /guide/survey/responses</c> stores the chosen options, only with the
    /// visitor's consent and its header. The request carries the questions, the options and the platform, nothing else.
    /// A survey that is not published, a service that does not answer or refuses (503) never blocks the visit: the survey
    /// is simply not offered, or the conversation closes with its closing words.
    /// </summary>
    public sealed class MuseumSurveyClient : IClosingSurveySource
    {
        public const string ConsentHeader = "X-Musiyo-Survey-Consent", Consent = "anonymous-v1";
        public const float DefaultTimeoutSeconds = 12;
        private readonly Func<string> apiBase;
        private readonly Func<double> clock;
        private readonly float timeoutSeconds;
        private readonly string platform;
        private UnityWebRequest active;
        private UnityWebRequestAsyncOperation operation;
        private double deadline;
        private bool loading;

        public ClosingSurveyLoad LoadState { get; private set; }
        public ClosingSurveyDefinition Survey { get; private set; }
        public ClosingSurveySubmit SubmitState { get; private set; }
        public event Action Changed;

        public MuseumSurveyClient(Func<string> apiBase, Func<double> clock, string platform = null, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
            this.platform = platform ?? Platform(Application.platform);
        }

        /// <summary>The platform the answers are stored with: quest, web or desktop.</summary>
        public static string Platform(RuntimePlatform runtime)
            => runtime == RuntimePlatform.Android ? "quest" : runtime == RuntimePlatform.WebGLPlayer ? "web" : "desktop";

        public void Load(string tourKey)
        {
            if (active != null || LoadState == ClosingSurveyLoad.Ready || string.IsNullOrWhiteSpace(tourKey)) return;
            string url;
            try { url = apiBase() + "/guide/survey?schema_version=1&tour_key=" + Uri.EscapeDataString(tourKey); }
            catch (ArgumentException) { Set(ClosingSurveyLoad.Failed); return; }
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/json");
            if (!Send(request)) { Set(ClosingSurveyLoad.Failed); return; }
            loading = true;
            Set(ClosingSurveyLoad.Loading);
        }

        public void Submit(ClosingSurveyDefinition survey, IReadOnlyList<ClosingSurveyAnswer> answers)
        {
            if (survey == null || answers == null || answers.Count == 0 || active != null) { SetSubmit(ClosingSurveySubmit.Failed); return; }
            var body = new GuideSurveyResponseContractV1
            {
                schema_version = 1, survey_key = survey.Key, version = survey.Version, platform = platform,
                answers = answers.Where(answer => !string.IsNullOrEmpty(answer.OptionId))
                    .Select(answer => new SurveyAnswerContractV1 { question_id = answer.QuestionId, option_id = answer.OptionId }).ToArray(),
            };
            if (body.answers.Length == 0) { SetSubmit(ClosingSurveySubmit.Failed); return; }
            string url;
            try { url = apiBase() + "/guide/survey/responses"; }
            catch (ArgumentException) { SetSubmit(ClosingSurveySubmit.Failed); return; }
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body))),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader(ConsentHeader, Consent);
            if (!Send(request)) { SetSubmit(ClosingSurveySubmit.Failed); return; }
            loading = false;
            SetSubmit(ClosingSurveySubmit.Sending);
        }

        public void Poll()
        {
            if (active == null) return;
            if (!operation.isDone)
            {
                if (clock() < deadline) return;
                Finish(UnityWebRequest.Result.ConnectionError, 0, null);
                return;
            }
            Finish(active.result, active.responseCode, active.downloadHandler?.text);
        }

        private void Finish(UnityWebRequest.Result result, long code, string text)
        {
            bool wasLoading = loading;
            Abandon();
            if (wasLoading)
            {
                var state = ClassifyLoad(result, code, text, out var survey);
                if (survey != null) Survey = survey;
                Set(state);
            }
            else SetSubmit(ClassifySubmit(result, code, text));
        }

        /// <summary>A published survey of this version is ready; no survey is "none"; anything else is a failure to retry later.</summary>
        public static ClosingSurveyLoad ClassifyLoad(UnityWebRequest.Result result, long code, string body, out ClosingSurveyDefinition survey)
        {
            survey = null;
            if (code == 404) return ClosingSurveyLoad.None;
            if (code != 200 || result != UnityWebRequest.Result.Success) return ClosingSurveyLoad.Failed;
            try { survey = Definition(ContractJsonV1.Parse<GuideSurveyContractV1>(body, ContractSchemasV1.GuideSurvey)); }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException) { return ClosingSurveyLoad.Failed; }
            return survey.Questions.Count > 0 ? ClosingSurveyLoad.Ready : ClosingSurveyLoad.None;
        }

        /// <summary>Stored only on the service's own confirmation; a refusal, a 503 or no answer leave nothing stored.</summary>
        public static ClosingSurveySubmit ClassifySubmit(UnityWebRequest.Result result, long code, string body)
        {
            if (code == 429) return ClosingSurveySubmit.RateLimited;
            if ((code != 201 && code != 200) || result != UnityWebRequest.Result.Success) return ClosingSurveySubmit.Failed;
            try
            {
                var stored = ContractJsonV1.Parse<GuideSurveyStoredContractV1>(body, ContractSchemasV1.GuideSurveyStored);
                return stored.status == "stored" ? ClosingSurveySubmit.Stored : ClosingSurveySubmit.Failed;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException) { return ClosingSurveySubmit.Failed; }
        }

        /// <summary>The published survey as the closing conversation reads it.</summary>
        public static ClosingSurveyDefinition Definition(GuideSurveyContractV1 contract)
        {
            var notice = contract.notice ?? new SurveyNoticeContractV1();
            return new ClosingSurveyDefinition
            {
                Key = contract.survey_key ?? "", Version = contract.version ?? "",
                Intro = notice.intro ?? "", AcceptLabel = notice.accept_label ?? "", LocalLabel = notice.local_label ?? "",
                DeclineLabel = notice.decline_label ?? "", MoreLabel = notice.more_label ?? "",
                NoticeTitle = notice.title ?? "", NoticeText = notice.text ?? "", Closing = contract.closing ?? "",
                Questions = (contract.questions ?? Array.Empty<SurveyQuestionContractV1>()).Where(question => question?.options != null && question.options.Length > 0)
                    .Select(question => new ClosingSurveyQuestion
                    {
                        Id = question.id, Text = question.text ?? "", Reply = question.reply ?? "", Optional = question.optional,
                        Options = question.options.Select(option => new ClosingSurveyOption { Id = option.id, Text = option.text ?? "" }).ToArray(),
                    }).ToArray(),
            };
        }

        private bool Send(UnityWebRequest request)
        {
            active = request;
            active.timeout = (int)Math.Ceiling(timeoutSeconds) + 5;
            deadline = clock() + timeoutSeconds;
            try { operation = active.SendWebRequest(); }
            catch (InvalidOperationException) { operation = null; }
            if (operation != null) return true;
            Abandon();
            return false;
        }

        private void Abandon()
        {
            if (active == null) return;
            if (operation != null && !operation.isDone) active.Abort();
            active.Dispose();
            active = null;
            operation = null;
        }

        private void Set(ClosingSurveyLoad state)
        {
            LoadState = state;
            Changed?.Invoke();
        }

        private void SetSubmit(ClosingSurveySubmit state)
        {
            SubmitState = state;
            Changed?.Invoke();
        }

        public void Dispose() => Abandon();
    }
}
