using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The top of an existing pedestal, or a place on the floor, where a published model stands instead of floating at the
    /// point's look target. Its forward axis is the side visitors look from. Models are scaled to fit its footprint and
    /// height; a mount that keeps natural scale only ever scales a model down, so a piece built at real size stands at its
    /// real size. Nothing published is stored here.
    /// </summary>
    public sealed class PointExhibitMount : MonoBehaviour
    {
        [SerializeField, Min(.05f)] private float maxFootprint = .84f;
        [SerializeField, Min(.05f)] private float maxHeight = 1.2f;
        [SerializeField] private bool keepNaturalScale;
        /// <summary>
        /// Times the built size a natural-scale model stands at. A reconstruction whose measurements are approximate may be
        /// shown larger than it was modelled when the owner decides so.
        /// </summary>
        [SerializeField, Min(.1f)] private float sizeFactor = 1;
        public float MaxFootprint => maxFootprint;
        public float MaxHeight => maxHeight;
        public bool KeepNaturalScale => keepNaturalScale;
        public float SizeFactor => sizeFactor;

        public void Configure(float footprint, float height, bool naturalScale = false, float factor = 1)
        {
            maxFootprint = Mathf.Max(.05f, footprint);
            maxHeight = Mathf.Max(.05f, height);
            keepNaturalScale = naturalScale;
            sizeFactor = Mathf.Max(.1f, factor);
        }

        /// <summary>
        /// Scale of a model of this size on this mount. <paramref name="naturalScale"/> is the scale at which the model has
        /// the size it was built at; a mount that keeps natural scale never goes beyond it.
        /// </summary>
        public float FitFor(Vector3 size, float naturalScale = 1)
        {
            float fit = Fit(size, maxFootprint, maxHeight);
            return keepNaturalScale ? Mathf.Min(naturalScale * sizeFactor, fit) : fit;
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
