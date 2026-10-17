using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Development builds only: every few seconds writes to the log each large canvas and each large moving renderer that
    /// is drawn near the visitor's head, so a panel or figure standing in the way can be named from the headset log
    /// (adb logcat -s Unity | findstr MuseumSurfaceProbe). It changes nothing in the scene.
    /// </summary>
    public sealed class MuseumSurfaceProbe : MonoBehaviour
    {
        public const string Tag = "MuseumSurfaceProbe";
        /// <summary>A surface whose larger side is at least this long, in metres, is reported.</summary>
        public const float MinimumSize = .8f;
        /// <summary>Only surfaces this close to the head, in metres, are reported.</summary>
        public const float Radius = 4;
        private const float Interval = 3;
        private const int MaximumEntries = 16;
        private readonly Dictionary<int, Vector3> headRelative = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> lastWorld = new Dictionary<int, Vector3>();
        private readonly List<Entry> entries = new List<Entry>();
        private Transform head;
        private Vector3 lastHead;
        private float next;
        private int sample;

        public readonly struct Entry
        {
            public readonly string Kind, Path;
            public readonly Vector2 Size;
            public readonly float Distance;
            public readonly bool Follows;
            public Entry(string kind, string path, Vector2 size, float distance, bool follows)
            { Kind = kind; Path = path; Size = size; Distance = distance; Follows = follows; }
        }

        public void Configure(Transform viewer)
        {
            head = viewer;
            lastHead = head != null ? head.position : Vector3.zero;
        }

        private void Update()
        {
            if (head == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            sample++;
            Collect(head, entries);
            var line = new StringBuilder();
            line.Append(Tag).Append(" #").Append(sample).Append(" head=").Append(Format(head.position))
                .Append(" surfaces=").Append(entries.Count);
            Debug.Log(line.ToString());
            for (int index = 0; index < entries.Count && index < MaximumEntries; index++)
            {
                var entry = entries[index];
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "{0} #{1} {2} size={3:F2}x{4:F2} dist={5:F2} follows={6} path={7}",
                    Tag, sample, entry.Kind, entry.Size.x, entry.Size.y, entry.Distance, entry.Follows ? "yes" : "no", entry.Path));
            }
            lastHead = head.position;
        }

        /// <summary>Large canvases and large non-static renderers drawn within the radius of the head, nearest first.</summary>
        public void Collect(Transform viewer, List<Entry> found)
        {
            found.Clear();
            bool headMoved = Vector3.Distance(viewer.position, lastHead) > .2f;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.renderMode != RenderMode.WorldSpace) continue;
                var rect = (RectTransform)canvas.transform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var size = new Vector2(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1]));
                var centre = (corners[0] + corners[2]) * .5f;
                Consider("canvas", canvas.transform, size, Vector3.Distance(viewer.position, centre), centre, viewer, headMoved, found);
            }
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                // Lightmapped and static museum geometry is the building itself; only things that can move are reported.
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.gameObject.isStatic
                    || renderer.lightmapIndex >= 0 && renderer.lightmapIndex < 0xFFFE || renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                var bounds = renderer.bounds;
                var extents = new[] { bounds.size.x, bounds.size.y, bounds.size.z };
                System.Array.Sort(extents);
                var size = new Vector2(extents[2], extents[1]);
                Consider("renderer", renderer.transform, size, Vector3.Distance(viewer.position, bounds.ClosestPoint(viewer.position)),
                    bounds.center, viewer, headMoved, found);
            }
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        private void Consider(string kind, Transform item, Vector2 size, float distance, Vector3 centre, Transform viewer,
            bool headMoved, List<Entry> found)
        {
            int id = item.GetInstanceID();
            var relative = viewer.InverseTransformPoint(centre);
            // Something follows the head when the head walked and it kept its place in front of the eyes while moving in the world.
            bool follows = item.IsChildOf(viewer) || headMoved && headRelative.TryGetValue(id, out var before)
                && Vector3.Distance(before, relative) < .15f && lastWorld.TryGetValue(id, out var world)
                && Vector3.Distance(world, centre) > .15f;
            headRelative[id] = relative;
            lastWorld[id] = centre;
            if (Mathf.Max(size.x, size.y) < MinimumSize || distance > Radius) return;
            found.Add(new Entry(kind, PathOf(item), size, distance, follows));
        }

        public static string PathOf(Transform item)
        {
            var path = new StringBuilder(item.name);
            for (var parent = item.parent; parent != null; parent = parent.parent) path.Insert(0, parent.name + "/");
            return path.ToString();
        }

        private static string Format(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", value.x, value.y, value.z);
    }
}
