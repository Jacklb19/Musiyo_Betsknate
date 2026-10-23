using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static MusiyoBetsknate.Editor.MuseumWorldDressing;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The entrance gate «Musiyo» given more presence and a better finish (owner, 22/10), built with the rest of
    /// <see cref="MuseumWorldDressing"/> so the saved scene stays as delivered: the whole gate stands a fifth larger about
    /// its foot, its posts rise from collars of rough stone set in the earth, and its name board sits in a frame of dark
    /// wood so it reads from the path. Plain wood and stone only: no flowers, palm or reeds, so it never reads as a second
    /// castle (research A1.5). Where the Editor merged the static pieces before this step (some Play mode loads), the gate
    /// is left as delivered.
    /// </summary>
    public static class MuseumGateDressing
    {
        public const string Prefix = "Portada_", StructureName = "Portada_Estructura", BoardName = "Portada_Letrero";
        /// <summary>How much larger the gate stands than delivered.</summary>
        public const float Scale = 1.2f;
        public const string FootingsName = "GateFootings", FrameName = "GateBoardFrame";
        private const int StonesPerPost = 14;

        public static void Apply(Transform root, Dictionary<string, Transform> objects, System.Random random)
        {
            if (!objects.TryGetValue(StructureName, out var structure) || !structure.TryGetComponent<MeshRenderer>(out var structureRenderer)) return;
            // Play mode in the Editor may have merged the static pieces already; scaling them then would move their
            // colliders and not what is seen, so the gate is left as delivered there. Builds process the scene before merging it.
            if (!structure.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null
                || filter.sharedMesh.name.StartsWith("Combined Mesh")) return;
            var foot = new Vector3(structure.position.x, structureRenderer.bounds.min.y, structure.position.z);
            foreach (var part in objects.Where(entry => entry.Key.StartsWith(Prefix)).Select(entry => entry.Value).ToList())
            {
                part.position = foot + (part.position - foot) * Scale;
                part.localScale *= Scale;
            }
            Physics.SyncTransforms();
            var parent = new GameObject("Gate").transform;
            parent.SetParent(root, false);
            Footings(parent, structure, structureRenderer, foot, random);
            if (objects.TryGetValue(BoardName, out var board) && board.TryGetComponent<MeshRenderer>(out var boardRenderer)) Frame(parent, boardRenderer.bounds, structure);
            Debug.Log("Museum world gate: " + Scale + " times its delivered size, with stone footings and a framed name board.");
        }

        /// <summary>A collar of rough stones around the foot of each post, half sunk in the earth.</summary>
        private static void Footings(Transform parent, Transform structure, MeshRenderer renderer, Vector3 foot, System.Random random)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var low = mesh.vertices.Select(structure.TransformPoint).Where(point => point.y < foot.y + .3f).ToList();
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            foreach (float side in new[] { -1f, 1f })
            {
                var post = low.Where(point => Mathf.Sign(point.x - foot.x) == side).ToList();
                if (post.Count == 0) continue;
                var min = new Vector3(post.Min(point => point.x), foot.y, post.Min(point => point.z));
                var max = new Vector3(post.Max(point => point.x), foot.y, post.Max(point => point.z));
                var middle = (min + max) * .5f;
                var half = (max - min) * .5f;
                for (int index = 0; index < StonesPerPost; index++)
                {
                    float angle = (index + Range(random, -.2f, .2f)) * Mathf.PI * 2 / StonesPerPost;
                    var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    // On the outline of the post's foot, pushed out by about half a stone.
                    float reach = Mathf.Min(half.x / Mathf.Max(.01f, Mathf.Abs(direction.x)), half.z / Mathf.Max(.01f, Mathf.Abs(direction.z)));
                    var size = new Vector3(Range(random, .3f, .42f), Range(random, .2f, .3f), Range(random, .24f, .34f));
                    var centre = middle + direction * (reach + size.z * .2f) + Vector3.up * (size.y * .3f - .05f);
                    Rock(random, centre, Quaternion.Euler(Range(random, -8, 8), -angle * Mathf.Rad2Deg + 90, Range(random, -8, 8)), size,
                        positions, normals, uvs, triangles);
                }
            }
            var stone = MuseumWorldSurroundings.Delivered("M_Piedra");
            if (stone == null || positions.Count == 0) return;
            MeshObject(parent, FootingsName, Build(FootingsName, positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs, normals), stone, true);
        }

        /// <summary>A frame of dark wood around the name board: four bars standing a little proud of it, and a ledge under it.</summary>
        private static void Frame(Transform parent, Bounds board, Transform structure)
        {
            var wood = MuseumWorldSurroundings.Delivered("M_Corteza_Sauce");
            if (wood == null) return;
            const float bar = .1f, depth = .14f;
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            var centre = board.center;
            var size = board.size;
            void Bar(Vector3 offset, Vector3 extent) => Box(positions, normals, uvs, triangles, centre + offset, extent);
            float width = size.x + bar * 2, height = size.y + bar * 2;
            Bar(new Vector3(0, size.y * .5f + bar * .5f, 0), new Vector3(width, bar, depth));
            Bar(new Vector3(0, -size.y * .5f - bar * .5f, 0), new Vector3(width, bar, depth));
            Bar(new Vector3(-size.x * .5f - bar * .5f, 0, 0), new Vector3(bar, height, depth));
            Bar(new Vector3(size.x * .5f + bar * .5f, 0, 0), new Vector3(bar, height, depth));
            Bar(new Vector3(0, -size.y * .5f - bar - .02f, -.02f), new Vector3(width + .06f, .04f, depth + .06f));
            MeshObject(parent, FrameName, Build(FrameName, positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs, normals), wood, true);
        }

        /// <summary>An upright box with its own faces, its texture laid about a metre per tile.</summary>
        private static void Box(List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int> triangles, Vector3 middle, Vector3 size)
        {
            var half = size * .5f;
            foreach (var normal in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var u = Mathf.Abs(normal.x) > 0 ? Vector3.forward : Vector3.right;
                var v = Vector3.Cross(normal, u);
                int start = positions.Count;
                foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                {
                    var local = Vector3.Scale(normal + u * a + v * b, half);
                    positions.Add(middle + local);
                    normals.Add(normal);
                    uvs.Add(new Vector2(Vector3.Dot(local, u), Vector3.Dot(local, v)) * .9f);
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }
        }

        private static float Range(System.Random random, float from, float to) => from + (float)random.NextDouble() * (to - from);

        /// <summary>A rough stone: an icosahedron pushed in and out at random, its underside flattened, its faces flat.</summary>
        private static void Rock(System.Random random, Vector3 centre, Quaternion turn, Vector3 size, List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var corners = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            }.Select(corner =>
            {
                var point = corner.normalized * Range(random, .8f, 1.15f);
                point.y = Mathf.Max(point.y, -.5f);
                return centre + turn * Vector3.Scale(point * .5f, size);
            }).ToArray();
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int index = 0; index < faces.Length; index += 3)
            {
                var a = corners[faces[index]]; var b = corners[faces[index + 1]]; var c = corners[faces[index + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                int start = positions.Count;
                foreach (var point in new[] { a, b, c })
                {
                    positions.Add(point);
                    normals.Add(normal);
                    uvs.Add(new Vector2(point.x + point.y, point.z + point.y) * 2.2f);
                }
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
        }
    }
}
