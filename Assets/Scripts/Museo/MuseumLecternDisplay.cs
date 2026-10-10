using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Carries the point panel into the museum: it unfolds from the reading stand of the active point
    /// and stays on the screen only for points that have no stand.
    /// </summary>
    public sealed class MuseumLecternDisplay : MonoBehaviour
    {
        private const float Border = 6;
        private const float SurfaceGap = .02f;
        private RectTransform panel;
        private Transform screenParent;
        private int screenSibling;
        private Vector2 screenAnchorMin, screenAnchorMax, screenPivot, screenPosition;
        private RectTransform surface;
        private CanvasGroup group;
        private GraphicRaycaster raycaster;
        private PointLectern[] stands;
        private bool open;

        /// <summary>Stand that currently carries the panel, or null while the panel is on the screen.</summary>
        public PointLectern Host { get; private set; }
        public bool InWorld => Host != null;
        public bool Open => open;
        /// <summary>0 folded on the board, 1 completely unfolded.</summary>
        public float Openness { get; private set; }
        public RectTransform Surface => surface;

        public void Configure(RectTransform pointPanel, Camera viewCamera)
        {
            panel = pointPanel;
            screenParent = panel.parent;
            screenSibling = panel.GetSiblingIndex();
            screenAnchorMin = panel.anchorMin;
            screenAnchorMax = panel.anchorMax;
            screenPivot = panel.pivot;
            screenPosition = panel.anchoredPosition;

            var root = new GameObject("LecternScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            surface = (RectTransform)root.transform;
            surface.pivot = new Vector2(.5f, 0);
            surface.sizeDelta = panel.sizeDelta + Vector2.one * (Border * 2);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera;
            group = root.GetComponent<CanvasGroup>();
            raycaster = root.GetComponent<GraphicRaycaster>();
            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(surface, false);
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;
            var image = frame.GetComponent<Image>();
            image.color = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            image.raycastTarget = false;
            root.SetActive(false);
        }

        /// <summary>Stand authored beside the point, if the museum has one there.</summary>
        public PointLectern Find(TourPoint point)
        {
            if (point == null) return null;
            stands ??= FindObjectsByType<PointLectern>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var stand in stands)
                if (stand != null && stand.Point == point.Anchor && stand.Board != null) return stand;
            return null;
        }

        /// <summary>Shows the panel for the point: unfolding from its stand, or on the screen when it has none.</summary>
        public void Show(TourPoint point)
        {
            var stand = Find(point);
            if (stand != Host || (stand == null && panel.parent != screenParent)) Place(stand);
            open = true;
            if (stand == null) panel.gameObject.SetActive(true);
        }

        public void Hide()
        {
            open = false;
            if (!InWorld) panel.gameObject.SetActive(false);
        }

        private void Place(PointLectern stand)
        {
            Host = stand;
            Openness = 0;
            if (stand == null)
            {
                panel.SetParent(screenParent, false);
                panel.SetSiblingIndex(screenSibling);
                panel.anchorMin = screenAnchorMin;
                panel.anchorMax = screenAnchorMax;
                panel.pivot = screenPivot;
                panel.anchoredPosition = screenPosition;
            }
            else
            {
                panel.SetParent(surface, false);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 0);
                panel.anchoredPosition = new Vector2(0, Border);
                panel.gameObject.SetActive(true);
                surface.SetPositionAndRotation(stand.Board.position - stand.Board.forward * SurfaceGap, stand.Board.rotation);
            }
            Apply();
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>Moves the fold by one time step; tests call it directly.</summary>
        public void Advance(float elapsed)
        {
            if (surface == null) return;
            float target = open && InWorld ? 1 : 0;
            Openness = Mathf.MoveTowards(Openness, target,
                Mathf.Max(0, elapsed) / MuseumExperienceConfiguration.Current.LecternUnfoldSeconds);
            Apply();
        }

        private void Apply()
        {
            bool visible = InWorld && Openness > 0;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (!visible) return;
            float eased = Mathf.SmoothStep(0, 1, Openness);
            float scale = MuseumExperienceConfiguration.Current.LecternScreenWidth / surface.sizeDelta.x;
            // The screen rises from the lower edge that rests on the board.
            surface.localScale = new Vector3(scale, scale * eased, scale);
            group.alpha = eased;
            // A captured pointer stays where it was locked, so its clicks must not land on the screen.
            raycaster.enabled = open && Cursor.lockState != CursorLockMode.Locked;
        }
    }
}
