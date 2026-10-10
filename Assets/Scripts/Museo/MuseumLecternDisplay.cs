using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Carries the point panel into the museum: it unfolds in front of the reading stand of the active point,
    /// floats there turned toward the visitor, and stays on the screen only for points that have no stand.
    /// </summary>
    public sealed class MuseumLecternDisplay : MonoBehaviour
    {
        private const float Border = 6;
        private const float MarkEdge = 4;
        private RectTransform panel;
        private Transform screenParent;
        private int screenSibling;
        private Vector2 screenAnchorMin, screenAnchorMax, screenPivot, screenPosition;
        private RectTransform surface;
        private CanvasGroup group;
        private GraphicRaycaster raycaster;
        private RectTransform aimMark;
        private readonly List<Selectable> controls = new List<Selectable>();
        private PointLectern[] stands;
        private Transform viewer;
        private float heading;
        private bool open;

        /// <summary>Stand that currently carries the panel, or null while the panel is on the screen.</summary>
        public PointLectern Host { get; private set; }
        public bool InWorld => Host != null;
        public bool Open => open;
        /// <summary>0 folded away, 1 completely unfolded.</summary>
        public float Openness { get; private set; }
        public RectTransform Surface => surface;
        /// <summary>Control of the floating screen the visitor's pointer rests on; pressing chooses it.</summary>
        public Selectable Aimed { get; private set; }

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
            viewer = viewCamera != null ? viewCamera.transform : null;
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
            var mark = new GameObject("PointerMark", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(surface, false);
            aimMark = (RectTransform)mark.transform;
            aimMark.anchorMin = aimMark.anchorMax = surface.pivot;
            var markColor = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.PointerColor);
            var markImage = mark.GetComponent<Image>();
            markImage.color = markColor;
            markImage.raycastTarget = false;
            // Solid edges keep the mark clear while its light fill leaves the label under it readable.
            markColor.a = 1;
            foreach (var (start, end) in new[] { (Vector2.zero, Vector2.right), (Vector2.up, Vector2.one),
                (Vector2.zero, Vector2.up), (Vector2.right, Vector2.one) })
            {
                var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                edge.transform.SetParent(aimMark, false);
                var edgeRect = (RectTransform)edge.transform;
                edgeRect.anchorMin = start;
                edgeRect.anchorMax = end;
                edgeRect.sizeDelta = start.y == end.y ? new Vector2(MarkEdge, MarkEdge) : new Vector2(MarkEdge, -MarkEdge);
                edge.GetComponent<Image>().color = markColor;
                edge.GetComponent<Image>().raycastTarget = false;
            }
            mark.SetActive(false);
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

        /// <summary>Shows the panel for the point: unfolding by its stand, or on the screen when it has none.</summary>
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
                aimMark.SetAsLastSibling();
                heading = HeadingToViewer();
            }
            Apply();
        }

        /// <summary>Compass angle from the stand toward the visitor; the reading side of the board when nobody is watching.</summary>
        private float HeadingToViewer()
        {
            var direction = viewer != null ? viewer.position - Host.BoardCentre : Host.Facing;
            direction.y = 0;
            // Right above the stand there is no side to turn to, so the screen keeps its heading.
            return direction.sqrMagnitude < .01f ? heading : Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Points at the floating screen along a ray: the centre of the view on a desktop, a hand controller on a headset.
        /// </summary>
        public void Aim(Ray ray)
        {
            Aimed = ControlAt(ray);
            MarkAim();
        }

        public void ClearAim()
        {
            Aimed = null;
            MarkAim();
        }

        /// <summary>Chooses the control the pointer rests on, as a click on it would.</summary>
        public bool Press()
        {
            if (Aimed == null) return false;
            ExecuteEvents.Execute(Aimed.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            return true;
        }

        /// <summary>Usable control where the ray meets the reading side of the unfolded screen, or null.</summary>
        public Selectable ControlAt(Ray ray)
        {
            if (!InWorld || !open || Openness < 1) return null;
            // The screen's forward axis points away from its reader, so a ray against it comes from behind.
            if (Vector3.Dot(ray.direction, surface.forward) <= 0) return null;
            if (!new Plane(surface.forward, surface.position).Raycast(ray, out float distance)) return null;
            var spot = ray.GetPoint(distance);
            surface.GetComponentsInChildren(false, controls);
            Selectable found = null;
            // Later controls are drawn on top, so the last one under the pointer is the one in sight.
            foreach (var control in controls)
                if (control.IsInteractable() && Covers(control.transform, spot) && !Clipped(control.transform, spot)) found = control;
            controls.Clear();
            return found;
        }

        private static bool Covers(Transform item, Vector3 spot)
            => item is RectTransform rect && rect.rect.Contains((Vector2)rect.InverseTransformPoint(spot));

        /// <summary>Whether a scrolling viewport above the control hides the spot.</summary>
        private bool Clipped(Transform item, Vector3 spot)
        {
            for (var parent = item.parent; parent != null && parent != surface; parent = parent.parent)
                if ((parent.TryGetComponent<RectMask2D>(out _) || parent.TryGetComponent<Mask>(out _)) && !Covers(parent, spot))
                    return true;
            return false;
        }

        /// <summary>Lays the pointer mark over the aimed control so the visitor sees what a press will choose.</summary>
        private void MarkAim()
        {
            if (aimMark == null) return;
            bool shown = Aimed != null && Aimed.isActiveAndEnabled && open && InWorld;
            if (aimMark.gameObject.activeSelf != shown) aimMark.gameObject.SetActive(shown);
            if (!shown) { Aimed = null; return; }
            var rect = (RectTransform)Aimed.transform;
            Vector2 low = surface.InverseTransformPoint(rect.TransformPoint(rect.rect.min));
            Vector2 high = surface.InverseTransformPoint(rect.TransformPoint(rect.rect.max));
            aimMark.anchoredPosition = (low + high) * .5f;
            aimMark.sizeDelta = high - low;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>Moves the fold by one time step; tests call it directly.</summary>
        public void Advance(float elapsed)
        {
            if (surface == null) return;
            float target = open && InWorld ? 1 : 0;
            var settings = MuseumExperienceConfiguration.Current;
            Openness = Mathf.MoveTowards(Openness, target, Mathf.Max(0, elapsed) / settings.LecternUnfoldSeconds);
            // The screen keeps turning toward the visitor, eased so it does not jitter with every step.
            if (InWorld)
                heading = Mathf.LerpAngle(heading, HeadingToViewer(), 1 - Mathf.Exp(-Mathf.Max(0, elapsed) / settings.LecternFollowSeconds));
            Apply();
        }

        private void Apply()
        {
            bool visible = InWorld && Openness > 0;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (!visible) return;
            var settings = MuseumExperienceConfiguration.Current;
            // It floats in front of the stand, on the side the visitor is on, upright and facing them.
            var toViewer = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            var centre = Host.BoardCentre;
            centre.y = Host.transform.position.y + settings.LecternFloatHeight;
            surface.SetPositionAndRotation(centre + toViewer * settings.LecternFloatDistance, Quaternion.LookRotation(-toViewer, Vector3.up));
            float eased = Mathf.SmoothStep(0, 1, Openness);
            float scale = settings.LecternScreenWidth / surface.sizeDelta.x;
            // The screen rises from its lower edge.
            surface.localScale = new Vector3(scale, scale * eased, scale);
            group.alpha = eased;
            // A captured pointer stays where it was locked, so its clicks must not land on the screen.
            raycaster.enabled = open && Cursor.lockState != CursorLockMode.Locked;
            MarkAim();
        }
    }
}
