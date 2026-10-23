using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The closing credits, rolling up like a film beside the fire of the title menu: a column on the left of the fire, lit
    /// by it, that fades out at its top and bottom edges. It opens on the museum's name alone, then every section in the order
    /// the API publishes it (title in amber, then each line: a small role in capitals above the name), and closes on
    /// "Gracias por tu visita." held still by the fire. The trigger or a click pauses; holding it speeds the roll up; the
    /// back button, Escape or "Saltar" skips to the end. It stands in the world, so it reads the same on every device.
    /// </summary>
    public sealed class MuseumCreditsRoll : MonoBehaviour
    {
        /// <summary>Column size in millimetres, and where it stands from the eyes of the set.</summary>
        public const float Width = 1250, Height = 1250, Distance = 3, Side = -.95f;
        /// <summary>
        /// In a headset the column stands closer and nearer the middle of the view, so it reads (owner, 22/10): its middle
        /// at this distance and this far to the left of the gaze, a little under the eyes, drawn at this share of its size.
        /// Seen from 1.7 m instead of 3 m, its letters look about half as large again as before.
        /// </summary>
        public const float HeadsetDistance = 1.7f, HeadsetSide = -.42f, HeadsetScale = .85f, HeadsetBelowEyes = .15f;
        /// <summary>Room under the column for the controls.</summary>
        public const float Footer = 150;
        /// <summary>Height over which the column fades out at its edges.</summary>
        public const int EdgeFade = 200;
        /// <summary>Millimetres per second: about the Web's 38 px/s on a 1080 px screen, for the column's height.</summary>
        public const float Speed = 75;
        public const float FastFactor = 4;
        /// <summary>A press held this long speeds the roll up instead of pausing it.</summary>
        public const float HoldSeconds = .35f;
        public const float OpeningSeconds = 4, ThanksSeconds = 6;

        private RectTransform viewport;
        private RectTransform content;
        private RectTransform thanks;
        private TextMeshProUGUI hint;
        private CanvasGroup group;
        private Canvas canvas;
        private MuseumPointerHover skipHover;
        private Action skip;
        private float start, stop;
        private float waited;
        private float pressedFor = -1;
        private float presence = 1;
        private float reveal;

        public RectTransform Content => content;
        public Button SkipButton { get; private set; }
        public TMP_Text Title { get; private set; }
        public TMP_Text Thanks { get; private set; }
        /// <summary>The section titles in the order they roll.</summary>
        public IReadOnlyList<TMP_Text> SectionTitles => sectionTitles;
        private readonly List<TMP_Text> sectionTitles = new List<TMP_Text>();
        public bool Paused { get; private set; }
        public bool Fast { get; private set; }
        /// <summary>The roll has reached its end and the thanks have been held.</summary>
        public bool Done { get; private set; }
        /// <summary>How far the roll has moved, from 0 at the opening to 1 when the thanks stand still.</summary>
        public float Progress => stop > start ? Mathf.Clamp01((content.anchoredPosition.y - start) / (stop - start)) : 1;
        /// <summary>From 1 to 0 as the roll dissolves into the title menu.</summary>
        public float Presence { get => presence; set => presence = Mathf.Clamp01(value); }

        public static MuseumCreditsRoll Create(Transform set, Transform pose, Transform eyes, Camera eventCamera,
            MuseumCreditsContractV1 credits, Action skip)
        {
            var canvas = MuseumWorldUi.Canvas("CreditsRoll", set, new Vector2(Width, Height + Footer), eventCamera);
            var roll = canvas.gameObject.AddComponent<MuseumCreditsRoll>();
            roll.canvas = canvas;
            roll.skip = skip;
            // Where the menu column stands: to the left of the fire, a little below the eyes and above the stools, turned to
            // them. On a screen it keeps a margin from the left edge whatever the window's proportions, and shrinks when the
            // window is narrow.
            var eye = new Vector3(pose.position.x, eyes != null ? eyes.position.y : pose.position.y, pose.position.z);
            float ground = set.position.y;
            bool headset = MuseumInput.Current.Spatial;
            float side = headset ? HeadsetSide : Side, scale = headset ? HeadsetScale : 1, distance = headset ? HeadsetDistance : Distance;
            if (!headset && eventCamera != null)
            {
                float halfWidth = Distance * Mathf.Tan(eventCamera.fieldOfView * .5f * Mathf.Deg2Rad) * eventCamera.aspect;
                float width = Width * MuseumWorldUi.Millimetre;
                scale = Mathf.Min(1, halfWidth * 1.1f / width);
                side = Mathf.Clamp(-(halfWidth * .86f - width * scale * .5f), -1.3f, -.35f);
            }
            var level = Vector3.ProjectOnPlane(pose.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, level);
            var position = eye + level * distance + right * side;
            position.y = headset ? eye.y - HeadsetBelowEyes : ground + Mathf.Clamp(eye.y - ground - .4f, .85f, 1.35f);
            canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - eye, Vector3.up));
            canvas.transform.localScale *= scale;
            roll.Build(credits);
            return roll;
        }

        private void Build(MuseumCreditsContractV1 credits)
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = true;
            viewport = MuseumWorldUi.Box("Viewport", transform);
            viewport.anchorMin = new Vector2(0, 0);
            viewport.anchorMax = new Vector2(1, 1);
            viewport.offsetMin = new Vector2(0, Footer);
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(0, EdgeFade);
            content = MuseumWorldUi.Box("Content", viewport);
            content.anchorMin = content.anchorMax = new Vector2(.5f, 1);
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(Width - 120, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // The opening: the word, the museum's name and its subtitle, alone.
            var opening = new List<RectTransform>();
            var kicker = Line("Kicker", MuseumInterfaceText.Get("credits_kicker"), 26, MuseumWorldUi.Accent);
            kicker.characterSpacing = 18;
            kicker.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            opening.Add(kicker.rectTransform);
            opening.Add(Gap(28));
            Title = Line("Title", credits.title, 92, MuseumWorldUi.Text, true);
            Title.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
            Title.characterSpacing = 6;
            opening.Add(Title.rectTransform);
            if (!string.IsNullOrWhiteSpace(credits.subtitle))
            {
                opening.Add(Gap(18));
                opening.Add(Line("Subtitle", credits.subtitle, 40, MuseumWorldUi.Muted).rectTransform);
            }
            Gap(Height * .55f);
            // Every section in the published order, each line as it comes.
            foreach (var section in credits.sections ?? Array.Empty<CreditSectionContractV1>())
            {
                var lines = section.lines ?? Array.Empty<CreditLineContractV1>();
                if (Array.TrueForAll(lines, line => string.IsNullOrWhiteSpace(line.text))) continue;
                var heading = Line("Section_" + section.key, section.title, 56, MuseumWorldUi.Accent, true);
                heading.characterSpacing = 3;
                sectionTitles.Add(heading);
                Gap(44);
                bool references = section.key == "sources";
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line.text)) continue;
                    if (!string.IsNullOrWhiteSpace(line.role))
                    {
                        var role = Line("Role", line.role, 24, MuseumWorldUi.Muted);
                        role.fontStyle = FontStyles.UpperCase;
                        role.characterSpacing = 10;
                        Gap(8);
                    }
                    // Long references wrap over two or three lines, a little smaller, aligned like the rest.
                    var text = Line(references ? "Reference" : "Name", line.text, references ? 31 : 40, MuseumWorldUi.Text);
                    if (references) text.lineSpacing = 6;
                    Gap(references ? 30 : 38);
                }
                Gap(170);
            }
            // A silence after the last section, so the thanks stand alone.
            Gap(Height * .6f);
            // A short amber line above the thanks, as on the title menu.
            var rule = MuseumWorldUi.Surface("ThanksRule", Gap(3), MuseumWorldUi.Accent);
            rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(.5f, .5f);
            rule.rectTransform.sizeDelta = new Vector2(120, 3);
            Gap(40);
            Thanks = Line("Thanks", MuseumInterfaceText.Get("credits_thanks"), 62, MuseumWorldUi.Text, true);
            thanks = Thanks.rectTransform;
            Gap(Height * .5f);

            // A discreet way out and a reminder of the controls, under the column.
            hint = MuseumWorldUi.Label("Hint", transform, "", 22, MuseumWorldUi.Muted);
            hint.alignment = TextAlignmentOptions.Left;
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0, 0);
            hintRect.anchorMax = new Vector2(1, 0);
            hintRect.pivot = new Vector2(.5f, 0);
            hintRect.offsetMin = new Vector2(40, 24);
            hintRect.offsetMax = new Vector2(-230, 90);
            SkipButton = MuseumWorldUi.Choice("Skip", transform, MuseumInterfaceText.Get("credits_skip"), 24, () => skip?.Invoke(), false);
            var skipRect = (RectTransform)SkipButton.transform;
            skipRect.anchorMin = skipRect.anchorMax = new Vector2(1, 0);
            skipRect.pivot = new Vector2(1, 0);
            skipRect.sizeDelta = new Vector2(180, 62);
            skipRect.anchoredPosition = new Vector2(-30, 26);
            skipHover = SkipButton.gameObject.AddComponent<MuseumPointerHover>();

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float openingHeight = 0;
            foreach (var item in opening) openingHeight += LayoutUtility.GetPreferredHeight(item);
            float shown = viewport.rect.height;
            // The opening first stands in the middle; the sections wait below the bottom edge.
            start = -(shown - openingHeight) * .5f;
            float centre = -content.InverseTransformPoint(thanks.TransformPoint(thanks.rect.center)).y;
            stop = Mathf.Max(start, centre - shown * .5f);
            content.anchoredPosition = new Vector2(0, start);
            RefreshHint();
        }

        private TextMeshProUGUI Line(string name, string text, float size, Color color, bool display = false)
        {
            var label = MuseumWorldUi.Label(name, content, text, size, color, display);
            label.alignment = TextAlignmentOptions.Top;
            label.horizontalAlignment = HorizontalAlignmentOptions.Center;
            return label;
        }

        private RectTransform Gap(float height)
        {
            var gap = MuseumWorldUi.Box("Gap", content);
            var size = gap.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height;
            return gap;
        }

        private void Update()
        {
            float elapsed = Time.unscaledDeltaTime;
            if (!MuseumExperienceConfiguration.Finite(elapsed) || elapsed < 0) elapsed = 0;
            reveal = Mathf.MoveTowards(reveal, 1, elapsed / 1.6f);
            group.alpha = reveal * presence;
            if (presence < 1) return;
            ReadControls(elapsed);
            if (Done) return;
            waited += elapsed;
            if (waited < OpeningSeconds && content.anchoredPosition.y <= start) return;
            if (content.anchoredPosition.y >= stop)
            {
                // The thanks stay still by the fire for a moment before the menu comes back.
                thanksHeld += Paused ? 0 : elapsed;
                if (thanksHeld >= ThanksSeconds) Done = true;
                return;
            }
            if (Paused) return;
            float step = Speed * (Fast ? FastFactor : 1) * elapsed;
            content.anchoredPosition = new Vector2(0, Mathf.Min(stop, content.anchoredPosition.y + step));
        }
        private float thanksHeld;

        /// <summary>A short press pauses or resumes; a held one speeds up; back skips. A press on "Saltar" only skips.</summary>
        private void ReadControls(float elapsed)
        {
            var input = MuseumInput.Current;
            if (input.Back.WasPressedThisFrame() || input.Pause.WasPressedThisFrame()) { skip?.Invoke(); return; }
            bool down = input.Click.IsPressed() || input.Confirm.IsPressed();
            bool pressed = input.Click.WasPressedThisFrame() || input.Confirm.WasPressedThisFrame();
            if (pressed && !(skipHover != null && skipHover.Hovered)) pressedFor = 0;
            if (pressedFor < 0) return;
            if (down)
            {
                pressedFor += elapsed;
                bool fast = pressedFor >= HoldSeconds;
                if (fast != Fast) { Fast = fast; RefreshHint(); }
                return;
            }
            if (!Fast) SetPaused(!Paused);
            Fast = false;
            pressedFor = -1;
            RefreshHint();
        }

        public void SetPaused(bool value)
        {
            if (Paused == value) return;
            Paused = value;
            RefreshHint();
        }

        /// <summary>Moves the roll by a time, as if it had been rolling; tests use it instead of waiting.</summary>
        public void Advance(float seconds)
        {
            waited = Mathf.Max(waited, OpeningSeconds);
            content.anchoredPosition = new Vector2(0, Mathf.Min(stop, content.anchoredPosition.y + Speed * seconds));
        }

        private void RefreshHint()
        {
            if (hint == null) return;
            hint.text = MuseumInterfaceText.Get(Paused ? "credits_paused" : Fast ? "credits_fast" : "credits_controls");
        }
    }
}
