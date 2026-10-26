using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The companion's small screen for a spoken question. It floats a little ahead and to the left of the visitor's eyes,
    /// follows them lazily when they turn or walk away, and says plainly whether the companion is listening or thinking.
    /// Its choices are aimed like every screen in the museum: the controller ray on a headset, the centre of the view or
    /// the mouse on a computer. The first time the companion can listen, the screen says once how to ask.
    /// </summary>
    [RequireComponent(typeof(MuseumCompanionVoice))]
    public sealed class MuseumCompanionVoicePanel : MonoBehaviour
    {
        public const float Width = 540;
        // On a headset it floats an arm's length away; on a computer screen, whose view is small and far from the eyes,
        // it comes closer so its text reads comfortably (owner, 25/10/2026: «está muy lejos y no se lee»).
        private static bool Headset => MuseumInput.Current.Spatial;
        private static float Ahead => Headset ? 1.15f : .66f;
        private static float Aside => Headset ? -.34f : -.2f;
        private static float Below => Headset ? .08f : .05f;
        private const float InviteSeconds = 9;
        private MuseumCompanionVoice voice;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private Camera head;
        private Canvas canvas;
        private RectTransform panel;
        private Image dot;
        private TMP_Text chip, note, body, sources, hint;
        private Button accept, decline, stop, confirm, again, close;
        private bool registered;
        private bool invited;
        private float inviteUntil;
        private Vector3 spot;
        private bool placed;
        private bool following;

        public RectTransform Surface => panel;
        public TMP_Text Body => body;
        public TMP_Text Chip => chip;
        public TMP_Text Note => note;
        public TMP_Text Sources => sources;
        public TMP_Text Hint => hint;
        public Button Accept => accept;
        public Button Decline => decline;
        public Button Stop => stop;
        public Button Confirm => confirm;
        public Button Again => again;
        public Button Close => close;
        /// <summary>The screen is on show, for a question or for the one-time invitation.</summary>
        public bool Shown => canvas != null && canvas.gameObject.activeSelf;
        public bool Inviting => Time.unscaledTime < inviteUntil && !voice.Conversing;

        private void Start()
        {
            voice = GetComponent<MuseumCompanionVoice>();
            interaction = FindAnyObjectByType<MuseumInteraction>();
            flow = interaction != null ? interaction.GetComponent<MuseumVisitFlow>() : null;
            Build();
            voice.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (voice != null) voice.Changed -= Refresh;
        }

        private void Build()
        {
            canvas = MuseumWorldUi.Canvas("CompanionVoice", transform, new Vector2(Width, 600), FindHead());
            canvas.sortingOrder = 5;
            // The screen hangs from its top edge, which stays just below the eyes.
            ((RectTransform)canvas.transform).pivot = new Vector2(.5f, 1);
            panel = MuseumWorldUi.Surface("Panel", canvas.transform, MuseumWorldUi.Panel).rectTransform;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 1);
            panel.sizeDelta = new Vector2(Width, 0);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 22, 22);
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            header.SetParent(panel, false);
            var row = header.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            dot = MuseumWorldUi.Surface("Listening", header, MuseumWorldUi.Accent);
            var dotSize = dot.gameObject.AddComponent<LayoutElement>();
            dotSize.preferredWidth = dotSize.preferredHeight = 18;
            var title = MuseumWorldUi.Label("Title", header, MuseumInterfaceText.Get("companion_voice_title"), 22, MuseumWorldUi.Accent, true);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            chip = MuseumWorldUi.Label("State", header, "", 20, MuseumWorldUi.Muted);
            chip.alignment = TextAlignmentOptions.MidlineRight;

            note = MuseumWorldUi.Label("NarrationPaused", panel, MuseumInterfaceText.Get("companion_voice_narration_paused"), 19, MuseumWorldUi.Muted);
            body = MuseumWorldUi.Label("Body", panel, "", 25, MuseumWorldUi.Text);
            sources = MuseumWorldUi.Label("Sources", panel, "", 19, MuseumWorldUi.Muted);

            var actions = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            actions.SetParent(panel, false);
            var buttons = actions.GetComponent<HorizontalLayoutGroup>();
            buttons.spacing = 10;
            buttons.childControlWidth = buttons.childControlHeight = true;
            buttons.childForceExpandWidth = true;
            actions.GetComponent<LayoutElement>().preferredHeight = 68;
            Button Make(string name, string key, UnityEngine.Events.UnityAction action, bool primary)
            {
                var made = MuseumWorldUi.Choice(name, actions, MuseumInterfaceText.Get(key), 22, action, primary);
                made.gameObject.AddComponent<LayoutElement>().preferredHeight = 68;
                return made;
            }
            accept = Make("Accept", "companion_voice_accept", () => voice.Accept(), true);
            decline = Make("Decline", "companion_voice_decline", () => voice.Decline(), false);
            stop = Make("Stop", "companion_voice_stop", () => voice.StopTalking(), true);
            confirm = Make("Confirm", "companion_voice_confirm", () => voice.Confirm(), true);
            again = Make("Again", "companion_voice_again", () => voice.Again(), false);
            close = Make("Close", "companion_voice_close", () => voice.End(), false);
            hint = MuseumWorldUi.Label("Hint", panel, "", 18, MuseumWorldUi.Muted);
            canvas.gameObject.SetActive(false);
        }

        private Camera FindHead()
        {
            if (head != null && head.isActiveAndEnabled) return head;
            var visitor = FindAnyObjectByType<DesktopVisitorController>();
            head = visitor != null ? visitor.GetComponentsInChildren<Camera>().FirstOrDefault(camera => camera.isActiveAndEnabled) : null;
            if (head == null) head = Camera.main;
            if (canvas != null) canvas.worldCamera = head;
            return head;
        }

        private void Refresh()
        {
            if (canvas == null) return;
            var stage = voice.Stage;
            bool conversing = voice.Conversing;
            bool shown = conversing || Inviting;
            if (shown && !canvas.gameObject.activeSelf) placed = following = false;
            canvas.gameObject.SetActive(shown);
            if (!shown) return;
            bool listening = stage == CompanionVoiceStage.Recording;
            bool thinking = stage == CompanionVoiceStage.Transcribing || stage == CompanionVoiceStage.Thinking;
            dot.gameObject.SetActive(listening || thinking);
            dot.color = listening ? MuseumWorldUi.Accent : MuseumWorldUi.Muted;
            chip.text = listening ? MuseumInterfaceText.Get("companion_voice_listening") : thinking ? MuseumInterfaceText.Get("companion_voice_thinking_chip") : "";
            note.gameObject.SetActive(conversing && voice.PausedNarration);
            string text = conversing ? voice.Status : MuseumInterfaceText.Get("companion_voice_invite");
            if (body.text != text) body.text = text;
            var cited = voice.Citations.Where(citation => citation != null && !string.IsNullOrWhiteSpace(citation.title))
                .Select(citation => MuseumInterfaceText.Format("companion_voice_source", citation.title)).Distinct().ToArray();
            sources.text = string.Join("\n", cited);
            sources.gameObject.SetActive(cited.Length > 0);
            accept.gameObject.SetActive(stage == CompanionVoiceStage.Notice);
            decline.gameObject.SetActive(stage == CompanionVoiceStage.Notice);
            stop.gameObject.SetActive(stage == CompanionVoiceStage.Recording);
            confirm.gameObject.SetActive(stage == CompanionVoiceStage.Review);
            again.gameObject.SetActive(stage == CompanionVoiceStage.Review || stage == CompanionVoiceStage.Answer || stage == CompanionVoiceStage.Problem);
            close.gameObject.SetActive(conversing && stage != CompanionVoiceStage.Notice);
            accept.transform.parent.gameObject.SetActive(conversing);
            string hintKey = stage == CompanionVoiceStage.Notice ? "companion_voice_hint_notice"
                : stage == CompanionVoiceStage.Recording ? "companion_voice_hint_recording"
                : stage == CompanionVoiceStage.Review ? "companion_voice_hint_review"
                : stage == CompanionVoiceStage.Transcribing || stage == CompanionVoiceStage.Thinking || stage == CompanionVoiceStage.Permission ? null
                : "companion_voice_hint";
            hint.text = hintKey != null ? MuseumInterfaceText.Get(hintKey) : "";
            hint.gameObject.SetActive(hintKey != null);
        }

        private void LateUpdate()
        {
            if (canvas == null) return;
            if (!registered && interaction != null && interaction.TryGetComponent<MuseumLecternDisplay>(out var display))
            {
                display.AddCue(panel);
                registered = true;
            }
            // The invitation: once per visit, as soon as the companion can listen and the visitor is exploring.
            if (!invited && voice.QuestionsOffered && voice.Listens && voice.CanAsk && flow != null && flow.Phase == VisitPhase.Exploring)
            {
                invited = true;
                inviteUntil = Time.unscaledTime + InviteSeconds;
                Refresh();
            }
            bool shown = voice.Conversing || Inviting;
            if (shown != canvas.gameObject.activeSelf) Refresh();
            if (!shown) return;
            var camera = FindHead();
            if (camera == null) return;
            var eyes = camera.transform.position;
            var forward = camera.transform.forward;
            forward.y = 0;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var wanted = eyes + forward * Ahead + right * Aside + Vector3.down * Below;
            var current = spot - eyes;
            current.y = 0;
            // It stays put while it is comfortably in view, so it can be read and aimed at; once out of view it glides back
            // in front and settles there.
            if (!placed) { spot = wanted; placed = true; }
            if (!following && (current.magnitude > Ahead * 1.6f || current.magnitude < Ahead * .5f || Vector3.Angle(current, forward) > 42))
                following = true;
            if (following)
            {
                spot = Vector3.Lerp(spot, wanted, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 5));
                if ((spot - wanted).sqrMagnitude < .0009f) following = false;
            }
            else spot.y = Mathf.Lerp(spot.y, wanted.y, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3));
            var holder = canvas.transform;
            holder.position = spot;
            holder.rotation = MuseumWorldUi.Facing(spot, eyes);
            if (voice.Stage == CompanionVoiceStage.Recording || voice.Stage == CompanionVoiceStage.Problem) Refresh();
            if (dot.gameObject.activeSelf)
            {
                float pulse = .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * (voice.Stage == CompanionVoiceStage.Recording ? 4 : 2)));
                var color = dot.color;
                color.a = pulse;
                dot.color = color;
            }
        }
    }
}
