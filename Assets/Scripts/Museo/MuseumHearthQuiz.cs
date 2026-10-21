using System;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The conversation about the visit by the hearth: the published questions of the tour, one at a time in their
    /// published order, each answered by choosing an option and followed by the cited explanation. There is no score and
    /// no time limit; a question can be passed and the conversation left at any moment. Walking away from the guide or
    /// closing the conversation abandons it and forgets every answer.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(TourRuntime))]
    public sealed class MuseumHearthQuiz : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourRuntime runtime;
        private GuideQuizClient client;
        private bool connected;
        private string chosen;

        public GuideQuizClient Client { get { Connect(); return client; } }
        /// <summary>Questions are published for this tour and the guide's screen can offer the conversation.</summary>
        public bool Available => Client.State == GuideQuizState.Ready && client.Questions.Length > 0;
        /// <summary>The visitor is answering questions.</summary>
        public bool Active { get; private set; }
        /// <summary>The visitor ended the conversation or went through every question; a thank-you stays until leaving.</summary>
        public bool Done { get; private set; }
        public int Index { get; private set; }
        public int Count => Client.Questions.Length;
        public QuizQuestionContractV1 Current => Active && Index >= 0 && Index < Count ? client.Questions[Index] : null;
        /// <summary>The option chosen for the current question, once sent.</summary>
        public string Chosen => chosen;
        /// <summary>The explanation of the current question, once it arrived.</summary>
        public GuideQuizAnswerResultContractV1 Explanation
            => Current != null && client.AnswerState == GuideQuizAnswerState.Answered && client.Asked == Current ? client.Result : null;
        public event Action Changed;

        /// <summary>Line shown at the top of the guide's screen while the conversation goes on.</summary>
        public string Message
        {
            get
            {
                if (Done) return MuseumInterfaceText.Get("quiz_done");
                if (!Active) return "";
                string progress = MuseumInterfaceText.Format("quiz_progress", Index + 1, Count);
                switch (client.AnswerState)
                {
                    case GuideQuizAnswerState.Sending: return progress + " · " + MuseumInterfaceText.Get("quiz_sending");
                    case GuideQuizAnswerState.Answered:
                        return Explanation == null ? progress : progress + " · " + MuseumInterfaceText.Get(Explanation.correct ? "quiz_correct" : "quiz_incorrect");
                    case GuideQuizAnswerState.RateLimited:
                        return progress + " · " + MuseumInterfaceText.Format("quiz_rate_limited", client.RetryWaitRemaining);
                    case GuideQuizAnswerState.Unavailable: return progress + " · " + MuseumInterfaceText.Get("quiz_unavailable");
                    case GuideQuizAnswerState.Failed:
                    case GuideQuizAnswerState.Incompatible: return progress + " · " + MuseumInterfaceText.Get("quiz_failure");
                    default: return progress;
                }
            }
        }

        private void OnEnable() => Connect();

        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            interaction.Changed -= OnInteractionChanged;
            runtime.Changed -= OnTourChanged;
            client.Changed -= Notify;
            client.Dispose();
            client = null;
        }

        private void Connect()
        {
            if (connected) return;
            connected = true;
            interaction = GetComponent<MuseumInteraction>();
            runtime = GetComponent<TourRuntime>();
            client = new GuideQuizClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            interaction.Changed += OnInteractionChanged;
            runtime.Changed += OnTourChanged;
            client.Changed += Notify;
        }

        private string ApiBase()
        {
            var loader = GetComponent<TourLoader>();
            return loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);
        }

        private void Update() => client?.Poll();
        private void Notify() => Changed?.Invoke();

        /// <summary>A new tour may publish other questions: they are read again the next time the guide is visited.</summary>
        private void OnTourChanged()
        {
            Leave();
            client.Clear();
        }

        /// <summary>Opening the guide reads the questions when they are not there yet; leaving it abandons the conversation.</summary>
        private void OnInteractionChanged()
        {
            if (interaction.State == InteractionState.GuideQuestion)
            {
                if (runtime.Contract != null && (client.State == GuideQuizState.Idle || client.State == GuideQuizState.Failed
                    || client.State == GuideQuizState.Incompatible || client.State == GuideQuizState.RateLimited && client.RetryWaitRemaining == 0))
                    client.Load(runtime.TourKey);
                return;
            }
            // Pausing keeps the place; anything else leaves the guide.
            if (interaction.State == InteractionState.Paused) return;
            if (Active || Done) Leave();
        }

        /// <summary>Begins with the first question.</summary>
        public bool Begin()
        {
            if (!Available || interaction.State != InteractionState.GuideQuestion) return false;
            Active = true;
            Done = false;
            Index = 0;
            chosen = null;
            client.ForgetAnswer();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Sends the option the visitor chose for the current question; one choice per question.</summary>
        public bool Choose(int option)
        {
            var question = Current;
            // After a failed or held-back answer the visitor may choose again; otherwise the first choice stands.
            bool again = client.AnswerState == GuideQuizAnswerState.Failed || client.AnswerState == GuideQuizAnswerState.RateLimited;
            if (question == null || (chosen != null && !again) || client.RetryWaitRemaining > 0
                || option < 0 || option >= question.options.Length) return false;
            if (!client.Answer(question, question.options[option].id)) return false;
            chosen = question.options[option].id;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Goes on to the next question, answered or passed; after the last one the conversation ends.</summary>
        public bool Next()
        {
            if (!Active) return false;
            client.ForgetAnswer();
            chosen = null;
            Index++;
            if (Index >= Count) { Active = false; Done = true; }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Ends the conversation now, keeping no answer.</summary>
        public void Finish()
        {
            if (!Active) return;
            client.Cancel();
            client.ForgetAnswer();
            Active = false;
            Done = true;
            chosen = null;
            Changed?.Invoke();
        }

        /// <summary>Reads the questions again after a failure or an answer to a question that is no longer published.</summary>
        public void Reload()
        {
            Leave();
            client.Load(runtime.TourKey);
        }

        private void Leave()
        {
            client.Cancel();
            client.ForgetAnswer();
            Active = Done = false;
            Index = 0;
            chosen = null;
            Changed?.Invoke();
        }
    }
}
