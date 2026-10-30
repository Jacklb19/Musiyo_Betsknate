using System;
using System.Diagnostics;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Narrations heard at 1.5x with the voice's pitch kept (owner, 28/10/2026: «Intentemos hacerlo a 1.5 nada mas»): the
    /// stretch itself, the remembered choice, the control on the screen's header line and the switch that hides it.
    /// </summary>
    public sealed class NarrationSpeedTests
    {
        private const int Frequency = 24000;

        private static float[] Tone(float hertz, float seconds, int frequency = Frequency)
        {
            var samples = new float[(int)(seconds * frequency)];
            for (int i = 0; i < samples.Length; i++) samples[i] = .5f * Mathf.Sin(2 * Mathf.PI * hertz * i / frequency);
            return samples;
        }

        /// <summary>The main frequency of a mono stretch, from its upward zero crossings away from the edges.</summary>
        private static float Pitch(float[] samples, int frequency)
        {
            int from = samples.Length / 10, to = samples.Length - samples.Length / 10, crossings = 0;
            for (int i = from + 1; i < to; i++) if (samples[i - 1] < 0 && samples[i] >= 0) crossings++;
            return crossings / ((to - from) / (float)frequency);
        }

        [Test]
        public void FasterNarrationIsShorterAndKeepsTheVoicesPitch()
        {
            var tone = Tone(220, 4);
            var fast = NarrationTimeStretch.Stretch(tone, 1, Frequency, MuseumNarrationSpeed.Fast);
            Assert.That(fast.Length, Is.EqualTo(tone.Length / 1.5f).Within(2), "Two thirds of the time.");
            Assert.That(Pitch(fast, Frequency), Is.EqualTo(220).Within(4), "The same pitch, not 330 Hz as a faster playback would give.");
            float peak = 0;
            foreach (var sample in fast) peak = Mathf.Max(peak, Mathf.Abs(sample));
            Assert.That(peak, Is.InRange(.45f, .55f), "The same loudness, without gaps or doubling.");
            var copy = NarrationTimeStretch.Stretch(tone, 1, Frequency, 1);
            Assert.That(copy, Is.EqualTo(tone).And.Not.SameAs(tone), "At 1x the narration is heard as recorded.");
        }

        [Test]
        public void WhatIsSaidAtASecondIsHeardAtTwoThirdsOfIt()
        {
            // Silence with a short burst at 3 s: subtitles follow the recording's time, so the burst must sound at 2 s.
            var samples = new float[Frequency * 6];
            for (int i = 0; i < Frequency / 20; i++) samples[Frequency * 3 + i] = .8f * Mathf.Sin(2 * Mathf.PI * 300 * i / Frequency);
            var fast = NarrationTimeStretch.Stretch(samples, 1, Frequency, MuseumNarrationSpeed.Fast);
            int loudest = 0;
            for (int i = 1; i < fast.Length; i++) if (Mathf.Abs(fast[i]) > Mathf.Abs(fast[loudest])) loudest = i;
            Assert.That(loudest / (float)Frequency, Is.EqualTo(2).Within(.05f));
        }

        [Test]
        public void StereoChannelsStayApart()
        {
            var left = Tone(200, 2);
            var stereo = new float[left.Length * 2];
            for (int i = 0; i < left.Length; i++) { stereo[i * 2] = left[i]; stereo[i * 2 + 1] = 0; }
            var fast = NarrationTimeStretch.Stretch(stereo, 2, Frequency, MuseumNarrationSpeed.Fast);
            Assert.That(fast.Length % 2, Is.Zero);
            float right = 0;
            for (int i = 1; i < fast.Length; i += 2) right = Mathf.Max(right, Mathf.Abs(fast[i]));
            Assert.That(right, Is.Zero, "The silent channel stays silent.");
            Assert.Throws<ArgumentOutOfRangeException>(() => NarrationTimeStretch.Stretch(stereo, 2, Frequency, 9));
        }

        [Test]
        public void ALongNarrationIsStretchedInAMomentOffTheMainThread()
        {
            // Three minutes is the longest narration the museum accepts; recordings are 44.1 kHz at most.
            var samples = Tone(180, 180, 44100);
            var clock = Stopwatch.StartNew();
            var fast = NarrationTimeStretch.Stretch(samples, 1, 44100, MuseumNarrationSpeed.Fast);
            clock.Stop();
            UnityEngine.Debug.Log($"[NarrationSpeed] stretched 180 s at 44.1 kHz in {clock.ElapsedMilliseconds} ms");
            Assert.That(fast.Length, Is.EqualTo(samples.Length / 1.5f).Within(2));
            Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(20), "Once per narration, on a worker thread.");
        }

        [Test]
        public void TheSpeedIsRememberedAndOnlyOneAndAHalfOrOne()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var earlier = MuseumVisitorPreferences.Load(settings, "{\"schema_version\":4,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":true}");
            Assert.That(earlier.NarrationSpeed, Is.EqualTo(1), "A visitor who never chose hears 1x.");
            earlier.SetNarrationSpeed(2);
            Assert.That(earlier.NarrationSpeed, Is.EqualTo(1), "2x is not offered (owner, 28/10/2026).");
            earlier.SetNarrationSpeed(MuseumNarrationSpeed.Next(earlier.NarrationSpeed));
            Assert.That(earlier.NarrationSpeed, Is.EqualTo(1.5f));
            var loaded = MuseumVisitorPreferences.Load(settings, earlier.Serialize());
            Assert.That(loaded.NarrationSpeed, Is.EqualTo(1.5f), "Kept for the next visit.");
            Assert.That(MuseumNarrationSpeed.Next(loaded.NarrationSpeed), Is.EqualTo(1));
            var odd = MuseumVisitorPreferences.Load(settings, earlier.Serialize().Replace("\"narration_speed\":1.5", "\"narration_speed\":3.0"));
            Assert.That(odd.NarrationSpeed, Is.EqualTo(1), "Anything else is heard at 1x.");
            loaded.Reset();
            Assert.That(loaded.NarrationSpeed, Is.EqualTo(1));
        }

        [Test]
        public void EachPlatformCanHideTheControl()
        {
            Assert.That(MuseumExperienceConfiguration.Current.NarrationSpeedHeadset, Is.True);
            Assert.That(MuseumExperienceConfiguration.Current.NarrationSpeedWeb, Is.True);
            Assert.That(MuseumNarrationSpeed.Offered(MuseumExperienceConfiguration.Current), Is.True);
            var json = Resources.Load<TextAsset>("MuseumExperienceConfiguration").text;
            var hidden = MuseumExperienceConfiguration.Parse(json.Replace("\"narration_speed_headset\": true", "\"narration_speed_headset\": false"));
            Assert.That(hidden.NarrationSpeedHeadset, Is.False);
            Assert.That(MuseumNarrationSpeed.Offered(hidden), Is.False, "The Editor follows the headset's switch.");
        }

        [Test]
        public void TheBrowsersElementIsAsLoudAsTheNarrationsSource()
        {
            Assert.That(BrowserNarration.Rolloff(.5f, 1, 8), Is.EqualTo(1), "Full volume up close.");
            Assert.That(BrowserNarration.Rolloff(2, 1, 8), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(BrowserNarration.Rolloff(20, 1, 8), Is.EqualTo(1 / 8f).Within(1e-5f), "No quieter past the farthest distance.");
            Assert.That(BrowserNarration.Available, Is.False, "Only a browser build has the page's audio element.");
        }

        [Test]
        public void TheSpeedControlStandsBeforePauseAndIsLitAtOneAndAHalf()
        {
            var root = new GameObject("NarrationSpeedTest", typeof(RectTransform), typeof(Canvas));
            try
            {
                var panel = (RectTransform)root.transform;
                panel.sizeDelta = new Vector2(832, 700);
                var style = new GameObject("Style", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                style.transform.SetParent(panel, false);
                Selectable pointed = null;
                int speeds = 0;
                var buttons = root.AddComponent<MuseumNarrationButtons>();
                buttons.Configure(panel, new Vector2(-16, -16), style, () => { }, () => { }, () => pointed, () => speeds++);
                Assert.That(buttons.Speed, Is.Not.Null);
                Assert.That(buttons.Speed.transform.GetSiblingIndex(), Is.LessThan(buttons.Toggle.transform.GetSiblingIndex()),
                    "Next to pause, before it.");
                buttons.Show(true, MuseumNarrationButtons.Glyph.Pause, "Pausar", "Reiniciar", "teclas");
                Assert.That(buttons.Speed.gameObject.activeSelf, Is.False, "Hidden until the narration can be heard faster.");
                buttons.ShowSpeed(true, false, "Escuchar más rápido (1,5×)");
                Assert.That(buttons.Speed.gameObject.activeSelf, Is.True);
                var icon = buttons.Speed.transform.Find("Icon").GetComponent<Image>();
                Assert.That(icon.sprite, Is.SameAs(MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Faster)));
                Assert.That(icon.color, Is.EqualTo(MuseumHudSkin.Text));
                Assert.That(buttons.Speed.GetComponent<RectTransform>().sizeDelta.x, Is.EqualTo(MuseumNarrationButtons.ButtonSize), "As small as the others.");
                pointed = buttons.Speed;
                buttons.RefreshHint();
                Assert.That(buttons.Hint.text, Is.EqualTo("Escuchar más rápido (1,5×)"), "Said in words under the pointer.");
                buttons.Speed.onClick.Invoke();
                Assert.That(speeds, Is.EqualTo(1));
                buttons.ShowSpeed(true, true, "Volver a la velocidad normal (1×)");
                Assert.That(buttons.SpeedLit, Is.True);
                Assert.That(icon.color, Is.EqualTo(MuseumHudSkin.Accent), "Lit while the narration is heard faster.");
                Assert.That(buttons.Hint.text, Is.EqualTo("Volver a la velocidad normal (1×)"));

                var faster = MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Faster);
                Assert.That(faster, Is.Not.SameAs(MuseumNarrationButtons.Icon(MuseumNarrationButtons.Glyph.Play)));
                float ink = 0;
                foreach (var pixel in faster.texture.GetPixels32()) ink += pixel.a / 255f;
                Assert.That(ink / (faster.texture.width * faster.texture.height), Is.InRange(.1f, .6f), "A clear shape.");
                Assert.That(faster.texture.GetPixel(32, 32).a, Is.LessThan(.5f), "Two triangles, apart in the middle.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AScreenWithoutTheSwitchHasNoSpeedControl()
        {
            var root = new GameObject("NarrationNoSpeedTest", typeof(RectTransform), typeof(Canvas));
            try
            {
                var style = new GameObject("Style", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                style.transform.SetParent(root.transform, false);
                var buttons = root.AddComponent<MuseumNarrationButtons>();
                buttons.Configure((RectTransform)root.transform, Vector2.zero, style, () => { }, () => { }, () => null);
                Assert.That(buttons.Speed, Is.Null);
                buttons.ShowSpeed(true, true, "x");
                Assert.That(buttons.Row.GetComponentsInChildren<Button>(true).Length, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
