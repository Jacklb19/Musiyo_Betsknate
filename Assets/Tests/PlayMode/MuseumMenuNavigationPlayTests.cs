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
    public sealed class MuseumMenuNavigationPlayTests : InputTestFixture
    {
        private IEnumerator Tap(KeyControl key)
        {
            Press(key); yield return null;
            Release(key); yield return null;
        }

        [UnityTest]
        public IEnumerator PrefabPagesReceiveKeyboardAndSlidersWithoutLeakingToTheVisit()
        {
            bool hadStoredPreferences = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string storedPreferences = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            var root = new GameObject("MenuNavigationFixture");
            root.SetActive(false);
            var runtime = new GameObject("Runtime"); runtime.transform.SetParent(root.transform);
            var loader = runtime.AddComponent<TourLoader>();
            typeof(TourLoader).GetField("loadOnStart", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, false);
            var interaction = runtime.AddComponent<MuseumInteraction>();
            var flow = runtime.AddComponent<MuseumVisitFlow>();
            var events = new GameObject("Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(root.transform);
            var module = events.GetComponent<InputSystemUIInputModule>(); module.AssignDefaultActions();
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)); canvas.transform.SetParent(root.transform);
            var preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
            var menu = canvas.AddComponent<MuseumMenuHud>(); menu.Configure(flow, interaction, loader, preferences);
            root.SetActive(true);
            try
            {
                yield return null;
                Assert.That(module.move, Is.Null);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Primary.gameObject));
                yield return Tap(keyboard.tabKey);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Settings.gameObject));
                yield return Tap(keyboard.enterKey);
                Assert.That(menu.View.Settings.gameObject.activeSelf, Is.True);
                float sensitivity = preferences.Sensitivity;
                yield return Tap(keyboard.rightArrowKey);
                Assert.That(preferences.Sensitivity, Is.GreaterThan(sensitivity));
                yield return Tap(keyboard.leftArrowKey);
                Assert.That(preferences.Sensitivity, Is.EqualTo(sensitivity).Within(.001f));
                yield return Tap(keyboard.sKey);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Settings.Volume.gameObject));
                yield return Tap(keyboard.wKey);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Settings.Sensitivity.gameObject));
                yield return Tap(keyboard.backspaceKey);
                Assert.That(menu.View.Main.gameObject.activeSelf, Is.True);
                Press(keyboard.leftShiftKey);
                yield return Tap(keyboard.tabKey);
                Release(keyboard.leftShiftKey);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(menu.View.Main.Controls.gameObject));
                yield return Tap(keyboard.enterKey);
                Assert.That(menu.View.Controls.gameObject.activeSelf, Is.True);
                yield return Tap(keyboard.escapeKey);
                Assert.That(menu.View.Main.gameObject.activeSelf, Is.True);
                Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
                Assert.That(interaction.ActivePoint, Is.Null);
                menu.enabled = false;
                Assert.That(module.move, Is.Not.Null);
                menu.enabled = true;
                yield return null;
                Assert.That(canvas.GetComponentsInChildren<MuseumMenuView>(true).Length, Is.EqualTo(1));
                Assert.That(module.move, Is.Null);
                Object.Destroy(menu);
                yield return null;
                Assert.That(flow.Menu, Is.Null);
                Assert.That(module.move, Is.Not.Null);
            }
            finally
            {
                root.SetActive(false);
                Object.Destroy(root);
                if (hadStoredPreferences) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, storedPreferences);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
            yield return null;
        }
    }
}
