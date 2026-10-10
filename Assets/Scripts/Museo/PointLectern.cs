using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Reading stand beside a point; the detail of that point unfolds from its board.</summary>
    public sealed class PointLectern : MonoBehaviour
    {
        [SerializeField] private PointAnchor point;
        [SerializeField] private Transform board;
        [SerializeField] private Vector2 boardSize;

        public PointAnchor Point => point;
        /// <summary>Bottom centre of the reading surface. Its forward axis points into the board, away from the reader.</summary>
        public Transform Board => board;
        public Vector2 BoardSize => boardSize;

        public void Configure(PointAnchor anchor, Transform surface, Vector2 size)
        {
            point = anchor;
            board = surface;
            boardSize = size;
            if (point != null) point.Attach(transform);
        }

        private void OnEnable() { if (point != null) point.Attach(transform); }
        private void OnDisable() { if (point != null && point.Station == transform) point.Attach(null); }

        /// <summary>Middle of the reading surface.</summary>
        public Vector3 BoardCentre => board.position + board.up * (boardSize.y * .5f);

        /// <summary>Horizontal direction from the board toward the visitor who reads it.</summary>
        public Vector3 Facing
        {
            get
            {
                var direction = -board.forward;
                direction.y = 0;
                return direction.normalized;
            }
        }

        /// <summary>Floor position in front of the exhibit and the board, from where both are in view.</summary>
        public Vector3 ViewingSpot(float distance)
        {
            var middle = (point.transform.position + transform.position) * .5f;
            middle.y = point.transform.position.y;
            return middle + Facing * distance;
        }

        /// <summary>Point to look at from the viewing spot: halfway between the exhibit and the middle of the board.</summary>
        public Vector3 ViewTarget => (point.LookTarget.position + BoardCentre) * .5f;

        /// <summary>
        /// Finds the reading surface of a stand mesh: the largest flat face that leans upward toward the reader.
        /// Vertices are expected in world space so "upward" is meaningful.
        /// </summary>
        public static bool TryFindBoard(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles,
            out Vector3 bottomCentre, out Vector3 normal, out Vector3 up, out Vector2 size)
        {
            bottomCentre = normal = up = Vector3.zero;
            size = Vector2.zero;
            var areas = new Dictionary<Vector3Int, float>();
            var normals = new Dictionary<Vector3Int, Vector3>();
            for (int index = 0; index + 2 < triangles.Count; index += 3)
            {
                var cross = Vector3.Cross(vertices[triangles[index + 1]] - vertices[triangles[index]],
                    vertices[triangles[index + 2]] - vertices[triangles[index]]);
                if (cross.sqrMagnitude < 1e-12f) continue;
                var direction = cross.normalized;
                // Vertical faces and faces that look down cannot be read from a standing position.
                if (direction.y < .1f || direction.y > .95f) continue;
                var key = Vector3Int.RoundToInt(direction * 50);
                areas.TryGetValue(key, out float area);
                areas[key] = area + cross.magnitude * .5f;
                normals[key] = direction;
            }
            float largest = 0;
            foreach (var entry in areas)
                if (entry.Value > largest) { largest = entry.Value; normal = normals[entry.Key]; }
            if (largest <= 0) return false;

            up = (Vector3.up - normal * Vector3.Dot(Vector3.up, normal)).normalized;
            var right = Vector3.Cross(up, normal);
            var key2 = Vector3Int.RoundToInt(normal * 50);
            float minRight = float.PositiveInfinity, maxRight = float.NegativeInfinity;
            float minUp = float.PositiveInfinity, maxUp = float.NegativeInfinity, depth = 0;
            int count = 0;
            for (int index = 0; index + 2 < triangles.Count; index += 3)
            {
                var a = vertices[triangles[index]];
                var cross = Vector3.Cross(vertices[triangles[index + 1]] - a, vertices[triangles[index + 2]] - a);
                if (cross.sqrMagnitude < 1e-12f || Vector3Int.RoundToInt(cross.normalized * 50) != key2) continue;
                for (int corner = 0; corner < 3; corner++)
                {
                    var vertex = vertices[triangles[index + corner]];
                    float along = Vector3.Dot(vertex, right), above = Vector3.Dot(vertex, up);
                    minRight = Mathf.Min(minRight, along); maxRight = Mathf.Max(maxRight, along);
                    minUp = Mathf.Min(minUp, above); maxUp = Mathf.Max(maxUp, above);
                    depth += Vector3.Dot(vertex, normal);
                    count++;
                }
            }
            size = new Vector2(maxRight - minRight, maxUp - minUp);
            bottomCentre = right * ((minRight + maxRight) * .5f) + up * minUp + normal * (depth / count);
            return size.x > 0 && size.y > 0;
        }
    }
}
