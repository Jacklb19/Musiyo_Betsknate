using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The way the visitor walked, as floor positions a little apart. The companion only moves between positions the
    /// visitor stood on, so it never takes a way through a wall, the water or furniture that the visitor did not.
    /// </summary>
    public sealed class CompanionTrail
    {
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly float spacing;
        private readonly int capacity;

        public CompanionTrail(float spacing = .3f, int capacity = 400)
        {
            if (!(spacing > 0) || capacity < 2) throw new ArgumentOutOfRangeException(nameof(spacing));
            this.spacing = spacing;
            this.capacity = capacity;
        }

        public int Count => points.Count;
        public Vector3 this[int index] => points[index];
        public Vector3 Newest => points[points.Count - 1];

        /// <summary>
        /// Notes where the visitor stands. Returns false when the visitor moved farther than a step since the last note
        /// (a jump, such as being taken to a point), in which case the old way no longer leads to the visitor and is cleared.
        /// </summary>
        public bool Record(Vector3 floor, float jump = 1.5f)
        {
            if (points.Count == 0) { points.Add(floor); return true; }
            var last = points[points.Count - 1];
            float distance = Planar(floor - last);
            if (distance > jump)
            {
                points.Clear();
                points.Add(floor);
                return false;
            }
            if (distance < spacing) return true;
            points.Add(floor);
            if (points.Count > capacity) points.RemoveAt(0);
            return true;
        }

        /// <summary>Forgets the way up to (not including) the given index; the companion has passed it.</summary>
        public void DropBefore(int index)
        {
            if (index > 0) points.RemoveRange(0, Mathf.Min(index, points.Count - 1));
        }

        public void Clear() => points.Clear();

        /// <summary>Walking length from a position through the remaining way to the visitor.</summary>
        public float LengthFrom(Vector3 from, int firstIndex, Vector3 visitor)
        {
            if (points.Count == 0) return Planar(visitor - from);
            firstIndex = Mathf.Clamp(firstIndex, 0, points.Count - 1);
            float length = Planar(points[firstIndex] - from);
            for (int index = firstIndex + 1; index < points.Count; index++) length += Planar(points[index] - points[index - 1]);
            return length + Planar(visitor - points[points.Count - 1]);
        }

        /// <summary>
        /// The newest point (closest to the visitor) that can be walked to straight from the given position, by the
        /// caller's test of a clear way; at most <paramref name="checks"/> tests, newest first. -1 when none is clear.
        /// </summary>
        public int FarthestReachable(Vector3 from, Func<Vector3, Vector3, bool> clear, int checks = 30)
        {
            for (int index = points.Count - 1, tested = 0; index >= 0 && tested < checks; index--, tested++)
                if (clear(from, points[index])) return index;
            return -1;
        }

        public static float Planar(Vector3 offset) => new Vector2(offset.x, offset.z).magnitude;
    }
}
