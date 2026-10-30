using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The room's title on its first entry (owner, 28/10/2026): the name as the service gives it and «Sala N de 6», for
    /// about three seconds, once per room; in the upper third; never over a menu, the entry or the credits; waiting for a
    /// record, a narration or the companion, then smaller; held by a tutorial until released.
    /// </summary>
    public sealed class MuseumRoomTitleTests
    {
        private static readonly string[] Names = { "Llegada", "Bienvenida", "Personajes", "Instrumentos y Danza", "Fogón", "Mirador" };
        private GameObject runtime, canvas, eyes;
        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private MuseumRoomTitle title;
        private string room;
        private bool busy;
        private List<RoomContractV1> rooms;

        [SetUp]
        public void Build()
        {
            runtime = new GameObject("RoomTitleTestRuntime");
            runtime.AddComponent<TourLoader>();
            interaction = runtime.AddComponent<MuseumInteraction>();
            flow = runtime.AddComponent<MuseumVisitFlow>();
            canvas = new GameObject("RoomTitleTestCanvas", typeof(RectTransform), typeof(Canvas));
            rooms = Names.Select((name, index) => new RoomContractV1 { key = "room." + index, name = name, order = index + 1 }).ToList();
            title = canvas.AddComponent<MuseumRoomTitle>();
            title.Configure(() => room, () => rooms, flow, interaction, null, null);
            title.Busy = () => busy;
            room = null;
            busy = false;
        }

        [TearDown]
        public void Clear()
        {
            foreach (var item in new[] { canvas, runtime, eyes }) if (item != null) Object.DestroyImmediate(item);
        }

        /// <summary>Runs the title from one moment to another in small steps, as frames would.</summary>
        private void Run(float from, float to)
        {
            for (float time = from; time <= to; time += .05f) title.Tick(time);
        }

        [Test]
        public void TheFirstEntryShowsTheRoomsNameAndPlaceForAboutThreeSecondsOnce()
        {
            flow.EnterImmediately();
            room = "room.1";
            title.Tick(0);
            Assert.That(title.Showing, Is.False, "A moment passes after the visit opens.");
            Run(0, 1.1f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.1"));
            Assert.That(title.NameText.text, Is.EqualTo("Bienvenida"), "The name exactly as the service gives it.");
            Assert.That(title.PositionText.text, Is.EqualTo(MuseumInterfaceText.Format("room_title_position", 2, 6)));
            Assert.That(title.PositionText.text, Is.EqualTo("Sala 2 de 6"));
            Assert.That(title.Compact, Is.False);
            Assert.That(title.Card.anchorMin.y, Is.EqualTo(1), "At the top of the screen, away from the subtitles below.");
            Assert.That(title.Card.gameObject.activeSelf, Is.True);
            Run(1.1f, 2);
            Assert.That(title.Alpha, Is.EqualTo(1));
            float total = MuseumRoomTitle.FadeInSeconds + MuseumRoomTitle.HoldSeconds + MuseumRoomTitle.FadeOutSeconds;
            Assert.That(total, Is.InRange(3.5f, 5), "About three seconds in view.");
            Run(2, 1.1f + total + .1f);
            Assert.That(title.Showing, Is.False);
            Assert.That(title.Card.gameObject.activeSelf, Is.False);
            room = null;
            Run(6, 7);
            room = "room.1";
            Run(7, 12);
            Assert.That(title.Showing, Is.False, "A room entered again is not announced again.");
            Assert.That(title.Announces("room.1"), Is.False);
            room = "room.4";
            Run(12, 12.2f);
            Assert.That(title.NameText.text, Is.EqualTo("Fogón"));
            Assert.That(title.PositionText.text, Is.EqualTo("Sala 5 de 6"));
        }

        [Test]
        public void NoTitleAtTheEntryInAMenuOrDuringTheCredits()
        {
            room = "room.0";
            Run(0, 5);
            Assert.That(title.Showing, Is.False, "The entry menu.");
            Assert.That(title.Announces("room.0"), Is.True, "It is still to come.");
            flow.EnterImmediately();
            interaction.SetPaused(true);
            Run(5, 10);
            Assert.That(title.Showing, Is.False, "The pause menu.");
            interaction.SetPaused(false);
            Run(10, 11.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.0"));
            // A menu opened over a title cut short brings it back afterwards.
            interaction.SetPaused(true);
            title.Tick(11.3f);
            Assert.That(title.Showing, Is.False);
            interaction.SetPaused(false);
            Run(11.4f, 13);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.0"), "Back once the menu closes.");
            flow.ReturnToMenu();
            room = "room.5";
            Run(13, 20);
            Assert.That(title.ShownRoomKey, Is.Not.EqualTo("room.5"), "Nothing over the menu or the credits.");
        }

        [Test]
        public void ItWaitsForARecordANarrationOrTheCompanionThenShowsSmaller()
        {
            flow.EnterImmediately();
            busy = true;
            room = "room.2";
            Run(0, 4);
            Assert.That(title.Showing, Is.False, "It waits while the visitor reads or listens.");
            busy = false;
            Run(4, 4.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.2"));
            Assert.That(title.Compact, Is.False, "Free again: full size.");
            Run(4.2f, 10);
            busy = true;
            room = "room.3";
            Run(10, 10 + MuseumRoomTitle.BusyWaitSeconds - .2f);
            Assert.That(title.Showing, Is.False);
            Run(10 + MuseumRoomTitle.BusyWaitSeconds - .2f, 10 + MuseumRoomTitle.BusyWaitSeconds + .2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.3"));
            Assert.That(title.Compact, Is.True, "Still busy: it shows smaller instead of waiting for ever.");
            Assert.That(title.Card.localScale.x, Is.EqualTo(MuseumRoomTitle.CompactScale));
            // A record opened while a title is in view: it stays, shrinking out of the way.
            busy = false;
            Run(10 + MuseumRoomTitle.BusyWaitSeconds + .2f, 25);
            room = "room.4";
            Run(25, 26.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.4"));
            Assert.That(title.Card.localScale.x, Is.EqualTo(1));
            busy = true;
            Run(26.25f, 26.25f + MuseumRoomTitle.ShrinkSeconds + .1f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.4"), "It does not vanish under the record.");
            Assert.That(title.Compact, Is.True);
            Assert.That(title.Card.localScale.x, Is.EqualTo(MuseumRoomTitle.CompactScale).Within(.001f));
        }

        [Test]
        public void ATutorialHoldsTheTitleUntilItReleasesIt()
        {
            flow.EnterImmediately();
            var tutorial = new object();
            title.Hold(tutorial);
            Assert.That(title.Held, Is.True);
            room = "room.0";
            Run(0, 8);
            Assert.That(title.Showing, Is.False, "The tutorial speaks first.");
            Assert.That(title.Announces("room.0"), Is.True);
            title.Release(new object());
            Run(8, 9);
            Assert.That(title.Showing, Is.False, "Only its own owner releases it.");
            title.Release(tutorial);
            Run(9, 9.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.0"));
            // Held while on screen: it leaves at once and comes back after.
            title.Hold(tutorial);
            Assert.That(title.Showing, Is.False);
            title.Release(tutorial);
            Run(9.3f, 9.6f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.0"));
        }

        [Test]
        public void LeavingARoomBeforeItsTitleShowsAnnouncesItOnTheNextEntry()
        {
            flow.EnterImmediately();
            room = "room.0";
            Run(0, .5f);
            room = "room.1";
            Run(.5f, 1.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.1"), "The room the visitor stands in.");
            Run(1.2f, 7);
            room = "room.0";
            Run(7, 7.2f);
            Assert.That(title.ShownRoomKey, Is.EqualTo("room.0"));
        }

        [Test]
        public void ARoomWithoutANameOrOutsideTheRoomsIsNotAnnounced()
        {
            rooms[2].name = " ";
            flow.EnterImmediately();
            room = "room.2";
            Run(0, 3);
            Assert.That(title.Showing, Is.False);
            Assert.That(title.Announces("room.2"), Is.False, "The orientation keeps its own arrival line there.");
            room = "room.unknown";
            Run(3, 6);
            Assert.That(title.Showing, Is.False);
            Assert.That(title.Announces(null), Is.False);
        }

        [Test]
        public void OnAHeadsetItStandsAheadAndAboveTheEyesOnItsOwnCanvas()
        {
            eyes = new GameObject("RoomTitleTestEyes", typeof(Camera));
            eyes.transform.SetPositionAndRotation(new Vector3(3, 1.6f, -40), Quaternion.Euler(25, 90, 0));
            title.UseSpatial(eyes.GetComponent<Camera>());
            Assert.That(title.SpatialCanvas, Is.Not.Null);
            Assert.That(title.SpatialCanvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
            Assert.That(title.Card.IsChildOf(title.SpatialCanvas.transform), Is.True, "Off the screen's canvas, which a headset draws only with a menu.");
            Assert.That(title.SpatialCanvas.transform.IsChildOf(eyes.transform), Is.False, "Not stuck to the face.");
            flow.EnterImmediately();
            room = "room.0";
            Run(0, 1.2f);
            Assert.That(title.Showing, Is.True);
            var place = title.SpatialCanvas.transform.position;
            var ahead = Vector3.ProjectOnPlane(place - eyes.transform.position, Vector3.up);
            Assert.That(ahead.magnitude, Is.EqualTo(MuseumRoomTitle.SpatialDistance).Within(.01f), "About two metres ahead.");
            Assert.That(Vector3.Angle(ahead, Vector3.right), Is.LessThan(1), "Along the level gaze, whatever the head's tilt.");
            Assert.That(place.y - eyes.transform.position.y, Is.EqualTo(MuseumRoomTitle.SpatialRise).Within(.01f), "A little above the eyes.");
            Assert.That(Vector3.Dot(title.SpatialCanvas.transform.forward, ahead.normalized), Is.GreaterThan(.99f), "Facing the visitor.");
        }
    }
}
