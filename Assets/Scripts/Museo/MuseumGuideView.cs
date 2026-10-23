using System;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museo;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The conversation with the guide inside the point panel: a question field, the answer and its sources, and by the
    /// hearth the conversation about the visit (<see cref="MuseumHearthQuiz"/>) with its options as choices on the same
    /// screen, and speaking with the guide (<see cref="MuseumGuideVoice"/>): its notice, recording, transcript to confirm
    /// and spoken answer, all on this screen. Text from the service is shown as plain text. Cited records in the loaded
    /// tour offer orientation to their room; others show their title only.
    /// </summary>
    public sealed class MuseumGuideView : MonoBehaviour
    {
        private const float FieldHeight = 60;
        private MuseumGuide guide;
        private MuseumInteraction interaction;
        private MuseumWayfinding wayfinding;
        private TMP_InputField field;
        private GameObject fieldRoot;
        private TMP_Text spatialNote;
        private TMP_Text appearanceNotice;
        private MuseumCompanionMask companionMask;
        private TMP_Text answer;
        private Transform sources;
        private TMP_Text sourceTemplate;
        private Button sourceButtonTemplate;
        private Button send;
        private Button cancel;
        private TMP_Text sendCaption;
        private readonly List<GameObject> shownSources = new List<GameObject>();
        private MuseumHearthQuiz quiz;
        private Transform readingColumn;
        private TMP_Text quizText;
        private Button quizStart, quizSkip, quizNext, quizClose, quizRetry, quizOptionTemplate;
        private Transform quizOptions;
        private readonly List<Button> shownOptions = new List<Button>();
        private QuizQuestionContractV1 optionsFor;
        private MuseumClosingSurvey survey;
        private TMP_Text surveyText;
        private Button surveyAccept, surveyWithout, surveyDecline, surveyMore, surveySkip, surveyFinish, surveyOptionTemplate;
        private Transform surveyOptions;
        private readonly List<Button> shownSurveyOptions = new List<Button>();
        private ClosingSurveyQuestion surveyOptionsFor;
        private MuseumGuideVoice voice;
        private TMP_Text voiceText;
        private Button voiceTalk, voiceStop, voiceDiscard, voiceAccept, voiceDecline, voiceConfirm, voiceStopSpeech;
        private string reviewed;
        private bool wasSpeaking;
        private int shownWait = -1;
        private bool wasOpen;
        private bool focusPending;

        public TMP_InputField Field => field;
        public Button SendButton => send;
        public Button CancelButton => cancel;
        public TMP_Text Answer => answer;
        public IReadOnlyList<GameObject> Sources => shownSources;
        public Button QuizStart => quizStart;
        public Button QuizSkip => quizSkip;
        public Button QuizNext => quizNext;
        public Button QuizClose => quizClose;
        public TMP_Text QuizText => quizText;
        /// <summary>The options of the question on show, in published order.</summary>
        public IReadOnlyList<Button> QuizOptions => shownOptions;
        public TMP_Text VoiceText => voiceText;
        public Button VoiceTalk => voiceTalk;
        public Button VoiceStop => voiceStop;
        public Button VoiceDiscard => voiceDiscard;
        public Button VoiceAccept => voiceAccept;
        public Button VoiceDecline => voiceDecline;
        public Button VoiceConfirm => voiceConfirm;
        public Button VoiceStopSpeech => voiceStopSpeech;
        public TMP_Text SurveyText => surveyText;
        public Button SurveyAccept => surveyAccept;
        public Button SurveyWithoutStoring => surveyWithout;
        public Button SurveyDecline => surveyDecline;
        /// <summary>"¿Para qué es esto?": shows under the invitation why the questions are asked.</summary>
        public Button SurveyMore => surveyMore;
        public Button SurveySkip => surveySkip;
        public Button SurveyFinish => surveyFinish;
        /// <summary>The choices of the opinion question on show, in published order.</summary>
        public IReadOnlyList<Button> SurveyOptions => shownSurveyOptions;

        /// <param name="bodyText">Panel body text whose style the conversation reuses.</param>
        /// <param name="content">Layout column of the point panel.</param>
        /// <param name="button">Creates a panel button from an interface text key.</param>
        public void Configure(MuseumGuide conversation, MuseumInteraction state, MuseumWayfinding orientation,
            TMP_Text bodyText, Transform content, Func<string, Transform, UnityAction, Button> button, MuseumHearthQuiz hearthQuiz = null,
            MuseumGuideVoice guideVoice = null)
        {
            voice = guideVoice;
            guide = conversation;
            interaction = state;
            wayfinding = orientation;
            quiz = hearthQuiz;
            readingColumn = content;
            fieldRoot = CreateField(bodyText, content);
            spatialNote = Text(bodyText, content, "GuideSpatialNote");
            spatialNote.text = MuseumInterfaceText.Get("guide_spatial_text_pending");
            appearanceNotice = Text(bodyText, content, "CompanionAppearanceNotice");
            var actions = new GameObject("GuideActions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            actions.transform.SetParent(content, false);
            var row = actions.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            actions.GetComponent<LayoutElement>().preferredHeight = 52;
            send = button("guide_send", actions.transform, Submit);
            send.name = "GuideSend";
            sendCaption = send.GetComponentInChildren<TMP_Text>();
            cancel = button("guide_cancel", actions.transform, () => guide.Client.Cancel());
            cancel.name = "GuideCancel";
            answer = Text(bodyText, content, "GuideAnswer");
            var list = new GameObject("GuideSources", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(content, false);
            var column = list.GetComponent<VerticalLayoutGroup>();
            column.spacing = 6;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandHeight = false;
            sources = list.transform;
            sourceTemplate = Text(bodyText, sources, "GuideSource");
            sourceTemplate.gameObject.SetActive(false);
            sourceButtonTemplate = button("guide_source_unplaced", sources, () => { });
            sourceButtonTemplate.name = "GuideSourceRoom";
            sourceButtonTemplate.gameObject.SetActive(false);
            if (voice != null) CreateVoice(bodyText, content, button);
            if (quiz != null) CreateQuiz(bodyText, content, button);
            survey = guide.GetComponent<MuseumClosingSurvey>();
            if (survey != null) CreateSurvey(bodyText, content, button);
            guide.Changed += Refresh;
            guide.TypingRequested += BeginTyping;
            interaction.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (guide != null) { guide.Changed -= Refresh; guide.TypingRequested -= BeginTyping; }
            if (interaction != null) interaction.Changed -= Refresh;
            if (quiz != null) quiz.Changed -= Refresh;
            if (voice != null) voice.Changed -= RefreshVoice;
            if (survey != null) survey.Changed -= Refresh;
        }

        /// <summary>
        /// The opinion questions: the notice (worded by the survey service) reads in the screen's column with its three
        /// large choices, then each question with its options as large choices; nothing else stands on the screen meanwhile.
        /// </summary>
        private void CreateSurvey(TMP_Text bodyText, Transform content, Func<string, Transform, UnityAction, Button> button)
        {
            surveyText = Text(bodyText, content, "SurveyText");
            Button Make(string key, string name, UnityAction action)
            {
                var made = button(key, content, action);
                made.name = name;
                Enlarge(made);
                return made;
            }
            surveyAccept = Make("survey_accept", "SurveyAccept", () => survey.AcceptAndAnswer());
            surveyWithout = Make("survey_without_storing", "SurveyWithoutStoring", () => survey.AnswerWithoutStoring());
            surveyDecline = Make("survey_decline", "SurveyDecline", () => survey.Decline());
            surveyMore = Make("survey_more", "SurveyMore", () => survey.ToggleMore());
            surveySkip = Make("survey_skip", "SurveySkip", () => survey.Skip());
            surveyFinish = Make("survey_finish", "SurveyFinish", () => survey.Finish());
            surveyOptionTemplate = Make("survey_skip", "SurveyOption", () => { });
            surveyOptionTemplate.gameObject.SetActive(false);
            survey.Changed += Refresh;
        }

        /// <summary>
        /// Large, wrapped captions at one size: these choices are read and aimed at from a headset. Auto-sizing inside the
        /// screen's layout measured itself again every frame and the choices flickered on the headset (23/10/2026), so the
        /// size is fixed and each choice is made tall enough for its words once, when they are set (<see cref="Fit"/>).
        /// </summary>
        private static void Enlarge(Button choice, float size = ChoiceFontSize)
        {
            var caption = choice.GetComponentInChildren<TMP_Text>();
            caption.richText = false;
            caption.textWrappingMode = TextWrappingModes.Normal;
            caption.enableAutoSizing = false;
            caption.fontSize = size;
            caption.margin = new Vector4(10, 4, 10, 4);
            if (choice.TryGetComponent<LayoutElement>(out var layout)) layout.preferredHeight = ChoiceHeight;
        }

        private const float ChoiceFontSize = 26, ChoiceHeight = 84;

        /// <summary>Makes a choice tall enough for its caption at the width of its column; never less than a large choice.</summary>
        private static void Fit(Button choice)
        {
            if (!choice.TryGetComponent<LayoutElement>(out var layout)) return;
            var caption = choice.GetComponentInChildren<TMP_Text>();
            float width = choice.transform.parent is RectTransform column && column.rect.width > 1 ? column.rect.width : 520;
            var size = caption.GetPreferredValues(caption.text, Mathf.Max(80, width - caption.margin.x - caption.margin.z - 16), 0);
            layout.preferredHeight = Mathf.Max(ChoiceHeight, Mathf.Ceil(size.y + caption.margin.y + caption.margin.w + 16));
        }

        private bool Surveying => guide != null && guide.Open && survey != null && survey.Stage != ClosingSurveyStage.Off;

        private void RefreshSurvey(bool open)
        {
            if (survey == null || surveyText == null) return;
            var stage = open ? survey.Stage : ClosingSurveyStage.Off;
            var question = stage == ClosingSurveyStage.Answering ? survey.Current : null;
            string text = "";
            var published = survey.Survey;
            if (stage == ClosingSurveyStage.Notice)
            {
                // The guide's invitation, and on request why the questions are asked; an older survey has only its notice.
                text = !string.IsNullOrWhiteSpace(published.Intro) ? published.Intro : published.NoticeText;
                if (survey.MoreShown && !string.IsNullOrWhiteSpace(published.Intro))
                    text += "\n\n" + (string.IsNullOrWhiteSpace(published.NoticeTitle) ? "" : published.NoticeTitle + "\n") + published.NoticeText;
            }
            else if (question != null)
            {
                // The reply to the last answer comes first, as the guide would say it, then the next question.
                text = (string.IsNullOrWhiteSpace(survey.Reply) ? "" : survey.Reply + "\n\n") + question.Text;
                if (question.Scale && (question.LowLabel.Length > 0 || question.HighLabel.Length > 0))
                    text += "\n" + MuseumInterfaceText.Format("survey_scale", question.Options[0].Text, question.LowLabel,
                        question.Options[question.Options.Count - 1].Text, question.HighLabel);
            }
            surveyText.text = text;
            surveyText.gameObject.SetActive(text.Length > 0);
            surveyAccept.gameObject.SetActive(stage == ClosingSurveyStage.Notice);
            surveyWithout.gameObject.SetActive(stage == ClosingSurveyStage.Notice);
            surveyDecline.gameObject.SetActive(stage == ClosingSurveyStage.Notice);
            surveyMore.gameObject.SetActive(stage == ClosingSurveyStage.Notice && published != null && !string.IsNullOrWhiteSpace(published.MoreLabel));
            if (stage == ClosingSurveyStage.Notice)
            {
                // The service words its own choices; the museum's catalog words them when it does not.
                Caption(surveyAccept, published.AcceptLabel, "survey_accept");
                Caption(surveyWithout, published.LocalLabel, "survey_without_storing");
                Caption(surveyDecline, published.DeclineLabel, "survey_decline");
                Caption(surveyMore, published.MoreLabel, "survey_more");
            }
            surveySkip.gameObject.SetActive(question != null && question.Optional);
            surveyFinish.gameObject.SetActive(question != null);
            ShowSurveyOptions(question);
        }

        private static void Caption(Button choice, string published, string catalogKey)
        {
            var label = choice.GetComponentInChildren<TMP_Text>(true);
            foreach (var bound in choice.GetComponentsInChildren<MuseumInterfaceLabel>(true)) bound.enabled = string.IsNullOrWhiteSpace(published);
            label.text = string.IsNullOrWhiteSpace(published) ? MuseumInterfaceText.Get(catalogKey) : published;
            Fit(choice);
        }

        private void ShowSurveyOptions(ClosingSurveyQuestion question)
        {
            if (question == surveyOptionsFor) return;
            foreach (var item in shownSurveyOptions) Destroy(item.gameObject);
            shownSurveyOptions.Clear();
            surveyOptionsFor = question;
            if (surveyOptions == null)
            {
                var list = new GameObject("SurveyOptions", typeof(RectTransform), typeof(VerticalLayoutGroup));
                list.transform.SetParent(surveySkip.transform.parent, false);
                var column = list.GetComponent<VerticalLayoutGroup>();
                column.spacing = 8;
                column.childControlWidth = column.childControlHeight = true;
                column.childForceExpandHeight = false;
                surveyOptions = list.transform;
            }
            surveyOptions.SetSiblingIndex(surveySkip.transform.GetSiblingIndex());
            surveyOptions.gameObject.SetActive(question != null);
            if (question == null) return;
            for (int index = 0; index < question.Options.Count; index++)
            {
                int chosen = index;
                var option = Instantiate(surveyOptionTemplate, surveyOptions);
                option.name = "SurveyOption";
                option.onClick.RemoveAllListeners();
                option.onClick.AddListener(() => survey.Choose(chosen));
                option.GetComponentInChildren<TMP_Text>().text = question.Options[index].Text;
                option.gameObject.SetActive(true);
                Fit(option);
                shownSurveyOptions.Add(option);
            }
        }

        /// <summary>
        /// Speaking with the guide: what the voice says reads in the screen's column (the notice, the recording, the
        /// transcript, a problem); talking, stopping, discarding, accepting and confirming stand with the other controls.
        /// </summary>
        private void CreateVoice(TMP_Text bodyText, Transform content, Func<string, Transform, UnityAction, Button> button)
        {
            voiceText = Text(bodyText, content, "VoiceText");
            Button Make(string key, string name, UnityAction action)
            {
                var made = button(key, content, action);
                made.name = name;
                return made;
            }
            voiceTalk = Make("voice_talk", "VoiceTalk", () => voice.Talk());
            voiceAccept = Make("voice_accept", "VoiceAccept", () => voice.Accept());
            voiceDecline = Make("voice_decline", "VoiceDecline", () => voice.Decline());
            voiceStop = Make("voice_stop", "VoiceStop", () => voice.StopTalking());
            voiceConfirm = Make("voice_confirm", "VoiceConfirm", () => voice.Confirm());
            voiceDiscard = Make("voice_discard", "VoiceDiscard", () => voice.Discard());
            voiceStopSpeech = Make("voice_stop_speech", "VoiceStopSpeech", () => voice.StopSpeech());
            voice.Changed += RefreshVoice;
        }

        /// <summary>Only the voice's own parts change while recording, so the rest of the screen is left alone.</summary>
        private void RefreshVoice()
        {
            if (voice == null || voiceText == null || guide == null) return;
            bool open = guide.Open;
            bool quizzing = open && quiz != null && (quiz.Active || quiz.Done) || Surveying;
            bool here = open && !quizzing;
            var stage = voice.Stage;
            voiceTalk.gameObject.SetActive(here && voice.Offered && (stage == GuideVoiceStage.Ready || stage == GuideVoiceStage.Problem)
                && !guide.Client.Busy);
            voiceTalk.interactable = voice.Client.RetryWaitRemaining == 0;
            voiceAccept.gameObject.SetActive(here && stage == GuideVoiceStage.Notice);
            voiceDecline.gameObject.SetActive(here && stage == GuideVoiceStage.Notice);
            voiceStop.gameObject.SetActive(here && stage == GuideVoiceStage.Recording);
            voiceConfirm.gameObject.SetActive(here && stage == GuideVoiceStage.Review);
            voiceDiscard.gameObject.SetActive(here && (stage == GuideVoiceStage.Recording || stage == GuideVoiceStage.Transcribing
                || stage == GuideVoiceStage.Review || stage == GuideVoiceStage.Permission));
            voiceStopSpeech.gameObject.SetActive(open && voice.Speaking);
            // Talking shows on the guide's own line at the top of the screen; below, only the note after an answer.
            string status = here && (stage == GuideVoiceStage.Ready || stage == GuideVoiceStage.Off) ? voice.Status : "";
            if (voiceText.text != status) voiceText.text = status;
            voiceText.gameObject.SetActive(status.Length > 0);
            // With a keyboard the transcript also goes to the question field, where it can be corrected before asking.
            var transcript = stage == GuideVoiceStage.Review ? voice.Client.Transcript : null;
            if (transcript != null && transcript != reviewed && !MuseumInput.Current.Spatial) field.SetTextWithoutNotify(transcript);
            reviewed = transcript;
            spatialNote.text = MuseumInterfaceText.Get(voice.Offered ? "guide_spatial_voice" : "guide_spatial_text_pending");
            wasSpeaking = voice.Speaking;
        }

        /// <summary>
        /// The conversation about the visit: its text reads in the screen's column; the options, starting, passing, going
        /// on and ending stand with the other controls. The options are made per question.
        /// </summary>
        private void CreateQuiz(TMP_Text bodyText, Transform content, Func<string, Transform, UnityAction, Button> button)
        {
            quizText = Text(bodyText, content, "QuizText");
            quizStart = button("quiz_start", content, () => quiz.Begin());
            quizStart.name = "QuizStart";
            quizSkip = button("quiz_skip", content, () => quiz.Next());
            quizSkip.name = "QuizSkip";
            quizNext = button("quiz_next", content, () => quiz.Next());
            quizNext.name = "QuizNext";
            quizClose = button("quiz_close", content, () => quiz.Finish());
            quizClose.name = "QuizClose";
            quizRetry = button("quiz_retry", content, () => quiz.Reload());
            quizRetry.name = "QuizRetry";
            quizOptionTemplate = button("quiz_start", content, () => { });
            quizOptionTemplate.name = "QuizOption";
            quizOptionTemplate.gameObject.SetActive(false);
            quiz.Changed += Refresh;
        }

        /// <summary>Visitor text with rich-text tags shown literally, so a service answer cannot restyle the panel.</summary>
        private static TMP_Text Text(TMP_Text style, Transform parent, string name)
        {
            var text = Instantiate(style, parent);
            text.name = name;
            text.text = "";
            text.richText = false;
            text.raycastTarget = false;
            if (text.TryGetComponent<LayoutElement>(out var layout)) layout.minHeight = 0;
            return text;
        }

        private GameObject CreateField(TMP_Text style, Transform parent)
        {
            var root = new GameObject("GuideQuestion", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            root.SetActive(false);
            root.transform.SetParent(parent, false);
            root.GetComponent<LayoutElement>().preferredHeight = FieldHeight;
            var settings = MuseumExperienceConfiguration.Current;
            var background = root.GetComponent<Image>();
            background.color = MuseumExperienceConfiguration.ColorValue(settings.SelectedColor);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(14, 6);
            viewportRect.offsetMax = new Vector2(-14, -6);
            var text = FieldText(style, viewport.transform, "Text", settings.TextColor);
            var placeholder = FieldText(style, viewport.transform, "Placeholder", settings.MutedTextColor);
            placeholder.text = MuseumInterfaceText.Get("guide_placeholder");
            placeholder.fontStyle = FontStyles.Italic;
            field = root.AddComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.fontAsset = text.font;
            field.pointSize = text.fontSize;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 500;
            field.richText = false;
            field.restoreOriginalTextOnEscape = false;
            field.targetGraphic = background;
            field.caretColor = MuseumExperienceConfiguration.ColorValue(settings.AccentColor);
            field.selectionColor = MuseumExperienceConfiguration.ColorValue(settings.PointerColor);
            field.onSubmit.AddListener(_ => Submit());
            root.SetActive(true);
            return root;
        }

        private static TMP_Text FieldText(TMP_Text style, Transform parent, string name, string color)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(parent, false);
            var rect = (RectTransform)item.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = item.GetComponent<TextMeshProUGUI>();
            text.font = style.font;
            text.fontSize = style.fontSize;
            text.color = MuseumExperienceConfiguration.ColorValue(color);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private bool CanWrite => guide.Open && guide.Offered && !MuseumInput.Current.Spatial;

        /// <summary>
        /// Gives the field the keyboard; walking stops until the visitor sends, presses Escape or clicks away. A screen that
        /// is still unfolding takes the keyboard as soon as it is shown.
        /// </summary>
        public void BeginTyping()
        {
            focusPending = CanWrite && field != null;
            if (focusPending && field.isActiveAndEnabled) Focus();
        }

        private void Focus()
        {
            focusPending = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(field.gameObject);
            field.ActivateInputField();
        }

        /// <summary>Sends what the field holds, or the kept question again, and gives the keyboard back to the visit.</summary>
        public void Submit()
        {
            if (!guide.Open || !guide.Offered || guide.Client.Busy || guide.Client.RetryWaitRemaining > 0) return;
            guide.Send(MuseumInput.Current.Spatial ? guide.Client.Question : field.text);
            voice?.Forget();
            field.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void Update()
        {
            if (guide == null) return;
            if (focusPending && (!CanWrite || field.isActiveAndEnabled))
            {
                if (CanWrite) Focus();
                else focusPending = false;
            }
            guide.Typing = guide.Open && field.isFocused;
            // A spoken answer ends without any event.
            if (voice != null && voice.Speaking != wasSpeaking) RefreshVoice();
            // The rate-limit countdowns change every second without any event.
            int wait = guide.Client.RetryWaitRemaining + (quiz != null ? quiz.Client.RetryWaitRemaining * 1000 : 0);
            if (wait != shownWait) { shownWait = wait; Refresh(); }
        }

        public void Refresh()
        {
            if (guide == null || field == null) return;
            bool open = guide.Open;
            bool spatial = MuseumInput.Current.Spatial;
            // While the visitor talks about the visit, the questions to the guide step aside.
            bool quizzing = open && quiz != null && (quiz.Active || quiz.Done) || Surveying;
            bool offered = open && guide.Offered && !quizzing;
            var client = guide.Client;
            RefreshQuiz(open && !Surveying);
            RefreshSurvey(open);
            RefreshVoice();
            fieldRoot.SetActive(offered && !spatial);
            spatialNote.gameObject.SetActive(offered && spatial);
            send.transform.parent.gameObject.SetActive(offered);
            send.gameObject.SetActive(!client.Busy && (!spatial || client.CanRetry));
            send.interactable = client.RetryWaitRemaining == 0;
            sendCaption.text = MuseumInterfaceText.Get(Failed(client.State) ? "guide_retry" : "guide_send");
            cancel.gameObject.SetActive(client.Busy);
            answer.gameObject.SetActive(offered && client.State == GuideQuestionState.Answered);
            answer.text = client.State == GuideQuestionState.Answered ? client.Response.answer : "";
            ShowSources(offered && client.State == GuideQuestionState.Answered);
            if (open && !wasOpen && !string.IsNullOrEmpty(client.Question)) field.SetTextWithoutNotify(client.Question);
            // The conversation opens ready to write, where the device has a keyboard.
            if (open && !wasOpen) BeginTyping();
            if (!open && field.isFocused) field.DeactivateInputField();
            wasOpen = open;
        }

        private void RefreshQuiz(bool open)
        {
            if (quiz == null || quizText == null) return;
            var client = quiz.Client;
            bool failed = client.State == GuideQuizState.Failed || client.State == GuideQuizState.Incompatible;
            bool answerLost = client.AnswerState == GuideQuizAnswerState.Unavailable || client.AnswerState == GuideQuizAnswerState.Incompatible;
            var explanation = quiz.Explanation;
            bool sending = client.AnswerState == GuideQuizAnswerState.Sending;
            quizStart.gameObject.SetActive(open && quiz.Available && !quiz.Active);
            quizSkip.gameObject.SetActive(open && quiz.Active && explanation == null && !sending);
            quizNext.gameObject.SetActive(open && quiz.Active && explanation != null);
            quizClose.gameObject.SetActive(open && quiz.Active);
            quizRetry.gameObject.SetActive(open && (quiz.Active ? answerLost : failed));
            string text = "";
            if (open && quiz.Done) text = MuseumInterfaceText.Get("quiz_done_text");
            else if (open && quiz.Active && quiz.Current != null)
            {
                text = quiz.Current.text;
                if (explanation != null)
                    text += "\n\n" + explanation.explanation + "\n\n" + MuseumInterfaceText.Format("quiz_evidence", explanation.citation.quote)
                        + "\n" + MuseumInterfaceText.Format("quiz_source", explanation.source.reference);
            }
            else if (open && failed) text = MuseumInterfaceText.Get("quiz_failure");
            quizText.text = text;
            quizText.gameObject.SetActive(text.Length > 0);
            ShowOptions(open && quiz.Active ? quiz.Current : null);
        }

        /// <summary>The options of the question on show, as choices beside the reading column; the chosen one is marked.</summary>
        private void ShowOptions(QuizQuestionContractV1 question)
        {
            if (question != optionsFor)
            {
                foreach (var item in shownOptions) Destroy(item.gameObject);
                shownOptions.Clear();
                optionsFor = question;
                if (question != null)
                {
                    if (quizOptions == null)
                    {
                        // The options stand first among the controls, where the screen grows to show them all.
                        var list = new GameObject("QuizOptions", typeof(RectTransform), typeof(VerticalLayoutGroup));
                        list.transform.SetParent(quizSkip.transform.parent, false);
                        var column = list.GetComponent<VerticalLayoutGroup>();
                        column.spacing = 8;
                        column.childControlWidth = column.childControlHeight = true;
                        column.childForceExpandHeight = false;
                        quizOptions = list.transform;
                    }
                    quizOptions.SetSiblingIndex(quizSkip.transform.GetSiblingIndex());
                    for (int index = 0; index < question.options.Length; index++)
                    {
                        int chosenIndex = index;
                        var option = Instantiate(quizOptionTemplate, quizOptions);
                        option.name = "QuizOption";
                        option.onClick.RemoveAllListeners();
                        option.onClick.AddListener(() => quiz.Choose(chosenIndex));
                        Enlarge(option, option.GetComponentInChildren<TMP_Text>().fontSize);
                        option.GetComponentInChildren<TMP_Text>().text = (quiz.Chosen == question.options[index].id
                            ? MuseumInterfaceText.Get("quiz_chosen_mark") : "") + question.options[index].text;
                        option.gameObject.SetActive(true);
                        Fit(option);
                        shownOptions.Add(option);
                    }
                }
            }
            if (quizOptions != null) quizOptions.gameObject.SetActive(question != null);
            if (question == null) return;
            bool open = quiz.Chosen == null || quiz.Client.AnswerState == GuideQuizAnswerState.Failed
                || quiz.Client.AnswerState == GuideQuizAnswerState.RateLimited;
            for (int index = 0; index < shownOptions.Count; index++)
            {
                var option = question.options[index];
                var caption = shownOptions[index].GetComponentInChildren<TMP_Text>();
                caption.text = (quiz.Chosen == option.id ? MuseumInterfaceText.Get("quiz_chosen_mark") : "") + option.text;
                shownOptions[index].interactable = open && quiz.Client.RetryWaitRemaining == 0;
            }
        }

        private void LateUpdate()
        {
            if (appearanceNotice == null) return;
            if (companionMask == null) companionMask = FindAnyObjectByType<MuseumCompanionMask>();
            string notice = companionMask != null ? companionMask.AppearanceNotice : "";
            appearanceNotice.gameObject.SetActive(guide != null && guide.Open && !string.IsNullOrEmpty(notice));
            if (appearanceNotice.text != notice) appearanceNotice.text = notice;
        }

        private static bool Failed(GuideQuestionState state) => state == GuideQuestionState.TimedOut
            || state == GuideQuestionState.Unreachable || state == GuideQuestionState.ServiceError
            || state == GuideQuestionState.RateLimited || state == GuideQuestionState.ProviderUnavailable
            || state == GuideQuestionState.Incompatible;

        private void ShowSources(bool shown)
        {
            foreach (var item in shownSources) Destroy(item);
            shownSources.Clear();
            sources.gameObject.SetActive(shown);
            if (!shown) return;
            var rooms = interaction.Runtime.Contract?.rooms;
            foreach (var citation in guide.Citations)
            {
                var point = guide.Locate(citation);
                var roomKey = guide.RoomOf(point);
                var roomName = rooms?.FirstOrDefault(room => room.key == roomKey)?.name;
                GameObject item;
                if (point != null && roomName != null && wayfinding != null)
                {
                    var choice = Instantiate(sourceButtonTemplate, sources);
                    var caption = choice.GetComponentInChildren<TMP_Text>();
                    caption.richText = false;
                    caption.text = MuseumInterfaceText.Format("guide_source_in_tour", citation.title, roomName);
                    choice.onClick.RemoveAllListeners();
                    choice.onClick.AddListener(() => wayfinding.SelectRoom(roomKey));
                    item = choice.gameObject;
                }
                else
                {
                    var label = Instantiate(sourceTemplate, sources);
                    label.text = MuseumInterfaceText.Format("guide_source_unplaced", citation.title);
                    item = label.gameObject;
                }
                item.name = "GuideCitation";
                item.SetActive(true);
                shownSources.Add(item);
            }
        }
    }
}
