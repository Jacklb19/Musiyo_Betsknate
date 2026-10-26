using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A small label beside a piece on its pedestal, for the pieces of a point whose card stays on the point's own record
    /// (owner, 25/10/2026: "los datos de la máscara en una etiqueta pequeña"). It shows only what the piece's published
    /// record says: its title, its technique (or its materials) and, muted, the rest of its description. It appears while
    /// the visitor looks at the piece or holds it and fades away otherwise; it has no box, like the subtitles.
    /// </summary>
    public sealed class MuseumPieceLabel : MonoBehaviour
    {
        /// <summary>Interface units per metre of the label.</summary>
        public const float UnitsPerMetre = 1000;
        public const float Width = 440, TitleSize = 26, TechniqueSize = 21, NoteSize = 17;
        /// <summary>How close to the centre of the view the piece must be to count as looked at, in degrees, and how near.</summary>
        public const float LookAngle = 14, LookDistance = 4.5f;
        public const float FadeSeconds = .3f;
        private MuseumInteraction interaction;
        private MuseumModelPresenter presenter;
        private MuseumModelGrab grab;
        private Transform viewer;
        private RectTransform surface;
        private CanvasGroup group;
        private TMP_Text title, technique, note;
        private string shownSlug;

        public RectTransform Surface => surface;
        public TMP_Text Title => title;
        public TMP_Text Technique => technique;
        public TMP_Text Note => note;
        /// <summary>0 hidden, 1 fully shown.</summary>
        public float Visibility => group != null ? group.alpha : 0;
        /// <summary>Whether the label wants to be seen now; the fade follows.</summary>
        public bool Wanted { get; private set; }

        public void Configure(MuseumInteraction state, MuseumModelPresenter models, Camera viewCamera)
        {
            interaction = state;
            presenter = models;
            viewer = viewCamera != null ? viewCamera.transform : null;
            var root = new GameObject("PieceLabel", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(VerticalLayoutGroup));
            root.transform.SetParent(transform, false);
            surface = (RectTransform)root.transform;
            surface.pivot = new Vector2(.5f, 1);
            surface.sizeDelta = new Vector2(Width, 160);
            surface.localScale = Vector3.one / UnitsPerMetre;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = viewCamera;
            group = root.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 4;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            title = Line("PieceTitle", TitleSize, MuseumHudSkin.Text, FontStyles.Bold);
            technique = Line("PieceTechnique", TechniqueSize, MuseumHudSkin.Text, FontStyles.Normal);
            note = Line("PieceNote", NoteSize, MuseumHudSkin.Muted, FontStyles.Italic);
            root.SetActive(false);
        }

        private TMP_Text Line(string name, float size, Color colour, FontStyles style)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            item.transform.SetParent(surface, false);
            var text = item.GetComponent<TextMeshProUGUI>();
            var font = Resources.Load<TMP_FontAsset>(MuseumTypography.BodyFont);
            if (font != null) text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = colour;
            text.alignment = TextAlignmentOptions.Top;
            text.richText = false;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.outlineWidth = .2f;
            text.outlineColor = new Color32(20, 14, 22, 235);
            return text;
        }

        /// <summary>Fills the label from a piece's record; an empty line is left out.</summary>
        public void Fill(string pieceTitle, string pieceTechnique, string pieceNote)
        {
            title.text = pieceTitle ?? "";
            technique.text = pieceTechnique ?? "";
            note.text = pieceNote ?? "";
            technique.gameObject.SetActive(!string.IsNullOrEmpty(pieceTechnique));
            note.gameObject.SetActive(!string.IsNullOrEmpty(pieceNote));
        }

        /// <summary>Whether a piece at this place is looked at from this eye.</summary>
        public static bool LookedAt(Vector3 piece, Transform eye)
        {
            if (eye == null) return false;
            var offset = piece - eye.position;
            return offset.magnitude <= LookDistance && Vector3.Angle(eye.forward, offset) <= LookAngle;
        }

        private void LateUpdate() => Advance(Time.unscaledDeltaTime);

        /// <summary>Places the label under the piece on show and fades it toward what is wanted; tests call it directly.</summary>
        public void Advance(float elapsed)
        {
            if (surface == null) return;
            grab ??= interaction != null ? interaction.GetComponent<MuseumModelGrab>() : null;
            var point = interaction != null ? interaction.ActivePoint : null;
            string slug = null, pieceTitle = null, pieceTechnique = null, pieceNote = null;
            bool piece = point != null && point.HasContent && presenter != null && presenter.ModelPoint == point
                && presenter.TryPieceLabel(out slug, out pieceTitle, out pieceTechnique, out pieceNote);
            piece = piece && MuseumPointRecords.Pieces(point.Content).Exists(candidate => candidate.slug == slug);
            Bounds bounds = default;
            bool placed = piece && Bounds(out bounds);
            if (placed && slug != shownSlug) { shownSlug = slug; Fill(pieceTitle, pieceTechnique, pieceNote); }
            bool held = grab != null && grab.Held;
            Wanted = placed && (held || grab != null && grab.Hovered || LookedAt(bounds.center, viewer));
            group.alpha = Mathf.MoveTowards(group.alpha, Wanted ? 1 : 0, Mathf.Max(0, elapsed) / FadeSeconds);
            bool visible = placed && group.alpha > .001f;
            if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
            if (!visible) return;
            // Just under the piece, turned toward the visitor, upright.
            var toViewer = viewer != null ? viewer.position - bounds.center : -surface.forward;
            toViewer.y = 0;
            if (toViewer.sqrMagnitude < .0001f) toViewer = Vector3.back;
            var facing = toViewer.normalized;
            var spot = new Vector3(bounds.center.x, bounds.min.y - .03f, bounds.center.z) + facing * Mathf.Min(bounds.extents.magnitude, .25f);
            surface.SetPositionAndRotation(spot, Quaternion.LookRotation(-facing, Vector3.up));
        }

        private bool Bounds(out Bounds bounds)
        {
            bounds = default;
            var renderers = presenter.Renderers;
            if (renderers == null || renderers.Length == 0) return false;
            bool found = false;
            foreach (var item in renderers)
            {
                if (item == null) continue;
                if (!found) { bounds = item.bounds; found = true; }
                else bounds.Encapsulate(item.bounds);
            }
            return found;
        }
    }
}
