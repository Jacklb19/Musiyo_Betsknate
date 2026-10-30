using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Two small icon controls on the header line of a point's screen: pause and resume as one toggle, and restart (owner,
    /// 25/10/2026: "controles pequeños de pausa, reanudar y reiniciar que no tapen el texto"). They stand above the text,
    /// never over it, are chosen with the same pointer as the rest of the screen, and keep the keys: a short hint beside
    /// them names the keys on a screen. Each control says what it does in its <see cref="Label"/>, for whoever cannot see
    /// the icon, and the hint shows the label of the control under the pointer. A third control, before them, hears the
    /// narrations at 1.5x and back at 1x (owner, 28/10/2026); it is lit while the narration is heard faster.
    /// </summary>
    public sealed class MuseumNarrationButtons : MonoBehaviour
    {
        public enum Glyph { Play, Pause, Restart, Faster }

        /// <summary>Height of the header line the controls stand on, and the size of each control, in interface units.</summary>
        public const float RowHeight = 54, ButtonSize = 46;
        public const float HintSize = 17;
        private static Sprite play, pause, restart, faster;
        private RectTransform row;
        private Image toggleIcon, speedIcon;
        private TMP_Text hint;
        private Func<Selectable> aimed;
        private string idleHint = "";

        public RectTransform Row => row;
        public Button Toggle { get; private set; }
        public Button Restart { get; private set; }
        /// <summary>The 1.5x control; null when the screen was built without one.</summary>
        public Button Speed { get; private set; }
        /// <summary>What the speed control does now, said in words.</summary>
        public string SpeedLabel { get; private set; } = "";
        /// <summary>True while the speed control shows the narration heard at 1.5x.</summary>
        public bool SpeedLit { get; private set; }
        /// <summary>What the toggle does now, said in words: pause, resume, listen or repeat.</summary>
        public string ToggleLabel { get; private set; } = "";
        public string RestartLabel { get; private set; } = "";
        public TMP_Text Hint => hint;
        public Glyph ToggleGlyph { get; private set; } = Glyph.Pause;

        /// <summary>
        /// Builds the controls on the top right of <paramref name="panel"/>, at <paramref name="inset"/> from its corner;
        /// the speed control only when <paramref name="toggleSpeed"/> is given.
        /// </summary>
        public void Configure(RectTransform panel, Vector2 inset, TMP_Text hintStyle, Action toggle, Action restartNarration,
            Func<Selectable> pointedAt, Action toggleSpeed = null)
        {
            aimed = pointedAt;
            row = new GameObject("NarrationButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            row.SetParent(panel, false);
            row.GetComponent<LayoutElement>().ignoreLayout = true;
            row.anchorMin = row.anchorMax = row.pivot = Vector2.one;
            row.anchoredPosition = new Vector2(inset.x, inset.y);
            row.sizeDelta = new Vector2(560, RowHeight);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            hint = Instantiate(hintStyle, row);
            hint.name = "NarrationHint";
            hint.richText = false;
            hint.fontSize = HintSize;
            hint.fontStyle = FontStyles.Normal;
            hint.alignment = TextAlignmentOptions.Right;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.overflowMode = TextOverflowModes.Ellipsis;
            hint.color = MuseumHudSkin.Muted;
            hint.raycastTarget = false;
            hint.rectTransform.sizeDelta = new Vector2(560 - (ButtonSize + 10) * (toggleSpeed != null ? 3 : 2), RowHeight);
            if (hint.TryGetComponent<LayoutElement>(out var hintLayout)) hintLayout.minHeight = 0;
            if (toggleSpeed != null)
            {
                Speed = Control("NarrationSpeed", toggleSpeed, out speedIcon);
                speedIcon.sprite = Icon(Glyph.Faster);
                Speed.gameObject.SetActive(false);
            }
            Toggle = Control("NarrationPlayPause", toggle, out toggleIcon);
            Restart = Control("NarrationRestart", restartNarration, out var restartIcon);
            restartIcon.sprite = Icon(Glyph.Restart);
            row.gameObject.SetActive(false);
        }

        private Button Control(string name, Action action, out Image icon)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            item.transform.SetParent(row, false);
            ((RectTransform)item.transform).sizeDelta = Vector2.one * ButtonSize;
            var layout = item.GetComponent<LayoutElement>();
            layout.preferredWidth = layout.preferredHeight = ButtonSize;
            var button = item.GetComponent<Button>();
            button.onClick.AddListener(() => action());
            var glyph = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            glyph.transform.SetParent(item.transform, false);
            var rect = (RectTransform)glyph.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.one * (ButtonSize * .5f);
            icon = glyph.GetComponent<Image>();
            icon.color = MuseumHudSkin.Text;
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            return button;
        }

        /// <summary>Shows or hides the controls and says what the toggle does now.</summary>
        public void Show(bool shown, Glyph toggleGlyph, string toggleLabel, string restartLabel, string keysHint)
        {
            if (row == null) return;
            if (row.gameObject.activeSelf != shown) row.gameObject.SetActive(shown);
            ToggleGlyph = toggleGlyph;
            ToggleLabel = toggleLabel ?? "";
            RestartLabel = restartLabel ?? "";
            idleHint = keysHint ?? "";
            toggleIcon.sprite = Icon(toggleGlyph);
            RefreshHint();
        }

        /// <summary>Shows or hides the speed control, lit while the narration is heard at 1.5x.</summary>
        public void ShowSpeed(bool shown, bool fast, string label)
        {
            if (Speed == null) return;
            if (Speed.gameObject.activeSelf != shown) Speed.gameObject.SetActive(shown);
            SpeedLit = fast;
            SpeedLabel = label ?? "";
            speedIcon.color = fast ? MuseumHudSkin.Accent : MuseumHudSkin.Text;
            RefreshHint();
        }

        private void LateUpdate() => RefreshHint();

        /// <summary>The label of the control under the pointer, or else the keys.</summary>
        public void RefreshHint()
        {
            if (hint == null) return;
            var pointed = aimed?.Invoke();
            string text = pointed == Toggle ? ToggleLabel : pointed == Restart ? RestartLabel
                : pointed != null && pointed == Speed ? SpeedLabel : idleHint;
            if (hint.text != text) hint.text = text;
        }

        /// <summary>A small white icon made once, so the controls need no texture asset.</summary>
        public static Sprite Icon(Glyph glyph)
        {
            switch (glyph)
            {
                case Glyph.Play: return play ??= Draw("NarrationPlay", (x, y) => Triangle(x, y));
                case Glyph.Pause: return pause ??= Draw("NarrationPause", (x, y) => Bars(x, y));
                case Glyph.Faster: return faster ??= Draw("NarrationFaster", (x, y) => Forward(x, y));
                default: return restart ??= Draw("NarrationRestart", (x, y) => Arrow(x, y));
            }
        }

        private const int IconPixels = 64;

        private static Sprite Draw(string name, Func<float, float, float> coverage)
        {
            var texture = new Texture2D(IconPixels, IconPixels, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var pixels = new Color32[IconPixels * IconPixels];
            for (int y = 0; y < IconPixels; y++)
                for (int x = 0; x < IconPixels; x++)
                {
                    // Four samples a pixel smooth the edges.
                    float covered = 0;
                    for (int sample = 0; sample < 4; sample++)
                        covered += coverage((x + .25f + (sample % 2) * .5f) / IconPixels * 2 - 1, (y + .25f + (sample / 2) * .5f) / IconPixels * 2 - 1);
                    pixels[y * IconPixels + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(covered / 4) * 255));
                }
            texture.SetPixels32(pixels);
            // Kept readable: four icons of 64 pixels cost nothing and can be checked.
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, IconPixels, IconPixels), new Vector2(.5f, .5f), IconPixels);
            sprite.name = name;
            return sprite;
        }

        /// <summary>A triangle pointing right, in coordinates from -1 to 1.</summary>
        private static float Triangle(float x, float y)
            => x >= -.55f && x <= .75f && Mathf.Abs(y) <= (.75f - x) / 1.3f * .85f ? 1 : 0;

        /// <summary>Two small triangles pointing right, one after the other: faster.</summary>
        private static float Forward(float x, float y)
        {
            bool Head(float left) => x >= left && x <= left + .7f && Mathf.Abs(y) <= (left + .7f - x) / .7f * .6f;
            return Head(-.75f) || Head(.05f) ? 1 : 0;
        }

        private static float Bars(float x, float y)
            => Mathf.Abs(y) <= .72f && (x >= -.6f && x <= -.18f || x >= .18f && x <= .6f) ? 1 : 0;

        /// <summary>A ring open at the top right, with an arrow head at its open end: start again.</summary>
        private static float Arrow(float x, float y)
        {
            float radius = Mathf.Sqrt(x * x + y * y);
            float angle = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
            bool ring = radius >= .48f && radius <= .72f && !(angle > 20 && angle < 80);
            // Arrow head at the ring's end near 80 degrees, pointing clockwise toward the gap.
            var tip = new Vector2(Mathf.Cos(80 * Mathf.Deg2Rad), Mathf.Sin(80 * Mathf.Deg2Rad)) * .6f;
            var local = new Vector2(x, y) - tip;
            var along = new Vector2(Mathf.Cos(-10 * Mathf.Deg2Rad), Mathf.Sin(-10 * Mathf.Deg2Rad));
            var across = new Vector2(-along.y, along.x);
            float forward = Vector2.Dot(local, along), side = Vector2.Dot(local, across);
            bool head = forward >= -.05f && forward <= .32f && Mathf.Abs(side) <= (.32f - forward) * .9f;
            return ring || head ? 1 : 0;
        }
    }
}
