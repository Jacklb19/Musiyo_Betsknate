using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Two small arrows that stand in the museum beside the piece on a pedestal and step through the pieces of its point,
    /// instead of a list or Previous and Next buttons on its screen. They turn toward the visitor, are chosen with the same
    /// pointer as the screen (the controller ray on a headset, the centre of the view or the mouse on a desktop) and grow
    /// slightly while aimed at.
    /// </summary>
    public sealed class MuseumPieceCues : MonoBehaviour
    {
        /// <summary>Interface units per metre of the cue surface.</summary>
        private const float UnitsPerMetre = 1000;
        /// <summary>Diameter of each arrow disc in metres.</summary>
        public const float Diameter = .1f;
        /// <summary>Gap between the piece and an arrow, in metres.</summary>
        public const float Gap = .06f;
        private const float AimedScale = 1.18f;
        private static Sprite disc;
        private MuseumInteraction interaction;
        private MuseumModelPresenter presenter;
        private MuseumLecternDisplay display;
        private Transform viewer;
        private RectTransform surface;
        private GraphicRaycaster raycaster;
        private Image previousDisc, nextDisc;
        private float heading;

        public RectTransform Surface => surface;
        public Button Previous { get; private set; }
        public Button Next { get; private set; }
        /// <summary>Whether the arrows are wanted now; they still wait for a place to stand.</summary>
        public bool Shown { get; set; }

        public void Configure(MuseumInteraction state, MuseumModelPresenter models, MuseumLecternDisplay screen, Camera viewCamera,
            System.Func<int, bool> step)
        {
            interaction = state;
            presenter = models;
            display = screen;
            viewer = viewCamera != null ? viewCamera.transform : null;
            var root = new GameObject("PieceCarousel", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            surface = (RectTransform)root.transform;
            surface.pivot = new Vector2(.5f, .5f);
            surface.localScale = Vector3.one / UnitsPerMetre;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera;
            raycaster = root.GetComponent<GraphicRaycaster>();
            Previous = Arrow("CarouselPrevious", -1, () => step(-1), out previousDisc);
            Next = Arrow("CarouselNext", 1, () => step(1), out nextDisc);
            display?.AddCue(surface);
            root.SetActive(false);
        }

        private Button Arrow(string name, int side, UnityEngine.Events.UnityAction action, out Image background)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            item.transform.SetParent(surface, false);
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(side < 0 ? 0 : 1, .5f);
            rect.pivot = new Vector2(side < 0 ? 0 : 1, .5f);
            rect.sizeDelta = Vector2.one * Diameter * UnitsPerMetre;
            background = item.GetComponent<Image>();
            background.sprite = Disc();
            var button = item.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(action);
            // A chevron of two strokes, pointing outward, in the accent colour.
            var accent = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            float size = Diameter * UnitsPerMetre;
            for (int stroke = 0; stroke < 2; stroke++)
            {
                var line = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
                line.transform.SetParent(rect, false);
                var lineRect = (RectTransform)line.transform;
                lineRect.sizeDelta = new Vector2(size * .34f, size * .07f);
                float tilt = stroke == 0 ? 45 : -45;
                lineRect.localRotation = Quaternion.Euler(0, 0, side > 0 ? -tilt : tilt);
                lineRect.anchoredPosition = new Vector2(side * size * .03f, (stroke == 0 ? 1 : -1) * size * .115f);
                var image = line.GetComponent<Image>();
                image.color = accent;
                image.raycastTarget = false;
            }
            return button;
        }

        /// <summary>A soft round disc made once, so the arrows need no texture asset.</summary>
        private static Sprite Disc()
        {
            if (disc != null) return disc;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float radius = size * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(radius, radius));
                    byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
            disc.name = "CueDisc";
            return disc;
        }

        /// <summary>Where the arrows stand: beside the piece on show, or above the pedestal while its piece is not there.</summary>
        private bool TryPlace(out Vector3 centre, out float halfWidth)
        {
            centre = default;
            halfWidth = .15f;
            var point = interaction != null ? interaction.ActivePoint : null;
            if (point == null || point.Anchor == null) return false;
            var renderers = presenter != null && presenter.ModelPoint == point ? presenter.Renderers : null;
            if (renderers != null && renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var item in renderers) if (item != null) bounds.Encapsulate(item.bounds);
                centre = bounds.center;
                halfWidth = Mathf.Max(bounds.extents.x, bounds.extents.z);
                return true;
            }
            if (presenter != null && presenter.TryExhibitPose(out var position, out _, out _))
            {
                centre = position + Vector3.up * .15f;
                return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (surface == null) return;
            var grab = interaction != null ? interaction.GetComponent<MuseumModelGrab>() : null;
            // While the piece is in the hand there is nothing beside the pedestal to step from.
            var centre = Vector3.zero;
            float halfWidth = 0;
            bool visible = Shown && (grab == null || !grab.Held) && TryPlace(out centre, out halfWidth);
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (!visible) return;
            var toViewer = viewer != null ? viewer.position - centre : -surface.forward;
            toViewer.y = 0;
            if (toViewer.sqrMagnitude > .01f) heading = Mathf.Atan2(toViewer.x, toViewer.z) * Mathf.Rad2Deg;
            var facing = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            // The arrows stand a little in front of the piece, on the visitor's side, so the piece never hides them.
            surface.SetPositionAndRotation(centre + facing * Mathf.Min(halfWidth, .2f), Quaternion.LookRotation(-facing, Vector3.up));
            surface.sizeDelta = new Vector2((halfWidth + Gap + Diameter) * 2 * UnitsPerMetre, Diameter * UnitsPerMetre * 1.4f);
            raycaster.enabled = !MuseumInput.Current.Spatial && Cursor.lockState != CursorLockMode.Locked;
            var night = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.PanelColor);
            var accent = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            foreach (var (button, background) in new[] { (Previous, previousDisc), (Next, nextDisc) })
            {
                bool aimed = display != null && display.Aimed == button;
                button.transform.localScale = Vector3.one * (aimed ? AimedScale : 1);
                var fill = aimed ? Color.Lerp(night, accent, .35f) : night;
                fill.a = aimed ? .95f : .78f;
                background.color = fill;
            }
        }
    }
}
