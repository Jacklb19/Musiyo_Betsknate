using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Where the entry menu looks at the museum from. A neutral scene anchor: move or turn it to reframe the entry.</summary>
    public sealed class MuseumEntryViewpoint : MonoBehaviour
    {
        [SerializeField, Range(30, 90)] private float fieldOfView = 55;
        public float FieldOfView => fieldOfView;

        public void Configure(float view) => fieldOfView = Mathf.Clamp(view, 30, 90);

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 8, .3f, 16f / 9f);
        }
    }
}
