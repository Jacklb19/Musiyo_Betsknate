using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The palette of the menus brought to the screen of the visit at runtime: the point screen, the orientation, the
    /// subtitles and the small prompts take the night surface, rounded corners and quiet choices of the shared tokens
    /// (coordination/design-tokens-20261018.json). The authored scene keeps its own colours; nothing is saved.
    /// </summary>
    public static class MuseumHudSkin
    {
        public const int PanelRadius = 18, PillRadius = 14, ChoiceRadius = 10;
        /// <summary>Space a prompt keeps around its text inside its pill.</summary>
        public const float PillPaddingX = 26, PillPaddingY = 12;

        public static Color Night => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.BackdropColor);
        public static Color Panel => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.PanelColor);
        public static Color Text => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.TextColor);
        public static Color Accent => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
        public static Color Muted => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.MutedTextColor);

        /// <summary>A screen or a card: the panel surface with rounded corners.</summary>
        public static void Surface(Image image, float alpha = .96f)
        {
            if (image == null) return;
            image.sprite = MuseumUiSprites.Rounded(PanelRadius);
            image.type = Image.Type.Sliced;
            var color = Panel;
            color.a = alpha;
            image.color = color;
        }

        /// <summary>A small dark pill behind a line of text, so a prompt reads over any part of the museum.</summary>
        public static void Pill(Image image, float alpha = .82f)
        {
            if (image == null) return;
            image.sprite = MuseumUiSprites.Rounded(PillRadius);
            image.type = Image.Type.Sliced;
            var color = Night;
            color.a = alpha;
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>A choice of the visit screen, in the manner of the menu: a quiet rounded surface that warms under the pointer.</summary>
        public static void Choice(Button button, bool primary = false)
        {
            if (button == null) return;
            if (button.TryGetComponent<Image>(out var surface))
            {
                surface.sprite = MuseumUiSprites.Rounded(ChoiceRadius);
                surface.type = Image.Type.Sliced;
                surface.color = Color.white;
            }
            var accent = Accent;
            var colors = button.colors;
            colors.normalColor = primary ? new Color(accent.r, accent.g, accent.b, .22f) : new Color(1, 1, 1, .07f);
            colors.highlightedColor = colors.selectedColor = new Color(accent.r, accent.g, accent.b, .42f);
            colors.pressedColor = new Color(accent.r, accent.g, accent.b, .6f);
            colors.disabledColor = new Color(1, 1, 1, .03f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = MuseumMenuMotion.InteractionSeconds * .75f;
            button.colors = colors;
            var caption = button.GetComponentInChildren<TMP_Text>(true);
            if (caption != null)
            {
                caption.color = Text;
                caption.fontStyle |= FontStyles.Bold;
            }
            if (button.GetComponent<MuseumMenuChoice>() == null) button.gameObject.AddComponent<MuseumMenuChoice>();
        }

        /// <summary>
        /// A pill placed behind a free-standing prompt and sized to its text each time the text changes. The pill stands
        /// just before the text among its siblings, so it is drawn under it.
        /// </summary>
        public static RectTransform PillBehind(TMP_Text text, float alpha = .82f)
        {
            if (text == null) return null;
            var pill = new GameObject(text.name + "Pill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            pill.SetParent(text.transform.parent, false);
            pill.SetSiblingIndex(text.transform.GetSiblingIndex());
            Pill(pill.GetComponent<Image>(), alpha);
            Fit(pill, text);
            return pill;
        }

        /// <summary>Sizes a pill to the text it stands behind; hidden while the text is empty.</summary>
        public static void Fit(RectTransform pill, TMP_Text text)
        {
            if (pill == null || text == null) return;
            bool shown = text.gameObject.activeSelf && !string.IsNullOrWhiteSpace(text.text) && text.text != ".";
            if (pill.gameObject.activeSelf != shown) pill.gameObject.SetActive(shown);
            if (!shown) return;
            var rect = text.rectTransform;
            pill.anchorMin = rect.anchorMin; pill.anchorMax = rect.anchorMax; pill.pivot = rect.pivot;
            var size = text.GetPreferredValues(text.text, Mathf.Max(1, rect.rect.width - PillPaddingX * 2), Mathf.Infinity);
            float width = Mathf.Min(rect.rect.width, size.x + PillPaddingX * 2);
            float height = size.y + PillPaddingY * 2;
            // The pill hugs the text wherever the text is aligned inside its box.
            float align = text.alignment.ToString().Contains("Left") ? 0 : text.alignment.ToString().Contains("Right") ? 1 : .5f;
            float vertical = text.alignment.ToString().StartsWith("Top") ? 1 : text.alignment.ToString().StartsWith("Bottom") ? 0 : .5f;
            pill.sizeDelta = new Vector2(width, height);
            var offset = new Vector2((align - rect.pivot.x) * rect.rect.width - (align - pill.pivot.x) * width,
                (vertical - rect.pivot.y) * rect.rect.height - (vertical - pill.pivot.y) * height);
            pill.anchoredPosition = rect.anchoredPosition + offset;
        }
    }
}
