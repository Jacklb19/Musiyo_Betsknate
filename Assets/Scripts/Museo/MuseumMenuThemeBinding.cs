using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public enum MuseumMenuColorToken { None, Backdrop, Panel, Text, Accent }
    public enum MuseumMenuFontToken { None, Body, Title }

    /// <summary>Only explicitly bound style properties use configuration. All other properties remain authored.</summary>
    public sealed class MuseumMenuThemeBinding : MonoBehaviour
    {
        [Tooltip("None preserves the Inspector color; other values override Graphic.color from the configuration.")]
        public MuseumMenuColorToken ColorToken;
        [Tooltip("None preserves the Inspector font size; Body/Title use configuration.")]
        public MuseumMenuFontToken FontToken;
        [Tooltip("Overrides only selectable normal/highlighted/selected colors using PanelColor/SelectedColor.")]
        public bool BindSelectableColors;

        private void OnEnable() => Apply();
        public void Apply()
        {
            var settings = MuseumExperienceConfiguration.Current;
            if (ColorToken != MuseumMenuColorToken.None && TryGetComponent<Graphic>(out var graphic))
            {
                var color = ColorToken == MuseumMenuColorToken.Backdrop ? settings.BackdropColor
                    : ColorToken == MuseumMenuColorToken.Panel ? settings.PanelColor
                    : ColorToken == MuseumMenuColorToken.Accent ? settings.AccentColor : settings.TextColor;
                graphic.color = MuseumExperienceConfiguration.ColorValue(color);
            }
            if (FontToken != MuseumMenuFontToken.None && TryGetComponent<TMP_Text>(out var text))
                text.fontSize = FontToken == MuseumMenuFontToken.Title ? settings.TitleSize : settings.BodySize;
            if (BindSelectableColors && TryGetComponent<Selectable>(out var selectable))
            {
                var colors = selectable.colors;
                colors.normalColor = MuseumExperienceConfiguration.ColorValue(selectable is Slider ? settings.TextColor : settings.PanelColor);
                colors.highlightedColor = colors.selectedColor = MuseumExperienceConfiguration.ColorValue(settings.SelectedColor);
                selectable.colors = colors;
            }
        }
    }
}
