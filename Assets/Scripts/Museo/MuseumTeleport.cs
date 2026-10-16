using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The jump headset visits usually offer. While the button is held an arc leaves the right controller and an arrow on
    /// the floor shows where the visitor will stand; releasing the button there moves the visitor at once. Only open floor
    /// near the visitor's own height is accepted, never a pedestal, a wall or a roof. The visitor keeps facing the same way,
    /// so the view never turns by itself, and walking with the stick stays as it was.
    /// </summary>
    public sealed class MuseumTeleport
    {
        /// <summary>Launch speed of the arc, in metres per second; with the controller level it lands about six metres away.</summary>
        public const float LaunchSpeed = 7.5f;
        public const float StepSeconds = .03f;
        public const float MaxSeconds = 1.6f;
        /// <summary>Furthest jump, measured along the floor.</summary>
        public const float MaxDistance = 7;
        /// <summary>Highest step up or down a jump accepts; pedestals and lecterns are taller.</summary>
        public const float MaxRise = .45f;
        /// <summary>Cosine of the steepest floor accepted (about 30 degrees).</summary>
        public const float MinFloorUp = .87f;
        private const float MarkerLift = .015f;

        private readonly Transform visitor;
        private readonly CharacterController body;
        private readonly List<Vector3> arc = new List<Vector3>(64);
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlap = new Collider[16];
        private readonly LineRenderer line;
        private readonly Transform marker;
        private readonly Material validMaterial;
        private readonly Material invalidMaterial;
        private Vector3 landing;

        public bool Aiming { get; private set; }
        public bool Valid { get; private set; }
        public Vector3 Landing => landing;

        public MuseumTeleport(Transform visitor, CharacterController body, Material pointer, Color accent, Color muted)
        {
            this.visitor = visitor;
            this.body = body;
            validMaterial = new Material(pointer) { name = "MuseumTeleportValid" };
            validMaterial.SetColor("_BaseColor", accent);
            invalidMaterial = new Material(pointer) { name = "MuseumTeleportInvalid" };
            invalidMaterial.SetColor("_BaseColor", muted);
            var arcObject = new GameObject("TeleportArc");
            arcObject.transform.SetParent(visitor, false);
            line = arcObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.startWidth = .012f; line.endWidth = .008f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            var markerObject = new GameObject("TeleportMarker");
            markerObject.transform.SetParent(visitor, false);
            markerObject.AddComponent<MeshFilter>().sharedMesh = BuildMarker();
            var renderer = markerObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = validMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            marker = markerObject.transform;
            marker.gameObject.SetActive(false);
        }

        /// <summary>Position along the arc after <paramref name="seconds"/>, under ordinary gravity.</summary>
        public static Vector3 ArcPoint(Vector3 origin, Vector3 velocity, float seconds)
            => origin + velocity * seconds + .5f * Physics.gravity * seconds * seconds;

        /// <summary>
        /// True when a surface reached by the arc is somewhere a visitor can stand: facing up, about the visitor's own
        /// height and within reach along the floor.
        /// </summary>
        public static bool Acceptable(Vector3 feet, Vector3 point, Vector3 normal)
        {
            if (!MuseumExperienceConfiguration.Finite(point.x) || !MuseumExperienceConfiguration.Finite(point.y)
                || !MuseumExperienceConfiguration.Finite(point.z)) return false;
            if (normal.y < MinFloorUp) return false;
            if (Mathf.Abs(point.y - feet.y) > MaxRise) return false;
            var along = point - feet;
            along.y = 0;
            return along.magnitude <= MaxDistance;
        }

        /// <summary>Traces the arc from the controller and shows it with the arrow where the visitor would stand.</summary>
        public void Aim(Vector3 origin, Vector3 direction, Vector3 feet, Vector3 facing)
        {
            Aiming = true;
            Valid = false;
            arc.Clear();
            arc.Add(origin);
            var velocity = direction.normalized * LaunchSpeed;
            var previous = origin;
            bool reached = false;
            RaycastHit hit = default;
            for (float seconds = StepSeconds; seconds <= MaxSeconds; seconds += StepSeconds)
            {
                var next = ArcPoint(origin, velocity, seconds);
                if (Cast(previous, next, out hit)) { arc.Add(hit.point); reached = true; break; }
                arc.Add(next);
                previous = next;
            }
            Valid = reached && Acceptable(feet, hit.point, hit.normal) && Fits(hit.point);
            if (Valid) landing = hit.point;
            line.positionCount = arc.Count;
            for (int index = 0; index < arc.Count; index++) line.SetPosition(index, arc[index]);
            line.sharedMaterial = Valid ? validMaterial : invalidMaterial;
            line.enabled = true;
            marker.gameObject.SetActive(Valid);
            if (Valid)
            {
                facing.y = 0;
                if (facing.sqrMagnitude < 1e-4f) facing = visitor.forward;
                marker.SetPositionAndRotation(landing + Vector3.up * MarkerLift, Quaternion.LookRotation(facing.normalized, Vector3.up));
            }
        }

        /// <summary>Hides the arc and the arrow without moving the visitor.</summary>
        public void Cancel()
        {
            Aiming = Valid = false;
            line.enabled = false;
            marker.gameObject.SetActive(false);
        }

        /// <summary>Moves the visitor so that the point under the head stands on the chosen floor.</summary>
        public bool Jump(Vector3 feet)
        {
            bool valid = Aiming && Valid;
            var target = landing;
            Cancel();
            if (!valid) return false;
            bool wasEnabled = body.enabled;
            body.enabled = false;
            visitor.position += target - feet;
            body.enabled = wasEnabled;
            Physics.SyncTransforms();
            return true;
        }

        private bool Cast(Vector3 from, Vector3 to, out RaycastHit nearest)
        {
            nearest = default;
            var offset = to - from;
            float length = offset.magnitude;
            if (length < 1e-4f) return false;
            int count = Physics.RaycastNonAlloc(from, offset / length, hits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                if (Belongs(hits[index].collider) || hits[index].distance >= best) continue;
                best = hits[index].distance;
                nearest = hits[index];
            }
            return best < float.MaxValue;
        }

        /// <summary>The visitor's body fits standing there: nothing but the visitor in a capsule of its size.</summary>
        private bool Fits(Vector3 point)
        {
            float radius = body.radius * .9f;
            var bottom = point + Vector3.up * (radius + .05f);
            var top = point + Vector3.up * Mathf.Max(radius + .1f, body.height - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++) if (!Belongs(overlap[index])) return false;
            return true;
        }

        private bool Belongs(Collider collider) => collider != null && collider.transform.IsChildOf(visitor);

        /// <summary>A flat ring with an arrow inside pointing the way the visitor will face, visible from above.</summary>
        private static Mesh BuildMarker()
        {
            const int segments = 40;
            const float inner = .24f, outer = .29f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2 / segments;
                var around = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                vertices.Add(around * inner);
                vertices.Add(around * outer);
            }
            for (int index = 0; index < segments; index++)
            {
                int a = index * 2, b = a + 1, c = (a + 2) % (segments * 2), d = c + 1;
                triangles.AddRange(new[] { a, b, d, a, d, c });
            }
            int arrow = vertices.Count;
            vertices.AddRange(new[]
            {
                new Vector3(0, 0, .2f), new Vector3(.11f, 0, .02f), new Vector3(-.11f, 0, .02f),
                new Vector3(.04f, 0, .02f), new Vector3(.04f, 0, -.16f), new Vector3(-.04f, 0, -.16f), new Vector3(-.04f, 0, .02f),
            });
            triangles.AddRange(new[] { arrow, arrow + 1, arrow + 2, arrow + 3, arrow + 4, arrow + 5, arrow + 3, arrow + 5, arrow + 6 });
            // Both faces, so the marker shows whichever way the floor normal of the import points.
            int single = triangles.Count;
            for (int index = 0; index < single; index += 3) triangles.AddRange(new[] { triangles[index], triangles[index + 2], triangles[index + 1] });
            var mesh = new Mesh { name = "MuseumTeleportMarker" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
