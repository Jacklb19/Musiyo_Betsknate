using System;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Tracks whether the visitor is still with the active point; narration and the Web page follow it.</summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumPointPresence : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private Transform visitor;
        private TourPoint tracked;
        public bool Away { get; private set; }
        /// <summary>Raised with true when the visitor leaves the active point and false when they return.</summary>
        public event Action<bool> Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        /// <summary>Hysteresis: leave beyond radius plus margin, return inside the radius, so the edge never stutters.</summary>
        public static bool IsAway(float distance, float radius, float margin, bool wasAway)
            => wasAway ? distance > radius : distance > radius + margin;

        private void Awake() => interaction = GetComponent<MuseumInteraction>();
        private void OnEnable() { interaction.Changed += Track; Track(); }
        private void OnDisable() => interaction.Changed -= Track;

        // A newly activated point always starts "near": its selection message already implies presence.
        private void Track()
        {
            if (interaction == null) interaction = GetComponent<MuseumInteraction>();
            if (interaction.ActivePoint == tracked) return;
            tracked = interaction.ActivePoint;
            Away = false;
        }

        private void Update()
        {
            if (visitor != null) Step(visitor.position);
        }

        /// <summary>Evaluates one visitor position; paused visits keep their last presence.</summary>
        public void Step(Vector3 visitorPosition)
        {
            Track();
            if (tracked == null || interaction.State == InteractionState.Paused) return;
            var offset = tracked.Anchor.transform.position - visitorPosition;
            offset.y = 0;
            bool away = IsAway(offset.magnitude, tracked.Anchor.ActivationRadius,
                MuseumExperienceConfiguration.Current.PointLeaveMargin, Away);
            if (away == Away) return;
            Away = away;
            Changed?.Invoke(away);
        }
    }
}
