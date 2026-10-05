using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public sealed class ModelExaminationPose
    {
        public float Distance { get; private set; } = .8f;
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public void Rotate(Vector2 degrees)
            => Rotation = Quaternion.AngleAxis(degrees.x, Vector3.up) * Rotation
                * Quaternion.AngleAxis(degrees.y, Vector3.right);
        public void Zoom(float delta) => Distance = Mathf.Clamp(Distance + delta, .4f, 1.5f);
        public void Reset() { Distance = .8f; Rotation = Quaternion.identity; }

        public static float FrameScale(Vector3 size, float verticalFieldOfView)
        {
            float radius = size.magnitude * .5f;
            if (radius <= .00001f) return 1;
            // Reserve vertical space for the museum header and controls, even after rotation.
            float angle = Mathf.Atan(Mathf.Tan(verticalFieldOfView * .5f * Mathf.Deg2Rad) * .65f);
            return Mathf.Min(1, .8f * Mathf.Sin(angle) / radius);
        }

        public static bool Normalize(Transform model, float size = .6f)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (!float.IsFinite(longest) || longest <= .00001f || !float.IsFinite(bounds.center.sqrMagnitude)) return false;
            var localCenter = model.InverseTransformPoint(bounds.center);
            model.localScale = Vector3.one * (size / longest);
            model.localPosition = -localCenter * model.localScale.x;
            return true;
        }
    }
}
