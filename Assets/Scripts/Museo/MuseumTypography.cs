using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The shared visual language of the Web and the visit (coordination/design-tokens-20261018.json): titles in Bodoni
    /// Moda, reading and controls in Instrument Sans. Texts set in the museum's original sans take the new faces at
    /// runtime; the menu prefab and the scene keep their authored fonts, so a missing font asset changes nothing.
    /// </summary>
    public static class MuseumTypography
    {
        public const string DisplayFont = "MuseumDisplay", BodyFont = "MuseumBody";
        /// <summary>Texts of this size or larger, or named as titles, are titles.</summary>
        public const float TitleSize = 36;
        private const string OriginalFont = "MuseumSans";
        private static TMP_FontAsset display, body;

        /// <summary>Gives every text under <paramref name="root"/> that still uses the original sans its new face.</summary>
        public static void Apply(Transform root)
        {
            if (root == null) return;
            display ??= Resources.Load<TMP_FontAsset>(DisplayFont);
            body ??= Resources.Load<TMP_FontAsset>(BodyFont);
            if (display == null || body == null) return;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null || text.font.name != OriginalFont) continue;
                bool title = text.name == "Title" || text.fontSize >= TitleSize;
                bool control = text.GetComponentInParent<Selectable>() != null;
                text.font = title ? display : body;
                // The tokens ask for a firm display weight (600) and firm controls; the faces ship one weight, so
                // the bold style gives them that weight.
                if (title || control) text.fontStyle |= FontStyles.Bold;
            }
        }
    }
}
