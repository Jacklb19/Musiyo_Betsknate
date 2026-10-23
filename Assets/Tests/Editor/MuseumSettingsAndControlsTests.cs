using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The settings rows (name on the left, value on the right) and the drawing on the controls page.</summary>
    public sealed class MuseumSettingsAndControlsTests
    {
        private sealed class MenuFixture : System.IDisposable
        {
            public readonly GameObject Root, Canvas, Events;
            public readonly MuseumMenuHud Menu;
            public readonly MuseumVisitFlow Flow;
            public readonly MuseumInteraction Interaction;
            public readonly TourLoader Loader;
            public readonly MuseumVisitorPreferences Preferences;
            private readonly bool hadStored;
            private readonly string stored;

            public MenuFixture()
            {
                hadStored = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
                stored = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
                MuseumInput.Replace(MuseumInput.CreateDefault());
                Root = new GameObject("LookTestRuntime");
                Loader = Root.AddComponent<TourLoader>();
                Interaction = Root.AddComponent<MuseumInteraction>();
                Flow = Root.AddComponent<MuseumVisitFlow>();
                Preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
                Canvas = new GameObject("LookTestCanvas", typeof(RectTransform), typeof(UnityEngine.Canvas));
                Events = new GameObject("LookTestEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events.GetComponent<EventSystem>(), null);
                EventSystem.current = Events.GetComponent<EventSystem>();
                Menu = Canvas.AddComponent<MuseumMenuHud>();
                Menu.Configure(Flow, Interaction, Loader, Preferences);
            }

            public MuseumTitleScreen Look()
            {
                var look = Canvas.AddComponent<MuseumTitleScreen>();
                look.Configure(Menu.View, Flow, null);
                return look;
            }

            public void Dispose()
            {
                typeof(MuseumMenuHud).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Menu, null);
                Object.DestroyImmediate(Canvas);
                Object.DestroyImmediate(Root);
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Events.GetComponent<EventSystem>(), null);
                Object.DestroyImmediate(Events);
                MuseumInput.Replace(null);
                if (hadStored) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, stored);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Owner, 22/10: in Lectura and Control the value lay over the option's name («Texto del atril» over its value,
        /// «Giro» over «Continuo»). Each option now shows one value, kept to the right, and its name stops before it.
        /// </summary>
        [Test]
        public void EveryOptionShowsOneValueToTheRightOfItsNameWithoutTouchingIt()
        {
            using var fixture = new MenuFixture();
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1024, 900);
            fixture.Look();
            var menu = fixture.Menu;
            foreach (var spatial in new[] { false, true })
            {
                menu.SetSpatial(spatial);
                var settings = menu.View.Settings;
                settings.gameObject.SetActive(true);
                var layout = menu.View.GetComponent<MuseumMenuLayout>();
                layout.Apply();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)settings.transform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(settings.Scroll.content);
                Canvas.ForceUpdateCanvases();
                var options = new (Button option, TMP_Text value)[]
                {
                    (menu.View.Settings.Subtitles, menu.SubtitlesValue), (menu.SubtitleSizeButton, menu.SubtitleSizeValue),
                    (menu.ReadingSizeButton, menu.ReadingSizeValue), (menu.VignetteButton, menu.VignetteValue),
                    (menu.TurnModeButton, menu.TurnModeValue), (menu.TurnSpeedButton, menu.TurnSpeedValue),
                };
                foreach (var (option, value) in options)
                {
                    var values = option.GetComponentsInChildren<TMP_Text>(true).Where(text => text.name == "Value").ToList();
                    Assert.That(values, Is.EqualTo(new[] { value }), option.name + " shows exactly its own value.");
                    var label = option.GetComponentsInChildren<TMP_Text>(true).First(text => text.name != "Value");
                    Assert.That(value.text, Is.Not.Empty.And.Not.EqualTo(label.text), option.name + " shows a value, not its name again.");
                    // Where it is shown (some options belong to the screen, others to the headset), name and value never touch.
                    if (!option.gameObject.activeSelf) continue;
                    var row = ((RectTransform)option.transform).rect.width;
                    float nameEnds = row - label.margin.z;
                    float valueBegins = row - MuseumTitleScreen.ValueRight - value.rectTransform.rect.width;
                    Assert.That(nameEnds, Is.LessThanOrEqualTo(valueBegins - MuseumTitleScreen.ValueGap + .01f), option.name + ": a gap separates name and value.");
                    // TextMeshPro's preferred width already counts the margins on both sides.
                    Assert.That(label.GetPreferredValues(label.text).x - label.margin.z, Is.LessThanOrEqualTo(nameEnds),
                        option.name + ": «" + label.text + "» fits before its value.");
                    Assert.That(value.GetPreferredValues(value.text).x, Is.LessThanOrEqualTo(value.rectTransform.rect.width),
                        option.name + ": «" + value.text + "» fits its column.");
                    Assert.That(value.alignment, Is.EqualTo(TextAlignmentOptions.MidlineRight));
                }
            }
        }

        /// <summary>The controls page opens on a drawing of the controls: the Quest controllers in a headset, keys and mouse on a screen.</summary>
        [Test]
        public void TheControlsPageDrawsTheControllersOrTheKeyboardWithEveryControlNamed()
        {
            using var fixture = new MenuFixture();
            ((RectTransform)fixture.Canvas.transform).sizeDelta = new Vector2(1600, 900);
            fixture.Look();
            var menu = fixture.Menu;
            var diagram = menu.ControlsDiagram;
            Assert.That(diagram, Is.Not.Null);
            var content = menu.View.Controls.Scroll.content;
            Assert.That(diagram.transform.parent, Is.SameAs(content));
            Assert.That(diagram.transform.GetSiblingIndex(), Is.EqualTo(menu.View.Controls.Description.transform.GetSiblingIndex() + 1),
                "The drawing comes first, right under the page's description.");
            Assert.That(diagram.Headset, Is.False);
            // On a screen every key a visitor may change is drawn and lit, and the mouse is there.
            foreach (var binding in MuseumInput.Current.Bindings.All.Where(binding => binding.Rebindable))
            {
                var mark = diagram.Marks.FirstOrDefault(item => item.Id == binding.Id);
                Assert.That(mark, Is.Not.Null, binding.Id + " is shown.");
                Assert.That(mark.Moving, Is.Not.Null, binding.Id + " has its key on the drawing.");
            }
            Assert.That(diagram.Marks.Any(mark => mark.Id == "mouse_right"), Is.True);
            Assert.That(diagram.Caption, Is.Not.Null);
            diagram.Animate(MuseumControlsDiagram.GestureSeconds * .5f);
            Assert.That(diagram.Caption.text, Does.Contain(diagram.Shown.Action));
            // In a headset: both controllers, each control in use named, the new ones included.
            menu.SetSpatial(true);
            Assert.That(diagram.Headset, Is.True);
            string[] expected = { "trigger_right", "stick_left", "stick_right", "button_a", "button_b", "button_x", "button_y", "button_menu", "grip_left", "grip_right" };
            Assert.That(diagram.Marks.Select(mark => mark.Id), Is.EquivalentTo(expected));
            foreach (var mark in diagram.Marks)
            {
                Assert.That(mark.TitleText, Is.Not.Null.And.Property("text").Not.Empty, mark.Id);
                Assert.That(mark.ActionText.text, Is.EqualTo(mark.Action), mark.Id);
                Assert.That(mark.Leader, Is.Not.Null, mark.Id + " is pointed at.");
            }
            Assert.That(diagram.Marks.First(mark => mark.Id == "button_x").Action, Does.Contain("colibrí"), "X asks the colibrí.");
            Assert.That(diagram.Marks.First(mark => mark.Id == "stick_right").Action, Does.Contain("sentarte"), "Pressing the right stick sits down.");
            Assert.That(diagram.Marks.First(mark => mark.Id == "stick_left").Action, Does.Contain("levantarte"), "Moving the left stick stands up.");
            // One gesture at a time: the control shown moves and takes the accent; the others rest.
            var first = diagram.Shown;
            diagram.Animate(MuseumControlsDiagram.GestureSeconds * 1.3f);
            var shown = diagram.Shown;
            Assert.That(shown, Is.Not.SameAs(first), "The tour of gestures moves on.");
            diagram.Animate(.2f);
            Assert.That(shown.Moving.anchoredPosition != shown.Home || shown.Moving.localScale.x < .999f, Is.True, shown.Id + " plays its gesture.");
            var rest = diagram.Marks.First(mark => mark != shown);
            Assert.That(rest.Moving.anchoredPosition, Is.EqualTo(rest.Home));
            // The drawing fits its column.
            float width = ((RectTransform)diagram.transform).rect.width;
            Assert.That(diagram.Drawing.localScale.x * MuseumControlsDiagram.HeadsetSize.x, Is.LessThanOrEqualTo(width + .5f));
        }
    }
}
