using System;
using System.Linq;
using System.Text.RegularExpressions;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The first-visit tutorial and the labels on the controllers: settings, texts, controls and progress.</summary>
    public sealed class MuseumTutorialTests
    {
        private static readonly string[] SixSteps = { "turn", "move", "menu", "approach", "grab", "companion" };

        [Test]
        public void TheTutorialProposesTheSixStepsInOrderWithTextsForEveryDevice()
        {
            var settings = MuseumTutorialSettings.Current;
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.StartRoom, Is.EqualTo("room.arrival"));
            Assert.That(settings.Steps.Select(step => step.Id), Is.EqualTo(SixSteps));
            var catalog = MuseumInterfaceText.Current;
            foreach (var step in settings.Steps)
            {
                Assert.That(catalog.Find(step.TextKey), Is.Not.Null.And.Not.Empty, step.TextKey);
                if (step.NeedsControllers) Assert.That(catalog.Find(step.TextKey + "_hands"), Is.Not.Null, step.TextKey + "_hands");
            }
            foreach (var step in new[] { "tutorial_turn", "tutorial_move", "tutorial_menu", "tutorial_grab", "tutorial_companion", "tutorial_skip_hint" })
                Assert.That(catalog.Find(step + "_xr"), Is.Not.Null, "The headset has its own words: " + step);
            foreach (var key in new[] { "tutorial_progress", "tutorial_step_done", "tutorial_end", "tutorial_skipped", "tutorial_repeat",
                "tutorial_skip", "controller_labels_option" })
                Assert.That(catalog.Find(key), Is.Not.Null.And.Not.Empty, key);
        }

        /// <summary>The screen's words name keys only through the bindings in force, so a changed key is named as it is.</summary>
        [Test]
        public void TheScreenTextsNameOnlyKeysOfTheVisitorsBindings()
        {
            var input = MuseumInput.CreateDefault();
            var catalog = MuseumInterfaceText.Current;
            foreach (var step in MuseumTutorialSettings.Current.Steps.Select(step => step.TextKey).Append("tutorial_skip_hint"))
            {
                string text = catalog.Find(step);
                foreach (Match token in Regex.Matches(text, @"\{input:([a-z_]+)\}"))
                    Assert.That(input.Bindings.Find(token.Groups[1].Value), Is.Not.Null, step + " names " + token.Value);
                Assert.That(text, Does.Not.Match(@"\b(Botón|botón) [ABXY]\b"), "A screen has no controller buttons: " + step);
            }
        }

        /// <summary>
        /// The headset's words name the controls the input actions really use (MuseumInput.inputactions, as the controls
        /// drawing shows them): X asks the companion, the left menu button pauses, A jumps, the grip takes, the sticks walk and turn.
        /// </summary>
        [Test]
        public void TheHeadsetTextsNameTheControlsThatAreReallyBound()
        {
            var asset = MuseumInput.CreateDefault().Asset;
            bool Bound(string action, string control) => asset.FindAction(action, true).bindings.Any(binding => binding.path == control);
            var catalog = MuseumInterfaceText.Current;
            Assert.That(Bound("Visit/AskCompanion", "<OculusTouchController>{LeftHand}/primaryButton"), Is.True);
            Assert.That(catalog.Find("tutorial_companion_xr"), Does.Contain("botón X del mando izquierdo"));
            Assert.That(Bound("Common/Pause", "<OculusTouchController>{LeftHand}/menu"), Is.True);
            Assert.That(catalog.Find("tutorial_menu_xr"), Does.Contain("botón de menú del mando izquierdo"));
            Assert.That(Bound("Locomotion/Teleport", "<OculusTouchController>{RightHand}/primaryButton"), Is.True);
            Assert.That(Bound("Locomotion/Move", "<OculusTouchController>{LeftHand}/thumbstick"), Is.True);
            Assert.That(catalog.Find("tutorial_move_xr"), Does.Contain("palanca izquierda").And.Contain("botón A"));
            Assert.That(Bound("Locomotion/Turn", "<OculusTouchController>{RightHand}/thumbstick"), Is.True);
            Assert.That(catalog.Find("tutorial_turn_xr"), Does.Contain("palanca derecha"));
            Assert.That(Bound("Visit/Grab", "<OculusTouchController>{RightHand}/gripPressed"), Is.True);
            Assert.That(catalog.Find("tutorial_grab_xr"), Does.Contain("agarre"));
            Assert.That(Bound("Common/Back", "<OculusTouchController>{LeftHand}/secondaryButton"), Is.True);
            Assert.That(catalog.Find("tutorial_skip_hint_xr"), Does.Contain("botón Y"));
        }

        [Test]
        public void EveryControllerLabelSitsOnAControlThatTriggersItsAction()
        {
            var settings = MuseumControllerLabelSettings.Current;
            var asset = MuseumInput.CreateDefault().Asset;
            var catalog = MuseumInterfaceText.Current;
            foreach (var label in settings.Labels)
            {
                var action = asset.FindAction(label.Action, true);
                Assert.That(action.bindings.Any(binding => binding.path == label.Control), Is.True, label.Hand + " " + label.Id + ": " + label.Control);
                Assert.That(label.Control, Does.Contain(label.Right ? "{RightHand}" : "{LeftHand}"), "On its own hand: " + label.Id);
                Assert.That(catalog.Find(label.TextKey), Is.Not.Null.And.Not.Empty, label.TextKey);
                Assert.That(Mathf.Sign(label.LabelOffset.x), Is.EqualTo(label.Right ? 1 : -1), "Labels stand on the outer side: " + label.Id);
            }
            // The owner's examples (28/10/2026): «Menú», «Hablar con el colibrí», «Tomar».
            var texts = settings.Labels.Select(label => catalog.Find(label.TextKey)).ToList();
            Assert.That(texts, Does.Contain("Menú").And.Contain("Hablar con el colibrí").And.Contain("Tomar"));
            Assert.Throws<InvalidOperationException>(() => MuseumControllerLabelSettings.Parse("{\"schema_version\":1,\"labels\":[]}"));
        }

        [Test]
        public void StepsAreDoneByDoingThemInAnyOrderAndTheFirstUndoneIsProposed()
        {
            var settings = MuseumTutorialSettings.Current;
            var progress = new MuseumTutorialProgress(settings);
            Assert.That(progress.Current.Id, Is.EqualTo("turn"));
            Assert.That(progress.CurrentNumber, Is.EqualTo(1));
            TutorialSignals Moment(float turned = 0, float moved = 0, bool menu = false, bool point = false, bool hold = false, bool call = false)
                => new TutorialSignals(turned, moved, menu, point, hold, call);
            Assert.That(progress.Observe(Moment(turned: settings.TurnDegrees * .4f)), Is.Empty, "A glance is not yet a turn.");
            Assert.That(progress.Observe(Moment(call: true)), Is.EqualTo(new[] { TutorialStep.Companion }), "Out of order counts at once.");
            Assert.That(progress.Current.Id, Is.EqualTo("turn"));
            Assert.That(progress.Observe(Moment(turned: -settings.TurnDegrees * .7f)), Is.EqualTo(new[] { TutorialStep.Turn }), "Turns add up either way.");
            Assert.That(progress.Current.Id, Is.EqualTo("move"));
            Assert.That(progress.Observe(Moment(moved: settings.MoveMetres + .1f, menu: true)), Is.EquivalentTo(new[] { TutorialStep.Move, TutorialStep.Menu }));
            Assert.That(progress.Current.Id, Is.EqualTo("approach"));
            Assert.That(progress.CurrentNumber, Is.EqualTo(4));
            Assert.That(progress.Observe(Moment(point: true, hold: true)), Is.EquivalentTo(new[] { TutorialStep.Approach, TutorialStep.Grab }));
            Assert.That(progress.Finished, Is.True);
            Assert.That(progress.Current, Is.Null);
            Assert.That(progress.Observe(Moment(call: true)), Is.Empty, "Nothing is done twice.");
        }

        [TestCase(TutorialEnding.Finished, true)]
        [TestCase(TutorialEnding.Skipped, true)]
        [TestCase(TutorialEnding.LeftHalfway, false)]
        public void OnlyAFinishedOrSkippedTutorialIsRemembered(TutorialEnding ending, bool remembered)
            => Assert.That(MuseumTutorialProgress.Remembered(ending), Is.EqualTo(remembered));

        [TestCase("{\"schema_version\":2}")]
        [TestCase("{\"schema_version\":1,\"steps\":[]}")]
        [TestCase("{\"schema_version\":1,\"steps\":[{\"id\":\"fly\",\"text_key\":\"tutorial_turn\"}]}")]
        [TestCase("{\"schema_version\":1,\"turn_degrees\":0,\"steps\":[{\"id\":\"turn\",\"text_key\":\"tutorial_turn\"}]}")]
        public void InvalidTutorialSettingsAreRejected(string json)
            => Assert.Throws<InvalidOperationException>(() => MuseumTutorialSettings.Parse(json));

        /// <summary>Preferences saved before the tutorial existed take it as not yet done and the labels as shown.</summary>
        [Test]
        public void TheTutorialAndTheLabelsAreRememberedWithThePreferences()
        {
            var configuration = MuseumExperienceConfiguration.Current;
            var older = MuseumVisitorPreferences.Load(configuration, "{\"schema_version\":4,\"sensitivity\":"
                + configuration.DefaultSensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"volume\":0.5,\"subtitles\":true}");
            Assert.That(older.TutorialDone, Is.False);
            Assert.That(older.ControllerLabels, Is.True);
            older.SetTutorialDone(true);
            older.SetControllerLabels(false);
            var again = MuseumVisitorPreferences.Load(configuration, older.Serialize());
            Assert.That(again.TutorialDone, Is.True);
            Assert.That(again.ControllerLabels, Is.False);
            again.Reset();
            Assert.That(again.ControllerLabels, Is.True, "Restoring the settings shows the labels again.");
            Assert.That(again.TutorialDone, Is.True, "Restoring the settings does not bring the tutorial back by itself.");
        }
    }
}
