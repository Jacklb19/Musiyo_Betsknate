using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MusiyoBetsknate.Tests
{
    public sealed class DesktopActionsTests : InputTestFixture
    {
        [Test]
        public void SavedActionsSupportKeyboardMovementAndLooking()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var input = MuseumInput.CreateDefault();
            try
            {
                input.SetContext(MuseumInputContext.Visit);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D, Key.LeftShift, Key.J));
                InputSystem.Update();
                var movement = input.Move.ReadValue<Vector2>();
                Assert.That(movement.x, Is.GreaterThan(0));
                Assert.That(movement.y, Is.GreaterThan(0));
                Assert.That(movement.magnitude, Is.EqualTo(1).Within(.001f));
                Assert.That(input.Sprint.IsPressed(), Is.True);
                Assert.That(input.KeyboardLook.ReadValue<Vector2>().x, Is.LessThan(0));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                Assert.That(input.Move.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            }
            finally { input.Dispose(); }
        }
    }
}
