using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public sealed class RoomAnchor : MonoBehaviour
    {
        [SerializeField] private string key;
        public string Key => key;
        public void Configure(string roomKey) => key = roomKey;
    }
}
