using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Stable point location; the API supplies its content.</summary>
    public sealed class PointAnchor : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField, Min(0.1f)] private float activationRadius = 1.8f;
        [SerializeField] private Transform lookTarget;

        public string Key => key;
        public float ActivationRadius => activationRadius;
        public Transform LookTarget => lookTarget != null ? lookTarget : transform;

        public void Configure(string pointKey, float radius, Transform target)
        {
            key = pointKey;
            activationRadius = radius;
            lookTarget = target;
        }
    }
}
