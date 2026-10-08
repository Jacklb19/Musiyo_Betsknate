using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public sealed class RoomAnchor : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private Vector2 halfSize = new Vector2(5, 5);
        public string Key => key;
        public void Configure(string roomKey) => key = roomKey;
        public void ConfigureBounds(Vector2 extent) => halfSize = extent;
        public bool Contains(Vector3 visitor)
        {
            var position = transform.InverseTransformPoint(visitor);
            return Mathf.Abs(position.x) <= halfSize.x && Mathf.Abs(position.z) <= halfSize.y;
        }
    }
}
