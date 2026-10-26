using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The narration's small icon controls on a screen's header line (owner, 25/10/2026).</summary>
    public sealed class NarrationButtonsTests
    {
        private GameObject root;
        private MuseumNarrationButtons buttons;
        private Selectable pointed;
        private int toggles, restarts;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("NarrationButtonsTest", typeof(RectTransform), typeof(Canvas));
            var panel = (RectTransform)root.transform;
            panel.sizeDelta = new Vector2(832, 700);
            var style = new GameObject("Style", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            style.transform.SetParent(panel, false);
            buttons = root.AddComponent<MuseumNarrationButtons>();
            buttons.Configure(panel, new Vector2(-16, -16), style, () => toggles++, () => restarts++, () => pointed);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void TwoSmallIconsStandOnTheTopRightAndDoWhatTheySay()
        {
            Assert.That(buttons.Row.gameObject.activeSelf, Is.False, "Nothing shows before a narration can play.");
            buttons.Show(true, MuseumNarrationButtons.Glyph.Pause, "Pausar narración (Espacio)", "Reiniciar narración (R)", "Espacio: pausa · R: reinicia");
            Assert.That(buttons.Row.gameObject.activeSelf, Is.True);
            Assert.That(buttons.Row.anchorMin, Is.EqualTo(Vector2.one), "On the top right corner.");
            Assert.That(buttons.Toggle.GetComponent<RectTransform>().sizeDelta.x, Is.EqualTo(MuseumNarrationButtons.ButtonSize));
            Assert.That(buttons.Toggle.GetComponentInChildren<TMP_Text>(), Is.Null, "An icon, not a caption.");
            Assert.That(buttons.Toggle.transform.Find("Icon").GetComponent<Image>().sprite, Is.SameAs(MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Pause)));
            buttons.Toggle.onClick.Invoke();
            buttons.Restart.onClick.Invoke();
            Assert.That((toggles, restarts), Is.EqualTo((1, 1)));
            buttons.Show(true, MuseumNarrationButtons.Glyph.Play, "Reanudar narración (Espacio)", "Reiniciar narración (R)", "Espacio: pausa · R: reinicia");
            Assert.That(buttons.Toggle.transform.Find("Icon").GetComponent<Image>().sprite, Is.SameAs(MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Play)),
                "Pause and resume share one control whose icon follows the narration.");
            buttons.Show(false, MuseumNarrationButtons.Glyph.Play, "", "", "");
            Assert.That(buttons.Row.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void TheHintNamesTheKeysOrWhatThePointedControlDoes()
        {
            buttons.Show(true, MuseumNarrationButtons.Glyph.Pause, "Pausar narración (Espacio)", "Reiniciar narración (R)", "Espacio: pausa · R: reinicia");
            Assert.That(buttons.Hint.text, Is.EqualTo("Espacio: pausa · R: reinicia"));
            pointed = buttons.Restart;
            buttons.RefreshHint();
            Assert.That(buttons.Hint.text, Is.EqualTo("Reiniciar narración (R)"), "Each icon is said in words under the pointer.");
            pointed = buttons.Toggle;
            buttons.RefreshHint();
            Assert.That(buttons.Hint.text, Is.EqualTo("Pausar narración (Espacio)"));
        }

        [Test]
        public void TheIconsAreDrawnOnceAndDifferFromEachOther()
        {
            var play = MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Play);
            var pause = MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Pause);
            var restart = MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Restart);
            Assert.That(play, Is.Not.Null);
            Assert.That(MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Play), Is.SameAs(play));
            float Ink(Sprite sprite)
            {
                float sum = 0;
                foreach (var pixel in sprite.texture.GetPixels32()) sum += pixel.a / 255f;
                return sum / (sprite.texture.width * sprite.texture.height);
            }
            foreach (var icon in new[] { play, pause, restart }) Assert.That(Ink(icon), Is.InRange(.1f, .6f), icon.name + " has a clear shape.");
            Assert.That(play.texture.GetPixel(48, 32).a, Is.GreaterThan(.5f), "The play triangle fills toward its right point.");
            Assert.That(pause.texture.GetPixel(32, 32).a, Is.LessThan(.5f), "The pause bars leave a gap in the middle.");
        }
    }
}
