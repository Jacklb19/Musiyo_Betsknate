using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The palette of the menus on the screen of the visit: surfaces, pills behind prompts and choices.</summary>
    public sealed class MuseumHudSkinTests
    {
        [Test]
        public void SurfacesAndChoicesTakeTheRoundedSpritesAndThePalette()
        {
            var canvas = new GameObject("SkinTestCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var screen = new GameObject("Screen", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                screen.transform.SetParent(canvas.transform, false);
                MuseumHudSkin.Surface(screen);
                Assert.That(screen.sprite, Is.SameAs(MuseumUiSprites.Rounded(MuseumHudSkin.PanelRadius)));
                Assert.That(screen.type, Is.EqualTo(Image.Type.Sliced));
                var panel = MuseumHudSkin.Panel;
                Assert.That(new Color(screen.color.r, screen.color.g, screen.color.b), Is.EqualTo(new Color(panel.r, panel.g, panel.b)));
                var button = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
                button.transform.SetParent(canvas.transform, false);
                var caption = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                caption.transform.SetParent(button.transform, false);
                MuseumHudSkin.Choice(button, true);
                Assert.That(button.GetComponent<MuseumMenuChoice>(), Is.Not.Null, "A choice takes the focus under the pointer, as in the menu.");
                Assert.That(button.colors.normalColor.a, Is.GreaterThan(button.colors.disabledColor.a));
                Assert.That(caption.color, Is.EqualTo(MuseumHudSkin.Text));
                Assert.That(caption.fontStyle & FontStyles.Bold, Is.EqualTo(FontStyles.Bold));
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        [Test]
        public void APillHugsItsPromptAndHidesWithIt()
        {
            var canvas = new GameObject("SkinTestCanvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600, 900);
                var prompt = new GameObject("Focus", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                prompt.transform.SetParent(canvas.transform, false);
                prompt.rectTransform.sizeDelta = new Vector2(560, 80);
                prompt.alignment = TextAlignmentOptions.Center;
                prompt.fontSize = 26;
                prompt.text = ".";
                var pill = MuseumHudSkin.PillBehind(prompt);
                Assert.That(pill.GetSiblingIndex(), Is.EqualTo(prompt.transform.GetSiblingIndex() - 1), "The pill is drawn under the text.");
                Assert.That(pill.gameObject.activeSelf, Is.False, "A dot is the empty prompt.");
                prompt.text = "Punto de prueba";
                MuseumHudSkin.Fit(pill, prompt);
                Assert.That(pill.gameObject.activeSelf, Is.True);
                Assert.That(pill.sizeDelta.x, Is.GreaterThan(MuseumHudSkin.PillPaddingX * 2).And.LessThanOrEqualTo(560));
                Assert.That(pill.sizeDelta.y, Is.GreaterThan(MuseumHudSkin.PillPaddingY * 2));
                float narrow = pill.sizeDelta.x;
                prompt.text = "Un punto con un nombre bastante más largo";
                MuseumHudSkin.Fit(pill, prompt);
                Assert.That(pill.sizeDelta.x, Is.GreaterThan(narrow), "A longer prompt gets a wider pill.");
                prompt.text = "";
                MuseumHudSkin.Fit(pill, prompt);
                Assert.That(pill.gameObject.activeSelf, Is.False);
                Assert.That(pill.GetComponent<Image>().sprite, Is.SameAs(MuseumUiSprites.Rounded(MuseumHudSkin.PillRadius)));
                Assert.That(pill.GetComponent<Image>().raycastTarget, Is.False);
            }
            finally { Object.DestroyImmediate(canvas); }
        }
    }
}
