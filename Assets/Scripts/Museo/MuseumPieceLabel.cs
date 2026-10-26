using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A small label beside a piece on its pedestal, for the pieces of a point whose card stays on the point's own record
    /// (owner, 25/10/2026: "los datos de la máscara en una etiqueta pequeña"). It shows only what the piece's published
    /// record says: its title, its technique (or its materials) and, muted, the rest of its description. It appears while
    /// the visitor looks at the piece or holds it and fades away otherwise; it has no box, like the subtitles. Above those
    /// lines, for any piece on show, a small hand and a short word say how to take it, or how to bring it closer and give
    /// it back once it is held, for a moment, and then fade (owner: the wide bars across the view were "invasivos").
    /// </summary>
    public sealed class MuseumPieceLabel : MonoBehaviour
    {
        /// <summary>Interface units per metre of the label.</summary>
        public const float UnitsPerMetre = 1000;
        public const float Width = 440, TitleSize = 26, TechniqueSize = 21, NoteSize = 17;
        /// <summary>How close to the centre of the view the piece must be to count as looked at, in degrees, and how near.</summary>
        public const float LookAngle = 14, LookDistance = 4.5f;
        public const float FadeSeconds = .3f;
        /// <summary>How long the cue on pointing at or taking a piece stays before it fades, in seconds; and its type size.</summary>
        public const float CueSeconds = 2.5f, CueSize = 17;
        private static Sprite hand;
        private RectTransform cueRow;
        private TMP_Text cue;
        private float cueShown = float.NegativeInfinity;
        private bool wasHovered, wasHeld;
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
        /// <summary>The short word beside the hand: how to take, or how to handle, the piece.</summary>
        public TMP_Text Cue => cue;
        public bool CueShown => cueRow != null && cueRow.gameObject.activeSelf;

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
            CreateCue();
            title = Line("PieceTitle", TitleSize, MuseumHudSkin.Text, FontStyles.Bold);
            technique = Line("PieceTechnique", TechniqueSize, MuseumHudSkin.Text, FontStyles.Normal);
            note = Line("PieceNote", NoteSize, MuseumHudSkin.Muted, FontStyles.Italic);
            root.SetActive(false);
        }

        /// <summary>A row with a small hand and a short word, at the top of the label, closest to the piece.</summary>
        private void CreateCue()
        {
            cueRow = new GameObject("PieceCue", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            cueRow.SetParent(surface, false);
            var layout = cueRow.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 6;
            // Each child as wide as it needs, the pair centred under the piece.
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            cueRow.GetComponent<LayoutElement>().preferredHeight = CueSize * 1.6f;
            var icon = new GameObject("Hand", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            icon.GetComponent<LayoutElement>().preferredWidth = CueSize * 1.3f;
            icon.transform.SetParent(cueRow, false);
            icon.rectTransform.sizeDelta = Vector2.one * (CueSize * 1.3f);
            icon.sprite = Hand();
            icon.color = MuseumHudSkin.Accent;
            icon.raycastTarget = false;
            cue = Line("PieceCueWord", CueSize, MuseumHudSkin.Text, FontStyles.Normal);
            cue.transform.SetParent(cueRow, false);
            cue.textWrappingMode = TextWrappingModes.NoWrap;
            cue.alignment = TextAlignmentOptions.MidlineLeft;
            cue.rectTransform.sizeDelta = new Vector2(0, CueSize * 1.6f);
            cueRow.gameObject.SetActive(false);
        }

        /// <summary>The cue to show now, or null: on pointing at a piece and on taking it, for a moment.</summary>
        private string CueText(bool held, bool hovered)
        {
            if (held != wasHeld || hovered && !wasHovered) cueShown = Time.unscaledTime;
            wasHeld = held;
            wasHovered = hovered;
            if (!(held || hovered) || Time.unscaledTime - cueShown > CueSeconds) return null;
            return MuseumInterfaceText.Get(held ? "grab_held_cue" : "grab_cue");
        }

        /// <summary>A small open hand, drawn once, so the cue needs no texture asset.</summary>
        public static Sprite Hand()
        {
            if (hand != null) return hand;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "PieceCueHand" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    bool palm = Mathf.Abs(u) <= .42f && v >= -.75f && v <= .1f;
                    bool fingers = v > .05f && (Finger(u, v, -.33f, .62f) || Finger(u, v, -.11f, .82f) || Finger(u, v, .11f, .78f) || Finger(u, v, .33f, .6f));
                    // The thumb leans out from the side of the palm.
                    float tu = u + .55f, tv = v + .1f;
                    bool thumb = Mathf.Abs(tu * .8f - tv * .6f) <= .09f && tu * .6f + tv * .8f >= -.05f && tu * .6f + tv * .8f <= .42f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(palm || fingers || thumb ? 255 : 0));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            hand = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
            hand.name = "PieceCueHand";
            return hand;
        }

        private static bool Finger(float u, float v, float centre, float top) => Mathf.Abs(u - centre) <= .085f && v <= top;

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
            bool onShow = point != null && point.HasContent && presenter != null && presenter.ModelPoint == point && presenter.Model != null;
            bool piece = onShow && presenter.TryPieceLabel(out slug, out pieceTitle, out pieceTechnique, out pieceNote);
            piece = piece && MuseumPointRecords.Pieces(point.Content).Exists(candidate => candidate.slug == slug);
            Bounds bounds = default;
            bool placed = onShow && Bounds(out bounds);
            if (piece && slug != shownSlug) { shownSlug = slug; Fill(pieceTitle, pieceTechnique, pieceNote); }
            if (!piece) shownSlug = null;
            bool held = grab != null && grab.Held;
            bool hovered = grab != null && grab.Hovered;
            string cueText = placed ? CueText(held, hovered) : null;
            if (cueText != null && cue.text != cueText) cue.text = cueText;
            bool showCue = cueText != null;
            if (cueRow.gameObject.activeSelf != showCue) cueRow.gameObject.SetActive(showCue);
            // A piece that is not one of a point's labelled pieces (a record on the card, the Castle) shows only the cue.
            if (title.gameObject.activeSelf != piece)
            {
                title.gameObject.SetActive(piece);
                technique.gameObject.SetActive(piece && !string.IsNullOrEmpty(technique.text));
                note.gameObject.SetActive(piece && !string.IsNullOrEmpty(note.text));
            }
            Wanted = placed && (piece && (held || hovered || LookedAt(bounds.center, viewer)) || showCue);
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
