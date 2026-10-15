using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The top of an existing pedestal where a published model stands instead of floating at the point's look target.
    /// Its forward axis is the side visitors look from. Models are scaled down to fit its footprint and height; nothing
    /// published is stored here.
    /// </summary>
    public sealed class PointExhibitMount : MonoBehaviour
    {
        [SerializeField, Min(.05f)] private float maxFootprint = .84f;
        [SerializeField, Min(.05f)] private float maxHeight = 1.2f;
        public float MaxFootprint => maxFootprint;
        public float MaxHeight => maxHeight;

        public void Configure(float footprint, float height)
        {
            maxFootprint = Mathf.Max(.05f, footprint);
            maxHeight = Mathf.Max(.05f, height);
        }

        /// <summary>Scale that fits a model of this size on the mount: its footprint diagonal and its height both fit.</summary>
        public static float Fit(Vector3 size, float footprint, float height)
        {
            float diagonal = new Vector2(size.x, size.z).magnitude;
            if (diagonal <= 0 || size.y <= 0) return 1;
            return Mathf.Min(footprint / diagonal, height / size.y);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * maxHeight * .5f, new Vector3(maxFootprint, maxHeight, maxFootprint));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * maxFootprint);
        }
    }
}
