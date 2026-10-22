using System;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>End of the suggested route: the last room of the contract offers a way back to the catalogue.</summary>
    [RequireComponent(typeof(TourRuntime))]
    public sealed class MuseumFarewell : MonoBehaviour
    {
        private TourRuntime runtime;
        private Transform visitor;
        private RoomContractV1 lastRoom;
        private RoomAnchor lastAnchor;
        private bool connected;
        public bool AtEnd { get; private set; }
        /// <summary>The last room of the suggested route, where the visit can end.</summary>
        public RoomAnchor Room => lastAnchor;
        /// <summary>The room the route comes from into the last one; the far end of the last room lies away from it.</summary>
        public RoomAnchor PreviousRoom { get; private set; }
        public string RoomName => lastRoom?.name ?? "";
        public string Message => !AtEnd ? ""
            : MuseumInterfaceText.Format("farewell", RoomName);
        public bool CanReturn => Application.platform == RuntimePlatform.WebGLPlayer && GetComponent<MuseumWebBridge>() != null;
        public event Action Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void OnEnable() => Connect();
        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            runtime.Changed -= Bind;
        }
        private void Update() => Refresh();

        private void Connect()
        {
            if (connected) return;
            connected = true;
            runtime = GetComponent<TourRuntime>();
            runtime.Changed += Bind;
            Bind();
        }

        private void Bind()
        {
            lastRoom = null;
            lastAnchor = null;
            PreviousRoom = null;
            if (runtime.Contract != null)
                foreach (var room in runtime.Contract.rooms)
                {
                    var anchor = runtime.FindRoom(room.key);
                    if (anchor == null) continue;
                    PreviousRoom = lastAnchor;
                    lastRoom = room;
                    lastAnchor = anchor;
                }
            Refresh();
            Changed?.Invoke();
        }

        public void Refresh()
        {
            Connect();
            bool atEnd = lastAnchor != null && visitor != null && lastAnchor.Contains(visitor.position);
            if (atEnd == AtEnd) return;
            AtEnd = atEnd;
            Changed?.Invoke();
        }

        public void ReturnToCatalog() => GetComponent<MuseumWebBridge>()?.ReturnToCatalog();
    }
}
