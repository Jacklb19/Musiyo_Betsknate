using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// The world around and inside the museum (owner approval 2026-10-18), built when the museum scene is processed for
    /// Play mode or a build (<see cref="MuseumWorldBuildStep"/>), so the saved scene and the repository stay light. Valley
    /// ground with relief, ridges and a forest of rendered tree cards around a clearing, clear shallow water in the lagoon,
    /// a leafy crown for the tree of life, grass along the path instead of the loose ferns and flowers, potted and hanging
    /// ferns by the walls, the entrance, surroundings and benches of <see cref="MuseumWorldSurroundings"/> and the finish of the
    /// entrance gate (<see cref="MuseumGateDressing"/>), and the decoration approved from the research
    /// (<see cref="MuseumCulturalDressing"/>). The
    /// delivered pieces it replaces keep their place in the scene with their renderers switched off. Sources and
    /// licences: ThirdParty/third_party_assets.json. Everything is seeded, so each build makes the same world.
    /// </summary>
    public static class MuseumWorldDressing
    {
        private const string PolyHaven = MuseumSetDressing.Root + "/PolyHaven/";
        public const string TreeCards = PolyHaven + "TreeSmall02/forest_tree_cards.png";
        public const string LeafCards = PolyHaven + "TreeSmall02/crown_leaf_cards.png";
        private const string ForestFloor = PolyHaven + "ForrestGround01/forrest_ground_01_diff_1k.jpg";
        private const string LagoonBed = PolyHaven + "BrownMudRocks01/brown_mud_rocks_01_diff_1k.jpg";
        private const string PotTexture = PolyHaven + "PlanterPotClay/textures/planter_pot_clay_diff_1k.jpg";
        /// <summary>Root of the built world (not "MuseumWorld", which holds the music and the companion).</summary>
        public const string RootName = "MuseumWorldDressing";
        private const string GrassTexture = "Assets/_Musiyo/Museum/Textures/leafy_grass_diff_1k_musiyo_1024.jpg";
        private const string FernModel = MuseumSetDressing.LeavesModel;
        private const string FernMaterial = MuseumSetDressing.MaterialFolder + "/PlanterLeaves.mat";
        /// <summary>Delivered pieces the world replaces.</summary>
        public static readonly string[] Replaced = { "Terreno_Valle_Provisional", "Entorno_Arboles_Copas", "Entorno_Arboles_Troncos", "Arbol_Betiyeguagua_Copa" };
        /// <summary>Walkable ground the visitor can stand on outdoors (also listed in MuseumXrConfiguration.json).</summary>
        public const string GroundName = "ValleyGround";

        /// <summary>The museum and its arrival garden; the ground stays flat inside and rises outside.</summary>
        private static readonly Rect Grounds = Rect.MinMaxRect(-30, -66, 36, 32);
        private static readonly Vector2 ValleyCentre = new Vector2(0, -17);
        /// <summary>Metres from the buildings and the arrival garden where an invisible edge keeps the visitor out of the forest.</summary>
        public const float WalkLimit = 18;

        /// <summary>Materials of the current build, shared with the surroundings.</summary>
        internal static Material GroundMaterial { get; private set; }
        internal static Material ForestMaterial { get; private set; }
        internal static Material UnderstoreyMaterial { get; private set; }
        /// <summary>The headset build takes the plant materials' headset twins.</summary>
        internal static bool Headset { get; private set; }

        /// <summary>Builds the world into an open museum scene, replacing any world built before.</summary>
        public static GameObject Apply(Scene scene, bool headset)
        {
            Headset = headset;
            foreach (var old in scene.GetRootGameObjects().Where(item => item.name == RootName)) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var objects = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                .GroupBy(item => item.name).ToDictionary(group => group.Key, group => group.First());

            Hide(objects, Replaced);
            var random = new System.Random(20261018);
            BuildGround(root.transform);
            BuildForest(root.transform, random);
            BuildWalkLimit(root.transform);
            BuildLagoon(objects);
            if (objects.TryGetValue("Arbol_Betiyeguagua_Copa", out var crown)) BuildCrown(root.transform, crown, random);
            ClearLooseArrivalPlants(objects);
            BuildPathGrass(root.transform, random, -33.6f);
            BuildWallPlants(root.transform, objects, random);
            MuseumWorldSurroundings.Apply(root.transform, objects, new System.Random(20261019));
            MuseumGateDressing.Apply(root.transform, objects, new System.Random(20261023));
            MuseumCulturalDressing.Apply(root.transform, new System.Random(20261024));
            return root;
        }

        private static void Hide(Dictionary<string, Transform> objects, params string[] names)
        {
            foreach (var name in names)
                if (objects.TryGetValue(name, out var item))
                    foreach (var renderer in item.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        internal static Texture2D Texture(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        internal static Material NewMaterial(string shader, string name)
        {
            var found = Shader.Find(shader);
            if (found == null) throw new System.InvalidOperationException("Shader missing: " + shader);
            return new Material(found) { name = name };
        }

        internal static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material, bool shadows = false)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return item;
        }

        internal static float Smooth(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3 - 2 * t);
        }

        internal static float Noise(float x, float z, float scale, float seed)
        {
            // Two octaves of Perlin noise in 0..1.
            float first = Mathf.PerlinNoise(x * scale + seed, z * scale - seed);
            float second = Mathf.PerlinNoise(x * scale * 2.7f - seed, z * scale * 2.7f + seed * .5f);
            return Mathf.Clamp01(first * .7f + second * .3f);
        }

        /// <summary>Metres from the museum grounds (0 inside).</summary>
        internal static float OutsideGrounds(float x, float z)
        {
            float dx = Mathf.Max(Grounds.xMin - x, 0, x - Grounds.xMax);
            float dz = Mathf.Max(Grounds.yMin - z, 0, z - Grounds.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Height of the valley ground: flat on the grounds, gentle rises, then the slopes of the valley.</summary>
        public static float GroundHeight(float x, float z)
        {
            float outside = OutsideGrounds(x, z);
            float near = Smooth(6, 60, outside) * (2 + 9 * Noise(x, z, .018f, 3.1f));
            float far = Smooth(60, 170, outside) * (12 + 34 * Noise(x, z, .009f, 7.7f));
            return -.02f + near + far;
        }

        private static void BuildGround(Transform root)
        {
            const float half = 220;
            const int cells = 110;
            var positions = new List<Vector3>();
            var colours = new List<Color>();
            for (int row = 0; row <= cells; row++)
                for (int column = 0; column <= cells; column++)
                {
                    float x = -half + column * (2 * half / cells);
                    float z = ValleyCentre.y - half + row * (2 * half / cells);
                    float outside = OutsideGrounds(x, z);
                    positions.Add(new Vector3(x, GroundHeight(x, z), z));
                    // Lawn only near the buildings; leaf litter spreads in under the trees of the clearing's edge.
                    float forest = Mathf.Clamp01(Smooth(6, 26, ClearingDistance(x, z)) * (.5f + .5f * Noise(x, z, .05f, 1.3f)));
                    colours.Add(new Color(forest, Noise(x, z, .03f, 9.2f), Smooth(50, 150, outside), 1));
                }
            var triangles = new List<int>();
            for (int row = 0; row < cells; row++)
                for (int column = 0; column < cells; column++)
                {
                    int a = row * (cells + 1) + column, b = a + 1, c = a + cells + 1, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = Build("ValleyGround", positions, colours, triangles);

            var material = NewMaterial("Musiyo/World/Valley Ground", "ValleyGround");
            material.SetTexture("_GrassTex", Texture(GrassTexture));
            material.SetTexture("_ForestTex", Texture(ForestFloor));
            material.SetColor("_GrassTint", new Color(.86f, .93f, .78f));
            material.SetColor("_ForestTint", new Color(.75f, .8f, .65f));
            material.SetColor("_FarColor", new Color(.08f, .15f, .1f));
            material.SetFloat("_HazeLimit", .45f);
            material.SetFloat("_TilesPerMetre", .22f);
            GroundMaterial = material;
            MeshObject(root, GroundName, mesh, material).AddComponent<MeshCollider>().sharedMesh = mesh;

            // The ridges that close the valley, beyond the ground.
            positions.Clear(); colours.Clear(); triangles.Clear();
            const int around = 180;
            float[] radii = { 200, 225, 250, 275, 300, 330 };
            for (int step = 0; step <= around; step++)
            {
                float angle = step * Mathf.PI * 2 / around;
                float ridge = 55 + 50 * Noise(Mathf.Cos(angle) * 40, Mathf.Sin(angle) * 40, .05f, 4.4f);
                for (int ring = 0; ring < radii.Length; ring++)
                {
                    float rise = new[] { 0f, .45f, .8f, 1f, .9f, .6f }[ring];
                    float jitter = Noise(step * .7f, ring * 3.1f, .4f, 2.2f);
                    var point = new Vector3(ValleyCentre.x + Mathf.Sin(angle) * radii[ring], 0, ValleyCentre.y + Mathf.Cos(angle) * radii[ring]);
                    point.y = ring == 0 ? 0 : ridge * rise * (.85f + .3f * jitter);
                    positions.Add(point);
                    colours.Add(new Color(1, jitter, 1, 1));
                }
            }
            for (int step = 0; step < around; step++)
                for (int ring = 0; ring < radii.Length - 1; ring++)
                {
                    int a = step * radii.Length + ring, b = a + 1, c = a + radii.Length, d = c + 1;
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            MeshObject(root, "ValleyRidges", Build("ValleyRidges", positions, colours, triangles), material);
        }

        internal static Mesh Build(string name, List<Vector3> positions, List<Color> colours, List<int> triangles, List<Vector2> uvs = null, List<Vector3> normals = null)
        {
            var mesh = new Mesh { name = name };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetColors(colours);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            if (normals != null) mesh.SetNormals(normals); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>One card of a 2 × 2 atlas, upright or turned, with normals that round it like a crown.</summary>
        internal static void Card(List<Vector3> positions, List<Color> colours, List<Vector2> uvs, List<Vector3> normals, List<int> triangles,
            Vector3 centre, Vector3 right, Vector3 up, int tile, Color tint, Vector3 roundCentre, float roundness)
        {
            int start = positions.Count;
            var corners = new[] { centre - right - up, centre + right - up, centre - right + up, centre + right + up };
            float u = (tile % 2) * .5f, v = (1 - tile / 2) * .5f;
            var tileUvs = new[] { new Vector2(u, v), new Vector2(u + .5f, v), new Vector2(u, v + .5f), new Vector2(u + .5f, v + .5f) };
            var facing = Vector3.Cross(up, right).normalized;
            for (int corner = 0; corner < 4; corner++)
            {
                positions.Add(corners[corner]);
                uvs.Add(tileUvs[corner]);
                colours.Add(tint);
                normals.Add(Vector3.Slerp(facing, (corners[corner] - roundCentre).normalized, roundness));
            }
            triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
        }

        /// <summary>The museum's buildings and its arrival garden, the clearing the forest grows around.</summary>
        private static readonly Rect[] Clearing =
        {
            Rect.MinMaxRect(-13.8f, -11.8f, 13.8f, 11.8f), Rect.MinMaxRect(14.8f, -10.2f, 26.2f, 1.2f), Rect.MinMaxRect(-4.6f, 14.2f, 4.6f, 22.8f),
            Rect.MinMaxRect(-17.2f, 16.2f, -4.6f, 20.8f), Rect.MinMaxRect(-9.6f, -61.5f, 9.6f, -26f),
        };

        /// <summary>Metres from the nearest building or the arrival garden (the round welcome room counts as a circle).</summary>
        internal static float ClearingDistance(float x, float z)
        {
            float nearest = new Vector2(x, z + 20).magnitude - 6.4f;
            foreach (var box in Clearing)
            {
                float dx = Mathf.Max(box.xMin - x, 0, x - box.xMax), dz = Mathf.Max(box.yMin - z, 0, z - box.yMax);
                nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return nearest;
        }

        /// <summary>
        /// The museum stands in a clearing of the forest. Past seven metres from the walls single trees, shrubs and tall
        /// grass begin; they thicken into forest by about thirty metres. Trees near the clearing get a crown of leaf cards
        /// around their crossed cards so they keep their body seen from close; far ones are crossed cards only.
        /// </summary>
        private static void BuildForest(Transform root, System.Random random)
        {
            var positions = new List<Vector3>(); var colours = new List<Color>(); var uvs = new List<Vector2>();
            var normals = new List<Vector3>(); var triangles = new List<int>();
            // Leafy cards: low ones between the trunks and in the clearing's edge, and the crowns of the near trees.
            var bushPositions = new List<Vector3>(); var bushColours = new List<Color>(); var bushUvs = new List<Vector2>();
            var bushNormals = new List<Vector3>(); var bushTriangles = new List<int>();
            var grassPositions = new List<Vector3>(); var grassColours = new List<Color>(); var grassTriangles = new List<int>();
            int trees = 0, near = 0;

            void Bush(Vector3 spot, float size, Color tint)
            {
                spot.y = GroundHeight(spot.x, spot.z) - .15f;
                float turn = (float)random.NextDouble() * Mathf.PI;
                for (int card = 0; card < 3; card++)
                {
                    float angle = turn + card * Mathf.PI / 3;
                    var right = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * size;
                    Card(bushPositions, bushColours, bushUvs, bushNormals, bushTriangles, spot + Vector3.up * size * .8f, right, Vector3.up * size * .8f,
                        random.Next(4), tint * .9f, spot + Vector3.up * size * .35f, .7f);
                }
            }

            void Meadow(Vector3 centre, int tufts, float scale)
            {
                for (int tuft = 0; tuft < tufts; tuft++)
                {
                    var spot = centre + new Vector3((float)(random.NextDouble() - .5) * 2.2f, 0, (float)(random.NextDouble() - .5) * 2.2f);
                    if (ClearingDistance(spot.x, spot.z) < 2) continue;
                    spot.y = GroundHeight(spot.x, spot.z);
                    AddTuft(grassPositions, grassColours, grassTriangles, spot, scale * (.8f + (float)random.NextDouble() * .5f), random);
                }
            }

            for (float x = -215; x <= 215; x += 5.5f)
                for (float z = ValleyCentre.y - 215; z <= ValleyCentre.y + 215; z += 5.5f)
                {
                    float px = x + (float)(random.NextDouble() - .5) * 5.5f, pz = z + (float)(random.NextDouble() - .5) * 5.5f;
                    float clearing = ClearingDistance(px, pz);
                    if (clearing < 7) continue;
                    float density = Noise(px, pz, .04f, 5.5f);
                    // Thin at the clearing's edge, full forest from about thirty metres; the arrival keeps its view open.
                    float chance = Mathf.Lerp(.3f, .85f, Smooth(8, 26, clearing)) * (.45f + .55f * density);
                    if (pz < -26 && Mathf.Abs(px) < 16 && clearing < 14) chance *= .3f;
                    float shade = .8f + (float)random.NextDouble() * .3f;
                    var tint = new Color(shade * .95f, shade, shade * (.85f + (float)random.NextDouble() * .15f), 1);
                    var foot = new Vector3(px, GroundHeight(px, pz) - .3f, pz);
                    if (clearing < 40 && random.NextDouble() < (clearing < 20 ? .65f : .35f)) Meadow(foot, 5 + random.Next(7), 1.4f);
                    if (random.NextDouble() > chance)
                    {
                        if (clearing < 30 && random.NextDouble() < (clearing < 16 ? .4f : .2f)) Bush(foot, .7f + (float)random.NextDouble() * .9f, tint);
                        continue;
                    }
                    bool close = clearing < 26;
                    float height = close ? 7 + (float)random.NextDouble() * 5 : 10 + (float)random.NextDouble() * 10;
                    float yaw = (float)random.NextDouble() * Mathf.PI;
                    int tile = random.Next(4);
                    var crownCentre = foot + Vector3.up * height * .65f;
                    int cards = close ? 4 : 2;
                    for (int card = 0; card < cards; card++)
                    {
                        float angle = yaw + card * Mathf.PI / cards;
                        var right = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * height * .5f;
                        Card(positions, colours, uvs, normals, triangles, foot + Vector3.up * height * .5f, right, Vector3.up * height * .5f,
                            (tile + card) % 4, tint, crownCentre, .6f);
                    }
                    if (close)
                    {
                        // A cloud of leaf cards fills the crown, rounded by its normals.
                        for (int leaf = 0; leaf < 30; leaf++)
                        {
                            var offset = Vector3.Scale(Jitter(random).normalized * Mathf.Sqrt((float)random.NextDouble()), new Vector3(.3f, .2f, .3f) * height);
                            var point = crownCentre + offset + Vector3.up * height * .04f;
                            var facing = (offset.normalized + Jitter(random) * .8f).normalized;
                            var right = Vector3.Cross(facing, Jitter(random)).normalized;
                            var up = Vector3.Cross(right, facing).normalized;
                            float size = height * (.09f + (float)random.NextDouble() * .05f);
                            Card(bushPositions, bushColours, bushUvs, bushNormals, bushTriangles, point, right * size, up * size, random.Next(4),
                                tint, crownCentre - Vector3.up * height * .1f, .85f);
                        }
                        near++;
                    }
                    for (int bush = 0; bush < 2; bush++)
                        if (random.NextDouble() < .7)
                            Bush(foot + new Vector3((float)(random.NextDouble() - .5) * 5.5f, 0, (float)(random.NextDouble() - .5) * 5.5f), 1.1f + (float)random.NextDouble() * 1.3f, tint);
                    trees++;
                }
            var material = NewMaterial("Musiyo/World/Leaf Card", "ForestCards");
            material.SetTexture("_BaseMap", Texture(TreeCards));
            material.SetColor("_BaseColor", new Color(.72f, .86f, .62f));
            material.SetFloat("_HazeLimit", .55f);
            ForestMaterial = material;
            MeshObject(root, "ForestCards", Build("ForestCards", positions, colours, triangles, uvs, normals), material);
            var bushMaterial = NewMaterial("Musiyo/World/Leaf Card", "ForestUnderstorey");
            bushMaterial.SetTexture("_BaseMap", Texture(LeafCards));
            bushMaterial.SetColor("_BaseColor", new Color(.62f, .78f, .55f));
            bushMaterial.SetFloat("_HazeLimit", .55f);
            UnderstoreyMaterial = bushMaterial;
            MeshObject(root, "ForestUnderstorey", Build("ForestUnderstorey", bushPositions, bushColours, bushTriangles, bushUvs, bushNormals), bushMaterial);
            MeshObject(root, "Meadow", GrassMesh("Meadow", grassPositions, grassColours, grassTriangles), GrassMaterial());
            Debug.Log("Museum world forest: " + trees + " trees (" + near + " near), " + triangles.Count / 3 + " + " + bushTriangles.Count / 3
                + " leaf triangles, " + grassTriangles.Count / 3 + " meadow triangles.");
        }

        /// <summary>
        /// An invisible edge where the clearing turns into forest, so the visitor cannot walk among the tree cards: a wall
        /// of colliders traced outward from the museum to <see cref="WalkLimit"/> metres from the buildings and the garden.
        /// </summary>
        private static void BuildWalkLimit(Transform root)
        {
            const int around = 240;
            var centre = new Vector2(0, -20);
            var positions = new List<Vector3>(); var triangles = new List<int>();
            for (int step = 0; step <= around; step++)
            {
                float angle = step * Mathf.PI * 2 / around;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                float reach = 1;
                while (reach < 150 && ClearingDistance(centre.x + direction.x * reach, centre.y + direction.y * reach) < WalkLimit) reach += .5f;
                var point = centre + direction * reach;
                float ground = GroundHeight(point.x, point.y);
                positions.Add(new Vector3(point.x, ground - 1, point.y));
                positions.Add(new Vector3(point.x, ground + 4, point.y));
            }
            for (int step = 0; step < around; step++)
            {
                int a = step * 2, b = a + 1, c = a + 2, d = a + 3;
                // Both faces, so the edge holds from either side.
                triangles.AddRange(new[] { a, b, c, c, b, d, a, c, b, c, d, b });
            }
            var mesh = new Mesh { name = "WalkLimit" };
            mesh.SetVertices(positions);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            var edge = new GameObject("WalkLimit");
            edge.transform.SetParent(root, false);
            edge.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void BuildLagoon(Dictionary<string, Transform> objects)
        {
            if (!objects.TryGetValue("Bienvenida_Agua_Laguna", out var water)) return;
            var renderer = water.GetComponent<Renderer>();
            if (renderer == null) return;
            var centre = renderer.bounds.center;
            float island = objects.TryGetValue("Bienvenida_Isla", out var isle) && isle.GetComponent<Renderer>() != null
                ? isle.GetComponent<Renderer>().bounds.extents.x : 1.3f;
            var material = NewMaterial("Musiyo/World/Clear Water", "ClearWater");
            material.SetTexture("_BedTex", Texture(LagoonBed));
            material.SetVector("_Center", new Vector4(centre.x, centre.z, 0, 0));
            material.SetFloat("_OuterRadius", renderer.bounds.extents.x);
            material.SetFloat("_InnerRadius", island);
            renderer.sharedMaterial = material;
        }

        /// <summary>
        /// The delivered crown keeps its volume: cards of rendered branches stand on its surface and inside it, with
        /// normals that round the whole crown, instead of the faceted green shell.
        /// </summary>
        private static void BuildCrown(Transform root, Transform crown, System.Random random)
        {
            var filter = crown.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices.Select(vertex => crown.TransformPoint(vertex)).ToArray();
            var faces = mesh.triangles;
            var bounds = crown.GetComponent<Renderer>().bounds;
            var centre = bounds.center;
            var positions = new List<Vector3>(); var colours = new List<Color>(); var uvs = new List<Vector2>();
            var normals = new List<Vector3>(); var triangles = new List<int>();
            for (int index = 0; index < 340; index++)
            {
                int face = random.Next(faces.Length / 3) * 3;
                float a = (float)random.NextDouble(), b = (float)random.NextDouble();
                if (a + b > 1) { a = 1 - a; b = 1 - b; }
                var point = vertices[faces[face]] + a * (vertices[faces[face + 1]] - vertices[faces[face]]) + b * (vertices[faces[face + 2]] - vertices[faces[face]]);
                // A quarter of the cards fill the inside so the crown has no hollow look from below.
                if (index % 4 == 0) point = Vector3.Lerp(centre, point, .45f + (float)random.NextDouble() * .3f);
                var outward = (point - centre).normalized;
                point += outward * (float)random.NextDouble() * .25f;
                var facing = (outward + Jitter(random) * .9f).normalized;
                var right = Vector3.Cross(facing, Jitter(random)).normalized;
                var up = Vector3.Cross(right, facing).normalized;
                float size = .75f + (float)random.NextDouble() * .45f;
                float shade = .85f + (float)random.NextDouble() * .25f;
                Card(positions, colours, uvs, normals, triangles, point, right * size, up * size, random.Next(4),
                    new Color(shade * .95f, shade, shade * .9f, 1), centre - Vector3.up * .5f, .8f);
            }
            var material = NewMaterial("Musiyo/World/Leaf Card", "CrownCards");
            material.SetTexture("_BaseMap", Texture(LeafCards));
            material.SetColor("_BaseColor", new Color(.9f, 1, .85f));
            material.SetFloat("_Wrap", .7f);
            material.SetFloat("_HazeLimit", 1);
            MeshObject(root, "TreeOfLifeCrown", Build("TreeOfLifeCrown", positions, colours, triangles, uvs, normals), material);
        }

        internal static Vector3 Jitter(System.Random random)
            => new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);

        /// <summary>The lone ferns and flower heads on the lawn along the arrival go; the courtyard planters stay.</summary>
        private static void ClearLooseArrivalPlants(Dictionary<string, Transform> objects)
        {
            if (!objects.TryGetValue(MuseumSetDressing.SceneRoot, out var dressing)) return;
            foreach (var plant in dressing.Cast<Transform>().ToList())
                if ((plant.name == MuseumSetDressing.LeavesName || plant.name == MuseumSetDressing.FlowerName) && plant.position.z < MuseumFoliageBatch.ArrivalEdgeZ)
                    Object.DestroyImmediate(plant.gameObject);
        }

        /// <summary>Low grass along both edges of the path and in loose patches between the maize.</summary>
        private static void BuildPathGrass(Transform root, System.Random random, float pathEnd)
        {
            var positions = new List<Vector3>(); var colours = new List<Color>(); var triangles = new List<int>();
            void Tuft(Vector3 foot, float scale) => AddTuft(positions, colours, triangles, foot, scale, random);
            for (float z = -60.5f; z < pathEnd; z += .3f)
                foreach (float sign in new[] { -1f, 1f })
                    if (random.NextDouble() < .8) Tuft(new Vector3(sign * (1.2f + (float)random.NextDouble() * .7f), -.02f, z + (float)random.NextDouble() * .3f), .8f + (float)random.NextDouble() * .5f);
            for (int patch = 0; patch < 120; patch++)
            {
                var centre = new Vector3((float)(random.NextDouble() * 2 - 1) * 8.4f, -.02f, -60 + (float)random.NextDouble() * 33);
                if (Mathf.Abs(centre.x) < 2.2f || (centre.z > pathEnd - 1 && Mathf.Abs(centre.x) < 5.5f)) continue;
                for (int tuft = 0; tuft < 4; tuft++)
                    Tuft(centre + new Vector3((float)(random.NextDouble() - .5) * .8f, 0, (float)(random.NextDouble() - .5) * .8f), .7f + (float)random.NextDouble() * .5f);
            }
            MeshObject(root, "PathGrass", GrassMesh("PathGrass", positions, colours, triangles), GrassMaterial());
        }

        internal static Material GrassMaterial()
        {
            var material = NewMaterial(MuseumSetDressing.FoliageShader, "Grass");
            material.SetColor("_BaseColor", new Color(.42f, .56f, .24f));
            material.SetFloat("_BaseShade", .45f);
            material.SetFloat("_Variation", .18f);
            material.SetFloat("_WindStrength", .02f);
            return material;
        }

        /// <summary>Grass blades lit as if they were the ground they stand on.</summary>
        internal static Mesh GrassMesh(string name, List<Vector3> positions, List<Color> colours, List<int> triangles)
            => Build(name, positions, colours, triangles, null, Enumerable.Repeat(Vector3.up, positions.Count).ToList());

        /// <summary>One tuft of single-triangle blades; vertex red is the height within the tuft, green its tone.</summary>
        internal static void AddTuft(List<Vector3> positions, List<Color> colours, List<int> triangles, Vector3 foot, float scale, System.Random random)
        {
            float tone = (float)random.NextDouble();
            int blades = 7 + random.Next(5);
            for (int blade = 0; blade < blades; blade++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2;
                var lean = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var side = Vector3.Cross(lean, Vector3.up) * .018f * scale;
                float height = (.22f + (float)random.NextDouble() * .3f) * scale;
                var bladeFoot = foot + lean * (float)random.NextDouble() * .06f;
                var tip = bladeFoot + Vector3.up * height + lean * height * (.25f + (float)random.NextDouble() * .4f);
                int start = positions.Count;
                positions.Add(bladeFoot - side); positions.Add(bladeFoot + side); positions.Add(tip);
                colours.Add(new Color(0, tone, 0, 1)); colours.Add(new Color(0, tone, 0, 1)); colours.Add(new Color(1, tone, 0, 1));
                triangles.AddRange(new[] { start, start + 1, start + 2 });
            }
        }

        /// <summary>
        /// Clay pots with a fern by the walls of the welcome room, the hearth and the instruments room, and ferns hanging
        /// from the courtyard beams. A spot is skipped when furniture, a lectern or a wall stands within reach.
        /// </summary>
        private static void BuildWallPlants(Transform root, Dictionary<string, Transform> objects, System.Random random)
        {
            var pots = PotMaker.Load();
            if (pots == null) return;
            var ropeMaterial = NewMaterial("Universal Render Pipeline/Simple Lit", "Rope");
            ropeMaterial.SetColor("_BaseColor", new Color(.36f, .29f, .2f));
            var ropeMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var obstacles = Obstacles(root);
            var plants = new GameObject("WallPlants").transform;
            plants.SetParent(root, false);
            int placed = 0;
            GameObject Pot(Vector3 foot, float width, string name, bool standing = true)
            {
                placed++;
                return pots.Place(plants, foot, width, name, random, standing);
            }
            bool Free(Vector3 foot, float reach) => IsFree(obstacles, foot, reach);

            // Floor pots: diagonals of the round welcome room, corners of the hearth and the instruments room.
            var spots = new List<Vector3>();
            foreach (float degrees in new[] { 45f, 135f, 225f, 315f })
                spots.Add(new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad) * 5.3f, 0, -20 + Mathf.Cos(degrees * Mathf.Deg2Rad) * 5.3f));
            foreach (var (centre, half) in new[] { (new Vector3(0, 0, 18.5f), 3.25f), (new Vector3(20.5f, 0, -4.5f), 4.75f) })
                foreach (var corner in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                    spots.Add(centre + corner * half);
            foreach (var spot in spots)
                if (Free(spot, .7f)) Pot(spot, .5f, "FloorPot");

            // Hanging ferns along the four beams at the courtyard edge.
            foreach (var beamName in new[] { "Personajes_Viga_N", "Personajes_Viga_S", "Personajes_Viga_E", "Personajes_Viga_O" })
            {
                if (!objects.TryGetValue(beamName, out var beam) || beam.GetComponent<Renderer>() == null) continue;
                var beamBounds = beam.GetComponent<Renderer>().bounds;
                bool alongX = beamBounds.size.x > beamBounds.size.z;
                float length = alongX ? beamBounds.size.x : beamBounds.size.z;
                for (float offset = -length * .5f + 2.6f; offset <= length * .5f - 2.6f; offset += 4.3f)
                {
                    var hook = new Vector3(beamBounds.center.x + (alongX ? offset : 0), beamBounds.min.y, beamBounds.center.z + (alongX ? 0 : offset));
                    var pot = Pot(hook + Vector3.down * 1.05f, .34f, "HangingPot", false);
                    for (int rope = 0; rope < 3; rope++)
                    {
                        float angle = rope * Mathf.PI * 2 / 3;
                        var rim = pot.transform.position + new Vector3(Mathf.Cos(angle) * .15f, .2f, Mathf.Sin(angle) * .15f);
                        var line = MeshObject(pot.transform, "Rope", ropeMesh, ropeMaterial);
                        line.transform.position = (rim + hook) * .5f;
                        line.transform.up = (hook - rim).normalized;
                        line.transform.localScale = new Vector3(.012f, (hook - rim).magnitude * .5f, .012f);
                    }
                }
            }
            Debug.Log("Museum world wall plants: " + placed + ".");
        }

        /// <summary>Bounds of every drawn piece that is not part of the building shell, to keep new pieces clear of it.</summary>
        internal static List<Bounds> Obstacles(Transform root)
            => Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(root) && !IsShell(renderer.name)).Select(renderer => renderer.bounds).ToList();

        internal static bool IsFree(List<Bounds> obstacles, Vector3 foot, float reach)
            => IsFree(obstacles, foot, Quaternion.identity, new Vector3(reach, .8f, reach));

        /// <summary>
        /// Room for a box standing on the floor: no collider inside it (walls, posts, furniture with collision) and no
        /// small drawn piece (lecterns, pedestals, pots) whose bounds reach it. Large merged meshes count only through
        /// their colliders, since their bounds cover whole rooms.
        /// </summary>
        internal static bool IsFree(List<Bounds> obstacles, Vector3 foot, Quaternion turn, Vector3 size)
        {
            Physics.SyncTransforms();
            var middle = foot + Vector3.up * (size.y * .5f + .06f);
            if (Physics.CheckBox(middle, size * .5f, turn, ~0, QueryTriggerInteraction.Ignore)) return false;
            var space = new Bounds(middle, Vector3.zero);
            foreach (var corner in new[] { new Vector3(-1, -1, -1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(1, -1, -1), new Vector3(-1, -1, 1), new Vector3(1, 1, -1), new Vector3(1, -1, 1), new Vector3(-1, 1, -1) })
                space.Encapsulate(middle + turn * Vector3.Scale(corner, size * .5f));
            return !obstacles.Any(bounds => bounds.size.x < 2.5f && bounds.size.z < 2.5f && bounds.Intersects(space));
        }

        /// <summary>A clay pot with a fern, sized by the pot's width.</summary>
        internal sealed class PotMaker
        {
            /// <summary>
            /// A clay pot turned like on a wheel: a flared wall with a rolled rim and a little soil inside, about 200
            /// triangles, a metre wide before scaling. The clay texture wraps once around it.
            /// </summary>
            private static Mesh Lathe()
            {
                // Profile from the foot outward and up, over the rim and down to the soil: (radius, height).
                var profile = new[] { new Vector2(0, 0), new Vector2(.36f, 0), new Vector2(.4f, .05f), new Vector2(.47f, .62f), new Vector2(.53f, .7f),
                    new Vector2(.53f, .78f), new Vector2(.46f, .79f), new Vector2(.44f, .64f), new Vector2(0, .64f) };
                const int around = 20;
                var positions = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
                float length = 0;
                var along = new float[profile.Length];
                for (int index = 1; index < profile.Length; index++) along[index] = length += Vector2.Distance(profile[index], profile[index - 1]);
                for (int step = 0; step <= around; step++)
                {
                    float angle = step * Mathf.PI * 2 / around;
                    for (int index = 0; index < profile.Length; index++)
                    {
                        positions.Add(new Vector3(Mathf.Cos(angle) * profile[index].x, profile[index].y, Mathf.Sin(angle) * profile[index].x));
                        uvs.Add(new Vector2((float)step / around, along[index] / length));
                    }
                }
                for (int step = 0; step < around; step++)
                    for (int index = 0; index < profile.Length - 1; index++)
                    {
                        int a = step * profile.Length + index, b = a + 1, c = a + profile.Length, d = c + 1;
                        triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
                var mesh = Build("ClayPot", positions, Enumerable.Repeat(Color.white, positions.Count).ToList(), triangles, uvs);
                return mesh;
            }

            private Mesh potMesh;
            private GameObject fern;
            private Material fernMaterial;
            private Material potMaterial;

            public static PotMaker Load()
            {
                var maker = new PotMaker
                {
                    potMesh = Lathe(),
                    fern = AssetDatabase.LoadAssetAtPath<GameObject>(FernModel),
                    fernMaterial = AssetDatabase.LoadAssetAtPath<Material>(Headset ? FernMaterial.Replace(".mat", MuseumSetDressing.HeadsetSuffix + ".mat") : FernMaterial),
                };
                if (maker.fern == null)
                {
                    Debug.LogWarning("Museum world: fern model missing; pots skipped.");
                    return null;
                }
                maker.potMaterial = NewMaterial("Universal Render Pipeline/Simple Lit", "ClayPot");
                maker.potMaterial.SetTexture("_BaseMap", Texture(PotTexture));
                return maker;
            }

            /// <summary>Places a pot; a standing one gets a collider so the visitor walks round it.</summary>
            public GameObject Place(Transform parent, Vector3 foot, float width, string name, System.Random random, bool standing = true)
            {
                var pot = new GameObject(name);
                pot.transform.SetParent(parent, false);
                pot.transform.position = foot;
                if (standing)
                {
                    var solid = pot.AddComponent<CapsuleCollider>();
                    solid.radius = width * .5f;
                    solid.height = width * 1.6f;
                    solid.center = Vector3.up * width * .8f;
                }
                var body = MeshObject(pot.transform, "Pot", potMesh, potMaterial);
                float scale = width / Mathf.Max(potMesh.bounds.size.x, .01f);
                body.transform.localScale = Vector3.one * scale;
                body.transform.localPosition = new Vector3(0, -potMesh.bounds.min.y * scale, 0);
                body.transform.localRotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                // The fern model holds four variants side by side; each pot takes one of the two lightest.
                var leaves = Object.Instantiate(fern, pot.transform);
                leaves.name = "Fern";
                var lightest = leaves.GetComponentsInChildren<MeshFilter>().OrderBy(filter => filter.sharedMesh.triangles.Length)
                    .Select(filter => filter.transform.GetSiblingIndex()).Take(2).ToList();
                int keep = lightest.Count > 0 ? lightest[random.Next(lightest.Count)] : 0;
                for (int child = leaves.transform.childCount - 1; child >= 0; child--)
                    if (child != keep) Object.DestroyImmediate(leaves.transform.GetChild(child).gameObject);
                if (leaves.transform.childCount > 0) leaves.transform.GetChild(0).localPosition = Vector3.zero;
                if (fernMaterial != null) foreach (var renderer in leaves.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = fernMaterial;
                var leafBounds = leaves.GetComponentsInChildren<Renderer>().Select(renderer => renderer.bounds).Aggregate((all, next) => { all.Encapsulate(next); return all; });
                float leafScale = width * 1.9f / Mathf.Max(leafBounds.size.x, leafBounds.size.z, .01f);
                leaves.transform.localScale *= leafScale;
                leaves.transform.localRotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
                leaves.transform.localPosition = new Vector3(0, potMesh.bounds.size.y * scale * .8f, 0);
                return pot;
            }
        }

        /// <summary>Floors, walls, roofs and large outdoor pieces are no obstacle for a pot standing by them.</summary>
        private static bool IsShell(string name)
            => new[] { "Piso", "Muro", "Techo", "Zocalo", "Estructura", "Friso", "Faja", "Tapices", "Terreno", "Cenefa", "Trazado", "Umbral", "Pasaje", "Viga", "Entorno", "Fleco" }
                .Any(part => name.Contains(part));
    }
}
