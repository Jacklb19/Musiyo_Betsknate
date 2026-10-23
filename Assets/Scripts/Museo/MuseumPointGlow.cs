using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A low, warm light on the floor in front of each published point, in place of the coloured spheres that floated
    /// over the pedestals and changed colour with the point's state (the owner felt they changed out of time, 22/10/2026).
    /// The light is steady: it shows where there is something to read, softens once the point has been visited and fades
    /// out while the visitor is at the point, whose screen then speaks for it. Every change is a slow fade, so nothing
    /// blinks. It lies where a visitor stands to read: in front of the reading stand, or at the point's side that faces
    /// the middle of its room where there is no stand.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumPointGlow : MonoBehaviour
    {
        /// <summary>Opacity of the light at a point not visited yet, at one visited, and while the visitor is there.</summary>
        public const float WaitingAlpha = .6f, VisitedAlpha = .28f, PresentAlpha = 0f;
        /// <summary>Seconds a change of light takes.</summary>
        public const float FadeSeconds = 1.2f;
        /// <summary>Width of the light on the floor, in metres.</summary>
        public const float Size = 1.4f;
        private const string GlowMaterial = "EntryBackdropGlow";
        private MuseumInteraction interaction;
        private readonly Dictionary<TourPoint, Glow> glows = new Dictionary<TourPoint, Glow>();
        private readonly HashSet<TourPoint> visited = new HashSet<TourPoint>();
        private MaterialPropertyBlock tint;

        private sealed class Glow
        {
            public MeshRenderer Renderer;
            public float Alpha;
        }

        /// <summary>The light of a point, for tests; null while the point has none.</summary>
        public Renderer LightOf(TourPoint point) => glows.TryGetValue(point, out var glow) ? glow.Renderer : null;
        public float AlphaOf(TourPoint point) => glows.TryGetValue(point, out var glow) ? glow.Alpha : 0;

        private void Awake() => interaction = GetComponent<MuseumInteraction>();

        private void OnEnable()
        {
            interaction.Runtime.Changed += Build;
            interaction.Changed += Remember;
            Build();
        }

        private void OnDisable()
        {
            interaction.Runtime.Changed -= Build;
            interaction.Changed -= Remember;
        }

        private void Remember()
        {
            if (interaction.ActivePoint != null) visited.Add(interaction.ActivePoint);
        }

        /// <summary>Lights the points the tour declares and hides the old state spheres.</summary>
        private void Build()
        {
            visited.Clear();
            var stands = FindObjectsByType<PointLectern>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var point in interaction.Runtime.GetComponentsInChildren<TourPoint>(true))
            {
                point.HideIndicator();
                if (glows.ContainsKey(point) || point.IsGuide) continue;
                PointLectern stand = null;
                foreach (var candidate in stands) if (candidate.Point == point.Anchor) stand = candidate;
                glows[point] = new Glow { Renderer = Create(point, stand) };
            }
        }

        /// <summary>Floor spot in front of a point: before its reading stand, or toward the middle of its room.</summary>
        public static Vector3 Spot(TourPoint point, PointLectern stand)
        {
            if (stand != null) return stand.ViewingSpot(.85f);
            var anchor = point.Anchor.transform.position;
            var room = point.GetComponentInParent<RoomAnchor>();
            var inward = room != null ? room.transform.position - anchor : -point.transform.forward;
            inward.y = 0;
            if (inward.sqrMagnitude < .01f) inward = -point.transform.forward;
            return anchor + inward.normalized * Mathf.Min(1, point.Anchor.ActivationRadius * .6f);
        }

        private MeshRenderer Create(TourPoint point, PointLectern stand)
        {
            var spot = Spot(point, stand);
            float floor = spot.y;
            // The floor under the spot: the first surface below a little above it.
            if (Physics.Raycast(spot + Vector3.up * .6f, Vector3.down, out var hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                floor = hit.point.y;
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "PointGlow";
            var collider = glow.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            glow.transform.SetParent(point.transform, true);
            glow.transform.SetPositionAndRotation(new Vector3(spot.x, floor + .02f, spot.z), Quaternion.Euler(90, 0, 0));
            glow.transform.localScale = Vector3.one * Size;
            var renderer = glow.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = Resources.Load<Material>(GlowMaterial);
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>Opacity a point's light moves toward.</summary>
        public static float Target(PointState state, bool present, bool seen)
        {
            if (state == PointState.Undeclared || state == PointState.Empty) return 0;
            if (present) return PresentAlpha;
            return seen ? VisitedAlpha : WaitingAlpha;
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>Moves every light one time step toward its opacity; tests call it directly.</summary>
        public void Advance(float elapsed)
        {
            tint ??= new MaterialPropertyBlock();
            var accent = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.AccentColor);
            foreach (var entry in glows)
            {
                var point = entry.Key;
                var glow = entry.Value;
                if (point == null || glow.Renderer == null) continue;
                bool present = interaction.ActivePoint == point;
                float target = Target(point.State, present, visited.Contains(point));
                glow.Alpha = Mathf.MoveTowards(glow.Alpha, target, Mathf.Max(0, elapsed) / FadeSeconds * WaitingAlpha);
                bool shown = glow.Alpha > .005f;
                if (glow.Renderer.enabled != shown) glow.Renderer.enabled = shown;
                if (!shown) continue;
                var colour = accent;
                colour.a = glow.Alpha;
                glow.Renderer.GetPropertyBlock(tint);
                tint.SetColor("_BaseColor", colour);
                glow.Renderer.SetPropertyBlock(tint);
            }
        }
    }
}
