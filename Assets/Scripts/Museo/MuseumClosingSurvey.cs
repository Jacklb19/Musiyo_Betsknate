using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>One opinion question as the survey service publishes it: options to choose, or a scale of numbers.</summary>
    public sealed class ClosingSurveyQuestion
    {
        public string Id;
        public string Text;
        /// <summary>Options in published order; for a scale, one per value from its lowest to its highest.</summary>
        public IReadOnlyList<ClosingSurveyOption> Options = Array.Empty<ClosingSurveyOption>();
        /// <summary>Words under the lowest and highest values of a scale; empty for choices.</summary>
        public string LowLabel = "", HighLabel = "";
        public bool Scale;
        /// <summary>What the guide says once the question is answered, before the next one; empty for none.</summary>
        public string Reply = "";
        /// <summary>The visitor may pass this question.</summary>
        public bool Optional = true;
    }

    public sealed class ClosingSurveyOption
    {
        public string Id;
        public string Text;
        /// <summary>The number of a scale value; null for a choice.</summary>
        public int? Value;
    }

    /// <summary>The published survey: its notice, worded and approved outside Unity, its questions and its closing words.</summary>
    public sealed class ClosingSurveyDefinition
    {
        public string Key = "", Version = "";
        /// <summary>The guide's invitation, and the words of its three choices and of the explanation; empty uses the museum's own.</summary>
        public string Intro = "", AcceptLabel = "", LocalLabel = "", DeclineLabel = "", MoreLabel = "";
        public string NoticeTitle = "", NoticeText = "";
        /// <summary>What the guide says when the conversation ends, stored or not.</summary>
        public string Closing = "";
        public IReadOnlyList<ClosingSurveyQuestion> Questions = Array.Empty<ClosingSurveyQuestion>();
    }

    public sealed class ClosingSurveyAnswer
    {
        public string QuestionId;
        public string OptionId;
        public int? Value;
    }

    public enum ClosingSurveyLoad { Idle, Loading, Ready, None, Failed }
    public enum ClosingSurveySubmit { Idle, Sending, Stored, RateLimited, Failed }

    /// <summary>
    /// Where the survey comes from and goes to: the museum API (<see cref="MuseumSurveyClient"/>). Without a published
    /// survey the conversation by the hearth simply has no opinion questions.
    /// </summary>
    public interface IClosingSurveySource : IDisposable
    {
        ClosingSurveyLoad LoadState { get; }
        ClosingSurveyDefinition Survey { get; }
        ClosingSurveySubmit SubmitState { get; }
        void Load(string tourKey);
        /// <summary>Sends the answers with the visitor's consent; called only when the visitor accepted to store them.</summary>
        void Submit(ClosingSurveyDefinition survey, IReadOnlyList<ClosingSurveyAnswer> answers);
        void Poll();
        event Action Changed;
    }

    public enum ClosingSurveyStage
    {
        /// <summary>Not offered: no survey published, or the visitor has not finished the conversation about the visit.</summary>
        Off,
        /// <summary>The notice and the three choices: answer and store anonymously, answer without storing, or not now.</summary>
        Notice,
        Answering,
        /// <summary>The answers are on their way to the service, with consent.</summary>
        Sending,
        /// <summary>Thank you; says whether the answers were stored, kept on this device only or could not be sent.</summary>
        Done,
        Declined
    }

    /// <summary>
    /// The opinion questions after the conversation about the visit, for the thesis (D6). The notice comes before the
    /// first question and offers to answer and store the answers anonymously, to answer without storing them, or not to
    /// answer. Stored answers carry nothing but the question and the choice: no name, no voice, no device or platform. If
    /// the visitor does not accept, nothing leaves the museum; the answers are forgotten when the conversation ends.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(TourRuntime))]
    public sealed class MuseumClosingSurvey : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourRuntime runtime;
        private MuseumHearthQuiz quiz;
        private IClosingSurveySource source;
        private readonly List<ClosingSurveyAnswer> answers = new List<ClosingSurveyAnswer>();
        private bool offeredThisVisit;

        public ClosingSurveyStage Stage { get; private set; }
        /// <summary>The visitor accepted to store the answers anonymously.</summary>
        public bool Storing { get; private set; }
        public int Index { get; private set; }
        public ClosingSurveyDefinition Survey => source?.Survey;
        public ClosingSurveyQuestion Current => Stage == ClosingSurveyStage.Answering && Survey != null && Index < Survey.Questions.Count
            ? Survey.Questions[Index] : null;
        public IReadOnlyList<ClosingSurveyAnswer> Answers => answers;
        public ClosingSurveySubmit SubmitState => source?.SubmitState ?? ClosingSurveySubmit.Idle;
        /// <summary>The guide's reply to the question just answered, shown above the next one.</summary>
        public string Reply { get; private set; } = "";
        /// <summary>The notice shows why the questions are asked (its "more" explanation).</summary>
        public bool MoreShown { get; private set; }
        /// <summary>A survey is published and can be offered.</summary>
        public bool Available => source != null && source.LoadState == ClosingSurveyLoad.Ready && Survey != null && Survey.Questions.Count > 0;
        public event Action Changed;

        /// <summary>Attaches the survey service; with none, nothing about the survey is ever shown.</summary>
        public IClosingSurveySource Source
        {
            get => source;
            set
            {
                if (source != null) { source.Changed -= OnSourceChanged; source.Dispose(); }
                source = value;
                if (source != null) source.Changed += OnSourceChanged;
                Changed?.Invoke();
            }
        }

        private void Awake()
        {
            interaction = GetComponent<MuseumInteraction>();
            runtime = GetComponent<TourRuntime>();
        }

        private void OnEnable() => interaction.Changed += OnInteractionChanged;

        private void OnDisable()
        {
            interaction.Changed -= OnInteractionChanged;
            if (quiz != null) quiz.Changed -= OnQuizChanged;
        }

        /// <summary>Leaving the scene releases the service quietly: the screens that listen are going too.</summary>
        private void OnDestroy()
        {
            Changed = null;
            if (source != null) { source.Changed -= OnSourceChanged; source.Dispose(); source = null; }
        }

        private void Update()
        {
            // The conversation about the visit is added with the guide's screen, after this component.
            if (quiz == null && TryGetComponent(out quiz)) quiz.Changed += OnQuizChanged;
            source?.Poll();
        }

        private void OnSourceChanged()
        {
            if (Stage == ClosingSurveyStage.Sending && source.SubmitState != ClosingSurveySubmit.Sending) Set(ClosingSurveyStage.Done);
            else Changed?.Invoke();
        }

        /// <summary>Opening the guide reads the survey when it is not there yet; leaving the guide forgets every answer.</summary>
        private void OnInteractionChanged()
        {
            if (interaction.State == InteractionState.GuideQuestion)
            {
                if (source != null && (source.LoadState == ClosingSurveyLoad.Idle || source.LoadState == ClosingSurveyLoad.Failed))
                    source.Load(runtime.TourKey);
                return;
            }
            if (interaction.State == InteractionState.Paused) return;
            // A submission already on its way finishes; anything not sent is forgotten.
            if (Stage != ClosingSurveyStage.Sending) Leave();
        }

        /// <summary>When the conversation about the visit ends, the opinion questions are offered once per visit.</summary>
        private void OnQuizChanged()
        {
            if (quiz.Done && !offeredThisVisit && Available && interaction.State == InteractionState.GuideQuestion) Offer();
        }

        /// <summary>Shows the notice; nothing is asked or sent before the visitor chooses.</summary>
        public bool Offer()
        {
            if (!Available || interaction.State != InteractionState.GuideQuestion) return false;
            offeredThisVisit = true;
            answers.Clear();
            Index = 0;
            Storing = false;
            Reply = "";
            MoreShown = false;
            Set(ClosingSurveyStage.Notice);
            return true;
        }

        /// <summary>Shows or hides, under the invitation, why the questions are asked.</summary>
        public void ToggleMore()
        {
            if (Stage != ClosingSurveyStage.Notice) return;
            MoreShown = !MoreShown;
            Changed?.Invoke();
        }

        /// <summary>The visitor answers and accepts that the answers are stored anonymously.</summary>
        public bool AcceptAndAnswer() => Begin(true);

        /// <summary>The visitor answers without storing anything; the answers stay on this screen only.</summary>
        public bool AnswerWithoutStoring() => Begin(false);

        public void Decline()
        {
            if (Stage == ClosingSurveyStage.Notice) Set(ClosingSurveyStage.Declined);
        }

        private bool Begin(bool storing)
        {
            if (Stage != ClosingSurveyStage.Notice) return false;
            Storing = storing;
            Index = 0;
            answers.Clear();
            Set(ClosingSurveyStage.Answering);
            return true;
        }

        /// <summary>Records the chosen option of the current question and goes on; after the last one the survey ends.</summary>
        public bool Choose(int option)
        {
            var question = Current;
            if (question == null || option < 0 || option >= question.Options.Count) return false;
            var chosen = question.Options[option];
            answers.RemoveAll(answer => answer.QuestionId == question.Id);
            answers.Add(new ClosingSurveyAnswer { QuestionId = question.Id, OptionId = chosen.Value.HasValue ? null : chosen.Id, Value = chosen.Value });
            Reply = question.Reply ?? "";
            Advance();
            return true;
        }

        /// <summary>Leaves the current question unanswered and goes on; only a question published as optional can be passed.</summary>
        public bool Skip()
        {
            if (Current == null || !Current.Optional) return false;
            Reply = "";
            Advance();
            return true;
        }

        /// <summary>Ends the survey now; with consent, what was answered so far is sent.</summary>
        public void Finish()
        {
            if (Stage != ClosingSurveyStage.Answering) return;
            Complete();
        }

        private void Advance()
        {
            Index++;
            if (Index >= Survey.Questions.Count) Complete();
            else Changed?.Invoke();
        }

        private void Complete()
        {
            if (Storing && answers.Count > 0)
            {
                source.Submit(Survey, answers.ToList());
                Set(source.SubmitState == ClosingSurveySubmit.Sending ? ClosingSurveyStage.Sending : ClosingSurveyStage.Done);
                return;
            }
            Set(ClosingSurveyStage.Done);
        }

        private void Leave()
        {
            answers.Clear();
            Index = 0;
            Storing = false;
            Reply = "";
            MoreShown = false;
            if (Stage != ClosingSurveyStage.Off) Set(ClosingSurveyStage.Off);
        }

        /// <summary>Line at the top of the guide's screen while the survey is on.</summary>
        public string Message
        {
            get
            {
                switch (Stage)
                {
                    case ClosingSurveyStage.Notice: return Survey.NoticeTitle;
                    case ClosingSurveyStage.Answering: return MuseumInterfaceText.Format("survey_progress", Index + 1, Survey.Questions.Count);
                    case ClosingSurveyStage.Sending: return MuseumInterfaceText.Get("survey_sending");
                    case ClosingSurveyStage.Done:
                        // The service's closing words end the conversation; a failure to store only adds a short note.
                        if (!string.IsNullOrWhiteSpace(Survey.Closing))
                            return Storing && answers.Count > 0 && SubmitState != ClosingSurveySubmit.Stored
                                ? Survey.Closing + " " + MuseumInterfaceText.Get("survey_not_stored_note") : Survey.Closing;
                        if (!Storing || answers.Count == 0) return MuseumInterfaceText.Get("survey_done_not_stored");
                        return SubmitState == ClosingSurveySubmit.Stored ? MuseumInterfaceText.Get("survey_done_stored")
                            : MuseumInterfaceText.Get("survey_done_failed");
                    case ClosingSurveyStage.Declined: return MuseumInterfaceText.Get("survey_declined");
                    default: return "";
                }
            }
        }

        private void Set(ClosingSurveyStage stage)
        {
            Stage = stage;
            Changed?.Invoke();
        }
    }
}
