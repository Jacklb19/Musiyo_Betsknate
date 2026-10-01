using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Binds API data to existing anchors; never creates cultural scene objects.</summary>
    public sealed class TourRuntime : MonoBehaviour
    {
        [SerializeField] private string tourKey = "museum-main";
        private readonly Dictionary<string, TourPoint> points = new Dictionary<string, TourPoint>(StringComparer.Ordinal);
        private readonly List<TourPoint> orderedPoints = new List<TourPoint>();
        private readonly List<string> diagnostics = new List<string>();
        public string TourKey => tourKey;
        public TourContractV1 Contract { get; private set; }
        public IReadOnlyList<TourPoint> OrderedPoints => orderedPoints;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public event Action Changed;

        public bool Apply(TourContractV1 contract, out string error)
        {
            Clear();
            if (contract == null) { error = "El recorrido está vacío."; return false; }
            if (!contract.Validate(out error)) return false;
            if (contract.tour.key != tourKey) { error = "El recorrido no corresponde a esta escena."; return false; }
            var rooms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in GetComponentsInChildren<RoomAnchor>(true))
                if (string.IsNullOrWhiteSpace(room.Key) || !rooms.Add(room.Key))
                { error = "Las salas de la escena tienen claves vacías o repetidas."; return false; }
            foreach (var point in GetComponentsInChildren<TourPoint>(true))
            {
                if (string.IsNullOrWhiteSpace(point.Anchor.Key) || points.ContainsKey(point.Anchor.Key))
                { error = "Los puntos de la escena tienen claves vacías o repetidas."; return false; }
                points.Add(point.Anchor.Key, point);
            }
            foreach (var room in contract.rooms)
            {
                if (!rooms.Contains(room.key))
                { diagnostics.Add("Unknown room anchor: " + room.key); continue; }
                foreach (var declared in room.points)
                {
                    if (!points.TryGetValue(declared.key, out var point))
                    { diagnostics.Add("Unknown point anchor: " + declared.key); continue; }
                    var owner = point.GetComponentInParent<RoomAnchor>();
                    if (owner == null || owner.Key != room.key)
                    { diagnostics.Add("Point belongs to another room: " + declared.key); continue; }
                    point.Bind(declared);
                    orderedPoints.Add(point);
                }
            }
            Contract = contract;
            Changed?.Invoke();
            return true;
        }

        public TourPoint FindPoint(string key) => key != null && points.TryGetValue(key, out var point) ? point : null;

        public void Clear()
        {
            foreach (var point in GetComponentsInChildren<TourPoint>(true)) point.Bind(null);
            points.Clear();
            orderedPoints.Clear();
            diagnostics.Clear();
            Contract = null;
            Changed?.Invoke();
        }
    }
}
