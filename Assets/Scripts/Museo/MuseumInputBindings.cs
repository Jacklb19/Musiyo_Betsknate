using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>One key of one action, as the visitor sees it in the controls list and in the prompts.</summary>
    public sealed class MuseumInputBinding
    {
        public string Id { get; }
        public InputAction Action { get; }
        public int Index { get; }
        public bool Rebindable { get; }
        public string LabelKey => "input_" + Id;
        public string DefaultPath => Action.bindings[Index].path;
        public string Path => Action.bindings[Index].effectivePath;
        public bool Overridden => !string.Equals(Path, DefaultPath, StringComparison.OrdinalIgnoreCase);

        internal MuseumInputBinding(string id, InputAction action, int index, bool rebindable)
        { Id = id; Action = action; Index = index; Rebindable = rebindable; }
    }

    public enum MuseumRebindResult { Applied, NotRebindable, UnsupportedKey, Conflict }

    /// <summary>
    /// The keys a visitor may change, their conflicts and their versioned storage.
    /// Stores key assignments only: never content, preferences of the visit or anything published.
    /// </summary>
    public sealed class MuseumInputBindings
    {
        private const int SchemaVersion = 1;
        private const string KeyboardPrefix = "<Keyboard>/";
        /// <summary>Escape always cancels a capture, so it can only be the default of the action that ships with it.</summary>
        public const string CancelPath = "<Keyboard>/escape";
        // Id, action, composite part and whether the visitor may change it. Navigation and confirmation stay fixed
        // so the controls page itself can always be operated.
        private static readonly (string id, string action, string part, bool rebindable)[] Definitions =
        {
            ("move_forward", "Locomotion/Move", "up", true), ("move_back", "Locomotion/Move", "down", true),
            ("move_left", "Locomotion/Move", "left", true), ("move_right", "Locomotion/Move", "right", true),
            ("look_up", "Locomotion/KeyboardLook", "up", true), ("look_down", "Locomotion/KeyboardLook", "down", true),
            ("look_left", "Locomotion/KeyboardLook", "left", true), ("look_right", "Locomotion/KeyboardLook", "right", true),
            ("sprint", "Locomotion/Sprint", null, true), ("interact", "Visit/Interact", null, true),
            ("detail", "Visit/Detail", null, true), ("examine", "Visit/Examine", null, true),
            ("guide", "Visit/Guide", null, true), ("wayfinding", "Visit/Wayfinding", null, true),
            ("narration_pause", "Visit/NarrationPause", null, true), ("narration_repeat", "Visit/NarrationRepeat", null, true),
            ("narration_mute", "Visit/NarrationMute", null, true), ("auto_tour", "Visit/AutoTour", null, true),
            ("ask_companion", "Visit/AskCompanion", null, true),
            ("sit", "Visit/Sit", null, true),
            ("pause", "Common/Pause", null, true),
            ("back", "Common/Back", null, true), ("confirm", "Common/Confirm", null, false), ("next", "Common/Next", null, false)
        };
        private readonly MuseumInput input;
        private readonly List<MuseumInputBinding> all = new();
        private readonly Dictionary<string, MuseumInputBinding> byId = new();

        /// <summary>Every listed key, in the order of the controls page.</summary>
        public IReadOnlyList<MuseumInputBinding> All => all;

        internal MuseumInputBindings(MuseumInput owner)
        {
            input = owner;
            foreach (var (id, path, part, rebindable) in Definitions)
            {
                var action = owner.Asset.FindAction(path, true);
                int index = action.bindings.IndexOf(binding => !binding.isComposite
                    && (part == null ? !binding.isPartOfComposite : binding.isPartOfComposite && binding.name == part));
                if (index < 0) throw new InvalidOperationException("Museum input actions lack the key of " + id + ".");
                var entry = new MuseumInputBinding(id, action, index, rebindable);
                all.Add(entry);
                byId.Add(id, entry);
            }
        }

        public MuseumInputBinding Find(string id) => id != null && byId.TryGetValue(id, out var entry) ? entry : null;

        /// <summary>Assigns a key after checking that no action listening in the same context already uses it.</summary>
        public MuseumRebindResult TryRebind(MuseumInputBinding binding, string path, out string conflictLabelKey)
        {
            var result = Apply(binding, path, out conflictLabelKey);
            if (result == MuseumRebindResult.Applied) input.NotifyBindingsChanged();
            return result;
        }

        public MuseumRebindResult Restore(MuseumInputBinding binding, out string conflictLabelKey)
        {
            conflictLabelKey = null;
            return binding == null ? MuseumRebindResult.NotRebindable : TryRebind(binding, binding.DefaultPath, out conflictLabelKey);
        }

        public void RestoreDefaults()
        {
            input.Asset.RemoveAllBindingOverrides();
            input.NotifyBindingsChanged();
        }

        private MuseumRebindResult Apply(MuseumInputBinding binding, string path, out string conflictLabelKey)
        {
            conflictLabelKey = null;
            if (binding == null || !binding.Rebindable) return MuseumRebindResult.NotRebindable;
            bool restoring = string.Equals(path, binding.DefaultPath, StringComparison.OrdinalIgnoreCase);
            if (!restoring && !Supported(path)) return MuseumRebindResult.UnsupportedKey;
            conflictLabelKey = FindConflict(binding, path);
            if (conflictLabelKey != null) return MuseumRebindResult.Conflict;
            if (restoring) binding.Action.RemoveBindingOverride(binding.Index);
            else binding.Action.ApplyBindingOverride(binding.Index, path);
            return MuseumRebindResult.Applied;
        }

        private static bool Supported(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, CancelPath, StringComparison.OrdinalIgnoreCase)) return false;
            string key = path.Substring(KeyboardPrefix.Length);
            if (key.Length == 0 || key == "anyKey" || key.IndexOfAny(new[] { '/', '*', '{', '<' }) >= 0) return false;
            // The path must name a real key, whether or not a keyboard is connected right now.
            return InputSystem.LoadLayout<Keyboard>()?.FindControl(new InternedString(key)) != null;
        }

        /// <summary>Label key of the binding that already uses the path where both could listen together, or null.</summary>
        public string FindConflict(MuseumInputBinding target, string path)
        {
            string wanted = Combination(target.Action, target.Index, path);
            foreach (var map in input.Asset.actionMaps)
            {
                if (!MuseumInput.ShareContext(target.Action.actionMap.name, map.name)) continue;
                foreach (var action in map.actions)
                    for (int index = 0; index < action.bindings.Count; index++)
                    {
                        var other = action.bindings[index];
                        if (other.isComposite || action == target.Action && index == target.Index
                            || other.isPartOfComposite && other.name == "modifier") continue;
                        if (string.Equals(Combination(action, index, other.effectivePath), wanted, StringComparison.OrdinalIgnoreCase))
                            return LabelKey(action, index);
                    }
            }
            return null;
        }

        /// <summary>A key held with a modifier is a different combination from the key alone.</summary>
        private static string Combination(InputAction action, int index, string path)
        {
            var bindings = action.bindings;
            if (!bindings[index].isPartOfComposite) return path;
            int head = index;
            while (head >= 0 && !bindings[head].isComposite) head--;
            for (int part = head + 1; part < bindings.Count && bindings[part].isPartOfComposite; part++)
                if (bindings[part].name == "modifier") return bindings[part].effectivePath + "+" + path;
            return path;
        }

        /// <summary>Interface text key naming what a binding does, including keys that are not in the controls list.</summary>
        public string LabelKey(InputAction action, int index)
        {
            MuseumInputBinding samePart = null;
            foreach (var entry in all)
            {
                if (entry.Action != action) continue;
                if (entry.Index == index) return entry.LabelKey;
                if (entry.Action.bindings[entry.Index].name == action.bindings[index].name) samePart ??= entry;
            }
            return samePart != null ? samePart.LabelKey : "input_action_" + action.name.ToLowerInvariant();
        }

        public string Serialize()
        {
            var saved = new SavedBindings { SchemaVersion = SchemaVersion, Bindings = new List<SavedBinding>() };
            foreach (var entry in all)
                if (entry.Rebindable && entry.Overridden) saved.Bindings.Add(new SavedBinding { Id = entry.Id, Path = entry.Path });
            return JsonConvert.SerializeObject(saved);
        }

        /// <summary>Replaces the current assignments with saved ones. Unknown versions, ids, keys and conflicts fall back to defaults.</summary>
        public void Load(string serialized)
        {
            input.Asset.RemoveAllBindingOverrides();
            if (string.IsNullOrEmpty(serialized)) return;
            SavedBindings saved = null;
            try { saved = JsonConvert.DeserializeObject<SavedBindings>(serialized); }
            catch (JsonException) { }
            if (saved == null || saved.SchemaVersion != SchemaVersion || saved.Bindings == null) return;
            // Two saved keys may have swapped places, so each is checked against the others only after all are in place.
            var applied = new List<MuseumInputBinding>();
            foreach (var item in saved.Bindings)
            {
                var entry = item == null ? null : Find(item.Id);
                if (entry == null || !entry.Rebindable || applied.Contains(entry) || !Supported(item.Path)) continue;
                entry.Action.ApplyBindingOverride(entry.Index, item.Path);
                applied.Add(entry);
            }
            foreach (var entry in applied)
                if (FindConflict(entry, entry.Path) != null) { input.Asset.RemoveAllBindingOverrides(); return; }
        }

        [Preserve]
        private sealed class SavedBindings
        {
            [JsonProperty("schema_version", Required = Required.Always)] public int SchemaVersion;
            [JsonProperty("bindings", Required = Required.Always)] public List<SavedBinding> Bindings;
        }

        [Preserve]
        private sealed class SavedBinding
        {
            [JsonProperty("id", Required = Required.Always)] public string Id;
            [JsonProperty("path", Required = Required.Always)] public string Path;
        }
    }
}
