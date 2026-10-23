using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static MusiyoBetsknate.Editor.MuseumWorldDressing;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The museum's entrance and edges (owner, 2026-10-18): a forecourt at the door, a soft band of earth, grass, shrubs
    /// and stones where the buildings meet the ground, and wooden benches in the forecourt, the welcome room, the
    /// courtyard corridor and the instruments room. Built with the rest of <see cref="MuseumWorldDressing"/>; plain wood
    /// and stone, nothing carved or painted.
    /// </summary>
    public static class MuseumWorldSurroundings
    {
        /// <summary>A delivered material of the authoring set; a build swaps it for its profile's namesake.</summary>
        internal static Material Delivered(string name)
            => AssetDatabase.LoadAssetAtPath<Material>(MuseumDeliveryManifest.MaterialPath(MuseumDeliveryAssets.AuthoringProfile, name));
        private static readonly Vector2 WelcomeCentre = new Vector2(0, -20);
        /// <summary>Centre and radius of the forecourt in front of the welcome room's door.</summary>
        private static readonly Vector2 ForecourtCentre = new Vector2(0, -28.9f);
        private const float ForecourtRadius = 4.4f;
        private const float BandWidth = 1.5f;
        private const float GroundTop = -.02f;

        /// <summary>A building seen from outside: a circle or a rectangle on the ground.</summary>
        private struct Footprint
        {
            public bool Round;
            public Vector2 Centre;
            public float Radius;
            public Rect Box;
            public bool Planted;

            /// <summary>Metres from the outline, negative inside.</summary>
            public float Distance(Vector2 point)
            {
                if (Round) return Vector2.Distance(point, Centre) - Radius;
                var outside = new Vector2(Mathf.Max(Box.xMin - point.x, point.x - Box.xMax), Mathf.Max(Box.yMin - point.y, point.y - Box.yMax));
                return outside.x > 0 || outside.y > 0 ? new Vector2(Mathf.Max(outside.x, 0), Mathf.Max(outside.y, 0)).magnitude : Mathf.Max(outside.x, outside.y);
            }
        }

        public static void Apply(Transform root, Dictionary<string, Transform> objects, System.Random random)
        {
            var parent = new GameObject("Surroundings").transform;
            parent.SetParent(root, false);
            var footprints = Footprints(objects);
            var obstacles = Obstacles(root);
            BuildGroundBand(parent, footprints);
            BuildForecourt(parent, footprints, random);
            BuildPlanting(parent, footprints, random);
            var benches = BuildBenches(parent, obstacles, Doorways(objects), random);
            ClearStoolsBefore(benches);
        }

        /// <summary>A bench keeps this much floor, in metres, between itself and a doorway or passage.</summary>
        public const float DoorwayClearance = 1.0f;
        /// <summary>A stool closer than this to a bench stands in the way of whoever sits on the bench.</summary>
        public const float StoolClearance = .35f;
        /// <summary>Root of the stools placed in the saved scene (MuseumStoolPlacement).</summary>
        private const string StoolsRoot = "MuseumStools";

        /// <summary>Thresholds and passages between rooms, which every seat must leave free.</summary>
        internal static List<Bounds> Doorways(Dictionary<string, Transform> objects)
            => objects.Where(entry => (entry.Key.StartsWith("Umbral_") || entry.Key.StartsWith("Pasaje_")) && entry.Value.GetComponent<Renderer>() != null)
                .Select(entry => entry.Value.GetComponent<Renderer>().bounds).ToList();

        /// <summary>Distance on the floor between two boxes, zero when they overlap.</summary>
        internal static float FloorGap(Bounds a, Bounds b)
        {
            float x = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float z = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>
        /// A stool of the saved scene standing against a bench (owner, 22/10: a stool in front of the welcome room's bench)
        /// is taken away before the scene is merged, so whoever sits on the bench has room for their legs.
        /// </summary>
        private static void ClearStoolsBefore(List<Bounds> benches)
        {
            var root = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(item => item.parent == null && item.name == StoolsRoot);
            if (root == null) return;
            foreach (Transform stool in root)
            {
                var renderers = stool.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                if (!benches.Any(bench => FloorGap(bench, bounds) < StoolClearance)) continue;
                stool.gameObject.SetActive(false);
                Debug.Log("Museum world: stool " + stool.name + " stood against a bench and was taken away.");
            }
        }

        private static Bounds? Union(Dictionary<string, Transform> objects, System.Func<string, bool> pick)
        {
            Bounds? all = null;
            foreach (var entry in objects)
            {
                if (!pick(entry.Key)) continue;
                var renderer = entry.Value.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (all == null) all = renderer.bounds;
                else { var grown = all.Value; grown.Encapsulate(renderer.bounds); all = grown; }
            }
            return all;
        }

        private static List<Footprint> Footprints(Dictionary<string, Transform> objects)
        {
            var footprints = new List<Footprint>();
            float welcome = objects.TryGetValue("Bienvenida_Zocalo_Piedra", out var plinth) && plinth.GetComponent<Renderer>() != null
                ? plinth.GetComponent<Renderer>().bounds.extents.x : 6.32f;
            footprints.Add(new Footprint { Round = true, Centre = WelcomeCentre, Radius = welcome, Planted = true });
            foreach (var (prefix, planted) in new[] { ("Personajes_Muro", true), ("Instrumentos_Muro", true), ("Fogon_Muro", true), ("Mirador_", false), ("Pasaje_", false) })
            {
                if (prefix == "Pasaje_")
                {
                    foreach (var passage in objects.Where(entry => entry.Key.StartsWith(prefix) && entry.Value.GetComponent<Renderer>() != null))
                        footprints.Add(Box(passage.Value.GetComponent<Renderer>().bounds, false));
                    continue;
                }
                var bounds = Union(objects, name => name.StartsWith(prefix));
                if (bounds != null) footprints.Add(Box(bounds.Value, planted));
            }
            return footprints;
        }

        private static Footprint Box(Bounds bounds, bool planted)
            => new Footprint { Box = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z), Planted = planted };

        private static float BuildingDistance(List<Footprint> footprints, Vector2 point)
            => footprints.Min(footprint => footprint.Distance(point));

        private static float PlantedDistance(List<Footprint> footprints, Vector2 point)
            => footprints.Where(footprint => footprint.Planted).Min(footprint => footprint.Distance(point));

        /// <summary>Inside the forecourt: near its centre and outside the welcome room.</summary>
        private static bool InForecourt(Vector2 point, float margin = 0)
            => Vector2.Distance(point, ForecourtCentre) < ForecourtRadius - margin && Vector2.Distance(point, WelcomeCentre) > 6.3f + margin;

        /// <summary>On the arrival path or in its maize garden.</summary>
        private static bool OnArrival(Vector2 point, float margin = 0)
            => point.y < -26 + margin && Mathf.Abs(point.x) < 1.1f + margin;

        /// <summary>
        /// The ground near the walls and around the forecourt turns from grass to earth and leaf litter. The band uses the
        /// valley ground's material and colour noise, so its outer edge is the same grass as the ground beside it.
        /// </summary>
        private static void BuildGroundBand(Transform parent, List<Footprint> footprints)
        {
            const float cell = .5f;
            var positions = new List<Vector3>(); var colours = new List<Color>(); var triangles = new List<int>();
            var index = new Dictionary<(int, int), int>();
            float Wear(Vector2 point)
            {
                float building = PlantedDistance(footprints, point);
                float forecourt = Vector2.Distance(point, ForecourtCentre) - ForecourtRadius;
                float wall = 1 - Smooth(0, BandWidth, building);
                float yard = 1 - Smooth(-.6f, 1.2f, forecourt);
                return Mathf.Clamp01(Mathf.Max(wall * (.75f + .25f * Noise(point.x, point.y, .6f, 2.4f)), yard));
            }
            int Vertex(int column, int row, float x0, float z0)
            {
                if (index.TryGetValue((column, row), out int found)) return found;
                var point = new Vector2(x0 + column * cell, z0 + row * cell);
                index[(column, row)] = positions.Count;
                positions.Add(new Vector3(point.x, GroundTop + .01f, point.y));
                colours.Add(new Color(Wear(point), Noise(point.x, point.y, .03f, 9.2f), 0, 1));
                return positions.Count - 1;
            }
            float xMin = -17, xMax = 30, zMin = -35, zMax = 27;
            for (int row = 0; row < (zMax - zMin) / cell; row++)
                for (int column = 0; column < (xMax - xMin) / cell; column++)
                {
                    var middle = new Vector2(xMin + (column + .5f) * cell, zMin + (row + .5f) * cell);
                    if (Wear(middle) < .02f && Wear(middle + Vector2.one * cell * .5f) < .02f && Wear(middle - Vector2.one * cell * .5f) < .02f) continue;
                    if (BuildingDistance(footprints, middle) < -cell || OnArrival(middle, -.05f)) continue;
                    int a = Vertex(column, row, xMin, zMin), b = Vertex(column + 1, row, xMin, zMin);
                    int c = Vertex(column, row + 1, xMin, zMin), d = Vertex(column + 1, row + 1, xMin, zMin);
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            MeshObject(parent, "GroundBand", Build("GroundBand", positions, colours, triangles, null, Enumerable.Repeat(Vector3.up, positions.Count).ToList()), GroundMaterial);
        }

        /// <summary>
        /// Tamped earth in front of the door, flagstones laid in rings around the end of the path, a border of low
        /// stones, two pots with ferns by the door posts.
        /// </summary>
        private static void BuildForecourt(Transform parent, List<Footprint> footprints, System.Random random)
        {
            var earth = Delivered("M_Tierra_Apisonada");
            var flagstone = Delivered("M_Piedra_DS");
            var rock = Delivered("M_Piedra");

            // Earth: a disc of cells, cut by the round wall and left open where the path runs.
            var positions = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            const float cell = .4f;
            for (float x = -ForecourtRadius; x < ForecourtRadius; x += cell)
                for (float z = ForecourtCentre.y - ForecourtRadius; z < ForecourtCentre.y + ForecourtRadius; z += cell)
                {
                    var middle = new Vector2(x + cell * .5f, z + cell * .5f);
                    if (Vector2.Distance(middle, ForecourtCentre) > ForecourtRadius - .3f || Vector2.Distance(middle, WelcomeCentre) < 6.1f) continue;
                    int start = positions.Count;
                    foreach (var corner in new[] { new Vector2(x, z), new Vector2(x + cell, z), new Vector2(x, z + cell), new Vector2(x + cell, z + cell) })
                    {
                        positions.Add(new Vector3(corner.x, GroundTop + .02f, corner.y));
                        uvs.Add(corner * .35f);
                    }
                    triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
                }
            if (earth != null)
                MeshObject(parent, "ForecourtEarth", Build("ForecourtEarth", positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs,
                    Enumerable.Repeat(Vector3.up, positions.Count).ToList()), earth);

            // Flagstones in rings; the path keeps its own slabs down the middle.
            positions.Clear(); uvs.Clear(); triangles.Clear();
            var normals = new List<Vector3>();
            foreach (float ring in new[] { 1.4f, 2.05f, 2.7f, 3.35f, 3.95f })
            {
                int count = Mathf.RoundToInt(2 * Mathf.PI * ring / .68f);
                float turn = (float)random.NextDouble();
                for (int stone = 0; stone < count; stone++)
                {
                    float angle = (stone + turn + (float)(random.NextDouble() - .5) * .2f) * Mathf.PI * 2 / count;
                    var centre = ForecourtCentre + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * ring;
                    if (Vector2.Distance(centre, WelcomeCentre) < 6.75f || OnArrival(centre, .45f) || Vector2.Distance(centre, ForecourtCentre) > ForecourtRadius - .3f) continue;
                    Slab(positions, uvs, normals, triangles, new Vector3(centre.x, GroundTop + .02f, centre.y), .33f + (float)random.NextDouble() * .06f, angle, random);
                }
            }
            if (flagstone != null) MeshObject(parent, "ForecourtFlagstones", Build("ForecourtFlagstones", positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs, normals), flagstone);

            // Low stones along the edge.
            positions.Clear(); uvs.Clear(); triangles.Clear(); normals.Clear();
            int around = Mathf.RoundToInt(2 * Mathf.PI * ForecourtRadius / .5f);
            for (int step = 0; step < around; step++)
            {
                float angle = step * Mathf.PI * 2 / around;
                var point = ForecourtCentre + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * (ForecourtRadius - .15f);
                if (Vector2.Distance(point, WelcomeCentre) < 6.6f || OnArrival(point, .3f)) continue;
                Rock(positions, uvs, normals, triangles, new Vector3(point.x, GroundTop, point.y), .13f + (float)random.NextDouble() * .09f, random);
            }
            if (rock != null) MeshObject(parent, "ForecourtBorder", Build("ForecourtBorder", positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs, normals), rock);

            var pots = PotMaker.Load();
            if (pots != null)
                foreach (float side in new[] { -1f, 1f })
                    pots.Place(parent, new Vector3(side * 2.25f, GroundTop + .02f, -26.95f), .62f, "DoorPot", random);
        }

        /// <summary>An irregular flat stone: a jittered polygon with a thin side.</summary>
        private static void Slab(List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals, List<int> triangles, Vector3 centre, float radius, float turn, System.Random random)
        {
            const int corners = 7;
            const float thickness = .045f;
            var outline = new Vector3[corners];
            for (int corner = 0; corner < corners; corner++)
            {
                float angle = turn + corner * Mathf.PI * 2 / corners;
                float reach = radius * (.8f + (float)random.NextDouble() * .3f);
                outline[corner] = centre + new Vector3(Mathf.Cos(angle) * reach, 0, Mathf.Sin(angle) * reach * .8f);
            }
            int top = positions.Count;
            positions.Add(centre + Vector3.up * thickness); uvs.Add(new Vector2(centre.x, centre.z) * .7f); normals.Add(Vector3.up);
            foreach (var point in outline) { positions.Add(point + Vector3.up * thickness); uvs.Add(new Vector2(point.x, point.z) * .7f); normals.Add(Vector3.up); }
            for (int corner = 0; corner < corners; corner++) triangles.AddRange(new[] { top, top + 1 + (corner + 1) % corners, top + 1 + corner });
            for (int corner = 0; corner < corners; corner++)
            {
                var a = outline[corner]; var b = outline[(corner + 1) % corners];
                var outward = Vector3.Cross(b - a, Vector3.up).normalized * -1;
                int start = positions.Count;
                foreach (var point in new[] { a, b, a + Vector3.up * thickness, b + Vector3.up * thickness })
                {
                    positions.Add(point); uvs.Add(new Vector2(point.x + point.y, point.z) * .7f); normals.Add(outward);
                }
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
        }

        /// <summary>A small rounded stone half sunk in the ground: a jittered octahedron.</summary>
        private static void Rock(List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals, List<int> triangles, Vector3 foot, float size, System.Random random)
        {
            var points = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var faces = new[] { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            float yaw = (float)random.NextDouble() * 360;
            var shape = Quaternion.Euler(0, yaw, 0);
            var placed = points.Select(point => foot + shape * Vector3.Scale(point, new Vector3(size * (1 + (float)random.NextDouble() * .5f), size * .55f, size * (.8f + (float)random.NextDouble() * .4f)))).ToArray();
            for (int face = 0; face < faces.Length; face += 3)
            {
                var a = placed[faces[face]]; var b = placed[faces[face + 1]]; var c = placed[faces[face + 2]];
                var normal = Vector3.Cross(b - a, c - a).normalized;
                int start = positions.Count;
                foreach (var point in new[] { a, b, c }) { positions.Add(point); normals.Add(normal); uvs.Add(new Vector2(point.x + point.y, point.z) * 1.5f); }
                triangles.AddRange(new[] { start, start + 1, start + 2 });
            }
        }

        /// <summary>
        /// Grass thick along the walls, low shrubs every few metres, a few stones; none in front of a door, on the
        /// forecourt or on the path.
        /// </summary>
        private static void BuildPlanting(Transform parent, List<Footprint> footprints, System.Random random)
        {
            var grassPositions = new List<Vector3>(); var grassColours = new List<Color>(); var grassTriangles = new List<int>();
            var shrubPositions = new List<Vector3>(); var shrubColours = new List<Color>(); var shrubUvs = new List<Vector2>();
            var shrubNormals = new List<Vector3>(); var shrubTriangles = new List<int>();
            var rockPositions = new List<Vector3>(); var rockUvs = new List<Vector2>(); var rockNormals = new List<Vector3>(); var rockTriangles = new List<int>();
            int shrubs = 0;
            for (int attempt = 0; attempt < 9000; attempt++)
            {
                var point = new Vector2(-17 + (float)random.NextDouble() * 47, -35 + (float)random.NextDouble() * 62);
                float distance = PlantedDistance(footprints, point);
                if (distance < .15f || distance > BandWidth + .6f || BuildingDistance(footprints, point) < .15f) continue;
                if (InForecourt(point, -.4f) || OnArrival(point, .5f)) continue;
                var foot = new Vector3(point.x, GroundTop, point.y);
                if (distance < BandWidth * .9f && random.NextDouble() < .55)
                    AddTuft(grassPositions, grassColours, grassTriangles, foot, .8f + (float)random.NextDouble() * .7f, random);
                // Shrubs stand close to the walls and only now and then.
                if (distance > .4f && distance < 1.2f && random.NextDouble() < .1)
                {
                    float size = .55f + (float)random.NextDouble() * .6f;
                    float turn = (float)random.NextDouble() * Mathf.PI;
                    float shade = .8f + (float)random.NextDouble() * .25f;
                    for (int card = 0; card < 3; card++)
                    {
                        float angle = turn + card * Mathf.PI / 3;
                        var right = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * size;
                        Card(shrubPositions, shrubColours, shrubUvs, shrubNormals, shrubTriangles, foot + Vector3.up * size * .85f, right, Vector3.up * size * .85f,
                            random.Next(4), new Color(shade * .9f, shade, shade * .85f, 1), foot + Vector3.up * size * .3f, .75f);
                    }
                    shrubs++;
                }
                if (distance > .2f && distance < 1.6f && random.NextDouble() < .012)
                    Rock(rockPositions, rockUvs, rockNormals, rockTriangles, foot, .1f + (float)random.NextDouble() * .12f, random);
            }
            MeshObject(parent, "WallGrass", GrassMesh("WallGrass", grassPositions, grassColours, grassTriangles), GrassMaterial());
            MeshObject(parent, "WallShrubs", Build("WallShrubs", shrubPositions, shrubColours, shrubTriangles, shrubUvs, shrubNormals), UnderstoreyMaterial);
            var rock = Delivered("M_Piedra");
            if (rock != null) MeshObject(parent, "WallStones", Build("WallStones", rockPositions, Enumerable.Repeat(Color.white, rockPositions.Count).ToList(), rockTriangles, rockUvs, rockNormals), rock);
            Debug.Log("Museum world surroundings: " + shrubs + " shrubs, " + (grassTriangles.Count + shrubTriangles.Count + rockTriangles.Count) / 3 + " triangles.");
        }

        /// <summary>
        /// Plain plank benches: two in the forecourt, two in the welcome room facing the tree, along the courtyard
        /// corridor facing the courtyard, one in the instruments room. A bench is left out where something stands or where
        /// it would stand within <see cref="DoorwayClearance"/> of a doorway; the footprints of those placed are returned.
        /// </summary>
        private static List<Bounds> BuildBenches(Transform parent, List<Bounds> obstacles, List<Bounds> doorways, System.Random random)
        {
            var footprints = new List<Bounds>();
            var wood = Delivered("M_Madera");
            if (wood == null) return footprints;
            var seats = new List<(Vector3 centre, float facing)>();
            foreach (float side in new[] { -1f, 1f })
            {
                float angle = side * 68 * Mathf.Deg2Rad;
                var point = ForecourtCentre + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 3.2f;
                seats.Add((new Vector3(point.x, GroundTop + .02f, point.y), Mathf.Atan2(ForecourtCentre.x - point.x, ForecourtCentre.y - point.y) * Mathf.Rad2Deg));
            }
            foreach (float degrees in new[] { 90f, 270f })
            {
                var point = WelcomeCentre + new Vector2(Mathf.Sin(degrees * Mathf.Deg2Rad), Mathf.Cos(degrees * Mathf.Deg2Rad)) * 5.1f;
                seats.Add((new Vector3(point.x, 0, point.y), degrees + 180));
            }
            foreach (float z in new[] { -5f, 5f })
            {
                seats.Add((new Vector3(-12.3f, 0, z), 90));
                seats.Add((new Vector3(12.3f, 0, z), -90));
            }
            foreach (float x in new[] { -6f, 6f })
            {
                seats.Add((new Vector3(x, 0, 10.3f), 180));
                seats.Add((new Vector3(x, 0, -10.3f), 0));
            }
            seats.Add((new Vector3(20.5f, 0, -9.4f), 0));

            var positions = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            int placed = 0;
            var refused = new List<string>();
            foreach (var (centre, facing) in seats)
            {
                var turn = Quaternion.Euler(0, facing, 0);
                float length = 1.7f;
                var footprint = new Bounds(centre + Vector3.up * .23f, Vector3.zero);
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                    footprint.Encapsulate(centre + Vector3.up * .23f + turn * Vector3.Scale(corner, new Vector3(length * .5f, 0, .19f)));
                // Owner, 22/10: the corridor bench before the doorway to the instruments room was in the way.
                if (!IsFree(obstacles, centre, turn, new Vector3(length + .1f, .8f, .5f))
                    || doorways.Any(doorway => FloorGap(doorway, footprint) < DoorwayClearance))
                {
                    refused.Add(centre.ToString("F1"));
                    continue;
                }
                footprints.Add(footprint);
                // Seat plank, two slab legs, a low stretcher.
                AddBox(positions, uvs, normals, triangles, centre, turn, new Vector3(0, .425f, 0), new Vector3(length, .07f, .38f));
                foreach (float end in new[] { -1f, 1f })
                    AddBox(positions, uvs, normals, triangles, centre, turn, new Vector3(end * (length * .5f - .2f), .195f, 0), new Vector3(.08f, .39f, .32f));
                AddBox(positions, uvs, normals, triangles, centre, turn, new Vector3(0, .12f, 0), new Vector3(length - .48f, .06f, .07f));
                var solid = new GameObject("Bench").AddComponent<BoxCollider>();
                solid.transform.SetParent(parent, false);
                solid.transform.SetPositionAndRotation(centre + Vector3.up * .23f, turn);
                solid.size = new Vector3(length, .46f, .38f);
                placed++;
            }
            MeshObject(parent, "Benches", Build("Benches", positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs, normals), wood, true);
            Debug.Log("Museum world benches: " + placed + " of " + seats.Count + "; no room at " + string.Join(" ", refused) + ".");
            return footprints;
        }

        /// <summary>A box with its own faces and texture laid along its length (about a metre per tile).</summary>
        private static void AddBox(List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals, List<int> triangles,
            Vector3 origin, Quaternion turn, Vector3 middle, Vector3 size)
        {
            var half = size * .5f;
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var normal in axes)
            {
                var u = Mathf.Abs(normal.x) > 0 ? Vector3.forward : Vector3.right;
                var v = Vector3.Cross(normal, u);
                int start = positions.Count;
                foreach (var (a, b) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                {
                    var local = middle + Vector3.Scale(normal + u * a + v * b, half);
                    positions.Add(origin + turn * local);
                    normals.Add(turn * normal);
                    uvs.Add(new Vector2(Vector3.Dot(local, u), Vector3.Dot(local, v)) * .9f);
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }
        }
    }
}
