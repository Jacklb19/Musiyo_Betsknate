using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The conversation with the guide inside the point panel: a question field, the answer and its sources. Text from the
    /// service is shown as plain text. Cited records in the loaded tour offer orientation to their room; others show their
    /// title only.
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
        private int shownWait = -1;
        private bool wasOpen;
        private bool focusPending;

        public TMP_InputField Field => field;
        public Button SendButton => send;
        public Button CancelButton => cancel;
        public TMP_Text Answer => answer;
        public IReadOnlyList<GameObject> Sources => shownSources;

        /// <param name="bodyText">Panel body text whose style the conversation reuses.</param>
        /// <param name="content">Layout column of the point panel.</param>
        /// <param name="button">Creates a panel button from an interface text key.</param>
        public void Configure(MuseumGuide conversation, MuseumInteraction state, MuseumWayfinding orientation,
            TMP_Text bodyText, Transform content, Func<string, Transform, UnityAction, Button> button)
        {
            guide = conversation;
            interaction = state;
            wayfinding = orientation;
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
            guide.Changed += Refresh;
            guide.TypingRequested += BeginTyping;
            interaction.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (guide != null) { guide.Changed -= Refresh; guide.TypingRequested -= BeginTyping; }
            if (interaction != null) interaction.Changed -= Refresh;
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
            // The rate-limit countdown changes every second without any event.
            int wait = guide.Client.RetryWaitRemaining;
            if (wait != shownWait) { shownWait = wait; Refresh(); }
        }

        public void Refresh()
        {
            if (guide == null || field == null) return;
            bool open = guide.Open;
            bool spatial = MuseumInput.Current.Spatial;
            bool offered = open && guide.Offered;
            var client = guide.Client;
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
