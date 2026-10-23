using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The ground of a fire, built from its own geometry so nothing depends on the museum's meshes (on a headset the
    /// museum's static pieces share one combined mesh, and a copy of one would bring the whole room with it): a ring of
    /// rough stones sunk a little in the earth, a bed of ash inside it and split logs leaning together over the flames,
    /// their feet in the ash. Seeded, so it is the same every time; its materials are the museum's own when the scene has
    /// them. Used by the hearth of the entry set.
    /// </summary>
    public static class MuseumFirePit
    {
        public const string StonesName = "FirePitStones", AshName = "FirePitAsh", LogsName = "FirePitLogs";
        /// <summary>Distance from the centre to the middle of the stones, in metres.</summary>
        public const float RingRadius = .56f;
        public const int StoneCount = 12;

        /// <summary>Builds the ring, the ash and the logs at a spot on the floor of <paramref name="parent"/>.</summary>
        public static Transform Build(Transform parent, Vector3 spot, int seed = 20261023)
        {
            var random = new System.Random(seed);
            var pit = new GameObject("FirePit").transform;
            pit.SetParent(parent, false);
            pit.localPosition = spot;
            Piece(pit, StonesName, Stones(random), "M_Piedra", new Color(.34f, .32f, .3f));
            Piece(pit, AshName, Ash(random), null, new Color(.055f, .05f, .047f));
            Piece(pit, LogsName, Logs(random), "M_Corteza_Sauce", new Color(.27f, .18f, .12f));
            return pit;
        }

        private static void Piece(Transform pit, string name, Mesh mesh, string museumMaterial, Color fallback)
        {
            var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(pit, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var found = museumMaterial == null ? null : Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SelectMany(candidate => candidate.sharedMaterials).FirstOrDefault(material => material != null && material.name == museumMaterial);
            if (found != null) { renderer.sharedMaterial = found; return; }
            renderer.sharedMaterial = DefaultMaterial();
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", fallback);
            renderer.SetPropertyBlock(tint);
        }

        private static Material defaultMaterial;
        /// <summary>The render pipeline's own default material, the one a primitive gets, so the build already carries its shader.</summary>
        private static Material DefaultMaterial()
        {
            if (defaultMaterial != null) return defaultMaterial;
            var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            defaultMaterial = probe.GetComponent<MeshRenderer>().sharedMaterial;
            if (Application.isPlaying) Object.Destroy(probe); else Object.DestroyImmediate(probe);
            return defaultMaterial;
        }

        private static float Range(System.Random random, float from, float to) => from + (float)random.NextDouble() * (to - from);

        /// <summary>A ring of rough stones, each a little different and sunk a few centimetres in the earth.</summary>
        public static Mesh Stones(System.Random random)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            for (int index = 0; index < StoneCount; index++)
            {
                float angle = (index + Range(random, -.18f, .18f)) * Mathf.PI * 2 / StoneCount;
                float radius = RingRadius + Range(random, -.03f, .03f);
                var size = new Vector3(Range(random, .2f, .26f), Range(random, .12f, .17f), Range(random, .15f, .2f));
                var centre = new Vector3(Mathf.Cos(angle) * radius, size.y * .5f - .035f, Mathf.Sin(angle) * radius);
                // Each stone lies along the ring, turned a little at random.
                var turn = Quaternion.Euler(Range(random, -6, 6), -angle * Mathf.Rad2Deg + 90 + Range(random, -18, 18), Range(random, -6, 6));
                Rock(random, centre, turn, size, positions, normals, uvs, triangles);
            }
            return Mesh("FirePitStones", positions, normals, uvs, triangles);
        }

        /// <summary>A rough stone: a twice-divided icosahedron pushed in and out at random, its underside flattened.</summary>
        private static void Rock(System.Random random, Vector3 centre, Quaternion turn, Vector3 size, List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var (vertices, faces) = Sphere(1);
            var bumps = vertices.Select(_ => Range(random, .82f, 1.14f)).ToArray();
            var shaped = new Vector3[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                var point = vertices[index] * bumps[index];
                // The underside rests flat on the earth.
                point.y = Mathf.Max(point.y, -.55f);
                shaped[index] = centre + turn * Vector3.Scale(point * .5f, size);
            }
            // Flat faces read as broken stone; every face keeps its own corners and normal.
            foreach (var (a, b, c) in faces)
            {
                int start = positions.Count;
                var normal = Vector3.Cross(shaped[b] - shaped[a], shaped[c] - shaped[a]).normalized;
                foreach (var index in new[] { a, b, c })
                {
                    positions.Add(shaped[index]);
                    normals.Add(normal);
                    uvs.Add(new Vector2(shaped[index].x + shaped[index].y, shaped[index].z + shaped[index].y) * 2.2f);
                }
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
        }

        /// <summary>A unit icosphere: its corners and its faces, wound outwards.</summary>
        private static (Vector3[] vertices, List<(int, int, int)> faces) Sphere(int divisions)
        {
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var vertices = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            }.Select(vertex => vertex.normalized).ToList();
            var faces = new List<(int, int, int)>
            {
                (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
                (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
            };
            for (int step = 0; step < divisions; step++)
            {
                var middles = new Dictionary<long, int>();
                int Middle(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                    if (middles.TryGetValue(key, out var found)) return found;
                    vertices.Add(((vertices[a] + vertices[b]) * .5f).normalized);
                    middles[key] = vertices.Count - 1;
                    return vertices.Count - 1;
                }
                var finer = new List<(int, int, int)>();
                foreach (var (a, b, c) in faces)
                {
                    int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                    finer.Add((a, ab, ca)); finer.Add((b, bc, ab)); finer.Add((c, ca, bc)); finer.Add((ab, bc, ca));
                }
                faces = finer;
            }
            // As listed, every face's cross product points outwards: the side Unity draws.
            return (vertices.ToArray(), faces);
        }

        /// <summary>A handful of coals lying close together under the flames, within a quarter of a metre of the middle.</summary>
        public static Mesh Embers(System.Random random)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            for (int index = 0; index < 11; index++)
            {
                float angle = Range(random, 0, Mathf.PI * 2), distance = Mathf.Sqrt(Range(random, 0, 1)) * .19f;
                var size = new Vector3(Range(random, .05f, .1f), Range(random, .03f, .05f), Range(random, .05f, .09f));
                var centre = new Vector3(Mathf.Cos(angle) * distance, size.y * .5f - .006f, Mathf.Sin(angle) * distance);
                Rock(random, centre, Quaternion.Euler(0, Range(random, 0, 360), 0), size, positions, normals, uvs, triangles);
            }
            return Mesh("FireEmbers", positions, normals, uvs, triangles);
        }

        /// <summary>A bed of ash inside the ring, its edge uneven, a hair above the earth.</summary>
        public static Mesh Ash(System.Random random)
        {
            const int sides = 28;
            var positions = new List<Vector3> { new Vector3(0, .008f, 0) };
            var normals = new List<Vector3> { Vector3.up };
            var uvs = new List<Vector2> { new Vector2(.5f, .5f) };
            var triangles = new List<int>();
            for (int index = 0; index < sides; index++)
            {
                float angle = index * Mathf.PI * 2 / sides;
                float radius = RingRadius - .14f + Range(random, -.035f, .02f);
                positions.Add(new Vector3(Mathf.Cos(angle) * radius, .004f, Mathf.Sin(angle) * radius));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(.5f + Mathf.Cos(angle) * .5f, .5f + Mathf.Sin(angle) * .5f));
                triangles.Add(0); triangles.Add(1 + (index + 1) % sides); triangles.Add(1 + index);
            }
            return Mesh("FirePitAsh", positions, normals, uvs, triangles);
        }

        /// <summary>Split logs leaning together over the centre, their feet in the ash and their tops crossing.</summary>
        public static Mesh Logs(System.Random random)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            const int count = 5;
            for (int index = 0; index < count; index++)
            {
                float angle = (index * 360f / count + 15 + Range(random, -9, 9)) * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Range(random, .3f, .36f) + Vector3.up * .035f;
                // The tops cross a little past the middle, so the logs rest on each other instead of meeting in one point.
                var top = -new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .1f + Vector3.up * Range(random, .34f, .41f);
                Log(foot, top, Range(random, .045f, .06f), positions, normals, uvs, triangles);
            }
            // Two logs lying across the ash, half burnt down.
            Log(new Vector3(-.26f, .04f, -.12f), new Vector3(.2f, .05f, -.2f), .05f, positions, normals, uvs, triangles);
            Log(new Vector3(.08f, .04f, .26f), new Vector3(.27f, .045f, -.05f), .045f, positions, normals, uvs, triangles);
            return Mesh("FirePitLogs", positions, normals, uvs, triangles);
        }

        /// <summary>A log of seven sides between two points, thinner at its upper end, closed at both ends.</summary>
        private static void Log(Vector3 from, Vector3 to, float radius, List<Vector3> positions, List<Vector3> normals,
            List<Vector2> uvs, List<int> triangles)
        {
            const int sides = 7;
            var axis = (to - from).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(side, axis);
            float length = (to - from).magnitude;
            int start = positions.Count;
            for (int index = 0; index <= sides; index++)
            {
                float angle = index * Mathf.PI * 2 / sides;
                var outward = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                positions.Add(from + outward * radius);
                positions.Add(to + outward * radius * .8f);
                normals.Add(outward); normals.Add(outward);
                uvs.Add(new Vector2(index / (float)sides, 0)); uvs.Add(new Vector2(index / (float)sides, length * 2));
            }
            for (int index = 0; index < sides; index++)
            {
                int a = start + index * 2, b = a + 1, c = a + 2, d = a + 3;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(c); triangles.Add(b); triangles.Add(d);
            }
            foreach (var (end, scale, facing) in new[] { (from, 1f, -axis), (to, .8f, axis) })
            {
                int centre = positions.Count;
                positions.Add(end); normals.Add(facing); uvs.Add(new Vector2(.5f, .5f));
                for (int index = 0; index < sides; index++)
                {
                    float angle = index * Mathf.PI * 2 / sides;
                    positions.Add(end + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * scale);
                    normals.Add(facing);
                    uvs.Add(new Vector2(.5f + Mathf.Cos(angle) * .5f, .5f + Mathf.Sin(angle) * .5f));
                }
                for (int index = 0; index < sides; index++)
                {
                    int a = centre + 1 + index, b = centre + 1 + (index + 1) % sides;
                    if (facing == axis) { triangles.Add(centre); triangles.Add(b); triangles.Add(a); }
                    else { triangles.Add(centre); triangles.Add(a); triangles.Add(b); }
                }
            }
        }

        private static Mesh flat;
        /// <summary>
        /// A unit square lying flat and facing up, with white vertex colours: the particle shaders that draw the fire's
        /// light and the soft shadows multiply by the vertex colour, which a primitive quad does not carry.
        /// </summary>
        public static Mesh FlatPatch()
        {
            if (flat != null) return flat;
            flat = new Mesh { name = "FlatPatch" };
            flat.SetVertices(new[] { new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f) });
            flat.SetNormals(Enumerable.Repeat(Vector3.up, 4).ToList());
            flat.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            flat.SetColors(Enumerable.Repeat(Color.white, 4).ToList());
            flat.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            flat.RecalculateBounds();
            return flat;
        }

        /// <summary>A soft round patch on the floor of <paramref name="parent"/>: light when the material adds, shade when it blends.</summary>
        public static MeshRenderer Patch(Transform parent, string name, Vector3 position, float size, Material material, Color tint)
        {
            var patch = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            patch.transform.SetParent(parent, false);
            patch.transform.localPosition = position;
            patch.transform.localScale = new Vector3(size, 1, size);
            patch.GetComponent<MeshFilter>().sharedMesh = FlatPatch();
            var renderer = patch.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (material != null) renderer.sharedMaterial = material;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", tint);
            renderer.SetPropertyBlock(block);
            return renderer;
        }

        private static Mesh Mesh(string name, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
