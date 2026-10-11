using System.Collections;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    public sealed class RebindingPlayTests : InputTestFixture
    {
        private string stored;
        private bool hadPreferences;
        private string preferencesBefore;

        public override void Setup()
        {
            base.Setup();
            stored = null;
            // The menu saves the visitor's preferences as it is used; this fixture puts them back afterwards.
            hadPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            preferencesBefore = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            MuseumInput.Replace(new MuseumInput(Resources.Load<InputActionAsset>(MuseumInput.ResourceName), null, saved => stored = saved));
        }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            if (hadPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, preferencesBefore);
            else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
            PlayerPrefs.Save();
            base.TearDown();
        }

        /// <summary>Presses a key and lets the capture settle: it waits a moment for a better match before accepting one.</summary>
        private IEnumerator Choose(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            currentTime += .2;
            InputSystem.Update();
            yield return null;
        }

        private static MuseumRebindRow Row(MuseumMenuHud menu, string id)
        {
            foreach (var row in menu.View.Controls.Bindings.GetComponentsInChildren<MuseumRebindRow>(true))
                if (row.Binding.Id == id) return row;
            return null;
        }

        [UnityTest]
        public IEnumerator VisitorReassignsAKeyUsesItReloadsItAndRestoresDefaults()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var input = MuseumInput.Current;
            var root = new GameObject("RebindingFixture");
            root.SetActive(false);
            var runtime = new GameObject("Runtime"); runtime.transform.SetParent(root.transform);
            var loader = runtime.AddComponent<TourLoader>();
            typeof(TourLoader).GetField("loadOnStart", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, false);
            var interaction = runtime.AddComponent<MuseumInteraction>();
            var flow = runtime.AddComponent<MuseumVisitFlow>();
            var events = new GameObject("Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(root.transform);
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)); canvas.transform.SetParent(root.transform);
            var preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
            var menu = canvas.AddComponent<MuseumMenuHud>(); menu.Configure(flow, interaction, loader, preferences);
            root.SetActive(true);
            try
            {
                yield return null;
                menu.View.Main.Controls.onClick.Invoke();
                yield return null;
                var controls = menu.View.Controls;
                Assert.That(controls.gameObject.activeSelf, Is.True);
                var interact = Row(menu, "interact");
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(Row(menu, "move_forward").gameObject),
                    "The first key of the list takes the focus.");

                // Choosing a key: nothing else listens while the museum waits for it.
                interact.Begin();
                Assert.That(input.Capturing, Is.True);
                foreach (var map in input.Asset.actionMaps) Assert.That(map.enabled, Is.False, map.name);
                Assert.That(controls.BindingStatus.text, Does.Contain(MuseumInterfaceText.Get("input_interact")));
                yield return Choose(keyboard.tKey);
                Assert.That(input.Capturing, Is.False);
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Menu));
                Assert.That(interact.Binding.Path, Is.EqualTo("<Keyboard>/t"));
                Assert.That(interact.Key.text, Is.EqualTo("T"));
                Assert.That(controls.gameObject.activeSelf, Is.True, "The chosen key does not also operate the menu.");
                Assert.That(stored, Does.Contain("\"id\":\"interact\"").And.Contain("<Keyboard>/t"));

                // A key already in use where both actions listen is refused and nothing changes.
                var guide = Row(menu, "guide");
                guide.Begin();
                yield return Choose(keyboard.tKey);
                Assert.That(guide.Binding.Path, Is.EqualTo("<Keyboard>/g"));
                Assert.That(guide.Key.text, Is.EqualTo("G"));
                Assert.That(controls.BindingStatus.text, Does.Contain(MuseumInterfaceText.Get("input_interact")));
                Assert.That(input.Capturing, Is.False);

                // Escape cancels the capture without leaving the page or changing the key.
                guide.Begin();
                Assert.That(input.Capturing, Is.True);
                yield return Choose(keyboard.escapeKey);
                Assert.That(input.Capturing, Is.False);
                Assert.That(guide.Binding.Path, Is.EqualTo("<Keyboard>/g"));
                Assert.That(controls.BindingStatus.text, Is.EqualTo(MuseumInterfaceText.Get("rebind_cancelled")));
                Assert.That(controls.gameObject.activeSelf, Is.True, "The cancel key must not also go back to the menu.");

                // Without any key the capture gives up on its own.
                guide.Begin();
                currentTime += MuseumExperienceConfiguration.Current.RebindTimeoutSeconds + 1;
                InputSystem.Update();
                yield return null;
                Assert.That(input.Capturing, Is.False, "A capture nobody answers ends by itself.");
                Assert.That(guide.Binding.Path, Is.EqualTo("<Keyboard>/g"));

                // In the visit the new key acts and the old one does not.
                flow.EnterImmediately();
                yield return null;
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Visit));
                Press(keyboard.eKey);
                yield return null;
                Assert.That(input.Interact.WasPressedThisFrame(), Is.False);
                Release(keyboard.eKey);
                Press(keyboard.tKey);
                yield return null;
                Assert.That(input.Interact.WasPressedThisFrame(), Is.True);
                Release(keyboard.tKey);
                yield return null;

                // A new session reads the stored assignment.
                var reloaded = new MuseumInput(Resources.Load<InputActionAsset>(MuseumInput.ResourceName), stored, null);
                Assert.That(reloaded.Bindings.Find("interact").Path, Is.EqualTo("<Keyboard>/t"));
                reloaded.Dispose();

                // Restoring puts every key back and saves that too.
                interaction.SetPaused(true);
                yield return null;
                menu.View.Pause.Controls.onClick.Invoke();
                yield return null;
                controls.RestoreBindings.onClick.Invoke();
                Assert.That(interact.Binding.Path, Is.EqualTo("<Keyboard>/e"));
                Assert.That(interact.Key.text, Is.EqualTo("E"));
                Assert.That(stored, Does.Not.Contain("interact"));
            }
            finally
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            yield return null;
        }
    }
}
