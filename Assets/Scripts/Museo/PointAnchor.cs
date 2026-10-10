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
        /// <summary>Furniture that belongs to the point, such as its reading stand; standing by it is being at the point.</summary>
        public Transform Station { get; private set; }

        public void Attach(Transform station) => Station = station;

        /// <summary>Floor distance to the point: to its exhibit or to its attached furniture, whichever is closer.</summary>
        public float DistanceTo(Vector3 position)
        {
            var offset = transform.position - position;
            offset.y = 0;
            if (Station == null) return offset.magnitude;
            var toStation = Station.position - position;
            toStation.y = 0;
            return Mathf.Min(offset.magnitude, toStation.magnitude);
        }

        public void Configure(string pointKey, float radius, Transform target)
        {
            key = pointKey;
            activationRadius = radius;
            lookTarget = target;
        }
    }
}
