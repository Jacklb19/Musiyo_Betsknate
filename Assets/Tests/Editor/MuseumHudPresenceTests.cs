using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumHudPresenceTests
    {
        [Test]
        public void TheKeyReminderNamesTheKeysInForceAndStepsAsideAfterTheStart()
        {
            var visit = new GameObject("PresenceVisit");
            var hud = new GameObject("PresenceHud", typeof(RectTransform));
            MuseumInput.Replace(MuseumInput.CreateDefault());
            try
            {
                visit.AddComponent<TourRuntime>();
                var loader = visit.AddComponent<TourLoader>();
                var interaction = visit.AddComponent<MuseumInteraction>();
                var flow = visit.AddComponent<MuseumVisitFlow>();
                var header = new GameObject("Header", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                header.SetParent(hud.transform, false);
                new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).transform.SetParent(header, false);
                var pause = new GameObject("Pause", typeof(RectTransform), typeof(Image)).transform;
                pause.SetParent(header, false);
                var bar = new GameObject("Controls", typeof(RectTransform)).GetComponent<RectTransform>();
                bar.SetParent(hud.transform, false);
                var keys = new GameObject("ControlText", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
                keys.transform.SetParent(bar, false);
                var presence = hud.AddComponent<MuseumHudPresence>();
                presence.Configure(flow, interaction, loader, header, bar);

                Assert.That(keys.text, Does.Contain("W").And.Contain("Esc").And.Not.Contain("{input:"));
                Assert.That(pause.parent, Is.SameAs(header), "The pause button stays in the header and never fades.");
                flow.EnterImmediately();
                presence.Advance(0, 0);
                presence.Advance(1, 10);
                Assert.That(presence.ControlsVisibility, Is.EqualTo(1), "The reminder shows when the visit starts.");
                presence.Advance(60, 10);
                Assert.That(presence.ControlsVisibility, Is.EqualTo(0), "After a while it steps aside.");
                Assert.That(presence.HeaderVisibility, Is.EqualTo(1), "The header stays while the tour is not ready.");
                interaction.SetPaused(true);
                presence.Advance(61, 10);
                interaction.SetPaused(false);
                presence.Advance(62, 10);
                Assert.That(presence.ControlsVisibility, Is.EqualTo(1), "Resuming the visit shows it again.");
            }
            finally
            {
                Object.DestroyImmediate(hud);
                Object.DestroyImmediate(visit);
                MuseumInput.Replace(null);
            }
        }
    }
}
