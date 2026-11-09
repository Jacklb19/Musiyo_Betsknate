using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>A Spanish question keyboard, aimed with the same controller ray as the museum's other screens.</summary>
    public sealed class MuseumVirtualKeyboard : MonoBehaviour
    {
        public const int MaximumCharacters = 500;
        private Canvas canvas;
        private RectTransform panel;
        private TMP_Text preview;
        private Button send;
        private Action<string> submitted;
        private Action closed;
        private readonly List<Button> keys = new();
        private readonly List<(TMP_Text label, string letter)> letters = new();
        private bool capitals;

        public bool Open => canvas != null && canvas.gameObject.activeSelf;
        public string Draft { get; private set; } = "";
        public RectTransform Surface => panel;
        public IReadOnlyList<Button> Keys => keys;
        public Button SendButton => send;

        public void Show(string draft, Camera eye, MuseumLecternDisplay display, Action<string> onSubmit, Action onClose)
        {
            if (canvas == null) Build(eye);
            submitted = onSubmit;
            closed = onClose;
            Draft = Limit(draft ?? "");
            capitals = false;
            foreach (var (label, letter) in letters) label.text = letter;
            canvas.worldCamera = eye;
            canvas.gameObject.SetActive(true);
            if (eye != null)
            {
                var ahead = Vector3.ProjectOnPlane(eye.transform.forward, Vector3.up);
                if (ahead.sqrMagnitude < .01f) ahead = Vector3.forward;
                var position = eye.transform.position + ahead.normalized * 1.05f - Vector3.up * .15f;
                canvas.transform.SetPositionAndRotation(position, MuseumWorldUi.Facing(position, eye.transform.position));
            }
            display?.AddCue(panel);
            Refresh();
        }

        private void Build(Camera eye)
        {
            canvas = MuseumWorldUi.Canvas("QuestionKeyboard", transform, new Vector2(780, 556), eye);
            canvas.sortingOrder = 10;
            panel = MuseumWorldUi.Surface("KeyboardPanel", canvas.transform, MuseumWorldUi.Panel).rectTransform;
            MuseumWorldUi.Fill(panel);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var title = MuseumWorldUi.Label("Title", panel, MuseumInterfaceText.Get("keyboard_title"), 26, MuseumWorldUi.Text, true);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            preview = MuseumWorldUi.Label("Draft", panel, "", 25, MuseumWorldUi.Text);
            preview.gameObject.AddComponent<LayoutElement>().preferredHeight = 78;
            preview.overflowMode = TextOverflowModes.Ellipsis;
            foreach (string row in new[] { "1234567890", "qwertyuiop", "asdfghjklñ", "zxcvbnmäëïö", "áéíóúü¿?.," })
            {
                var parent = Row("Letters", 48);
                foreach (char letter in row)
                {
                    string value = letter.ToString();
                    var key = Key(value, parent, value, () => Insert(capitals ? value.ToUpperInvariant() : value));
                    letters.Add((key.GetComponentInChildren<TMP_Text>(), value));
                }
            }
            var editing = Row("Editing", 52);
            Key("Shift", editing, MuseumInterfaceText.Get("keyboard_shift"), ToggleCase);
            Key("Space", editing, MuseumInterfaceText.Get("keyboard_space"), () => Insert(" "));
            Key("Backspace", editing, MuseumInterfaceText.Get("keyboard_backspace"), Backspace);
            Key("Clear", editing, MuseumInterfaceText.Get("keyboard_clear"), Clear);
            var actions = Row("Actions", 54);
            send = Key("Send", actions, MuseumInterfaceText.Get("keyboard_send"), Submit, true);
            Key("Close", actions, MuseumInterfaceText.Get("keyboard_close"), () => Hide(true));
            canvas.gameObject.SetActive(false);
        }

        private RectTransform Row(string name, float height)
        {
            var row = MuseumWorldUi.Box(name, panel);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            return row;
        }

        private Button Key(string name, Transform parent, string caption, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var key = MuseumWorldUi.Choice(name, parent, caption, 23, action, primary);
            key.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            keys.Add(key);
            return key;
        }

        public void Insert(string text)
        {
            if (!Open) return;
            Draft = Limit(Draft + (text ?? ""));
            Refresh();
        }

        public void Backspace()
        {
            if (!Open || Draft.Length == 0) return;
            var starts = StringInfo.ParseCombiningCharacters(Draft);
            Draft = Draft.Substring(0, starts[starts.Length - 1]);
            Refresh();
        }

        public void Clear() { if (Open) { Draft = ""; Refresh(); } }

        private static string Limit(string text)
        {
            if (text.Length <= MaximumCharacters) return text;
            int length = MaximumCharacters;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }

        private void ToggleCase()
        {
            capitals = !capitals;
            foreach (var (label, letter) in letters) label.text = capitals ? letter.ToUpperInvariant() : letter;
        }

        private void Refresh()
        {
            if (preview != null) preview.text = Draft.Length == 0 ? MuseumInterfaceText.Get("guide_placeholder")
                : Draft.Length > 90 ? "..." + Draft.Substring(Draft.Length - 90) : Draft;
            if (send != null) send.interactable = Draft.Trim().Length >= 3;
        }

        public void Submit()
        {
            if (!Open || Draft.Trim().Length < 3) return;
            var callback = submitted;
            string text = Draft;
            Hide(false);
            callback?.Invoke(text);
        }

        /// <summary>Closing preserves the draft in memory; only the caller decides whether to send it.</summary>
        public void Hide(bool notify = false)
        {
            if (!Open) return;
            var callback = closed;
            canvas.gameObject.SetActive(false);
            submitted = null;
            closed = null;
            if (notify) callback?.Invoke();
        }

        private void OnDisable() => Hide(true);
    }
}
