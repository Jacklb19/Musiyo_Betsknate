using System;
using System.Text;
using MusiyoBetsknate.Museo;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    public enum GuideQuizState
    {
        Idle,
        Loading,
        /// <summary>Questions arrived; there may be none.</summary>
        Ready,
        /// <summary>HTTP 429: wait for the announced delay before asking again.</summary>
        RateLimited,
        /// <summary>No answer, an error status or a timeout.</summary>
        Failed,
        /// <summary>HTTP 200 whose body does not follow the shared contract.</summary>
        Incompatible
    }

    public enum GuideQuizAnswerState
    {
        Idle,
        Sending,
        /// <summary>The service explained the chosen option, with its citation and source.</summary>
        Answered,
        RateLimited,
        /// <summary>The question is no longer published (HTTP 404 or 422); the questions must be read again.</summary>
        Unavailable,
        Failed,
        Incompatible
    }

    /// <summary>
    /// The hearth questions of the tour: GET {api}/guide/quiz for the whole tour (no room, since the questions have no
    /// place of their own at the hearth) and POST {api}/guide/quiz/answers for each chosen option. One request at a time;
    /// a cancelled or superseded request never reaches the visitor. Nothing is scored or kept. Its owner calls
    /// <see cref="Poll"/> every frame.
    /// </summary>
    public sealed class GuideQuizClient : IDisposable
    {
        public const float DefaultTimeoutSeconds = 15;
        private readonly Func<string> apiBase;
        private readonly Func<double> clock;
        private readonly float timeoutSeconds;
        private UnityWebRequest active;
        private UnityWebRequestAsyncOperation operation;
        private bool answering;
        private double deadline;
        private double retryAt;
        private QuizQuestionContractV1 asked;

        public GuideQuizState State { get; private set; }
        public QuizQuestionContractV1[] Questions { get; private set; } = Array.Empty<QuizQuestionContractV1>();
        public GuideQuizAnswerState AnswerState { get; private set; }
        /// <summary>Explanation of the last answered question; null otherwise.</summary>
        public GuideQuizAnswerResultContractV1 Result { get; private set; }
        /// <summary>Question the last answer was sent for.</summary>
        public QuizQuestionContractV1 Asked => asked;
        public int RetryWaitRemaining => State == GuideQuizState.RateLimited || AnswerState == GuideQuizAnswerState.RateLimited
            ? Math.Max(0, (int)Math.Ceiling(retryAt - clock())) : 0;
        public bool Busy => active != null;
        public event Action Changed;

        public GuideQuizClient(Func<string> apiBase, Func<double> clock, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
        }

        /// <summary>Reads the questions of the tour again, abandoning anything in flight.</summary>
        public void Load(string tourKey)
        {
            Abandon();
            ForgetAnswer();
            string url;
            try { url = apiBase() + "/guide/quiz?tour_key=" + Uri.EscapeDataString(tourKey ?? "") + "&schema_version=1"; }
            catch (ArgumentException) { Set(GuideQuizState.Failed); return; }
            answering = false;
            if (!Send(UnityWebRequest.Get(url))) { Set(GuideQuizState.Failed); return; }
            Set(GuideQuizState.Loading);
        }

        /// <summary>Sends the chosen option of a loaded question. Returns false, without sending, when either is unknown.</summary>
        public bool Answer(QuizQuestionContractV1 question, string optionId)
        {
            if (State != GuideQuizState.Ready || question == null || Array.IndexOf(Questions, question) < 0
                || Array.FindIndex(question.options ?? Array.Empty<QuizOptionContractV1>(), option => option.id == optionId) < 0
                || !GuideQuizAnswerContractV1.TryCreate(question.id, optionId, out var body)) return false;
            Abandon();
            ForgetAnswer();
            asked = question;
            string url;
            try { url = apiBase() + "/guide/quiz/answers"; }
            catch (ArgumentException) { SetAnswer(GuideQuizAnswerState.Failed); return true; }
            answering = true;
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (!Send(request)) { SetAnswer(GuideQuizAnswerState.Failed); return true; }
            SetAnswer(GuideQuizAnswerState.Sending);
            return true;
        }

        /// <summary>Forgets the explanation shown, as when moving to the next question.</summary>
        public void ForgetAnswer()
        {
            if (answering) Abandon();
            asked = null;
            Result = null;
            if (AnswerState != GuideQuizAnswerState.Idle) SetAnswer(GuideQuizAnswerState.Idle);
        }

        /// <summary>Forgets the questions and any answer, as when another tour is loaded.</summary>
        public void Clear()
        {
            Abandon();
            asked = null;
            Result = null;
            Questions = Array.Empty<QuizQuestionContractV1>();
            AnswerState = GuideQuizAnswerState.Idle;
            Set(GuideQuizState.Idle);
        }

        /// <summary>Abandons whatever is in flight; loaded questions stay.</summary>
        public void Cancel()
        {
            if (active == null) return;
            bool wasAnswer = answering;
            Abandon();
            if (wasAnswer) SetAnswer(GuideQuizAnswerState.Idle);
            else Set(Questions.Length > 0 ? GuideQuizState.Ready : GuideQuizState.Idle);
        }

        public void Poll()
        {
            if (active == null) return;
            if (!operation.isDone)
            {
                if (clock() < deadline) return;
                bool wasAnswer = answering;
                Abandon();
                if (wasAnswer) SetAnswer(GuideQuizAnswerState.Failed); else Set(GuideQuizState.Failed);
                return;
            }
            var result = active.result;
            var code = active.responseCode;
            var text = active.downloadHandler?.text;
            var retryHeader = active.GetResponseHeader("Retry-After");
            bool answer = answering;
            Abandon();
            if (code == 429)
            {
                retryAt = clock() + GuideQuestionClient.ParseRetryAfter(retryHeader, DateTimeOffset.UtcNow);
                if (answer) SetAnswer(GuideQuizAnswerState.RateLimited); else Set(GuideQuizState.RateLimited);
                return;
            }
            if (answer)
            {
                var answerState = ClassifyAnswer(result, code, text, asked, out var explained);
                Result = explained;
                SetAnswer(answerState);
                return;
            }
            var quizState = ClassifyQuiz(result, code, text, out var questions);
            if (questions != null) Questions = questions;
            Set(quizState);
        }

        public static GuideQuizState ClassifyQuiz(UnityWebRequest.Result result, long code, string body, out QuizQuestionContractV1[] questions)
        {
            questions = null;
            if (code != 200 || result != UnityWebRequest.Result.Success) return GuideQuizState.Failed;
            if (!GuideQuizContractV1.TryParse(body, out var quiz)) return GuideQuizState.Incompatible;
            questions = quiz.questions ?? Array.Empty<QuizQuestionContractV1>();
            return GuideQuizState.Ready;
        }

        public static GuideQuizAnswerState ClassifyAnswer(UnityWebRequest.Result result, long code, string body,
            QuizQuestionContractV1 question, out GuideQuizAnswerResultContractV1 explained)
        {
            explained = null;
            if (code == 404 || code == 422) return GuideQuizAnswerState.Unavailable;
            if (code != 200 || result != UnityWebRequest.Result.Success) return GuideQuizAnswerState.Failed;
            if (!GuideQuizAnswerResultContractV1.TryParse(body, out var parsed) || !parsed.Explains(question))
                return GuideQuizAnswerState.Incompatible;
            explained = parsed;
            return GuideQuizAnswerState.Answered;
        }

        private bool Send(UnityWebRequest request)
        {
            active = request;
            active.SetRequestHeader("Accept", "application/json");
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

        private void Set(GuideQuizState state)
        {
            State = state;
            Changed?.Invoke();
        }

        private void SetAnswer(GuideQuizAnswerState state)
        {
            AnswerState = state;
            Changed?.Invoke();
        }

        public void Dispose() => Abandon();
    }
}
