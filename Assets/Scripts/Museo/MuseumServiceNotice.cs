using System;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Keeps a missing service apart from a point without content. While the tour cannot be loaded, the screen of the
    /// point the visitor stands at says so in neutral terms and offers to retry, instead of showing nothing as if nothing
    /// were published. The visit stays free meanwhile.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction), typeof(TourLoader))]
    public sealed class MuseumServiceNotice : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourLoader loader;
        private MuseumVisitFlow flow;
        private Transform visitor;
        private TourPoint[] points = Array.Empty<TourPoint>();
        /// <summary>Scene point whose screen carries the notice, or null.</summary>
        public TourPoint Point { get; private set; }
        public event Action Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void Awake() => Watch();

        private void Watch()
        {
            if (interaction != null) return;
            interaction = GetComponent<MuseumInteraction>();
            loader = GetComponent<TourLoader>();
            points = interaction.Runtime.GetComponentsInChildren<TourPoint>(true);
        }

        private void Update()
        {
            if (visitor != null) Step(visitor.position);
        }

        /// <summary>Evaluates one visitor position. The notice follows the same radius and leave margin as published points.</summary>
        public void Step(Vector3 position)
        {
            Watch();
            if (flow == null) flow = GetComponent<MuseumVisitFlow>();
            TourPoint next = null;
            bool missing = loader.State != TourLoadState.Ready && interaction.Runtime.Contract == null
                && interaction.State == InteractionState.Exploration && (flow == null || !flow.BlocksInput);
            if (missing)
            {
                float margin = MuseumExperienceConfiguration.Current.PointLeaveMargin;
                if (Point != null && !MuseumPointPresence.IsAway(Point.Anchor.DistanceTo(position), Point.Anchor.ActivationRadius, margin, false))
                    next = Point;
                else
                {
                    float nearest = float.PositiveInfinity;
                    foreach (var point in points)
                    {
                        if (point == null || !point.isActiveAndEnabled) continue;
                        float distance = point.Anchor.DistanceTo(position);
                        if (distance <= point.Anchor.ActivationRadius && distance < nearest) { nearest = distance; next = point; }
                    }
                }
            }
            if (next == Point) return;
            Point = next;
            Changed?.Invoke();
        }
    }
}
