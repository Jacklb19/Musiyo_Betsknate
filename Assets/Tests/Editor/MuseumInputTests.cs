using System.Collections.Generic;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumInputTests : InputTestFixture
    {
        private static readonly MuseumInputContext[] Contexts =
        {
            MuseumInputContext.Suspended, MuseumInputContext.Visit, MuseumInputContext.Panel,
            MuseumInputContext.Examine, MuseumInputContext.Menu
        };
        private MuseumInput input;
        private Keyboard keyboard;
        private Mouse mouse;
        private string stored;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            stored = null;
            input = new MuseumInput(Resources.Load<InputActionAsset>(MuseumInput.ResourceName), null, saved => stored = saved);
            MuseumInput.Replace(input);
        }

        public override void TearDown()
        {
            MuseumInput.Replace(null);
            base.TearDown();
        }

        private MuseumInputBinding Binding(string id) => input.Bindings.Find(id);

        [Test]
        public void EachContextListensToItsMapsAndWalkingNeverSharesOneWithNavigation()
        {
            foreach (var context in Contexts)
            {
                input.SetContext(context);
                Assert.That(input.Context, Is.EqualTo(context));
                Assert.That(input.Move.enabled && input.Navigate.enabled, Is.False, context.ToString());
                Assert.That(input.Move.enabled, Is.EqualTo(context == MuseumInputContext.Visit), context.ToString());
                Assert.That(input.Navigate.enabled,
                    Is.EqualTo(context == MuseumInputContext.Panel || context == MuseumInputContext.Menu), context.ToString());
                Assert.That(input.Rotate.enabled, Is.EqualTo(context == MuseumInputContext.Examine), context.ToString());
                Assert.That(input.Interact.enabled, Is.EqualTo(context != MuseumInputContext.Menu
                    && context != MuseumInputContext.Suspended), context.ToString());
                Assert.That(input.Pause.enabled, Is.EqualTo(context != MuseumInputContext.Suspended), context.ToString());
                Assert.That(input.Click.enabled, Is.EqualTo(context != MuseumInputContext.Suspended), context.ToString());
            }
        }

        [Test]
        public void OneKeyPressNeverWalksAndMovesTheFocusTogether()
        {
            foreach (var context in Contexts)
            {
                input.SetContext(context);
                foreach (var key in new[] { keyboard.wKey, keyboard.upArrowKey, keyboard.sKey, keyboard.leftArrowKey })
                {
                    Press(key);
                    bool walks = input.Move.ReadValue<Vector2>() != Vector2.zero;
                    bool navigates = input.Navigate.ReadValue<Vector2>() != Vector2.zero;
                    Assert.That(walks && navigates, Is.False, context + " " + key.name);
                    Assert.That(walks, Is.EqualTo(context == MuseumInputContext.Visit), context + " " + key.name);
                    Release(key);
                }
            }
        }

        [Test]
        public void ReapplyingTheContextRepairsMapsAnotherComponentChanged()
        {
            input.SetContext(MuseumInputContext.Menu);
            input.Asset.FindActionMap("Locomotion").Enable();
            input.Pause.Disable();
            input.SetContext(MuseumInputContext.Menu);
            Assert.That(input.Move.enabled, Is.False);
            Assert.That(input.Pause.enabled, Is.True);
        }

        [Test]
        public void CaptureSilencesEveryActionAndReturnsToTheRequestedContext()
        {
            input.SetContext(MuseumInputContext.Menu);
            input.BeginCapture();
            Assert.That(input.Capturing, Is.True);
            foreach (var map in input.Asset.actionMaps) Assert.That(map.enabled, Is.False, map.name);
            // The visit may change state during a capture; that context applies once the capture ends.
            input.SetContext(MuseumInputContext.Visit);
            Assert.That(input.Move.enabled, Is.False);
            input.EndCapture();
            Assert.That(input.Capturing, Is.False);
            Assert.That(input.CaptureEndedThisFrame, Is.True);
            Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Visit));
            Assert.That(input.Move.enabled, Is.True);
        }

        [Test]
        public void ShippedKeysDoNotCollideWhereTheyCanListenTogether()
        {
            foreach (var binding in input.Bindings.All)
                Assert.That(input.Bindings.FindConflict(binding, binding.Path), Is.Null, binding.Id);
            // Every key on the keyboard that the museum uses has a name for the conflict message.
            foreach (var map in input.Asset.actionMaps)
                foreach (var action in map.actions)
                    for (int index = 0; index < action.bindings.Count; index++)
                    {
                        var binding = action.bindings[index];
                        if (binding.isComposite || !binding.path.StartsWith("<Keyboard>/")) continue;
                        Assert.That(MuseumInterfaceText.Current.Find(input.Bindings.LabelKey(action, index)), Is.Not.Null,
                            map.name + "/" + action.name + " " + binding.path);
                    }
            foreach (var binding in input.Bindings.All)
                Assert.That(MuseumInterfaceText.Current.Find(binding.LabelKey), Is.Not.Null, binding.Id);
        }

        [Test]
        public void ConflictsAreJudgedByContext()
        {
            // Walking and the visit actions listen together.
            Assert.That(input.Bindings.TryRebind(Binding("guide"), "<Keyboard>/w", out var conflict), Is.EqualTo(MuseumRebindResult.Conflict));
            Assert.That(conflict, Is.EqualTo("input_move_forward"));
            Assert.That(input.Bindings.TryRebind(Binding("move_forward"), "<Keyboard>/upArrow", out conflict), Is.EqualTo(MuseumRebindResult.Conflict));
            Assert.That(conflict, Is.EqualTo("input_move_forward"), "The fixed arrow keys already walk.");
            Assert.That(input.Bindings.TryRebind(Binding("interact"), "<Keyboard>/enter", out conflict), Is.EqualTo(MuseumRebindResult.Conflict));
            Assert.That(conflict, Is.EqualTo("input_confirm"));
            // The visit actions also listen while a model is examined and while a panel takes navigation.
            Assert.That(input.Bindings.TryRebind(Binding("guide"), "<Keyboard>/home", out conflict), Is.EqualTo(MuseumRebindResult.Conflict));
            Assert.That(conflict, Is.EqualTo("input_action_reset"));
            Assert.That(Binding("guide").Path, Is.EqualTo("<Keyboard>/g"), "A rejected key leaves the assignment untouched.");
            Assert.That(stored, Is.Null, "A rejected key is not saved.");
            // Walking never listens while a model is examined, so the same key is free for it.
            Assert.That(input.Bindings.TryRebind(Binding("sprint"), "<Keyboard>/home", out conflict), Is.EqualTo(MuseumRebindResult.Applied));
            // Shift with Tab is a different combination from the key that is being assigned alone.
            Assert.That(input.Bindings.TryRebind(Binding("detail"), "<Keyboard>/tab", out conflict), Is.EqualTo(MuseumRebindResult.Conflict));
            Assert.That(conflict, Is.EqualTo("input_next"));
            // A key freed by one action becomes available to another.
            Assert.That(input.Bindings.TryRebind(Binding("move_forward"), "<Keyboard>/t", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(input.Bindings.TryRebind(Binding("guide"), "<Keyboard>/w", out conflict), Is.EqualTo(MuseumRebindResult.Conflict),
                "W still moves the focus in panels, where the guide key also listens.");
            Assert.That(conflict, Is.EqualTo("input_action_navigate"));
            Assert.That(input.Bindings.TryRebind(Binding("sprint"), "<Keyboard>/w", out _), Is.EqualTo(MuseumRebindResult.Applied));
        }

        [TestCase("<Mouse>/leftButton")]
        [TestCase("<Keyboard>/escape")]
        [TestCase("<Keyboard>/anyKey")]
        [TestCase("<Keyboard>/notAKey")]
        [TestCase("<Gamepad>/buttonSouth")]
        [TestCase("")]
        [TestCase(null)]
        public void OnlyRealKeyboardKeysOtherThanTheCancelKeyCanBeAssigned(string path)
        {
            Assert.That(input.Bindings.TryRebind(Binding("guide"), path, out _), Is.EqualTo(MuseumRebindResult.UnsupportedKey));
            Assert.That(Binding("guide").Path, Is.EqualTo("<Keyboard>/g"));
        }

        [Test]
        public void FixedKeysStayFixedAndPauseCanReturnToItsDefault()
        {
            Assert.That(input.Bindings.TryRebind(Binding("confirm"), "<Keyboard>/t", out _), Is.EqualTo(MuseumRebindResult.NotRebindable));
            Assert.That(input.Bindings.TryRebind(Binding("pause"), "<Keyboard>/p", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(input.Bindings.Restore(Binding("pause"), out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(Binding("pause").Path, Is.EqualTo("<Keyboard>/escape"));
            Assert.That(Binding("pause").Overridden, Is.False);
        }

        [Test]
        public void AReassignedKeyActsAndTheOldOneNoLongerDoes()
        {
            input.SetContext(MuseumInputContext.Visit);
            Assert.That(input.Bindings.TryRebind(Binding("interact"), "<Keyboard>/t", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Press(keyboard.eKey);
            Assert.That(input.Interact.WasPressedThisFrame(), Is.False);
            Release(keyboard.eKey);
            Press(keyboard.tKey);
            Assert.That(input.Interact.WasPressedThisFrame(), Is.True);
            Release(keyboard.tKey);
            Assert.That(input.Bindings.TryRebind(Binding("move_forward"), "<Keyboard>/u", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Press(keyboard.uKey);
            Assert.That(input.Move.ReadValue<Vector2>().y, Is.GreaterThan(0));
            Release(keyboard.uKey);
            Press(keyboard.wKey);
            Assert.That(input.Move.ReadValue<Vector2>(), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void AssignmentsSurviveAReloadAndDefaultsCanBeRestored()
        {
            int changes = 0;
            input.BindingsChanged += () => changes++;
            Assert.That(input.Bindings.TryRebind(Binding("guide"), "<Keyboard>/t", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(input.Bindings.TryRebind(Binding("move_left"), "<Keyboard>/q", out _), Is.EqualTo(MuseumRebindResult.Applied));
            Assert.That(changes, Is.EqualTo(2));
            Assert.That(stored, Does.Contain("\"schema_version\":1").And.Contain("\"id\":\"guide\"").And.Contain("<Keyboard>/t"));
            Assert.That(stored, Does.Not.Contain("move_forward"), "Only changed keys are stored.");
            var reloaded = new MuseumInput(Resources.Load<InputActionAsset>(MuseumInput.ResourceName), stored, null);
            try
            {
                Assert.That(reloaded.Bindings.Find("guide").Path, Is.EqualTo("<Keyboard>/t"));
                Assert.That(reloaded.Bindings.Find("move_left").Path, Is.EqualTo("<Keyboard>/q"));
                Assert.That(reloaded.Bindings.Find("detail").Overridden, Is.False);
            }
            finally { reloaded.Dispose(); }
            input.Bindings.RestoreDefaults();
            Assert.That(Binding("guide").Path, Is.EqualTo("<Keyboard>/g"));
            Assert.That(Binding("move_left").Path, Is.EqualTo("<Keyboard>/a"));
            Assert.That(stored, Does.Not.Contain("guide"));
            Assert.That(changes, Is.EqualTo(3));
        }

        [TestCase("{\"schema_version\":2,\"bindings\":[{\"id\":\"guide\",\"path\":\"<Keyboard>/t\"}]}")]
        [TestCase("{\"bindings\":[{\"id\":\"guide\",\"path\":\"<Keyboard>/t\"}]}")]
        [TestCase("{\"schema_version\":1}")]
        [TestCase("{\"schema_version\":1,\"bindings\":[{\"id\":\"guide\",\"path\":\"<Keyboard>/w\"}]}")]
        [TestCase("{\"schema_version\":1,\"bindings\":[{\"id\":\"guide\",\"path\":\"<Keyboard>/t\"},{\"id\":\"detail\",\"path\":\"<Keyboard>/t\"}]}")]
        [TestCase("not json")]
        public void UnknownVersionsAndConflictingSavedKeysFallBackToDefaults(string saved)
        {
            input.Bindings.Load(saved);
            foreach (var binding in input.Bindings.All) Assert.That(binding.Overridden, Is.False, binding.Id);
        }

        [Test]
        public void SavedKeysSkipUnknownEntriesAndAcceptASwap()
        {
            input.Bindings.Load("{\"schema_version\":1,\"bindings\":[{\"id\":\"removed_action\",\"path\":\"<Keyboard>/t\"},"
                + "{\"id\":\"confirm\",\"path\":\"<Keyboard>/y\"},{\"id\":\"guide\",\"path\":\"<Mouse>/leftButton\"},"
                + "{\"id\":\"detail\",\"path\":\"<Keyboard>/x\"},{\"id\":\"examine\",\"path\":\"<Keyboard>/f\"}]}");
            Assert.That(Binding("confirm").Overridden, Is.False);
            Assert.That(Binding("guide").Overridden, Is.False);
            Assert.That(Binding("detail").Path, Is.EqualTo("<Keyboard>/x"));
            Assert.That(Binding("examine").Path, Is.EqualTo("<Keyboard>/f"));
        }

        [Test]
        public void PromptsNameTheKeyInForce()
        {
            Assert.That(MuseumInputLabels.For("guide"), Is.EqualTo("G"));
            Assert.That(MuseumInputLabels.For("narration_pause"), Is.EqualTo(MuseumInterfaceText.Get("key_space")));
            Assert.That(MuseumInputLabels.For("pause"), Is.EqualTo(MuseumInterfaceText.Get("key_escape")));
            Assert.That(MuseumInterfaceText.Get("examine_model"), Does.EndWith("(X)"));
            input.Bindings.TryRebind(Binding("examine"), "<Keyboard>/rightShift", out _);
            Assert.That(MuseumInterfaceText.Get("examine_model"), Does.EndWith("(" + MuseumInterfaceText.Get("key_rightShift") + ")"));
            Assert.That(MuseumInputLabels.Expand("{input:examine} · {input:unknown} · {input:broken"),
                Is.EqualTo(MuseumInterfaceText.Get("key_rightShift") + " · unknown · {input:broken"));
            Assert.That(MuseumInputLabels.Expand("{0} {input:guide}", true), Is.EqualTo("{0} G"));
            // No shipped text is left with a marker nobody can resolve.
            var entries = (Dictionary<string, string>)typeof(MuseumInterfaceText)
                .GetField("entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(MuseumInterfaceText.Current);
            foreach (var entry in entries)
                foreach (System.Text.RegularExpressions.Match marker in System.Text.RegularExpressions.Regex.Matches(entry.Value, "\\{input:([^}]*)\\}"))
                    Assert.That(Binding(marker.Groups[1].Value), Is.Not.Null, entry.Key + " names " + marker.Value);
        }

        [Test]
        public void PointerAndNumberKeysAreReadThroughActions()
        {
            input.SetContext(MuseumInputContext.Visit);
            Press(mouse.leftButton);
            Assert.That(input.Click.WasPressedThisFrame(), Is.True);
            Assert.That(input.Click.IsPressed(), Is.True);
            Release(mouse.leftButton);
            Assert.That(input.Click.WasPressedThisFrame(), Is.False);
            Assert.That(input.ChosenElement(), Is.EqualTo(-1));
            Press(keyboard.digit3Key);
            Assert.That(input.ChooseElement.WasPressedThisFrame(), Is.True);
            Assert.That(input.ChosenElement(), Is.EqualTo(2));
            Release(keyboard.digit3Key);
            Press(keyboard.pageDownKey);
            Assert.That(input.ScrollPanel.WasPressedThisFrame(), Is.True);
            Assert.That(input.ScrollPanel.ReadValue<float>(), Is.LessThan(0));
            Release(keyboard.pageDownKey);
            Press(keyboard.leftShiftKey);
            Press(keyboard.tabKey);
            Assert.That(input.Previous.WasPressedThisFrame(), Is.True, "Shift with Tab goes back.");
        }

        [Test]
        public void InterfaceModuleUsesTheSharedActionsAndFollowsArrowsOnlyInPanels()
        {
            var events = new GameObject("InputTestEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
            try
            {
                var module = events.GetComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
                input.SetContext(MuseumInputContext.Visit);
                input.BindUi(module);
                Assert.That(module.actionsAsset, Is.SameAs(input.Asset));
                Assert.That(module.point.action, Is.SameAs(input.Point));
                Assert.That(module.leftClick.action, Is.SameAs(input.Click));
                Assert.That(module.submit.action, Is.SameAs(input.Confirm));
                Assert.That(module.cancel, Is.Null);
                Assert.That(module.move, Is.Null, "Walking keys must not move the focus.");
                Assert.That(input.Navigate.enabled, Is.False);
                input.SetContext(MuseumInputContext.Panel);
                Assert.That(module.move.action, Is.SameAs(input.Navigate));
                input.SetContext(MuseumInputContext.Menu);
                Assert.That(module.move, Is.Null);
                input.SetContext(MuseumInputContext.Examine);
                Assert.That(module.move, Is.Null, "Arrows rotate the model, not the focus.");
                Assert.That(input.Navigate.enabled, Is.False);
            }
            finally { Object.DestroyImmediate(events); }
        }

        [Test]
        public void VisitStateDecidesTheContext()
        {
            var root = new GameObject("InputDirectorTest");
            try
            {
                root.AddComponent<TourRuntime>();
                var interaction = root.AddComponent<MuseumInteraction>();
                var director = MuseumInputDirector.Ensure(interaction);
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Visit), "Without an entry menu the visitor simply walks.");
                var flow = root.AddComponent<MuseumVisitFlow>();
                director.Apply();
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Menu));
                flow.EnterImmediately();
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Visit), "Entering the museum applies at once.");
                interaction.SetPaused(true);
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Menu));
                interaction.SetPaused(false);
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Visit));
                Assert.That(MuseumInputDirector.Ensure(interaction), Is.SameAs(director));
                flow.ReturnToMenu();
                Assert.That(input.Context, Is.EqualTo(MuseumInputContext.Menu));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
