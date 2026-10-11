using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Fits authored pages to the available canvas; spatial hosts center every page.</summary>
    [DisallowMultipleComponent]
    public sealed class MuseumMenuLayout : MonoBehaviour
    {
        public bool Spatial { get; set; }
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
            bool compact = Spatial || previous.x < 1100;
            float margin = Mathf.Min(compact ? 24 : 52, Mathf.Min(previous.x, previous.y) * .08f);
            foreach (var page in new[] { view.Main, view.Pause, view.Settings, view.Controls })
            {
                var desired = page.GetComponent<LayoutElement>();
                float width = Mathf.Min(desired != null && desired.preferredWidth > 0 ? desired.preferredWidth : 620,
                    previous.x - margin * 2);
                float height = Mathf.Min(desired != null && desired.preferredHeight > 0 ? desired.preferredHeight : 850,
                    previous.y - margin * 2);
                var rect = (RectTransform)page.transform;
                bool entryColumn = page == view.Main && !compact;
                if (page == view.Main && page.transform.parent != view.Overlay.transform)
                {
                    var column = (RectTransform)page.transform.parent;
                    Place(column, entryColumn, margin, width, height);
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
                else Place(rect, false, margin, width, height);
            }
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
