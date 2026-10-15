using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Keeps the visit's screen chrome out of the way: the title header and the key reminder show when the visit starts
    /// or resumes, then fade, and come back while the tour is not ready. The reminder names the keys in force.
    /// </summary>
    public sealed class MuseumHudPresence : MonoBehaviour
    {
        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private TourLoader loader;
        private Image headerBackground;
        private float headerAlpha;
        private CanvasGroup headerText;
        private CanvasGroup controls;
        private TMP_Text controlText;
        private MuseumInput input;
        private bool wasExploring;
        private float headerUntil;
        private float controlsUntil;

        /// <summary>Visible share of the header text and the key reminder, from 0 to 1.</summary>
        public float HeaderVisibility => headerText != null ? headerText.alpha : 1;
        public float ControlsVisibility => controls != null ? controls.alpha : 1;

        public void Configure(MuseumVisitFlow visit, MuseumInteraction state, TourLoader tour, RectTransform header, RectTransform controlBar)
        {
            flow = visit;
            interaction = state;
            loader = tour;
            if (header != null)
            {
                headerBackground = header.GetComponent<Image>();
                headerAlpha = headerBackground != null ? headerBackground.color.a : 1;
                // Title and status fade together; the pause button in the header stays.
                var texts = new GameObject("HeaderText", typeof(RectTransform), typeof(CanvasGroup));
                texts.transform.SetParent(header, false);
                var rect = (RectTransform)texts.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                texts.transform.SetSiblingIndex(0);
                foreach (var name in new[] { "Title", "Status" })
                {
                    var child = header.Find(name);
                    if (child != null) child.SetParent(texts.transform, true);
                }
                headerText = texts.GetComponent<CanvasGroup>();
                headerText.blocksRaycasts = false;
            }
            if (controlBar != null)
            {
                if (!controlBar.TryGetComponent(out controls)) controls = controlBar.gameObject.AddComponent<CanvasGroup>();
                controls.blocksRaycasts = false;
                controlText = controlBar.GetComponentInChildren<TMP_Text>(true);
                if (controlText != null && controlText.TryGetComponent<MuseumInterfaceLabel>(out var label)) label.enabled = false;
            }
            input = MuseumInput.Current;
            input.BindingsChanged += RefreshKeys;
            RefreshKeys();
        }

        private void OnDestroy() { if (input != null) input.BindingsChanged -= RefreshKeys; }

        public void RefreshKeys()
        {
            if (controlText != null) controlText.text = MuseumInterfaceText.Get("hud_controls");
        }

        private void Update() => Advance(Time.unscaledTime, Time.unscaledDeltaTime);

        /// <summary>One step of the fades at a given time; tests call it directly.</summary>
        public void Advance(float now, float elapsed)
        {
            if (flow == null) return;
            var settings = MuseumExperienceConfiguration.Current;
            bool exploring = flow.Phase == VisitPhase.Exploring && interaction.State != InteractionState.Paused;
            if (exploring && !wasExploring)
            {
                headerUntil = now + settings.HeaderHintSeconds;
                controlsUntil = now + settings.ControlsHintSeconds;
            }
            wasExploring = exploring;
            bool header = !exploring || loader.State != TourLoadState.Ready || now < headerUntil;
            bool keys = exploring && now < controlsUntil;
            float step = Mathf.Max(0, elapsed) / settings.PanelFadeSeconds * .25f;
            if (headerText != null)
            {
                headerText.alpha = Mathf.MoveTowards(headerText.alpha, header ? 1 : 0, step);
                if (headerBackground != null)
                {
                    var colour = headerBackground.color;
                    colour.a = headerAlpha * headerText.alpha;
                    headerBackground.color = colour;
                }
            }
            if (controls != null) controls.alpha = Mathf.MoveTowards(controls.alpha, keys ? 1 : 0, step);
        }
    }
}
