using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Scrolls the point screen by hand and shows where the reader is in it. The right controller stick scrolls
    /// continuously, faster the further it is pushed, every frame it is held (it used to move a fixed step only when the
    /// stick crossed its threshold, which read as scrolling "now and then"); the page keys still move one step per press.
    /// A thin bar at the edge of the text shows the visible part of the record while it is longer than the screen.
    /// </summary>
    public sealed class MuseumPanelScroll : MonoBehaviour
    {
        /// <summary>Stick travel that is ignored, so a resting thumb never scrolls.</summary>
        public const float Deadzone = .2f;
        /// <summary>Width of the position bar, in interface units.</summary>
        public const float BarWidth = 7;
        /// <summary>How far one notch of the mouse wheel moves the text, in interface units: about three lines.</summary>
        public const float WheelUnits = 96;
        private static int scrolledFrame = -10;
        private ScrollRect scroll;
        private RectTransform track, handle;

        public RectTransform Track => track;
        public RectTransform Handle => handle;
        /// <summary>True while the stick scrolls a screen, so the same stick does not also turn the visitor.</summary>
        public static bool Engaged => Time.frameCount - scrolledFrame <= 1;

        public void Configure(ScrollRect panelScroll)
        {
            scroll = panelScroll;
            if (scroll == null) return;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            track = Bar("ScrollPosition", viewport, MuseumHudSkin.Muted, .22f);
            track.anchorMin = new Vector2(1, 0);
            track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, .5f);
            track.sizeDelta = new Vector2(BarWidth, 0);
            track.anchoredPosition = Vector2.zero;
            handle = Bar("Handle", track, MuseumHudSkin.Accent, .9f);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            track.gameObject.SetActive(false);
        }

        private static RectTransform Bar(string name, Transform parent, Color colour, float alpha)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            item.GetComponent<LayoutElement>().ignoreLayout = true;
            var image = item.GetComponent<Image>();
            image.sprite = MuseumUiSprites.Rounded(3);
            image.type = Image.Type.Sliced;
            colour.a = alpha;
            image.color = colour;
            image.raycastTarget = false;
            return (RectTransform)item.transform;
        }

        /// <summary>Speed of the content, in interface units per second, for a stick pushed this far (-1 to 1).</summary>
        public static float StickSpeed(float value, float fullSpeed)
        {
            float travel = Mathf.Abs(value);
            if (travel < Deadzone) return 0;
            float share = Mathf.Clamp01((travel - Deadzone) / (1 - Deadzone));
            // Gentle at first, so a slight push reads line by line; full speed skims.
            return Mathf.Sign(value) * fullSpeed * share * share;
        }

        /// <summary>Applies one frame of the scroll action; true when the visitor moved the text.</summary>
        public bool Step(InputAction action, bool allowed, float elapsed)
        {
            if (scroll == null || action == null || !allowed || !scroll.isActiveAndEnabled) return false;
            float scrollable = Scrollable();
            if (scrollable <= 1) return false;
            var settings = MuseumExperienceConfiguration.Current;
            float position = scroll.verticalNormalizedPosition;
            if (action.activeControl?.device is Keyboard)
            {
                if (!action.WasPressedThisFrame()) return false;
                int direction = System.Math.Sign(action.ReadValue<float>());
                if (direction == 0) return false;
                scroll.verticalNormalizedPosition = Mathf.Clamp01(position + direction * settings.PanelScrollStep);
                return true;
            }
            float speed = StickSpeed(action.ReadValue<float>(), settings.PanelScrollSpeed);
            if (speed == 0) return false;
            scrolledFrame = Time.frameCount;
            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(position + speed * Mathf.Max(0, elapsed) / scrollable);
            return true;
        }

        /// <summary>
        /// Moves the text by a mouse wheel reading: forward shows what is above, as on any page. True when the text moved.
        /// A browser's finer wheel steps move it too (see <see cref="MuseumModelGrab.WheelNotches"/>).
        /// </summary>
        public bool Wheel(float wheel, bool allowed)
        {
            if (scroll == null || !allowed || wheel == 0 || !scroll.isActiveAndEnabled) return false;
            float scrollable = Scrollable();
            if (scrollable <= 1) return false;
            float position = scroll.verticalNormalizedPosition;
            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(position + MuseumModelGrab.WheelNotches(wheel) * WheelUnits / scrollable);
            return !Mathf.Approximately(position, scroll.verticalNormalizedPosition);
        }

        private float Scrollable()
        {
            if (scroll == null || scroll.content == null) return 0;
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            return scroll.content.rect.height - viewport.rect.height;
        }

        private void LateUpdate() => RefreshBar();

        /// <summary>Places the bar's handle over the part of the record in view; tests call it directly.</summary>
        public void RefreshBar()
        {
            if (track == null || scroll == null || scroll.content == null) return;
            float content = scroll.content.rect.height;
            float scrollable = Scrollable();
            bool shown = scrollable > 1 && content > 0;
            if (track.gameObject.activeSelf != shown) track.gameObject.SetActive(shown);
            if (!shown) return;
            float visible = Mathf.Clamp01((content - scrollable) / content);
            float top = 1 - (1 - Mathf.Clamp01(scroll.verticalNormalizedPosition)) * (1 - visible);
            handle.anchorMin = new Vector2(0, top - visible);
            handle.anchorMax = new Vector2(1, top);
        }
    }
}
