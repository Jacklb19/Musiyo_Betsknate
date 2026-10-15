using System.Collections;
using System.Collections.Generic;
using System.Text;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The panel follows a synthetic timed transcription inside a real scrolling viewport; no published content.</summary>
    public sealed class MuseumReadingPlayTests
    {
        private GameObject root;

        [TearDown]
        public void Release() { if (root != null) Object.Destroy(root); }

        private static float LineOffsetFromTop(ScrollRect scroll, TMP_Text text, int characterIndex)
        {
            text.ForceMeshUpdate();
            var info = text.textInfo;
            int found = 0;
            for (int index = 0; index < info.characterCount; index++)
                if (info.characterInfo[index].index >= characterIndex) { found = index; break; }
            float ascender = info.lineInfo[info.characterInfo[found].lineNumber].ascender;
            var viewport = scroll.viewport;
            float line = viewport.InverseTransformPoint(text.rectTransform.TransformPoint(new Vector3(0, ascender, 0))).y;
            return viewport.rect.yMax - line;
        }

        [UnityTest]
        public IEnumerator TheHeardFragmentIsBroughtIntoViewUntilTheVisitorScrollsByHand()
        {
            root = new GameObject("ReadingTest", typeof(Canvas), typeof(CanvasScaler));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(ScrollRect));
            panel.transform.SetParent(root.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.sizeDelta = new Vector2(600, 300);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(panel.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one; contentRect.pivot = new Vector2(.5f, 1);
            content.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
            content.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var textObject = new GameObject("Transcript", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(content.transform, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.richText = false;
            text.fontSize = 24;
            var scroll = panel.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var resume = new GameObject("Resume", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            resume.transform.SetParent(root.transform, false);

            var sentences = new List<string>();
            var cues = new List<SubtitleCue>();
            var transcript = new StringBuilder();
            for (int index = 0; index < 40; index++)
            {
                var sentence = "Frase sintética número " + index + " para comprobar el desplazamiento de la lectura.";
                sentences.Add(sentence);
                cues.Add(new SubtitleCue(index * 2, index * 2 + 2, sentence));
                transcript.Append(sentence).Append(' ');
            }
            float time = 0;
            string slug = "synthetic-reading";
            var reading = root.AddComponent<MuseumNarrationReading>();
            reading.Configure(() => slug, () => cues, () => time, () => true, scroll, text, resume);
            reading.Show("Estado", transcript.ToString().TrimEnd(), "");
            yield return null;
            Assert.That(reading.Aligned, Is.True);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(viewportRect.rect.height * 3), "The fixture must scroll.");
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(.01f), "The first fragment is at the top.");

            int Start(int cue) => "Estado\n".Length + transcript.ToString().IndexOf(sentences[cue], System.StringComparison.Ordinal);
            float follow = viewportRect.rect.height * MuseumExperienceConfiguration.Current.ReadingFollowLine;
            time = 61; // Fragment 30.
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(reading.ActiveCue, Is.EqualTo(30));
            Assert.That(LineOffsetFromTop(scroll, text, Start(30)), Is.EqualTo(follow).Within(12), "The heard line sits at its reading height.");
            Assert.That(resume.gameObject.activeSelf, Is.False);

            // The visitor scrolls by hand: following stops and the panel stays where they left it.
            ExecuteEvents.Execute(panel, new PointerEventData(EventSystem.current), ExecuteEvents.scrollHandler);
            Assert.That(reading.Following, Is.False);
            Assert.That(resume.gameObject.activeSelf, Is.True);
            scroll.verticalNormalizedPosition = .9f;
            time = 51; // Fragment 25, far enough from the end to be brought to its height.
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(.9f).Within(.001f), "Manual reading is never fought.");

            // Asking to follow again brings the current fragment back.
            reading.Resume();
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(reading.ActiveCue, Is.EqualTo(25));
            Assert.That(LineOffsetFromTop(scroll, text, Start(25)), Is.EqualTo(follow).Within(12));

            // Paused playback keeps the place; going back to the start, as Repeat does, follows from the top again.
            reading.Suspend();
            time = 0;
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.That(reading.Following, Is.True, "Going back to the start resumes following.");
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(.01f));

            // A different narration starts following from scratch.
            reading.Suspend();
            slug = "another-synthetic-reading";
            time = 3;
            yield return null;
            Assert.That(reading.Following, Is.True);
        }
    }
}
