using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Measures how each visible surface of the museum scene wears its texture: the real size of one texture repeat,
    /// how many repeats a surface shows, stretched or collapsed UVs, and coplanar faces that overlap and flicker.
    /// It reads the scene and writes a report; it changes nothing.
    /// </summary>
    public static class MuseumSurfaceAudit
    {
        public const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        public const string OutputFolder = "TestResults/museum-surface-audit";
        /// <summary>Faces closer than this to the same plane are drawn on top of each other.</summary>
        private const float PlaneTolerance = .004f;
        private const float NormalQuantum = 50;
        private const float MinimumOverlap = .0005f;

        public sealed class Surface
        {
            public Renderer Renderer;
            public int Submesh;
            public Material Material;
            public Texture Texture;
            public Vector2 Tiling;
            public double Area;
            public double UvArea;
            public double StretchedArea;
            public double CollapsedArea;
            /// <summary>Upright faces turned toward the middle of their object and away from it, for thin two-sided pieces.</summary>
            public double InwardArea, OutwardArea;
            public double DensityMean;
            public double DensityDeviation;
            public Vector2 UvMin = Vector2.positiveInfinity, UvMax = Vector2.negativeInfinity;
            /// <summary>Metres the texture covers before it starts again.</summary>
            public double Period => UvArea <= 0 || Area <= 0 ? double.PositiveInfinity
                : 1 / (Math.Sqrt(UvArea / Area) * Math.Sqrt(Mathf.Abs(Tiling.x * Tiling.y)));
            /// <summary>How many whole copies of the texture this surface shows.</summary>
            public double Repeats => double.IsInfinity(Period) ? 0 : Area / (Period * Period);
            public double TexelDensity => Texture == null || double.IsInfinity(Period) ? 0 : Texture.width / Period;
            public string Name => Renderer.name + (Renderer.sharedMaterials.Length > 1 ? "#" + Submesh : "");
        }

        public sealed class Overlap
        {
            public string First, Second;
            public double Area;
            public bool SameFacing;
            /// <summary>Both faces can be seen from the same side: same facing, or one of them drawn on both sides.</summary>
            public bool Visible;
            public Vector3 Centre;
        }

        private struct Face
        {
            public int Surface;
            public Vector3 A, B, C, Normal;
            public float Offset;
        }

        [MenuItem("Musiyo/Museum Delivery/Audit Surfaces")]
        public static void RunInEditor() => Run(captures: true);

        /// <summary>Batch entry; captures need a graphics device, so they are skipped under -nographics.</summary>
        public static void RunFromCommandLine() => Run(captures: SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null);

        public static void Run(bool captures)
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Single);
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(false))
                .Where(renderer => renderer.enabled && renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                .ToList();
            var surfaces = new List<Surface>();
            var faces = new List<Face>();
            foreach (var renderer in renderers) Measure(renderer, surfaces, faces);
            var overlaps = FindOverlaps(surfaces, faces);
            Directory.CreateDirectory(OutputFolder);
            WriteReport(surfaces, overlaps);
            if (captures) CaptureFindings(surfaces, overlaps);
            Debug.Log("Museum surface audit: " + surfaces.Count + " surfaces, " + overlaps.Count + " overlapping pairs; report in " + OutputFolder);
        }

        private static void Measure(MeshRenderer renderer, List<Surface> surfaces, List<Face> faces)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (!mesh.isReadable) { Debug.LogWarning("Mesh not readable: " + renderer.name); return; }
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var matrix = renderer.localToWorldMatrix;
            var world = vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToArray();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var material = submesh < renderer.sharedMaterials.Length ? renderer.sharedMaterials[submesh] : null;
                var texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                var surface = new Surface
                {
                    Renderer = renderer, Submesh = submesh, Material = material, Texture = texture,
                    Tiling = texture != null ? material.GetTextureScale("_BaseMap") : Vector2.one,
                };
                var indices = mesh.GetTriangles(submesh);
                var densities = new List<(double density, double area)>();
                for (int index = 0; index + 2 < indices.Length; index += 3)
                {
                    int i0 = indices[index], i1 = indices[index + 1], i2 = indices[index + 2];
                    Vector3 a = world[i0], b = world[i1], c = world[i2];
                    var cross = Vector3.Cross(b - a, c - a);
                    double area = cross.magnitude * .5;
                    if (area < 1e-8) continue;
                    surface.Area += area;
                    var normal = cross.normalized;
                    if (Mathf.Abs(normal.y) < .7f)
                    {
                        var fromMiddle = (a + b + c) / 3 - renderer.bounds.center;
                        fromMiddle.y = 0;
                        if (Vector3.Dot(normal, fromMiddle) < 0) surface.InwardArea += area;
                        else surface.OutwardArea += area;
                    }
                    faces.Add(new Face { Surface = surfaces.Count, A = a, B = b, C = c, Normal = cross.normalized,
                        Offset = Vector3.Dot(cross.normalized, a) });
                    if (uvs.Length != vertices.Length) continue;
                    Vector2 u0 = uvs[i0], u1 = uvs[i1], u2 = uvs[i2];
                    surface.UvMin = Vector2.Min(surface.UvMin, Vector2.Min(u0, Vector2.Min(u1, u2)));
                    surface.UvMax = Vector2.Max(surface.UvMax, Vector2.Max(u0, Vector2.Max(u1, u2)));
                    double uvArea = Math.Abs((u1.x - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (u1.y - u0.y)) * .5;
                    surface.UvArea += uvArea;
                    if (uvArea < area * 1e-6) { surface.CollapsedArea += area; continue; }
                    densities.Add((Math.Sqrt(uvArea / area), area));
                    // Stretch is judged in texels: a band texture is not square, and its UV square is not either.
                    var texels = surface.Texture != null
                        ? new Vector2(surface.Texture.width * surface.Tiling.x, surface.Texture.height * surface.Tiling.y) : Vector2.one;
                    if (Anisotropy(a, b, c, Vector2.Scale(u0, texels), Vector2.Scale(u1, texels), Vector2.Scale(u2, texels)) > 2)
                        surface.StretchedArea += area;
                }
                double weight = densities.Sum(entry => entry.area);
                if (weight > 0)
                {
                    surface.DensityMean = densities.Sum(entry => entry.density * entry.area) / weight;
                    surface.DensityDeviation = Math.Sqrt(densities.Sum(entry => entry.area
                        * (entry.density - surface.DensityMean) * (entry.density - surface.DensityMean)) / weight);
                }
                surfaces.Add(surface);
            }
        }

        /// <summary>Ratio of the largest to the smallest scale the texture undergoes across one face.</summary>
        private static double Anisotropy(Vector3 a, Vector3 b, Vector3 c, Vector2 u0, Vector2 u1, Vector2 u2)
        {
            // Face in its own 2D frame.
            var e1 = b - a;
            var e2 = c - a;
            var x = e1.normalized;
            var y = Vector3.Cross(Vector3.Cross(e1, e2).normalized, x);
            double p1x = Vector3.Dot(e1, x), p2x = Vector3.Dot(e2, x), p2y = Vector3.Dot(e2, y);
            double q1x = u1.x - u0.x, q1y = u1.y - u0.y, q2x = u2.x - u0.x, q2y = u2.y - u0.y;
            if (Math.Abs(p1x * p2y) < 1e-12) return 1;
            // Jacobian from face coordinates to UV.
            double j11 = q1x / p1x, j21 = q1y / p1x;
            double j12 = (q2x - j11 * p2x) / p2y, j22 = (q2y - j21 * p2x) / p2y;
            double t = j11 * j11 + j12 * j12 + j21 * j21 + j22 * j22;
            double det = j11 * j22 - j12 * j21;
            double root = Math.Sqrt(Math.Max(0, t * t * .25 - det * det));
            double high = Math.Sqrt(t * .5 + root), low = Math.Sqrt(Math.Max(0, t * .5 - root));
            return low < 1e-9 ? double.PositiveInfinity : high / low;
        }

        private static List<Overlap> FindOverlaps(List<Surface> surfaces, List<Face> faces)
        {
            var buckets = new Dictionary<(int, int, int, int), List<int>>();
            for (int index = 0; index < faces.Count; index++)
            {
                var face = faces[index];
                var key = PlaneKey(face.Normal, face.Offset, out _);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
                list.Add(index);
            }
            var pairs = new Dictionary<(int, int), Overlap>();
            foreach (var entry in buckets)
            {
                var (nx, ny, nz, d) = entry.Key;
                // Faces near a quantum boundary land in the next bucket; compare with it too.
                var candidates = new List<int>(entry.Value);
                if (buckets.TryGetValue((nx, ny, nz, d + 1), out var next)) candidates.AddRange(next);
                var own = entry.Value;
                for (int i = 0; i < own.Count; i++)
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        var first = faces[own[i]];
                        var second = faces[candidates[j]];
                        if (Math.Abs(Math.Abs(Vector3.Dot(first.Normal, second.Normal)) - 1) > .0005f) continue;
                        float distance = Mathf.Abs(Vector3.Dot(first.Normal, second.A) - first.Offset);
                        if (distance > PlaneTolerance) continue;
                        double area = OverlapArea(first, second);
                        if (area < 1e-6) continue;
                        int s1 = Math.Min(first.Surface, second.Surface), s2 = Math.Max(first.Surface, second.Surface);
                        if (!pairs.TryGetValue((s1, s2), out var overlap))
                            pairs[(s1, s2)] = overlap = new Overlap
                            {
                                First = surfaces[s1].Name, Second = surfaces[s2].Name,
                                SameFacing = Vector3.Dot(first.Normal, second.Normal) > 0, Centre = (first.A + first.B + first.C) / 3,
                            };
                        overlap.Visible = overlap.SameFacing || DoubleSided(surfaces[s1].Material) || DoubleSided(surfaces[s2].Material);
                        overlap.Area += area;
                    }
            }
            return pairs.Values.Where(overlap => overlap.Area >= MinimumOverlap).OrderByDescending(overlap => overlap.Area).ToList();
        }

        private static bool DoubleSided(Material material)
            => material != null && material.HasProperty("_Cull") && material.GetFloat("_Cull") == 0;

        /// <summary>Plane of a face with its normal turned to one canonical side, so back-to-back faces share it.</summary>
        private static (int, int, int, int) PlaneKey(Vector3 normal, float offset, out bool flipped)
        {
            flipped = normal.x < -1e-4f || Mathf.Abs(normal.x) <= 1e-4f && (normal.y < -1e-4f || Mathf.Abs(normal.y) <= 1e-4f && normal.z < 0);
            if (flipped) { normal = -normal; offset = -offset; }
            return (Mathf.RoundToInt(normal.x * NormalQuantum), Mathf.RoundToInt(normal.y * NormalQuantum),
                Mathf.RoundToInt(normal.z * NormalQuantum), Mathf.FloorToInt(offset / PlaneTolerance));
        }

        private static double OverlapArea(Face first, Face second)
        {
            var x = (first.B - first.A).normalized;
            var y = Vector3.Cross(first.Normal, x);
            Vector2 Flat(Vector3 point) => new Vector2(Vector3.Dot(point - first.A, x), Vector3.Dot(point - first.A, y));
            var subject = new List<Vector2> { Flat(first.A), Flat(first.B), Flat(first.C) };
            var clip = new List<Vector2> { Flat(second.A), Flat(second.B), Flat(second.C) };
            if (Signed(clip) < 0) clip.Reverse();
            if (Signed(subject) < 0) subject.Reverse();
            for (int edge = 0; edge < 3 && subject.Count > 0; edge++)
            {
                Vector2 p = clip[edge], q = clip[(edge + 1) % 3];
                var input = subject;
                subject = new List<Vector2>();
                for (int k = 0; k < input.Count; k++)
                {
                    var current = input[k];
                    var previous = input[(k + input.Count - 1) % input.Count];
                    bool inside = Side(p, q, current) >= 0, wasInside = Side(p, q, previous) >= 0;
                    if (inside)
                    {
                        if (!wasInside) subject.Add(Cross(p, q, previous, current));
                        subject.Add(current);
                    }
                    else if (wasInside) subject.Add(Cross(p, q, previous, current));
                }
            }
            return subject.Count < 3 ? 0 : Math.Abs(Signed(subject));
        }

        private static double Signed(List<Vector2> polygon)
        {
            double sum = 0;
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var b = polygon[(k + 1) % polygon.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum * .5;
        }

        private static float Side(Vector2 p, Vector2 q, Vector2 point) => (q.x - p.x) * (point.y - p.y) - (q.y - p.y) * (point.x - p.x);

        private static Vector2 Cross(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
        {
            float sa = Side(p, q, a), sb = Side(p, q, b);
            return a + (b - a) * (sa / (sa - sb));
        }

        private static string Number(double value, int digits = 3)
            => double.IsInfinity(value) || double.IsNaN(value) ? "" : Math.Round(value, digits).ToString(CultureInfo.InvariantCulture);

        private static void WriteReport(List<Surface> surfaces, List<Overlap> overlaps)
        {
            var csv = new StringBuilder("object,material,texture,texture_px,tiling_x,tiling_y,area_m2,period_m,repeats,texel_px_per_m,"
                + "density_variation,stretched_share,collapsed_share,inward_share,uv_min_x,uv_min_y,uv_max_x,uv_max_y\n");
            var list = new JArray();
            foreach (var surface in surfaces.OrderByDescending(surface => surface.Area))
            {
                double variation = surface.DensityMean > 0 ? surface.DensityDeviation / surface.DensityMean : 0;
                string textureName = surface.Texture != null ? surface.Texture.name : "";
                csv.AppendLine(string.Join(",", surface.Name, surface.Material != null ? surface.Material.name : "", textureName,
                    surface.Texture != null ? surface.Texture.width.ToString(CultureInfo.InvariantCulture) : "",
                    Number(surface.Tiling.x), Number(surface.Tiling.y), Number(surface.Area, 2),
                    surface.Texture != null ? Number(surface.Period) : "", surface.Texture != null ? Number(surface.Repeats, 1) : "",
                    surface.Texture != null ? Number(surface.TexelDensity, 0) : "", Number(variation),
                    Number(surface.StretchedArea / Math.Max(surface.Area, 1e-9)), Number(surface.CollapsedArea / Math.Max(surface.Area, 1e-9)),
                    Number(surface.InwardArea / Math.Max(surface.InwardArea + surface.OutwardArea, 1e-9)),
                    Number(surface.UvMin.x), Number(surface.UvMin.y), Number(surface.UvMax.x), Number(surface.UvMax.y)));
                list.Add(new JObject
                {
                    ["object"] = surface.Name, ["material"] = surface.Material != null ? surface.Material.name : null,
                    ["texture"] = textureName, ["area_m2"] = Math.Round(surface.Area, 2),
                    ["period_m"] = surface.Texture != null && !double.IsInfinity(surface.Period) ? Math.Round(surface.Period, 3) : (double?)null,
                    ["repeats"] = surface.Texture != null ? Math.Round(surface.Repeats, 1) : (double?)null,
                    ["density_variation"] = Math.Round(variation, 3),
                    ["stretched_share"] = Math.Round(surface.StretchedArea / Math.Max(surface.Area, 1e-9), 3),
                    ["collapsed_share"] = Math.Round(surface.CollapsedArea / Math.Max(surface.Area, 1e-9), 3),
                    ["inward_share"] = Math.Round(surface.InwardArea / Math.Max(surface.InwardArea + surface.OutwardArea, 1e-9), 3),
                    ["double_sided"] = DoubleSided(surface.Material),
                });
            }
            File.WriteAllText(Path.Combine(OutputFolder, "surfaces.csv"), csv.ToString());
            var overlapList = new JArray(overlaps.Select(overlap => new JObject
            {
                ["first"] = overlap.First, ["second"] = overlap.Second, ["area_m2"] = Math.Round(overlap.Area, 4),
                ["same_facing"] = overlap.SameFacing, ["visible"] = overlap.Visible,
                ["centre"] = new JArray(Math.Round(overlap.Centre.x, 2), Math.Round(overlap.Centre.y, 2), Math.Round(overlap.Centre.z, 2)),
            }));
            File.WriteAllText(Path.Combine(OutputFolder, "surface-audit.json"), new JObject
            {
                ["schema_version"] = 1, ["scene"] = MuseumScene, ["surfaces"] = list, ["coplanar_overlaps"] = overlapList,
            }.ToString());
        }

        /// <summary>Close views of the largest overlaps and of the most repeated large textures.</summary>
        private static void CaptureFindings(List<Surface> surfaces, List<Overlap> overlaps)
        {
            var views = new List<(string name, Vector3 eye, Vector3 target)>();
            foreach (var (overlap, index) in overlaps.Where(overlap => overlap.Visible).Take(6).Select((overlap, index) => (overlap, index)))
            {
                var surface = surfaces.First(candidate => candidate.Name == overlap.First);
                views.Add(("overlap_" + index + "_" + Safe(overlap.First), Eye(surface.Renderer.bounds, overlap.Centre), overlap.Centre));
            }
            foreach (var (surface, index) in surfaces.Where(surface => surface.Texture != null && surface.Area > 4)
                         .OrderByDescending(surface => surface.Repeats).Take(8).Select((surface, index) => (surface, index)))
            {
                var bounds = surface.Renderer.bounds;
                views.Add(("repeat_" + index + "_" + Safe(surface.Name), Eye(bounds, bounds.center), bounds.center));
            }
            MuseumViewCapture.Render(Path.Combine(OutputFolder, "finding"), views);
        }

        private static string Safe(string name) => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        /// <summary>A viewpoint at eye height between the museum centre and a spot, far enough to see its surroundings.</summary>
        private static Vector3 Eye(Bounds bounds, Vector3 spot)
        {
            var outward = spot - new Vector3(0, spot.y, -5);
            outward.y = 0;
            if (outward.sqrMagnitude < .01f) outward = Vector3.forward;
            float distance = Mathf.Clamp(bounds.extents.magnitude, 2, 8);
            var eye = spot - outward.normalized * distance;
            eye.y = Mathf.Max(1.6f, spot.y - 1);
            return eye;
        }
    }

    /// <summary>Renders the open scene from given viewpoints into PNG files.</summary>
    public static class MuseumViewCapture
    {
        public static void Render(string prefix, IEnumerable<(string name, Vector3 eye, Vector3 target)> views, float verticalField = 50,
            int width = 960, int height = 540)
        {
            var cameraObject = new GameObject("AuditCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 800;
            camera.fieldOfView = verticalField;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.Render();
                foreach (var view in views)
                {
                    camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.target - view.eye, Vector3.up));
                    camera.Render();
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.active = previous;
                    File.WriteAllBytes(prefix + "__" + view.name + ".png", image.EncodeToPNG());
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asynchronous;
                camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
