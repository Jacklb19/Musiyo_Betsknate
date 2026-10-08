using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Shared dwell rules for desktop, head gaze, and deterministic tests.</summary>
    public sealed class DwellActivation
    {
        private string candidate;
        private string fired;
        private float elapsed;
        public float Progress { get; private set; }

        public void SuppressUntilExit(string key)
        {
            candidate = key;
            fired = key;
            elapsed = 0;
            Progress = 0;
        }

        public bool Step(string key, bool eligible, float deltaTime, float duration)
        {
            if (!eligible || string.IsNullOrEmpty(key))
            { candidate = null; fired = null; elapsed = 0; Progress = 0; return false; }
            if (candidate != key) { candidate = key; elapsed = 0; fired = null; }
            // A point that already fired (or was closed) shows no progress until the visitor leaves it.
            if (fired == key) { elapsed = 0; Progress = 0; return false; }
            elapsed += Mathf.Max(0, deltaTime);
            Progress = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));
            if (Progress < 1 || fired == key) return false;
            fired = key;
            return true;
        }
    }

    public static class PointActivation
    {
        public static bool IsInRange(TourPoint point, Vector3 visitor)
        {
            var difference = point.Anchor.transform.position - visitor;
            difference.y = 0;
            return difference.sqrMagnitude <= point.Anchor.ActivationRadius * point.Anchor.ActivationRadius;
        }

        public static bool IsVisible(TourPoint point, Transform eye, float maxDistance, float maxAngle)
        {
            if (point == null || eye == null) return false;
            var offset = point.Anchor.LookTarget.position - eye.position;
            if (offset.sqrMagnitude > maxDistance * maxDistance || Vector3.Angle(eye.forward, offset) > maxAngle) return false;
            if (offset.sqrMagnitude < 0.001f) return true;
            if (!Physics.Raycast(eye.position, offset.normalized, out var hit, offset.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            return hit.transform == point.transform || hit.transform.IsChildOf(point.transform);
        }
    }
}
