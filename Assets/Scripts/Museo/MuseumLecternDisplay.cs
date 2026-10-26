using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Carries the point panel into the museum. By a reading stand the screen is a page lying on the stand's reading
    /// board: the same slope, inside the board's edges, fixed in the museum like a real lectern (owner's decisions D1 of
    /// 22/10/2026 and of 23/10/2026): it is read by stopping in front of it. Where a point has no stand, the screen floats at the stable point anchor turned toward
    /// the visitor.
    /// </summary>
    public sealed class MuseumLecternDisplay : MonoBehaviour
    {
        private const float Border = 6;
        private const float MarkEdge = 4;
        private RectTransform panel;
        private Transform screenParent;
        private int screenSibling;
        private Vector2 screenAnchorMin, screenAnchorMax, screenPivot, screenPosition, screenSize;
        private ScrollRect scroll;
        private bool fittedSpatial;
        /// <summary>A floating screen is never shorter than this, so its buttons keep their place.</summary>
        private const float MinimumHeight = 300;
        /// <summary>
        /// Layout width of the screen on a stand, in interface units. With the configured width of 1.1 m it reads at about
        /// 750 units per metre, so body text of <see cref="TextSize"/> stands some 3.7 cm high.
        /// </summary>
        public const float InWorldWidth = 820;
        /// <summary>A screen on a stand grows with its text up to this height (about 0.6 m), wide and low like a label.</summary>
        public const float InWorldMaxHeight = 440;
        /// <summary>Reading size of the screen's body text.</summary>
        public const float TextSize = 28;
        /// <summary>Width of the column of choices and controls on the right of the screen.</summary>
        public const float SideColumnWidth = 260;
        public const float ColumnGap = 20;
        private RectTransform surface;
        private CanvasGroup group;
        private GraphicRaycaster raycaster;
        private RectTransform aimMark;
        private readonly List<Selectable> controls = new List<Selectable>();
        /// <summary>Small surfaces in the museum, such as the arrows beside a piece, that the same pointer chooses on.</summary>
        private readonly List<RectTransform> cues = new List<RectTransform>();
        private PointLectern[] stands;
        private Transform viewer;
        private float heading;
        private bool open;
        /// <summary>Column of choices and controls beside the reading column; its height counts toward the screen's.</summary>
        public RectTransform SideColumn { get; set; }

        /// <summary>Existing stand beside the active point, or null when the stable point anchor carries the panel.</summary>
        public PointLectern Host { get; private set; }
        public PointAnchor Anchor { get; private set; }
        public bool InWorld => Anchor != null;
        public bool CanDisplay(TourPoint point) => surface != null && point != null && point.Anchor != null;
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
            screenSize = panel.sizeDelta;
            panel.TryGetComponent(out scroll);

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
            // The stands take their finish as the visit starts, so they are finished before anyone reads at them.
            if (Application.isPlaying)
                foreach (var stand in FindObjectsByType<PointLectern>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    MuseumLecternFinish.Apply(stand);
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

        /// <summary>Shows the panel for the point: unfolding by its existing stand or stable point anchor.</summary>
        public void Show(TourPoint point)
        {
            var stand = Find(point);
            var anchor = point != null ? point.Anchor : null;
            if (stand != Host || anchor != Anchor || (anchor == null && panel.parent != screenParent)) Place(stand, anchor);
            open = true;
            if (anchor == null) panel.gameObject.SetActive(true);
        }

        public void Hide()
        {
            open = false;
            if (!InWorld) panel.gameObject.SetActive(false);
        }

        private void Place(PointLectern stand, PointAnchor anchor)
        {
            Host = stand;
            Anchor = anchor;
            Openness = 0;
            if (anchor == null)
            {
                panel.SetParent(screenParent, false);
                panel.SetSiblingIndex(screenSibling);
                panel.anchorMin = screenAnchorMin;
                panel.anchorMax = screenAnchorMax;
                panel.pivot = screenPivot;
                panel.anchoredPosition = screenPosition;
                panel.sizeDelta = screenSize;
            }
            else
            {
                panel.SetParent(surface, false);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, 0);
                panel.anchoredPosition = new Vector2(0, Border);
                panel.sizeDelta = new Vector2(InWorldWidth, stand != null ? PageHeight(MuseumExperienceConfiguration.Current, MuseumLecternFinish.Spatial) : InWorldMaxHeight);
                fittedSpatial = MuseumLecternFinish.Spatial;
                surface.sizeDelta = panel.sizeDelta + Vector2.one * (Border * 2);
                panel.gameObject.SetActive(true);
                aimMark.SetAsLastSibling();
                if (stand == null) heading = HeadingToViewer();
            }
            Apply();
        }

        /// <summary>Compass angle from the point toward the visitor; the point's front when nobody is watching.</summary>
        private float HeadingToViewer()
        {
            var centre = Anchor.transform.position;
            var direction = viewer != null ? viewer.position - centre : Anchor.transform.forward;
            direction.y = 0;
            // Right above the stand there is no side to turn to, so the screen keeps its heading.
            return direction.sqrMagnitude < .01f ? heading : Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>Lets the pointer choose on a further surface in the museum; it is aimed only while active.</summary>
        public void AddCue(RectTransform cue)
        {
            if (cue != null && !cues.Contains(cue)) cues.Add(cue);
        }

        /// <summary>
        /// Points at the floating screen, or at a cue in the museum, along a ray: the centre of the view on a desktop, a hand
        /// controller on a headset. Something solid nearer than <paramref name="reach"/> along the ray hides what is behind it.
        /// </summary>
        public void Aim(Ray ray, float reach = float.PositiveInfinity)
        {
            Aimed = ControlAt(ray, reach);
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

        /// <summary>Usable control where the ray meets the reading side of the unfolded screen or of a cue, or null.</summary>
        public Selectable ControlAt(Ray ray, float reach = float.PositiveInfinity)
        {
            Selectable found = null;
            float nearest = reach + .01f;
            if (InWorld && open && Openness >= 1) found = ControlOn(surface, ray, ref nearest);
            foreach (var cue in cues)
                if (cue != null && cue.gameObject.activeInHierarchy)
                {
                    var control = ControlOn(cue, ray, ref nearest);
                    if (control != null) found = control;
                }
            return found;
        }

        private Selectable ControlOn(RectTransform side, Ray ray, ref float nearest)
        {
            // A surface's forward axis points away from its reader, so a ray against it comes from behind.
            if (Vector3.Dot(ray.direction, side.forward) <= 0) return null;
            if (!new Plane(side.forward, side.position).Raycast(ray, out float distance) || distance > nearest) return null;
            var spot = ray.GetPoint(distance);
            side.GetComponentsInChildren(false, controls);
            Selectable found = null;
            // Later controls are drawn on top, so the last one under the pointer is the one in sight.
            foreach (var control in controls)
                if (control.IsInteractable() && Covers(control.transform, spot) && !Clipped(control.transform, spot, side)) found = control;
            controls.Clear();
            if (found != null) nearest = distance;
            return found;
        }

        private static bool Covers(Transform item, Vector3 spot)
            => item is RectTransform rect && rect.rect.Contains((Vector2)rect.InverseTransformPoint(spot));

        /// <summary>Whether a scrolling viewport above the control hides the spot.</summary>
        private static bool Clipped(Transform item, Vector3 spot, Transform side)
        {
            for (var parent = item.parent; parent != null && parent != side; parent = parent.parent)
                if ((parent.TryGetComponent<RectMask2D>(out _) || parent.TryGetComponent<Mask>(out _)) && !Covers(parent, spot))
                    return true;
            return false;
        }

        /// <summary>Lays the pointer mark over the aimed control so the visitor sees what a press will choose.</summary>
        private void MarkAim()
        {
            if (aimMark == null) return;
            bool onScreen = Aimed != null && Aimed.transform.IsChildOf(surface);
            if (Aimed != null && (!Aimed.isActiveAndEnabled || onScreen && !(open && InWorld))) Aimed = null;
            // A cue shows that it is aimed by itself; the mark belongs to the screen.
            bool shown = Aimed != null && onScreen;
            if (aimMark.gameObject.activeSelf != shown) aimMark.gameObject.SetActive(shown);
            if (!shown) return;
            var rect = (RectTransform)Aimed.transform;
            Vector2 low = surface.InverseTransformPoint(rect.TransformPoint(rect.rect.min));
            Vector2 high = surface.InverseTransformPoint(rect.TransformPoint(rect.rect.max));
            aimMark.anchoredPosition = (low + high) * .5f;
            aimMark.sizeDelta = high - low;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>
        /// Height of the panel on a reading board, in interface units: the page fills the board inside its margin, whatever
        /// it shows; its text scrolls within it.
        /// </summary>
        public static float PageHeight(MuseumExperienceConfiguration settings, bool spatial)
        {
            var board = settings.LecternBoard(spatial);
            float width = board.x - settings.LecternBoardMargin * 2;
            float height = board.y - settings.LecternBoardMargin * 2;
            return height * (InWorldWidth + Border * 2) / width - Border * 2;
        }

        /// <summary>A floating screen is as tall as what it shows, up to the height of the screen panel, so short texts leave no empty board.</summary>
        private void LateUpdate()
        {
            // A page on a reading board keeps the board's size.
            if (!InWorld || Host != null || scroll == null || scroll.viewport == null || scroll.content == null) return;
            float chrome = panel.rect.height - scroll.viewport.rect.height;
            float wanted = chrome + LayoutUtility.GetPreferredHeight(scroll.content);
            // The controls column is never cut: the screen grows for it beyond its usual height when it must.
            float side = SideColumn != null && SideColumn.gameObject.activeSelf ? chrome + LayoutUtility.GetPreferredHeight(SideColumn) : 0;
            float limit = Mathf.Max(InWorldMaxHeight, Mathf.Min(side, InWorldMaxHeight * 1.5f));
            float height = Mathf.Clamp(Mathf.Max(wanted, side), MinimumHeight, limit);
            if (Mathf.Abs(panel.sizeDelta.y - height) < .5f) return;
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
            surface.sizeDelta = panel.sizeDelta + Vector2.one * (Border * 2);
            Apply();
        }

        /// <summary>Builds the reading boards again at the size of the current view, and fits the page on its stand to it.</summary>
        private void Refit()
        {
            fittedSpatial = MuseumLecternFinish.Spatial;
            foreach (var stand in FindObjectsByType<PointLectern>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                MuseumLecternFinish.Apply(stand);
            if (Host == null) return;
            panel.sizeDelta = new Vector2(InWorldWidth, PageHeight(MuseumExperienceConfiguration.Current, fittedSpatial));
            surface.sizeDelta = panel.sizeDelta + Vector2.one * (Border * 2);
        }

        /// <summary>Moves the fold by one time step; tests call it directly.</summary>
        public void Advance(float elapsed)
        {
            if (surface == null) return;
            float target = open && InWorld ? 1 : 0;
            var settings = MuseumExperienceConfiguration.Current;
            // Putting on or taking off a headset changes the size of the reading boards, and of the page on them.
            if (MuseumLecternFinish.Spatial != fittedSpatial) Refit();
            Openness = Mathf.MoveTowards(Openness, target, Mathf.Max(0, elapsed) / settings.LecternUnfoldSeconds);
            // A floating screen keeps turning toward the visitor, eased so it does not jitter with every step; a screen
            // resting on a stand stays where it is.
            if (InWorld && Host == null)
                heading = Mathf.LerpAngle(heading, HeadingToViewer(), 1 - Mathf.Exp(-Mathf.Max(0, elapsed) / settings.LecternFollowSeconds));
            Apply();
        }

        /// <summary>
        /// Where the lower edge of the page on a stand lies and how it is turned: on the reading board, inside its margin,
        /// on the board's slope and facing the board's reader. Its forward axis points into the board, away from the reader.
        /// </summary>
        public static Pose RestingPose(PointLectern stand, MuseumExperienceConfiguration settings)
        {
            var (bottom, rotation, _) = MuseumLecternFinish.Page(stand, settings);
            return new Pose(bottom, rotation);
        }

        private void Apply()
        {
            bool visible = InWorld && Openness > 0;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (!visible) return;
            var settings = MuseumExperienceConfiguration.Current;
            if (Host != null)
            {
                // It rests on the stand, fixed in the museum.
                var pose = RestingPose(Host, settings);
                surface.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
            {
                // Without a stand it floats at the point, on the side the visitor is on, upright and facing them.
                var toViewer = Quaternion.Euler(0, heading, 0) * Vector3.forward;
                var centre = Anchor.transform.position;
                centre.y = Anchor.transform.position.y + settings.LecternFloatHeight;
                surface.SetPositionAndRotation(centre + toViewer * settings.LecternFloatDistance, Quaternion.LookRotation(-toViewer, Vector3.up));
            }
            float eased = Mathf.SmoothStep(0, 1, Openness);
            float width = Host != null ? settings.LecternBoard(MuseumLecternFinish.Spatial).x - settings.LecternBoardMargin * 2 : settings.LecternScreenWidth;
            float scale = width / surface.sizeDelta.x;
            // The screen rises from its lower edge.
            surface.localScale = new Vector3(scale, scale * eased, scale);
            group.alpha = eased;
            // A captured pointer stays where it was locked, so its clicks must not land on the screen.
            raycaster.enabled = open && !MuseumInput.Current.Spatial && Cursor.lockState != CursorLockMode.Locked;
            MarkAim();
        }
    }
}
