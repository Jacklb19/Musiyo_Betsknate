using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Shapes for the museum's decoration (research PART A, approved by the owner 23/10), drawn with one shared palette:
    /// a small texture of colour swatches, so every piece of a room shares one material and the headset draws a room's
    /// decoration in one or two passes. Boxes, tapered cylinders, lathed vessels, ellipsoids and leaves, all low in
    /// triangles; the leaves use the double-sided twin of the material. Nothing is downloaded: the palette is generated.
    /// Batch, once: -executeMethod MusiyoBetsknate.Editor.MuseumDecorKit.BuildAssets.
    /// </summary>
    public sealed class MuseumDecorKit
    {
        public const string Folder = "Assets/_Musiyo/Museum/Decor";
        public const string PalettePath = Folder + "/decor_palette.png", MaterialPath = Folder + "/MuseumDecor.mat",
            LeavesMaterialPath = Folder + "/MuseumDecorLeaves.mat";
        private const int Swatches = 8, SwatchPixels = 8;

        /// <summary>The palette, eight by eight swatches; a shape takes its colour by the swatch's index.</summary>
        public static readonly Color32[] Colours =
        {
            // 0-7: woods and fibres
            C(0xB0, 0x86, 0x5A), C(0x86, 0x5E, 0x3C), C(0x5A, 0x3D, 0x27), C(0x3E, 0x2D, 0x22), C(0xC9, 0xA9, 0x6E), C(0xA8, 0x8A, 0x55), C(0x2B, 0x22, 0x1D), C(0xD9, 0xC3, 0x95),
            // 8-15: greens
            C(0x4E, 0x7A, 0x3A), C(0x37, 0x5C, 0x2C), C(0x7E, 0xA3, 0x5E), C(0x9C, 0xB8, 0x7A), C(0x5F, 0x8C, 0x4A), C(0x6B, 0x8F, 0x2F), C(0x2F, 0x4A, 0x27), C(0x8B, 0x9E, 0x55),
            // 16-23: fruits, vegetables and food
            C(0xD9, 0x7A, 0x1E), C(0xC2, 0x3B, 0x22), C(0xE8, 0xA3, 0x2E), C(0xE4, 0xC6, 0x3B), C(0xF2, 0xEC, 0xDC), C(0xB7, 0x8F, 0x5B), C(0xA4, 0x1F, 0x1A), C(0xEF, 0xE6, 0xC8),
            // 24-31: earth, clay and stone
            C(0x7A, 0x56, 0x3A), C(0x5B, 0x40, 0x2C), C(0xA2, 0x5B, 0x37), C(0x7C, 0x42, 0x29), C(0x8F, 0x8A, 0x82), C(0x5F, 0x5B, 0x55), C(0xC8, 0xB9, 0x9A), C(0x4A, 0x6E, 0x7A),
            // 32-39: cloth (the colours of the sayo, the faja and the banners)
            C(0xF1, 0xEA, 0xD7), C(0x20, 0x1C, 0x1A), C(0xB8, 0x24, 0x2B), C(0x1F, 0x5F, 0x9E), C(0x2E, 0x7D, 0x4F), C(0xE3, 0xB3, 0x26), C(0x7A, 0x2A, 0x6E), C(0xE8, 0x83, 0x2A),
            // 40-47: petals, beads and animals
            C(0xE8, 0x7D, 0xA0), C(0xD8, 0x3C, 0x6A), C(0xF5, 0xD0, 0x45), C(0xF7, 0xF4, 0xEC), C(0x3C, 0x6E, 0xB4), C(0xF6, 0xF3, 0xEA), C(0x9A, 0x5B, 0x2E), C(0x6E, 0x4A, 0x2E),
            // 48-55: animals and details
            C(0xD8, 0x2A, 0x23), C(0xE6, 0xB0, 0x3A), C(0xDB, 0xD4, 0xC3), C(0x4B, 0x3A, 0x2A), C(0xE9, 0xDD, 0xC6), C(0x14, 0x14, 0x14), C(0xEA, 0xD9, 0xB4), C(0x9F, 0x9F, 0x9A),
            // 56-63: landscape
            C(0x6F, 0x8F, 0x45), C(0x9A, 0xA4, 0x4C), C(0xB9, 0xA6, 0x55), C(0x55, 0x7A, 0x3B), C(0x8C, 0x6A, 0x42), C(0xD6, 0xC9, 0xA8), C(0x45, 0x75, 0x86), C(0xA9, 0x93, 0x63),
        };
        private static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        // Names for the swatches the rooms use.
        public const int WoodLight = 0, Wood = 1, WoodDark = 2, Bark = 3, Straw = 4, Fibre = 5, Charred = 6, Shaving = 7;
        public const int Greenery = 8, LeafDark = 9, LeafLight = 10, Cabbage = 11, LeafMid = 12, Palm = 13, Forest = 14, PalmDry = 15;
        public const int Squash = 16, TreeTomato = 17, Lulo = 18, Chilacuan = 19, Mote = 20, Potato = 21, Aji = 22, Egg = 23;
        public const int Earth = 24, EarthDark = 25, Clay = 26, ClayDark = 27, Stone = 28, StoneDark = 29, Ash = 30, Water = 31;
        public const int Wool = 32, Black = 33, Red = 34, Blue = 35, Green = 36, Yellow = 37, Purple = 38, Orange = 39;
        public const int PetalPink = 40, PetalMagenta = 41, PetalYellow = 42, PetalWhite = 43, BeadBlue = 44, BeadWhite = 45, Horn = 46, Hide = 47;
        public const int Comb = 48, Beak = 49, HenWhite = 50, HenBrown = 51, Cream = 52, Eye = 53, Gourd = 54, Metal = 55;
        public const int Field1 = 56, Field2 = 57, Field3 = 58, Field4 = 59, Fallow = 60, WallLight = 61, Lagoon = 62, Paramo = 63;

        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        public int Triangles => triangles.Count / 3;
        public bool Empty => triangles.Count == 0;

        /// <summary>The centre of a swatch in the palette's texture.</summary>
        public static Vector2 Swatch(int colour)
        {
            int column = colour % Swatches, row = colour / Swatches;
            return new Vector2((column + .5f) / Swatches, (row + .5f) / Swatches);
        }

        private int Vertex(Vector3 position, Vector3 normal, int colour)
        {
            positions.Add(position);
            normals.Add(normal);
            uvs.Add(Swatch(colour));
            return positions.Count - 1;
        }

        private void Face(Vector3 a, Vector3 b, Vector3 c, int colour)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            triangles.Add(Vertex(a, normal, colour)); triangles.Add(Vertex(b, normal, colour)); triangles.Add(Vertex(c, normal, colour));
        }

        /// <summary>A box at a point, turned, of a size; its faces flat.</summary>
        public void Box(Vector3 centre, Quaternion turn, Vector3 size, int colour)
        {
            var half = size * .5f;
            Vector3 P(float x, float y, float z) => centre + turn * Vector3.Scale(new Vector3(x, y, z), half);
            var corners = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
            int[][] quads = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 0, 1, 5, 4 } };
            foreach (var q in quads) Quad(corners[q[0]], corners[q[1]], corners[q[2]], corners[q[3]], colour);
        }

        /// <summary>A box whose top is narrower than its foot, as a bed of earth heaped up: centre at its foot.</summary>
        public void Frustum(Vector3 foot, Quaternion turn, Vector2 footSize, Vector2 topSize, float height, int colour)
        {
            Vector3 P(float x, float y, float z)
            {
                var size = y < 0 ? footSize : topSize;
                return foot + turn * new Vector3(x * size.x * .5f, y < 0 ? 0 : height, z * size.y * .5f);
            }
            var corners = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
            int[][] quads = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 } };
            foreach (var q in quads) Quad(corners[q[0]], corners[q[1]], corners[q[2]], corners[q[3]], colour);
        }

        /// <summary>A flat four-sided face, wound so its front is seen from the side its corners turn clockwise.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int colour)
        {
            Face(a, b, c, colour);
            Face(a, c, d, colour);
        }

        /// <summary>A cylinder between two points, its radius changing from one end to the other, closed at both ends.</summary>
        public void Cylinder(Vector3 from, Vector3 to, float fromRadius, float toRadius, int colour, int sides = 7, bool caps = true, int capColour = -1)
        {
            var axis = (to - from).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > .95f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(side, axis);
            Vector3 Ring(Vector3 centre, float radius, int index)
            {
                float angle = index * Mathf.PI * 2 / sides;
                return centre + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
            }
            for (int index = 0; index < sides; index++)
            {
                var a = Ring(from, fromRadius, index); var b = Ring(to, toRadius, index);
                var c = Ring(to, toRadius, index + 1); var d = Ring(from, fromRadius, index + 1);
                // Smooth sides: each corner takes the normal of its direction from the axis.
                int start = positions.Count;
                Vector3 N(Vector3 point, Vector3 centre) => Vector3.ProjectOnPlane(point - centre, axis).normalized;
                Vertex(a, N(a, from), colour); Vertex(b, N(b, to), colour); Vertex(c, N(c, to), colour); Vertex(d, N(d, from), colour);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            if (!caps) return;
            int cap = capColour < 0 ? colour : capColour;
            for (int index = 0; index < sides; index++)
            {
                if (toRadius > 0) Face(to, Ring(to, toRadius, index + 1), Ring(to, toRadius, index), cap);
                if (fromRadius > 0) Face(from, Ring(from, fromRadius, index), Ring(from, fromRadius, index + 1), cap);
            }
        }

        /// <summary>
        /// A vessel turned on a wheel around an upright axis: a profile of (radius, height) from the foot up; the inside is
        /// drawn when the profile comes back towards the axis (a bowl, a pot's mouth).
        /// </summary>
        public void Lathe(Vector3 foot, Quaternion turn, IList<Vector2> profile, int colour, int sides = 10)
        {
            for (int ring = 0; ring < profile.Count - 1; ring++)
                for (int index = 0; index < sides; index++)
                {
                    float a0 = index * Mathf.PI * 2 / sides, a1 = (index + 1) * Mathf.PI * 2 / sides;
                    Vector3 P(Vector2 point, float angle) => foot + turn * new Vector3(Mathf.Cos(angle) * point.x, point.y, Mathf.Sin(angle) * point.x);
                    var p0 = profile[ring]; var p1 = profile[ring + 1];
                    var a = P(p0, a0); var b = P(p1, a0); var c = P(p1, a1); var d = P(p0, a1);
                    int start = positions.Count;
                    // The profile's slope gives the normal of each ring.
                    var slope = p1 - p0;
                    Vector3 N(float angle) => (turn * new Vector3(Mathf.Cos(angle) * slope.y, -slope.x, Mathf.Sin(angle) * slope.y)).normalized;
                    Vertex(a, N(a0), colour); Vertex(b, N(a0), colour); Vertex(c, N(a1), colour); Vertex(d, N(a1), colour);
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
        }

        /// <summary>An ellipsoid, smooth, of few faces: bodies, heads, fruits, stones of the arrival.</summary>
        public void Ellipsoid(Vector3 centre, Quaternion turn, Vector3 radii, int colour, int rings = 5, int segments = 8)
        {
            for (int ring = 0; ring < rings; ring++)
                for (int index = 0; index < segments; index++)
                {
                    Vector3 Unit(int r, int s)
                    {
                        float theta = Mathf.PI * r / rings, phi = Mathf.PI * 2 * s / segments;
                        return new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    }
                    var u = new[] { Unit(ring, index), Unit(ring + 1, index), Unit(ring + 1, index + 1), Unit(ring, index + 1) };
                    int start = positions.Count;
                    foreach (var point in u)
                        Vertex(centre + turn * Vector3.Scale(point, radii), (turn * new Vector3(point.x / radii.x, point.y / radii.y, point.z / radii.z)).normalized, colour);
                    triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
        }

        /// <summary>A leaf, a blade or a cloth: a pointed strip from its base along a direction, bending, seen from both sides.</summary>
        public void Leaf(Vector3 foot, Vector3 direction, Vector3 across, float length, float width, int colour, float bend = .25f, int segments = 3)
        {
            direction = direction.normalized;
            across = Vector3.ProjectOnPlane(across, direction).normalized;
            var bendAxis = Vector3.Cross(direction, across).normalized;
            Vector3 previousLeft = foot, previousRight = foot;
            for (int step = 1; step <= segments; step++)
            {
                float t = step / (float)segments;
                var along = foot + direction * (length * t) - bendAxis * (bend * length * t * t);
                float half = width * .5f * Mathf.Sin(Mathf.PI * Mathf.Min(1, t * 1.15f)) + (step == segments ? 0 : .002f);
                var left = along - across * half; var right = along + across * half;
                Quad(previousLeft, left, right, previousRight, colour);
                previousLeft = left; previousRight = right;
            }
        }

        /// <summary>A flat cloth, sheet or field: four corners, the same colour, front facing the way its corners turn.</summary>
        public void Sheet(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int colour) => Quad(a, b, c, d, colour);

        /// <summary>The finished mesh of all shapes added so far.</summary>
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The palette's material; the double-sided twin for leaves and cloth.</summary>
        public static Material Material(bool doubleSided) => AssetDatabase.LoadAssetAtPath<Material>(doubleSided ? LeavesMaterialPath : MaterialPath);

        [MenuItem("Musiyo/Museum World/Build Decoration Palette")]
        public static void BuildAssets()
        {
            Directory.CreateDirectory(Folder);
            int size = Swatches * SwatchPixels;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, Colours[(y / SwatchPixels) * Swatches + x / SwatchPixels]);
            texture.Apply();
            File.WriteAllBytes(PalettePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(PalettePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(PalettePath);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            foreach (var (path, cull) in new[] { (MaterialPath, CullMode.Back), (LeavesMaterialPath, CullMode.Off) })
            {
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", palette);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Cull", (float)cull);
                material.SetFloat("_Smoothness", .12f);
                material.SetFloat("_SpecularHighlights", 0);
                material.DisableKeyword("_SPECULAR_COLOR");
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.doubleSidedGI = cull == CullMode.Off;
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Decoration palette built: " + Colours.Length + " colours.");
        }
    }
}
