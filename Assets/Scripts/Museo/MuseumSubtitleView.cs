using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Where and how the narration's subtitles are shown (owner, 25/10/2026: "subtítulos abajo del todo y centrados, sin
    /// recuadro"). On a screen they stand centred at the very bottom of the view, across its width with side margins, with
    /// no box behind them: light letters with a dark outline and a soft shadow read over any part of the museum. On a
    /// headset, where the screen's canvas is only drawn with a menu, they stand on a small canvas of their own low in the
    /// view, carried by the head, at the same size the visitor chose.
    /// </summary>
    public sealed class MuseumSubtitleView : MonoBehaviour
    {
        /// <summary>Distance from the bottom of the screen and from its sides, in interface units.</summary>
        public const float BottomMargin = 36, SideMargin = 96, Height = 170;
        /// <summary>Outline of the letters and their soft shadow, in the text shader's own units.</summary>
        public const float OutlineWidth = .22f, ShadowSoftness = .55f, ShadowDilate = .35f, ShadowOffset = -.6f;
        /// <summary>
        /// On a headset: distance in front of the eyes, drop below them and width of the subtitle line, in metres. The drop
        /// puts the line about 28 degrees below the gaze: low in the headset's view and below a reading stand's page read
        /// face on, so it does not cover the page's text.
        /// </summary>
        public const float SpatialDistance = .65f, SpatialDrop = .345f, SpatialWidth = .62f;
        /// <summary>On a headset, metres per interface unit: the shipped size reads about 1.7 cm high at 0.65 m.</summary>
        public const float SpatialUnit = .0007f;
        private TMP_Text text;

        public TMP_Text Text => text;
        /// <summary>The canvas that carries the subtitles on a headset, or null on a screen.</summary>
        public Canvas SpatialCanvas { get; private set; }

        public void Configure(TMP_Text subtitles)
        {
            text = subtitles;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, BottomMargin);
            rect.sizeDelta = new Vector2(-SideMargin * 2, Height);
            text.alignment = TextAlignmentOptions.Bottom;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            Legible(text);
        }

        /// <summary>Light letters, a dark outline and a soft dark shadow under them: readable with no box behind.</summary>
        public static void Legible(TMP_Text subtitles)
        {
            subtitles.color = MuseumHudSkin.Text;
            subtitles.outlineWidth = OutlineWidth;
            subtitles.outlineColor = new Color32(20, 14, 22, 255);
            // The instance material carries the shadow; the shared font material stays as it is for every other text.
            var material = subtitles.fontMaterial;
            if (material.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, .8f));
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0);
                material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, ShadowOffset);
                material.SetFloat(ShaderUtilities.ID_UnderlayDilate, ShadowDilate);
                material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, ShadowSoftness);
            }
        }

        /// <summary>
        /// On a headset the subtitles move to a canvas of their own carried by the head, low in the view and close enough
        /// that a reading stand in front of the visitor does not hide them.
        /// </summary>
        public void UseSpatial(Camera head)
        {
            if (text == null || head == null || SpatialCanvas != null) return;
            var root = new GameObject("SpatialSubtitles", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(head.transform, false);
            var rect = (RectTransform)root.transform;
            rect.localPosition = new Vector3(0, -SpatialDrop, SpatialDistance);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * SpatialUnit;
            rect.sizeDelta = new Vector2(SpatialWidth / SpatialUnit, Height);
            SpatialCanvas = root.GetComponent<Canvas>();
            SpatialCanvas.renderMode = RenderMode.WorldSpace;
            SpatialCanvas.worldCamera = head;
            // Drawn after the museum's other interface, so a panel behind does not cover the line being heard.
            SpatialCanvas.sortingOrder = 50;
            text.transform.SetParent(rect, false);
            var line = text.rectTransform;
            line.anchorMin = new Vector2(0, 0);
            line.anchorMax = new Vector2(1, 0);
            line.pivot = new Vector2(.5f, 0);
            line.anchoredPosition = Vector2.zero;
            line.sizeDelta = new Vector2(0, Height);
        }
    }
}
