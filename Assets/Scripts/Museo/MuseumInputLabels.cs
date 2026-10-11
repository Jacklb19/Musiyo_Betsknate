using System.Text;
using UnityEngine.InputSystem;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Names the keys currently assigned, so controls, prompts and the tutorial never show a stale key.</summary>
    public static class MuseumInputLabels
    {
        private const string TokenStart = "{input:";
        private const string KeyboardPrefix = "<Keyboard>/";

        /// <summary>Key assigned to a listed binding such as "pause" or "move_forward".</summary>
        public static string For(string id)
        {
            var binding = MuseumInput.Current.Bindings.Find(id);
            return binding == null ? id : Path(binding.Path);
        }

        /// <summary>Visitor-facing name of a control path. Named keys come from the interface catalog; letters follow the keyboard layout.</summary>
        public static string Path(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.StartsWith(KeyboardPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                var named = MuseumInterfaceText.Current.Find("key_" + path.Substring(KeyboardPrefix.Length));
                if (named != null) return named;
            }
            var control = InputSystem.FindControl(path);
            string name = control != null && !string.IsNullOrEmpty(control.displayName) ? control.displayName
                : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return name.Length == 1 ? name.ToUpperInvariant() : name;
        }

        /// <summary>Replaces every {input:id} in interface text with the key assigned to that binding.</summary>
        public static string Expand(string text, bool escapeBraces = false)
        {
            if (text == null) return null;
            int start = text.IndexOf(TokenStart, System.StringComparison.Ordinal);
            if (start < 0) return text;
            var result = new StringBuilder(text.Length);
            int position = 0;
            while (start >= 0)
            {
                int end = text.IndexOf('}', start);
                if (end < 0) break;
                result.Append(text, position, start - position);
                string label = For(text.Substring(start + TokenStart.Length, end - start - TokenStart.Length));
                // Text that is formatted afterwards must not read a key such as "{" as a placeholder.
                result.Append(escapeBraces ? label.Replace("{", "{{").Replace("}", "}}") : label);
                position = end + 1;
                start = text.IndexOf(TokenStart, position, System.StringComparison.Ordinal);
            }
            return result.Append(text, position, text.Length - position).ToString();
        }
    }
}
