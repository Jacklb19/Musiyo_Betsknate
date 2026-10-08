using System;
using System.Linq;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Welcome card shown in the arrival area: museum name, suggested route, and controls. Not cultural content.</summary>
    [RequireComponent(typeof(TourRuntime), typeof(MuseumInteraction))]
    public sealed class MuseumArrival : MonoBehaviour
    {
        [SerializeField] private string roomKey = "room.arrival";
        [SerializeField] private string zoneKey = "zona.llegada.tutorial";
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform visitor;
        private RoomAnchor room;
        private PointAnchor zone;
        private bool connected;
        private bool dismissed;
        private bool wasInside;
        public bool Visible { get; private set; }
        public string Title { get { Connect(); return runtime.Contract?.tour?.name ?? ""; } }
        public string Route
        {
            get
            {
                Connect();
                return runtime.Contract == null ? ""
                    : string.Join(" · ", runtime.Contract.rooms.Where(item => !string.IsNullOrEmpty(item.name)).Select(item => item.name));
            }
        }
        public string Body => MuseumInterfaceText.Format("arrival_body", MuseumInterfaceText.Current.SuggestedVisitMinutes, Route);
        public event Action Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void OnEnable() => Connect();
        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            runtime.Changed -= Bind;
            interaction.Changed -= Refresh;
        }
        private void Update() => Refresh();

        private void Connect()
        {
            if (connected) return;
            connected = true;
            runtime = GetComponent<TourRuntime>();
            interaction = GetComponent<MuseumInteraction>();
            runtime.Changed += Bind;
            interaction.Changed += Refresh;
            Bind();
        }

        private void Bind()
        {
            room = runtime.FindRoom(roomKey);
            zone = runtime.FindAnchor(zoneKey);
            dismissed = false;
            wasInside = false;
            Refresh();
        }

        public bool InsideArea()
        {
            Connect();
            if (visitor == null) return false;
            if (room != null && room.Contains(visitor.position)) return true;
            if (zone == null) return false;
            var offset = zone.transform.position - visitor.position;
            offset.y = 0;
            return offset.sqrMagnitude <= zone.ActivationRadius * zone.ActivationRadius;
        }

        public void Refresh()
        {
            Connect();
            bool inside = runtime.Contract != null && InsideArea();
            if (wasInside && !inside) dismissed = true;
            wasInside = inside;
            bool visible = inside && !dismissed && interaction.State == InteractionState.Exploration;
            if (visible == Visible) return;
            Visible = visible;
            Changed?.Invoke();
        }

        public void Dismiss()
        {
            Connect();
            dismissed = true;
            Refresh();
        }
    }
}
