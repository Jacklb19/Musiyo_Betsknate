using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The narration's subtitles as the owner asked on 25/10/2026: at the very bottom, centred, with no box.</summary>
    public sealed class SubtitleViewTests
    {
        private GameObject root;
        private TextMeshProUGUI text;
        private MuseumSubtitleView view;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SubtitleTest", typeof(RectTransform), typeof(Canvas));
            var line = new GameObject("NarrationSubtitles", typeof(RectTransform), typeof(TextMeshProUGUI));
            line.transform.SetParent(root.transform, false);
            text = line.GetComponent<TextMeshProUGUI>();
            view = root.AddComponent<MuseumSubtitleView>();
            view.Configure(text);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void TheSubtitlesStandCentredAtTheBottomAcrossTheView()
        {
            var rect = text.rectTransform;
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0, 0)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(1, 0)), "Across the whole width of the view.");
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(.5f, 0)));
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(MuseumSubtitleView.BottomMargin), "At the very bottom.");
            Assert.That(rect.sizeDelta.x, Is.EqualTo(-MuseumSubtitleView.SideMargin * 2), "With a margin on each side.");
            Assert.That(text.alignment, Is.EqualTo(TextAlignmentOptions.Bottom), "Centred, its last line on the bottom.");
            Assert.That(text.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal), "A long line wraps instead of being cut.");
        }

        [Test]
        public void TheLettersCarryTheirOwnOutlineAndShadowInsteadOfABox()
        {
            Assert.That(root.GetComponentsInChildren<UnityEngine.UI.Image>(true), Is.Empty, "No box behind the subtitles.");
            Assert.That(text.outlineWidth, Is.EqualTo(MuseumSubtitleView.OutlineWidth).Within(.001f));
            Assert.That(text.color.grayscale, Is.GreaterThan(.8f), "Light letters.");
            var material = text.fontMaterial;
            if (material.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                Assert.That(material.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay), Is.True, "A soft shadow under the letters.");
                Assert.That(material.GetColor(ShaderUtilities.ID_UnderlayColor).a, Is.GreaterThan(.5f));
            }
        }

        [Test]
        public void OnAHeadsetTheyStandLowInTheViewOnTheirOwnCanvas()
        {
            var head = new GameObject("Head", typeof(Camera)).GetComponent<Camera>();
            head.transform.SetParent(root.transform);
            view.UseSpatial(head);
            var canvas = view.SpatialCanvas;
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(canvas.transform.parent, Is.SameAs(head.transform), "Carried by the head.");
            Assert.That(text.transform.parent, Is.SameAs(canvas.transform));
            var local = canvas.transform.localPosition;
            Assert.That(local.z, Is.EqualTo(MuseumSubtitleView.SpatialDistance).Within(.001f));
            Assert.That(local.y, Is.LessThan(0), "Below the eyes.");
            Assert.That(local.x, Is.EqualTo(0).Within(.001f), "Centred.");
            float width = ((RectTransform)canvas.transform).sizeDelta.x * canvas.transform.localScale.x;
            Assert.That(width, Is.EqualTo(MuseumSubtitleView.SpatialWidth).Within(.001f));
            // The shipped size reads well at that distance: between 1.4 and 2.4 cm.
            Assert.That(24 * MuseumSubtitleView.SpatialUnit, Is.InRange(.014f, .024f));
            view.UseSpatial(head);
            Assert.That(head.GetComponentsInChildren<Canvas>().Length, Is.EqualTo(1), "Asking twice builds one canvas.");
        }
    }
}
