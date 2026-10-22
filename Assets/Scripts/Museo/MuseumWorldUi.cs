using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Small pieces for interfaces that stand in the world instead of on the screen: a canvas measured in millimetres, texts
    /// in the museum's faces and colours, and quiet buttons. A world canvas is chosen with the mouse on a desktop and with the
    /// controller ray on a headset.
    /// </summary>
    public static class MuseumWorldUi
    {
        /// <summary>One canvas unit is one millimetre in the world.</summary>
        public const float Millimetre = .001f;

        public static Color Text => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.TextColor);
        public static Color Muted => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.MutedTextColor);
        public static Color Accent => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
        public static Color Panel => MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.PanelColor);

        public static Canvas Canvas(string name, Transform parent, Vector2 size, Camera eventCamera)
        {
            var holder = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            holder.transform.SetParent(parent, false);
            var canvas = holder.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;
            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * Millimetre;
            // The mouse reaches it through the camera that shows it; a headset through the controller ray.
            if (MuseumInput.Current.Spatial)
            {
                var ray = holder.AddComponent<TrackedDeviceRaycaster>();
                ray.checkFor3DOcclusion = false;
                ray.maxDistance = MuseumXrConfiguration.Current.RayLength;
            }
            else holder.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Box(string name, Transform parent)
        {
            var box = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(parent, false);
            return box;
        }

        public static void Fill(RectTransform rect, Vector2 inset = default)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
        }

        public static Image Surface(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A text in the museum's display face (titles) or reading face; service text is never parsed as markup.</summary>
        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, bool display = false)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            var face = Resources.Load<TMP_FontAsset>(display ? MuseumTypography.DisplayFont : MuseumTypography.BodyFont);
            if (face != null) label.font = face;
            if (display) label.fontStyle = FontStyles.Bold;
            label.richText = false;
            label.text = text ?? "";
            label.fontSize = size;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        /// <summary>A choice that reads as text on a quiet surface; the one in focus or under the pointer takes the accent.</summary>
        public static Button Choice(string name, Transform parent, string caption, float size, UnityEngine.Events.UnityAction action, bool primary)
        {
            var surface = Surface(name, parent, Color.white);
            surface.raycastTarget = true;
            var button = surface.gameObject.AddComponent<Button>();
            var accent = Accent;
            var colors = button.colors;
            var rest = primary ? new Color(accent.r, accent.g, accent.b, .22f) : new Color(1, 1, 1, .06f);
            colors.normalColor = rest;
            colors.highlightedColor = colors.selectedColor = new Color(accent.r, accent.g, accent.b, .42f);
            colors.pressedColor = new Color(accent.r, accent.g, accent.b, .6f);
            colors.disabledColor = new Color(1, 1, 1, .03f);
            colors.colorMultiplier = 1;
            colors.fadeDuration = .12f;
            button.colors = colors;
            button.onClick.AddListener(action);
            var label = Label("Caption", surface.transform, caption, size, Text);
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            Fill(label.rectTransform);
            return button;
        }

        /// <summary>Faces a world interface towards an eye, upright, turning only around the vertical.</summary>
        public static Quaternion Facing(Vector3 position, Vector3 eye)
        {
            var away = position - eye;
            away.y = 0;
            return Quaternion.LookRotation(away.sqrMagnitude > .0001f ? away.normalized : Vector3.forward, Vector3.up);
        }
    }

    /// <summary>Knows whether a pointer, the mouse or a controller ray, rests on it.</summary>
    public sealed class MuseumPointerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public bool Hovered { get; private set; }
        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;
        public void OnPointerExit(PointerEventData eventData) => Hovered = false;
        private void OnDisable() => Hovered = false;
    }
}
