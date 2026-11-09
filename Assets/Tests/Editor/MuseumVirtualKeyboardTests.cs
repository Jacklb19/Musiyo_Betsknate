using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumVirtualKeyboardTests
    {
        [Test]
        public void SpanishDraftsAreBoundedAndBackspaceRemovesAWholeCharacter()
        {
            var root = new GameObject("KeyboardTest");
            try
            {
                var keyboard = root.AddComponent<MuseumVirtualKeyboard>();
                keyboard.Show("¿Qué es Bëtsknaté?", null, null, _ => { }, () => { });
                keyboard.Insert(" ñ");
                Assert.That(keyboard.Draft, Is.EqualTo("¿Qué es Bëtsknaté? ñ"));
                keyboard.Backspace();
                Assert.That(keyboard.Draft, Is.EqualTo("¿Qué es Bëtsknaté? "));
                keyboard.Insert("a\u0301");
                keyboard.Backspace();
                Assert.That(keyboard.Draft, Is.EqualTo("¿Qué es Bëtsknaté? "));
                keyboard.Clear();
                keyboard.Insert(new string('x', 499) + "\U0001F600");
                Assert.That(keyboard.Draft.Length, Is.EqualTo(499), "The limit cannot split a surrogate pair.");
                keyboard.Insert("ab");
                Assert.That(keyboard.Draft.Length, Is.EqualTo(500));
                Assert.That(keyboard.Keys.Any(key => key.name == "ñ"), Is.True);
                Assert.That(keyboard.Keys.Any(key => key.name == "¿"), Is.True);
                keyboard.Clear();
                keyboard.Keys.Single(key => key.name == "ë").onClick.Invoke();
                keyboard.Keys.Single(key => key.name == "2").onClick.Invoke();
                Assert.That(keyboard.Draft, Is.EqualTo("ë2"), "Visitors can type the museum's name and numbers.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ClosingNeverSubmitsAndSendingDeliversTheDraftOnlyOnce()
        {
            var root = new GameObject("KeyboardTest");
            try
            {
                var keyboard = root.AddComponent<MuseumVirtualKeyboard>();
                int sent = 0, closed = 0;
                string received = null;
                keyboard.Show("", null, null, text => { sent++; received = text; }, () => closed++);
                Assert.That(keyboard.SendButton.interactable, Is.False);
                keyboard.Insert("  a ");
                keyboard.Submit();
                Assert.That(sent, Is.Zero);
                keyboard.Hide(true);
                Assert.That(closed, Is.EqualTo(1));
                Assert.That(sent, Is.Zero);
                keyboard.Show("¿Qué es esta pieza?", null, null, text => { sent++; received = text; }, () => closed++);
                keyboard.Submit();
                keyboard.Submit();
                Assert.That(sent, Is.EqualTo(1));
                Assert.That(received, Is.EqualTo("¿Qué es esta pieza?"));
                Assert.That(keyboard.Open, Is.False);
                Assert.That(closed, Is.EqualTo(1), "Sending does not run the cancellation action.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
