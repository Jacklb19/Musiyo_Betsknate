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
        public void TheVisitHasNoPermanentHeaderOrKeyBar()
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
                new GameObject("Pause", typeof(RectTransform), typeof(Image)).transform.SetParent(header, false);
                var bar = new GameObject("Controls", typeof(RectTransform)).GetComponent<RectTransform>();
                bar.SetParent(hud.transform, false);
                new GameObject("ControlText", typeof(RectTransform), typeof(TextMeshProUGUI)).transform.SetParent(bar, false);
                var presence = hud.AddComponent<MuseumHudPresence>();
                presence.Configure(flow, interaction, loader, header, bar);
                Assert.That(header.gameObject.activeSelf, Is.False, "No title, status, pause or retry bar on the screen.");
                Assert.That(bar.gameObject.activeSelf, Is.False, "No permanent list of keys.");
                Assert.That(presence.BarsVisible, Is.False);
                flow.EnterImmediately();
                interaction.SetPaused(true);
                interaction.SetPaused(false);
                Assert.That(presence.BarsVisible, Is.False, "Starting or resuming the visit does not bring them back.");
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
