using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The record on the reading stand as the owner asked on 22/10/2026: narration first, short credits, smooth scrolling.</summary>
    public sealed class FichaReadingTests
    {
        private const string Record = "{\"slug\":\"test\",\"title\":\"Pieza\",\"description\":\"Descripción sintética\","
            + "\"blocks\":[{\"id\":\"b\",\"kind\":\"documented_fact\",\"text\":\"Bloque sintético\",\"source_id\":\"s\"}],"
            + "\"sources\":[{\"id\":\"s\",\"kind\":\"publication\",\"reference\":\"Autora, A. (2020). Título.\"}],"
            + "\"credits\":[{\"role\":\"Pertenencia colectiva\",\"name\":\"Cabildo indígena\"},{\"role\":\"Captura y digitalización\",\"name\":\"Persona\"},"
            + "{\"role\":\"Narración\",\"name\":\"voz sintética «Diego» de ElevenLabs\"}],"
            + "\"resources\":[{\"id\":\"n\",\"kind\":\"narration\",\"mime\":\"audio/mpeg\",\"transcription\":\"Lo que dice el narrador.\","
            + "\"credit\":\"Narración: voz sintética «Diego» de ElevenLabs.\",\"provenance\":\"MP3 producido (GUIONES.md), SHA256 abc, Blender 5.2\"},"
            + "{\"id\":\"m\",\"kind\":\"model_3d\",\"mime\":\"model/gltf-binary\",\"credit\":\"Captura: Persona.\",\"provenance\":\"Archivo local, notas internas\"}]}";

        [Test]
        public void TheScreenReadsTheNarrationApartFromTheRestAndEndsWithTheReferences()
        {
            Assert.That(ElementText.TryFormatSections("test", Record, out var sections), Is.True);
            Assert.That(sections.Title, Is.EqualTo("Pieza"));
            Assert.That(sections.Transcription, Is.EqualTo("Lo que dice el narrador."));
            Assert.That(sections.Body, Does.Contain("Descripción sintética").And.Contain("Bloque sintético"));
            Assert.That(sections.Body, Does.Not.Contain("Lo que dice el narrador"), "The narration is read in its own place, first.");
            Assert.That(sections.Body, Does.Not.Contain("Referencias"));
            Assert.That(sections.Closing, Does.Contain("Referencias").And.Contain("Autora (2020)"));
            Assert.That(ElementText.TryFormatSections("other", Record, out _), Is.False);
        }

        [Test]
        public void TheCreditsAreTheRecordsOwnWithoutTheSyntheticVoiceOrWorkingNotes()
        {
            Assert.That(ElementText.TryFormat("test", Record, out var text), Is.True);
            Assert.That(ElementText.TryFormatSections("test", Record, out var sections), Is.True);
            foreach (var shown in new[] { text, sections.Closing })
            {
                Assert.That(shown, Does.Contain("Pertenencia colectiva: Cabildo indígena · Captura y digitalización: Persona"));
                Assert.That(shown, Does.Not.Contain("ElevenLabs").And.Not.Contain("voz sintética"), "The synthetic voice is credited at the end of the museum.");
                Assert.That(shown, Does.Not.Contain("GUIONES").And.Not.Contain("SHA256").And.Not.Contain("notas internas"), "Provenance notes are never shown.");
                Assert.That(shown, Does.Not.Contain("Crédito:").And.Not.Contain("Procedencia:"));
            }
            Assert.That(RecordReferences.IsSyntheticVoice("Voz Sintética"), Is.True);
            Assert.That(RecordReferences.IsSyntheticVoice("Cabildo indígena"), Is.False);
        }

        [Test]
        public void TheHeardFragmentIsMarkedAndEverythingElseStaysLiteral()
        {
            const string transcript = "Uno <b>dos</b> tres.";
            string text = MuseumNarrationReading.Markup("Reproduciendo", transcript, "", (4, 10), out int marked);
            Assert.That(text.Substring(marked, 10), Is.EqualTo("<b>dos</b>"), "The mark points at the fragment's first letter.");
            Assert.That(text, Does.Contain(MuseumNarrationReading.MarkOpen + "<noparse><b>dos</b></noparse>" + MuseumNarrationReading.MarkClose));
            Assert.That(text, Does.Contain("<noparse>Uno </noparse>").And.Contain("<noparse> tres.</noparse>"));
            Assert.That(text, Does.Contain("<noparse>Reproduciendo</noparse>"));
            string plain = MuseumNarrationReading.Markup("", transcript, "", null, out marked);
            Assert.That(marked, Is.EqualTo(-1));
            Assert.That(plain, Is.EqualTo("<noparse>" + transcript + "</noparse>"), "Without a heard fragment nothing is marked.");
        }

        [Test]
        public void TheStickScrollsSmoothlyFasterTheFurtherItIsPushed()
        {
            Assert.That(MuseumPanelScroll.StickSpeed(.1f, 600), Is.EqualTo(0), "A resting thumb does not scroll.");
            Assert.That(MuseumPanelScroll.StickSpeed(MuseumPanelScroll.Deadzone, 600), Is.EqualTo(0));
            float slow = MuseumPanelScroll.StickSpeed(-.4f, 600), fast = MuseumPanelScroll.StickSpeed(-.8f, 600);
            Assert.That(slow, Is.LessThan(0), "Down scrolls down.");
            Assert.That(fast, Is.LessThan(slow), "Further is faster.");
            Assert.That(MuseumPanelScroll.StickSpeed(1, 600), Is.EqualTo(600).Within(.001f));
            Assert.That(MuseumPanelScroll.StickSpeed(-1, 600), Is.EqualTo(-600).Within(.001f));
        }

        [Test]
        public void TheBarShowsThePartOfTheRecordInView()
        {
            var root = new GameObject("ScrollBarTest", typeof(RectTransform), typeof(ScrollRect));
            try
            {
                var scroll = root.GetComponent<ScrollRect>();
                ((RectTransform)root.transform).sizeDelta = new Vector2(400, 250);
                var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
                viewport.SetParent(root.transform, false);
                viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = viewport.offsetMax = Vector2.zero;
                var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
                content.SetParent(viewport, false);
                content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
                content.sizeDelta = new Vector2(0, 1000);
                scroll.viewport = viewport;
                scroll.content = content;
                var scrolling = root.AddComponent<MuseumPanelScroll>();
                scrolling.Configure(scroll);
                scroll.verticalNormalizedPosition = 1;
                scrolling.RefreshBar();
                Assert.That(scrolling.Track.gameObject.activeSelf, Is.True, "A record longer than the screen shows its bar.");
                Assert.That(scrolling.Handle.anchorMax.y, Is.EqualTo(1).Within(.001f));
                Assert.That(scrolling.Handle.anchorMin.y, Is.EqualTo(.75f).Within(.001f), "The handle is as long as the part in view.");
                scroll.verticalNormalizedPosition = 0;
                scrolling.RefreshBar();
                Assert.That(scrolling.Handle.anchorMin.y, Is.EqualTo(0).Within(.001f));
                Assert.That(scrolling.Handle.anchorMax.y, Is.EqualTo(.25f).Within(.001f));
                // The mouse wheel moves the record about three lines a notch: back shows what follows, forward what is above.
                scroll.verticalNormalizedPosition = 1;
                Assert.That(scrolling.Wheel(-120, true), Is.True);
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1 - MuseumPanelScroll.WheelUnits / 750).Within(.001f));
                Assert.That(scrolling.Wheel(120, false), Is.False, "A wheel busy with a piece leaves the record alone.");
                Assert.That(scrolling.Wheel(120, true), Is.True);
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(.001f));
                Assert.That(scrolling.Wheel(120, true), Is.False, "At the top there is nothing more above.");
                Assert.That(scrolling.Wheel(-3, true), Is.True, "A browser's fine wheel step moves it too.");
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1 - .25f * MuseumPanelScroll.WheelUnits / 750).Within(.001f));
                content.sizeDelta = new Vector2(0, 200);
                scrolling.RefreshBar();
                Assert.That(scrolling.Track.gameObject.activeSelf, Is.False, "A short record needs no bar.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
