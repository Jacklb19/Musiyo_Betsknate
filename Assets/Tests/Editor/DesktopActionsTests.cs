using System.IO;
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
            var asset = InputActionAsset.FromJson(File.ReadAllText("Assets/_Musiyo/Configuration/MuseumDesktop.inputactions"));
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                asset.devices = new InputDevice[] { keyboard };
                asset.Enable();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D, Key.LeftShift, Key.J));
                InputSystem.Update();
                var movement = asset.FindAction("Desktop/Move").ReadValue<Vector2>();
                Assert.That(movement.x, Is.GreaterThan(0));
                Assert.That(movement.y, Is.GreaterThan(0));
                Assert.That(movement.magnitude, Is.EqualTo(1).Within(.001f));
                Assert.That(asset.FindAction("Desktop/Sprint").IsPressed(), Is.True);
                Assert.That(asset.FindAction("Desktop/KeyboardLook").ReadValue<Vector2>().x, Is.LessThan(0));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                Assert.That(asset.FindAction("Desktop/Move").ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
            }
            finally
            {
                asset.Disable();
                InputSystem.RemoveDevice(keyboard);
                Object.DestroyImmediate(asset);
            }
        }
    }
}
