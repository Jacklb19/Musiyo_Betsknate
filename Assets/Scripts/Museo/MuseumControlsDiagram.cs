using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A drawing of the controls at the top of the controls page, made of interface shapes so it needs no picture: on a
    /// headset the two Quest controllers, every control in use named beside it; on a screen a keyboard and a mouse with the
    /// keys of the visit lit. One control at a time plays its gesture in a short loop (a press, the stick pushed, the trigger
    /// pulled, the grip squeezed) and takes the accent colour with its name; on a screen the key of the row in focus is the
    /// one shown. Names come from the interface catalog and keys from the bindings in force.
    /// </summary>
    public sealed class MuseumControlsDiagram : MonoBehaviour
    {
        public const string ObjectName = "ControlsDiagram";
        public static readonly Vector2 HeadsetSize = new Vector2(900, 520), DesktopSize = new Vector2(900, 290);
        /// <summary>Seconds each control plays its gesture before the next one takes its turn.</summary>
        public const float GestureSeconds = 2.4f;
        /// <summary>The drawing is never drawn larger than its design.</summary>
        public const float MaximumScale = 1.1f;
        public enum Gesture { Press, PushUp, PushSide, StickPress, Trigger, Grip }

        /// <summary>One named control: what it is called, what it does, and the shapes that play its gesture.</summary>
        public sealed class Mark
        {
            public string Id;
            public string Title;
            public string Action;
            public Gesture[] Gestures = { Gesture.Press };
            /// <summary>The shape the gesture moves (a key, a button, the stick's knob, the trigger).</summary>
            public RectTransform Moving;
            public readonly List<Graphic> Lit = new List<Graphic>();
            public readonly List<Color> Rest = new List<Color>();
            public TMP_Text TitleText;
            public TMP_Text ActionText;
            public Image Leader;
            public Vector2 Home;
            public float Amount;
        }

        private readonly List<Mark> marks = new List<Mark>();
        private RectTransform drawing;
        private LayoutElement size;
        private TMP_Text caption;
        private Vector2 design;
        private float width = -1;
        private float clock;
        private Color text, muted, accent, body, part, partText;

        public IReadOnlyList<Mark> Marks => marks;
        public bool Headset { get; private set; }
        /// <summary>The control playing its gesture now.</summary>
        public Mark Shown { get; private set; }
        public RectTransform Drawing => drawing;
        /// <summary>On a screen, the line under the drawing that names the key shown and its action.</summary>
        public TMP_Text Caption => caption;

        /// <summary>The drawing of a controls page, made once and placed right under its description.</summary>
        public static MuseumControlsDiagram Ensure(MuseumMenuPageView page)
        {
            var content = page.Scroll != null ? page.Scroll.content : null;
            if (content == null) return null;
            var found = content.Find(ObjectName);
            if (found != null && found.TryGetComponent<MuseumControlsDiagram>(out var existing)) return existing;
            var holder = new GameObject(ObjectName, typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(content, false);
            if (page.Description != null && page.Description.transform.parent == content)
                holder.transform.SetSiblingIndex(page.Description.transform.GetSiblingIndex() + 1);
            return holder.AddComponent<MuseumControlsDiagram>();
        }

        /// <summary>Draws the controllers of a headset or the keyboard and mouse of a screen.</summary>
        public void Build(bool headset)
        {
            Headset = headset;
            var settings = MuseumExperienceConfiguration.Current;
            text = MuseumExperienceConfiguration.ColorValue(settings.TextColor);
            muted = MuseumExperienceConfiguration.ColorValue(settings.MutedTextColor);
            accent = MuseumExperienceConfiguration.ColorValue(settings.AccentColor);
            var panel = MuseumExperienceConfiguration.ColorValue(settings.PanelColor);
            panel.a = 1;
            body = Color.Lerp(panel, text, .13f);
            part = Color.Lerp(panel, text, .32f);
            partText = Color.Lerp(panel, Color.black, .35f);
            size = GetComponent<LayoutElement>();
            marks.Clear();
            Shown = null;
            caption = null;
            if (drawing != null) MuseumTitleScreen.Discard(drawing.gameObject);
            drawing = MuseumWorldUi.Box("Drawing", transform);
            drawing.anchorMin = drawing.anchorMax = new Vector2(.5f, 1);
            drawing.pivot = new Vector2(.5f, 1);
            design = headset ? HeadsetSize : DesktopSize;
            drawing.sizeDelta = design;
            if (headset) BuildControllers();
            else BuildKeyboard();
            width = -1;
            clock = 0;
            Fit();
            Animate(0);
        }

        private void LateUpdate()
        {
            if (drawing == null) return;
            Fit();
            Animate(Time.unscaledDeltaTime);
        }

        /// <summary>The drawing keeps its proportions and fills the column it stands in.</summary>
        private void Fit()
        {
            float available = ((RectTransform)transform).rect.width;
            if (available <= 1 || Mathf.Approximately(available, width)) return;
            width = available;
            float scale = Mathf.Min(MaximumScale, available / design.x);
            drawing.localScale = new Vector3(scale, scale, 1);
            drawing.anchoredPosition = Vector2.zero;
            if (size != null) size.minHeight = size.preferredHeight = design.y * scale;
        }

        /// <summary>
        /// Plays the gesture of one control at a time. On a screen the row in focus chooses which, so the drawing shows the
        /// key the visitor is reading about.
        /// </summary>
        public void Animate(float elapsed)
        {
            if (marks.Count == 0) return;
            clock += elapsed;
            int turn = (int)(clock / GestureSeconds) % marks.Count;
            var focused = Focused();
            if (focused != null && focused != Shown) clock = 0;
            var shown = focused ?? marks[turn];
            float local = focused != null ? Mathf.Repeat(clock, GestureSeconds) / GestureSeconds : Mathf.Repeat(clock, GestureSeconds) / GestureSeconds;
            Shown = shown;
            foreach (var mark in marks)
            {
                bool active = mark == shown;
                mark.Amount = Mathf.MoveTowards(mark.Amount, active ? 1 : 0, elapsed * 5);
                for (int index = 0; index < mark.Lit.Count; index++)
                    if (mark.Lit[index] != null) mark.Lit[index].color = Color.Lerp(mark.Rest[index], accent, mark.Amount);
                if (mark.TitleText != null) mark.TitleText.color = Color.Lerp(text, accent, mark.Amount);
                if (mark.Leader != null)
                {
                    var line = accent;
                    line.a = Mathf.Lerp(.28f, .95f, mark.Amount);
                    mark.Leader.color = line;
                }
                if (mark.Moving == null) continue;
                var offset = Vector2.zero;
                float press = 1;
                if (active) Play(mark, local, ref offset, ref press);
                mark.Moving.anchoredPosition = mark.Home + offset;
                mark.Moving.localScale = new Vector3(press, press, 1);
            }
            if (caption != null) caption.text = shown.Title + "  ·  " + shown.Action;
        }

        /// <summary>The gesture at a moment of its loop: each gesture of the control takes an equal share of the loop.</summary>
        private static void Play(Mark mark, float local, ref Vector2 offset, ref float press)
        {
            int count = mark.Gestures.Length;
            int index = Mathf.Min(count - 1, (int)(local * count));
            float t = local * count - index;
            // A soft rise and fall within each share, resting at both ends.
            float wave = Mathf.Sin(Mathf.Clamp01((t - .1f) / .8f) * Mathf.PI);
            switch (mark.Gestures[index])
            {
                case Gesture.Press: case Gesture.StickPress: press = 1 - .16f * wave; break;
                case Gesture.PushUp: offset = new Vector2(0, 11 * Mathf.Sin(t * Mathf.PI * 2)); break;
                case Gesture.PushSide: offset = new Vector2(11 * Mathf.Sin(t * Mathf.PI * 2), 0); break;
                case Gesture.Trigger: offset = new Vector2(0, -7 * wave); press = 1 - .06f * wave; break;
                case Gesture.Grip: offset = new Vector2(mark.Home.x > 0 ? 5 * wave : -5 * wave, 0); break;
            }
        }

        /// <summary>The control of the rebinding row in focus, on a screen.</summary>
        private Mark Focused()
        {
            if (Headset || EventSystem.current == null) return null;
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null) return null;
            var row = selected.GetComponentInParent<MuseumRebindRow>();
            if (row == null || row.Binding == null) return null;
            return marks.Find(mark => mark.Id == row.Binding.Id);
        }

        // ---- Shapes -----------------------------------------------------------------------------------------------------

        private Image Shape(string name, Sprite sprite, Vector2 position, Vector2 extent, Color color, Transform parent = null)
        {
            var image = MuseumWorldUi.Surface(name, parent != null ? parent : drawing, color);
            image.sprite = sprite;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = extent;
            return image;
        }

        private TMP_Text Text(string name, string value, Vector2 position, Vector2 extent, float fontSize, Color color,
            TextAlignmentOptions alignment, bool display = false, Transform parent = null)
        {
            var label = MuseumWorldUi.Label(name, parent != null ? parent : drawing, value, fontSize, color, display);
            label.alignment = alignment;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = extent;
            return label;
        }

        /// <summary>A thin line from one point of the drawing to another.</summary>
        private Image Line(string name, Vector2 from, Vector2 to)
        {
            var delta = to - from;
            var line = Shape(name, null, (from + to) * .5f, new Vector2(delta.magnitude, 2), accent);
            line.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            return line;
        }

        private static void Light(Mark mark, Graphic graphic)
        {
            mark.Lit.Add(graphic);
            mark.Rest.Add(graphic.color);
        }

        private static string Copy(string key) => MuseumInterfaceText.Current.Read(key);

        // ---- Headset: the two Quest controllers -------------------------------------------------------------------------

        /// <summary>Where a control of a controller stands, from its face centre; the outer side is +x on the right hand.</summary>
        private static Vector2 On(Vector2 centre, float side, float x, float y) => centre + new Vector2(side * x, y);

        private void BuildControllers()
        {
            // Names and what each control does are set large enough to read on a headset at the menu's distance.
            const float labelWidth = 250;
            var left = new Vector2(-104, 20);
            var right = new Vector2(104, 20);
            var leftMarks = new List<(Mark mark, Vector2 point)>();
            var rightMarks = new List<(Mark mark, Vector2 point)>();
            foreach (var (centre, side) in new[] { (left, -1f), (right, 1f) })
            {
                var marksOfHand = side < 0 ? leftMarks : rightMarks;
                string hand = side < 0 ? "left" : "right";
                // The handle under the face, the face with its controls, the trigger on top and the grip on the inner side.
                var handle = Shape("Handle_" + hand, MuseumUiSprites.Rounded(34), On(centre, side, 8, -84), new Vector2(76, 156), body);
                var grip = Shape("Grip_" + hand, MuseumUiSprites.Rounded(8), On(centre, side, -30, -76), new Vector2(18, 82), part);
                var trigger = Shape("Trigger_" + hand, MuseumUiSprites.Rounded(9), On(centre, side, -6, 104), new Vector2(66, 22), part);
                Shape("Face_" + hand, MuseumUiSprites.Rounded(48), On(centre, side, 0, 34), new Vector2(158, 136), body);
                var ring = Shape("StickRing_" + hand, MuseumUiSprites.Ring(64, 5), On(centre, side, -28, 52), new Vector2(62, 62), part);
                var knob = Shape("Stick_" + hand, MuseumUiSprites.Dot(64), On(centre, side, -28, 52), new Vector2(36, 36), part);
                var lower = Shape(side < 0 ? "ButtonX" : "ButtonA", MuseumUiSprites.Dot(64), On(centre, side, 34, 18), new Vector2(32, 32), part);
                var upper = Shape(side < 0 ? "ButtonY" : "ButtonB", MuseumUiSprites.Dot(64), On(centre, side, 50, 60), new Vector2(32, 32), part);
                Text("Letter", side < 0 ? "X" : "A", Vector2.zero, new Vector2(32, 32), 17, partText, TextAlignmentOptions.Center, true, lower.transform);
                Text("Letter", side < 0 ? "Y" : "B", Vector2.zero, new Vector2(32, 32), 17, partText, TextAlignmentOptions.Center, true, upper.transform);

                var stick = new Mark
                {
                    Id = "stick_" + hand, Title = Copy("controls_stick_" + hand), Action = Copy("controls_stick_" + hand + "_action"),
                    Gestures = side < 0 ? new[] { Gesture.PushUp, Gesture.StickPress } : new[] { Gesture.PushSide, Gesture.PushUp, Gesture.StickPress },
                    Moving = knob.rectTransform,
                };
                Light(stick, knob);
                Light(stick, ring);
                var gripMark = new Mark { Id = "grip_" + hand, Title = Copy("controls_grip"), Action = Copy("controls_grip_action"), Gestures = new[] { Gesture.Grip }, Moving = grip.rectTransform };
                Light(gripMark, grip);
                var lowerMark = new Mark
                {
                    Id = side < 0 ? "button_x" : "button_a", Title = Copy(side < 0 ? "controls_button_x" : "controls_button_a"),
                    Action = Copy(side < 0 ? "controls_button_x_action" : "controls_button_a_action"), Moving = lower.rectTransform,
                };
                Light(lowerMark, lower);
                var upperMark = new Mark
                {
                    Id = side < 0 ? "button_y" : "button_b", Title = Copy(side < 0 ? "controls_button_y" : "controls_button_b"),
                    Action = Copy(side < 0 ? "controls_button_y_action" : "controls_button_b_action"), Moving = upper.rectTransform,
                };
                Light(upperMark, upper);
                if (side > 0)
                {
                    var triggerMark = new Mark { Id = "trigger_right", Title = Copy("controls_trigger"), Action = Copy("controls_trigger_action"), Gestures = new[] { Gesture.Trigger }, Moving = trigger.rectTransform };
                    Light(triggerMark, trigger);
                    marksOfHand.Add((triggerMark, On(centre, side, 26, 104)));
                }
                else
                {
                    // Only the right trigger chooses; the left one is drawn quiet.
                    var menu = Shape("ButtonMenu", MuseumUiSprites.Dot(32), On(centre, side, 4, -6), new Vector2(18, 18), part);
                    var menuMark = new Mark { Id = "button_menu", Title = Copy("controls_button_menu"), Action = Copy("controls_button_menu_action"), Moving = menu.rectTransform };
                    Light(menuMark, menu);
                    marksOfHand.Add((menuMark, On(centre, side, 4, -6) + new Vector2(side * 12, 0)));
                }
                marksOfHand.Add((upperMark, On(centre, side, 66, 60)));
                marksOfHand.Add((stick, On(centre, side, 4, 52) + new Vector2(side * 12, 18)));
                marksOfHand.Add((lowerMark, On(centre, side, 50, 18)));
                marksOfHand.Add((gripMark, On(centre, side, 46, -100)));
            }
            // Names beside each hand, in the order of their controls from top to bottom, so no two leader lines cross.
            foreach (var (list, side) in new[] { (leftMarks, -1f), (rightMarks, 1f) })
            {
                list.Sort((a, b) => b.point.y.CompareTo(a.point.y));
                float top = design.y * .5f - 48, step = (design.y - 120) / Mathf.Max(1, list.Count - 1);
                float x = side * (design.x * .5f - labelWidth * .5f);
                for (int index = 0; index < list.Count; index++)
                {
                    var (mark, point) = list[index];
                    float y = top - step * index;
                    mark.TitleText = Text("Name_" + mark.Id, mark.Title, new Vector2(x, y + 16), new Vector2(labelWidth, 30), 24, text,
                        side < 0 ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft, true);
                    mark.ActionText = Text("Does_" + mark.Id, mark.Action, new Vector2(x, y - 30), new Vector2(labelWidth, 66), 19, muted,
                        side < 0 ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft);
                    var end = new Vector2(x - side * (labelWidth * .5f + 8), y + 8);
                    mark.Leader = Line("Leader_" + mark.Id, point, end);
                    mark.Leader.transform.SetAsFirstSibling();
                    marks.Add(mark);
                }
            }
            // The tour of gestures goes in the order the visit needs them.
            string[] order = { "trigger_right", "stick_left", "stick_right", "button_a", "button_b", "button_x", "button_y", "button_menu", "grip_right", "grip_left" };
            marks.Sort((a, b) => Array.IndexOf(order, a.Id).CompareTo(Array.IndexOf(order, b.Id)));
            foreach (var mark in marks) mark.Home = mark.Moving.anchoredPosition;
        }

        // ---- Screen: keyboard and mouse ---------------------------------------------------------------------------------

        private const float Unit = 40, Gap = 4;
        /// <summary>The main block of a keyboard, row by row: control path and width in keys; an empty path leaves a gap.</summary>
        private static readonly (string path, float width)[][] Rows =
        {
            new[] { ("escape", 1f), ("", .5f), ("1", 1f), ("2", 1f), ("3", 1f), ("4", 1f), ("5", 1f), ("6", 1f), ("7", 1f), ("8", 1f), ("9", 1f), ("0", 1f), ("backspace", 2f) },
            new[] { ("tab", 1.5f), ("q", 1f), ("w", 1f), ("e", 1f), ("r", 1f), ("t", 1f), ("y", 1f), ("u", 1f), ("i", 1f), ("o", 1f), ("p", 1f), ("", .5f), ("enter", 1.5f) },
            new[] { ("capsLock", 1.75f), ("a", 1f), ("s", 1f), ("d", 1f), ("f", 1f), ("g", 1f), ("h", 1f), ("j", 1f), ("k", 1f), ("l", 1f), ("semicolon", 1f) },
            new[] { ("leftShift", 2.25f), ("z", 1f), ("x", 1f), ("c", 1f), ("v", 1f), ("b", 1f), ("n", 1f), ("m", 1f), ("comma", 1f), ("period", 1f) },
            new[] { ("leftCtrl", 1.5f), ("", 2f), ("space", 6f) },
        };
        private static readonly (string path, int column, int row)[] Navigation =
        {
            ("pageUp", 0, 0), ("pageDown", 2, 0), ("upArrow", 1, 3), ("leftArrow", 0, 4), ("downArrow", 1, 4), ("rightArrow", 2, 4),
        };

        private void BuildKeyboard()
        {
            var keys = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            float rowsTop = design.y * .5f - 10;
            float left = -design.x * .5f + 10;
            for (int row = 0; row < Rows.Length; row++)
            {
                float x = left;
                float y = rowsTop - row * (Unit + Gap) - Unit * .5f;
                foreach (var (path, keyWidth) in Rows[row])
                {
                    float extent = keyWidth * Unit + (keyWidth - 1) * Gap;
                    if (path.Length > 0) keys[path] = Key(path, new Vector2(x + extent * .5f, y), new Vector2(extent, Unit));
                    x += extent + Gap;
                }
            }
            float navigationLeft = left + 14.5f * (Unit + Gap);
            foreach (var (path, column, row) in Navigation)
                keys[path] = Key(path, new Vector2(navigationLeft + column * (Unit + Gap) + Unit * .5f, rowsTop - row * (Unit + Gap) - Unit * .5f), new Vector2(Unit, Unit));
            // The mouse: two buttons over a rounded body, the wheel between them.
            var mouseCentre = new Vector2(design.x * .5f - 70, rowsTop - 2 * (Unit + Gap) - 10);
            Shape("Mouse", MuseumUiSprites.Rounded(48), mouseCentre + new Vector2(0, -28), new Vector2(104, 160), body);
            var leftButton = Shape("MouseLeft", MuseumUiSprites.Rounded(14), mouseCentre + new Vector2(-24, 28), new Vector2(44, 58), part);
            var rightButton = Shape("MouseRight", MuseumUiSprites.Rounded(14), mouseCentre + new Vector2(24, 28), new Vector2(44, 58), part);
            var wheel = Shape("MouseWheel", MuseumUiSprites.Rounded(5), mouseCentre + new Vector2(0, 30), new Vector2(10, 30), Color.Lerp(part, text, .3f));

            // Every key a visitor may change, in the order of the list below the drawing, then the fixed controls.
            var bindings = MuseumInput.Current.Bindings;
            foreach (var binding in bindings.All)
            {
                if (!binding.Rebindable) continue;
                string path = binding.Path ?? "";
                const string prefix = "<Keyboard>/";
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var mark = new Mark
                {
                    Id = binding.Id, Title = MuseumInputLabels.Path(path), Action = MuseumInterfaceText.Get(binding.LabelKey),
                };
                if (keys.TryGetValue(path.Substring(prefix.Length), out var key))
                {
                    mark.Moving = key.rectTransform;
                    Light(mark, key);
                    // A key with something to do stands out from the rest even while another plays its gesture.
                    var bound = Color.Lerp(part, accent, .28f);
                    mark.Rest[0] = bound;
                    key.color = bound;
                }
                marks.Add(mark);
            }
            AddFixed("mouse_left", Copy("controls_mouse_left"), Copy("controls_mouse_left_action"), leftButton);
            AddFixed("mouse_right", Copy("controls_mouse_right"), Copy("controls_mouse_right_action"), rightButton);
            AddFixed("mouse_wheel", Copy("controls_mouse_wheel"), Copy("controls_mouse_wheel_action"), wheel);
            caption = Text("Caption", "", new Vector2(0, -design.y * .5f + 22), new Vector2(design.x - 40, 36), 22, text, TextAlignmentOptions.Center);
            foreach (var mark in marks) if (mark.Moving != null) mark.Home = mark.Moving.anchoredPosition;
        }

        /// <summary>Short names for the keys whose full names do not fit on a key cap.</summary>
        private static readonly Dictionary<string, string> Caps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "escape", "Esc" }, { "backspace", "Borrar" }, { "tab", "Tab" }, { "enter", "Enter" }, { "capsLock", "Bloq" },
            { "leftShift", "Mayús" }, { "leftCtrl", "Ctrl" }, { "space", "Espacio" }, { "pageUp", "Re Pág" }, { "pageDown", "Av Pág" },
            { "upArrow", "↑" }, { "downArrow", "↓" }, { "leftArrow", "←" }, { "rightArrow", "→" },
        };

        private Image Key(string path, Vector2 position, Vector2 extent)
        {
            var key = Shape("Key_" + path, MuseumUiSprites.Rounded(7), position, extent, part);
            string name = Caps.TryGetValue(path, out var cap) ? cap : MuseumInputLabels.Path("<Keyboard>/" + path);
            var label = Text("Cap", name, Vector2.zero, extent - new Vector2(6, 4), name.Length > 2 ? 12 : 17, partText, TextAlignmentOptions.Center, false, key.transform);
            label.fontStyle = FontStyles.Bold;
            return key;
        }

        private void AddFixed(string id, string title, string action, Image shape)
        {
            var mark = new Mark { Id = id, Title = title, Action = action, Moving = shape.rectTransform };
            Light(mark, shape);
            marks.Add(mark);
        }
    }
}
