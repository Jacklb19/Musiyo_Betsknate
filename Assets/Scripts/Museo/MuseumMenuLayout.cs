using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Fits authored pages to the available canvas. On a wide screen every page is a column on the left, so the world
    /// stays in view on the right; a spatial host, or a narrow window, centres every page.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MuseumMenuLayout : MonoBehaviour
    {
        /// <summary>Below this width a screen is too narrow for a column beside the world.</summary>
        public const float CompactWidth = 1100;
        public bool Spatial { get; set; }
        /// <summary>True when pages are centred cards instead of columns on the left.</summary>
        public bool Compact { get; private set; }
        private Vector2 previous;
        private bool previousSpatial;

        private void OnEnable() => Apply();
        private void LateUpdate()
        {
            var rect = transform as RectTransform;
            if (rect != null && (rect.rect.size != previous || Spatial != previousSpatial)) Apply();
        }
        public void Apply()
        {
            var view = GetComponent<MuseumMenuView>();
            var root = transform as RectTransform;
            if (view == null || root == null || root.rect.width < 1 || root.rect.height < 1) return;
            previous = root.rect.size;
            previousSpatial = Spatial;
            bool compact = Spatial || previous.x < CompactWidth;
            Compact = compact;
            float margin = Mathf.Min(compact ? 24 : 52, Mathf.Min(previous.x, previous.y) * .08f);
            foreach (var page in new[] { view.Main, view.Pause, view.Settings, view.Controls })
            {
                var desired = page.GetComponent<LayoutElement>();
                float width = Mathf.Min(desired != null && desired.preferredWidth > 0 ? desired.preferredWidth : 620,
                    previous.x - margin * 2);
                float height = Mathf.Min(desired != null && desired.preferredHeight > 0 ? desired.preferredHeight : 850,
                    previous.y - margin * 2);
                var rect = (RectTransform)page.transform;
                bool column = !compact;
                if (page == view.Main && page.transform.parent != view.Overlay.transform)
                {
                    var holder = (RectTransform)page.transform.parent;
                    Place(holder, column, margin, width, height);
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    Fit(page, holder, height);
                }
                else
                {
                    Place(rect, column, margin, width, height);
                    Fit(page, rect, height);
                }
            }
        }

        /// <summary>
        /// A page as tall as what it holds, up to its authored height, so short pages leave no empty panel below their
        /// last option. Longer pages keep scrolling.
        /// </summary>
        private static void Fit(MuseumMenuPageView page, RectTransform frame, float height)
        {
            var scroll = page.Scroll;
            if (scroll == null || scroll.viewport == null || scroll.content == null || !page.gameObject.activeInHierarchy) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)page.transform);
            float chrome = frame.rect.height - scroll.viewport.rect.height;
            float content = LayoutUtility.GetPreferredHeight(scroll.content);
            if (chrome <= 0 || content <= 0) return;
            frame.sizeDelta = new Vector2(frame.sizeDelta.x, Mathf.Min(height, chrome + content));
        }
        private static void Place(RectTransform rect, bool left, float margin, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(left ? 0 : .5f, .5f);
            rect.pivot = new Vector2(left ? 0 : .5f, .5f);
            rect.anchoredPosition = new Vector2(left ? margin : 0, 0);
            rect.sizeDelta = new Vector2(Mathf.Max(1, width), Mathf.Max(1, height));
        }
    }
}
