using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Corrections applied while the delivered model is imported, read from <c>Delivery/mesh_repairs.json</c>.</summary>
    public sealed class MuseumMeshRepairs
    {
        public const string AssetPath = MuseumDeliveryManifest.Root + "/Delivery/mesh_repairs.json";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("two_sided_cards")] public CardRepair Cards;
        [JsonProperty("extended_rafters")] public RafterRepair Rafters = new RafterRepair();
        [JsonProperty("coplanar_overlaps")] public OverlapRepair Overlaps = new OverlapRepair();
        [JsonProperty("grounded_posts")] public PostRepair Posts = new PostRepair();
        [JsonProperty("removed_pieces")] public PieceRepair Pieces = new PieceRepair();

        public sealed class PieceRepair
        {
            [JsonProperty("reason")] public string Reason;
            [JsonProperty("pieces")] public List<Piece> Pieces = new List<Piece>();
        }

        /// <summary>A closed piece taken out of a delivered object: every triangle wholly inside a box, model space.</summary>
        public sealed class Piece
        {
            [JsonProperty("object")] public string Object;
            [JsonProperty("box_centre_m")] public float[] Centre;
            [JsonProperty("box_size_m")] public float[] Size;
            [JsonProperty("triangles_removed")] public int Removed;
            [JsonProperty("where")] public string Where;
            public Bounds Box => new Bounds(new Vector3(Centre[0], Centre[1], Centre[2]), new Vector3(Size[0], Size[1], Size[2]));
        }

        public sealed class PostRepair
        {
            [JsonProperty("reason")] public string Reason;
            [JsonProperty("objects")] public Dictionary<string, PostObject> Objects = new Dictionary<string, PostObject>();
        }

        /// <summary>Posts of one object that stop above the floor; heights in metres and plan positions (x, z), model space.</summary>
        public sealed class PostObject
        {
            [JsonProperty("delivered_bottom_m")] public float DeliveredBottom;
            [JsonProperty("repaired_bottom_m")] public float RepairedBottom;
            [JsonProperty("posts_plan_m")] public List<float[]> Plan = new List<float[]>();
            [JsonProperty("where")] public string Where;
        }

        public sealed class OverlapRepair
        {
            /// <summary>Applied in order; each object appears once.</summary>
            [JsonProperty("trims")] public List<Trim> Trims = new List<Trim>();
            [JsonProperty("mitred_bands")] public Dictionary<string, BandObject> Bands = new Dictionary<string, BandObject>();
        }

        /// <summary>
        /// Faces of an object that lie in the plane of faces of other objects give way to them where they overlap: to faces
        /// turned the same way (<see cref="Under"/>), or to faces of either side for objects drawn on both sides
        /// (<see cref="UnderEitherSide"/>). The object itself in a list means its later faces give way to its earlier ones.
        /// </summary>
        public sealed class Trim
        {
            [JsonProperty("object")] public string Object;
            [JsonProperty("under")] public List<string> Under = new List<string>();
            [JsonProperty("under_either_side")] public List<string> UnderEitherSide = new List<string>();
            /// <summary>Triangles when the trim starts (after the card repair, for a card object) and after it.</summary>
            [JsonProperty("triangles_before")] public int Before;
            [JsonProperty("triangles_after")] public int After;
            [JsonProperty("removed_area_m2")] public float RemovedArea;
            [JsonProperty("where")] public string Where;
        }

        /// <summary>A frame of flat strips whose ends overlap at the corners; each corner becomes one mitre joint.</summary>
        public sealed class BandObject
        {
            [JsonProperty("strips")] public int Strips;
            [JsonProperty("mitred_ends")] public int MitredEnds;
            [JsonProperty("where")] public string Where;
        }

        public sealed class RafterRepair
        {
            [JsonProperty("objects")] public Dictionary<string, RafterObject> Objects = new Dictionary<string, RafterObject>();
        }

        /// <summary>Rafters of one object that end short of their beam; distances in metres, model space.</summary>
        public sealed class RafterObject
        {
            [JsonProperty("minimum_height_m")] public float MinimumHeight;
            [JsonProperty("maximum_abs_x_m")] public float MaximumAbsX;
            [JsonProperty("delivered_end_abs_z_m")] public float DeliveredEnd;
            [JsonProperty("repaired_end_abs_z_m")] public float RepairedEnd;
            [JsonProperty("expected_rafters")] public int Expected;
        }

        public sealed class CardRepair
        {
            [JsonProperty("material")] public string Material;
            [JsonProperty("objects")] public Dictionary<string, CardObject> Objects = new Dictionary<string, CardObject>();
        }

        public sealed class CardObject
        {
            [JsonProperty("delivered_triangles")] public int Delivered;
            [JsonProperty("coincident_triangles")] public int Coincident;
            /// <summary>Each face once, front and back.</summary>
            public int Repaired => 2 * (Delivered - Coincident);
        }

        public static MuseumMeshRepairs Load()
        {
            var repairs = JsonConvert.DeserializeObject<MuseumMeshRepairs>(File.ReadAllText(AssetPath));
            if (repairs == null || repairs.SchemaVersion != 1 || repairs.Cards == null || string.IsNullOrEmpty(repairs.Cards.Material))
                throw new InvalidOperationException("Invalid museum mesh repairs: " + AssetPath);
            repairs.Rafters ??= new RafterRepair();
            foreach (var rafters in repairs.Rafters.Objects.Values)
                if (rafters == null || !(rafters.DeliveredEnd > rafters.RepairedEnd) || !(rafters.RepairedEnd > 0) || rafters.Expected <= 0)
                    throw new InvalidOperationException("Invalid rafter repair in " + AssetPath);
            repairs.Pieces ??= new PieceRepair();
            foreach (var piece in repairs.Pieces.Pieces)
                if (piece == null || string.IsNullOrEmpty(piece.Object) || piece.Centre == null || piece.Centre.Length != 3
                    || piece.Size == null || piece.Size.Length != 3 || piece.Removed <= 0)
                    throw new InvalidOperationException("Invalid piece removal in " + AssetPath);
            repairs.Posts ??= new PostRepair();
            foreach (var posts in repairs.Posts.Objects.Values)
                if (posts == null || !(posts.DeliveredBottom > posts.RepairedBottom) || posts.Plan == null || posts.Plan.Count == 0
                    || posts.Plan.Any(spot => spot == null || spot.Length != 2))
                    throw new InvalidOperationException("Invalid post repair in " + AssetPath);
            repairs.Overlaps ??= new OverlapRepair();
            var trimmed = new HashSet<string>();
            foreach (var trim in repairs.Overlaps.Trims)
            {
                if (trim != null) { trim.Under ??= new List<string>(); trim.UnderEitherSide ??= new List<string>(); }
                if (trim == null || string.IsNullOrEmpty(trim.Object) || trim.Under.Count + trim.UnderEitherSide.Count == 0 || !trimmed.Add(trim.Object)
                    || trim.Before <= 0 || trim.After <= 0 || !(trim.RemovedArea > 0))
                    throw new InvalidOperationException("Invalid coplanar trim in " + AssetPath + ": " + trim?.Object);
            }
            foreach (var band in repairs.Overlaps.Bands.Values)
                if (band == null || band.Strips <= 0 || band.MitredEnds <= 0)
                    throw new InvalidOperationException("Invalid mitred band in " + AssetPath);
            return repairs;
        }

        /// <summary>Triangles an object carries after import.</summary>
        public int ExpectedTriangles(string name, int delivered)
            => (Overlaps.Trims.FirstOrDefault(trim => trim.Object == name)?.After
                ?? (Cards.Objects.TryGetValue(name, out var card) ? card.Repaired : delivered))
                - Pieces.Pieces.Where(piece => piece.Object == name).Sum(piece => piece.Removed);

        /// <summary>Whether import trims faces off this object, so a delivered box no longer closes on every side.</summary>
        public bool Trimmed(string name) => Overlaps.Trims.Any(trim => trim.Object == name);

        /// <summary>A material whose every user carries explicit back faces draws one side only.</summary>
        public bool DrawsOneSide(string material) => material == Cards.Material;
    }

    /// <summary>
    /// Applies <c>mesh_repairs.json</c> to the museum model as it is imported: thin cards get one consistent face per side
    /// (coincident faces kept once, each with an explicit back face, so lighting is right from both sides), short rafters
    /// reach their beam, overlapping coplanar faces give way to one another and crossing floor bands are mitred.
    /// </summary>
    public sealed class MuseumDeliveryMeshRepair : AssetPostprocessor
    {
        /// <summary>Raise when the repair changes, so every machine reimports the model.</summary>
        public override uint GetVersion() => 8;

        /// <summary>A change to the repairs reimports the model.</summary>
        private void OnPreprocessModel()
        {
            if (assetPath == MuseumDeliveryManifest.ModelPath) context.DependsOnSourceAsset(MuseumMeshRepairs.AssetPath);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (assetPath != MuseumDeliveryManifest.ModelPath || !File.Exists(MuseumMeshRepairs.AssetPath)) return;
            var repairs = MuseumMeshRepairs.Load();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (repairs.Cards.Objects.TryGetValue(filter.name, out var card))
                {
                    var (delivered, coincident) = MakeTwoSided(filter.sharedMesh);
                    if (delivered != card.Delivered || coincident != card.Coincident)
                        Debug.LogError(filter.name + ": the repair expected " + card.Delivered + " triangles with " + card.Coincident
                            + " coincident, the model has " + delivered + " with " + coincident + ".");
                }
                if (repairs.Rafters.Objects.TryGetValue(filter.name, out var rafters))
                {
                    // The imported root stands at the origin, so its world space is the museum's, unit scale included.
                    int extended = ExtendRafters(filter.sharedMesh, filter.transform.localToWorldMatrix, rafters);
                    if (extended != rafters.Expected)
                        Debug.LogError(filter.name + ": the rafter repair expected " + rafters.Expected + " rafters ending at |z| "
                            + rafters.DeliveredEnd + " m, the model has " + extended + ".");
                }
                foreach (var piece in repairs.Pieces.Pieces.Where(item => item.Object == filter.name))
                {
                    // Boxes are given in the plan of the museum scene; the importer may mirror that plan, so the mirrored box
                    // is tried when the given one holds nothing.
                    var toModel = filter.transform.localToWorldMatrix;
                    int removed = MusiyoBetsknate.Museum.MuseumMeshCut.RemoveInside(filter.sharedMesh, toModel, piece.Box);
                    if (removed == 0)
                    {
                        var mirrored = piece.Box;
                        mirrored.center = new Vector3(-mirrored.center.x, mirrored.center.y, -mirrored.center.z);
                        removed = MusiyoBetsknate.Museum.MuseumMeshCut.RemoveInside(filter.sharedMesh, toModel, mirrored);
                    }
                    if (removed != piece.Removed)
                        Debug.LogError(filter.name + ": the piece removal expected " + piece.Removed + " triangles inside its box, the model has " + removed + ".");
                }
                if (repairs.Posts.Objects.TryGetValue(filter.name, out var posts))
                {
                    int grounded = GroundPosts(filter.sharedMesh, filter.transform.localToWorldMatrix, posts);
                    if (grounded != posts.Plan.Count)
                    {
                        var matrix = filter.transform.localToWorldMatrix;
                        var low = filter.sharedMesh.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex))
                            .Where(point => Mathf.Abs(Mathf.Abs(point.x) - .84f) < .3f && point.y < 1).Take(12).Select(point => point.ToString("F2"));
                        Debug.LogError(filter.name + ": the post repair expected " + posts.Plan.Count + " posts ending " + posts.DeliveredBottom
                            + " m above the floor, the model has " + grounded + ". Low corners near |x| 0.84: " + string.Join(" ", low));
                    }
                }
                if (repairs.Overlaps.Bands.TryGetValue(filter.name, out var band))
                {
                    var (strips, mitred) = MitreBands(filter.sharedMesh, filter.transform.localToWorldMatrix);
                    if (strips != band.Strips || mitred != band.MitredEnds)
                        Debug.LogError(filter.name + ": the band repair expected " + band.Strips + " strips with " + band.MitredEnds
                            + " mitred ends, the model has " + strips + " with " + mitred + ".");
                }
            }
            var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh != null)
                .GroupBy(filter => filter.name).ToDictionary(group => group.Key, group => group.First());
            foreach (var trim in repairs.Overlaps.Trims)
            {
                if (!filters.TryGetValue(trim.Object, out var filter)) { Debug.LogError("The coplanar trim names a missing object: " + trim.Object); continue; }
                var cutters = new List<Cutter>();
                bool? self = null;
                foreach (var (names, eitherSide) in new[] { (trim.Under, false), (trim.UnderEitherSide, true) })
                    foreach (var name in names)
                    {
                        if (name == trim.Object) { self = eitherSide; continue; }
                        if (!filters.TryGetValue(name, out var under)) { Debug.LogError("The coplanar trim of " + trim.Object + " names a missing object: " + name); continue; }
                        cutters.AddRange(ModelFaces(under).Select(face => new Cutter(face, eitherSide)));
                    }
                var (before, after, removed) = TrimCoplanar(filter.sharedMesh, filter.transform.localToWorldMatrix, cutters, self);
                if (before != trim.Before || after != trim.After || Mathf.Abs(removed - trim.RemovedArea) > .001f + trim.RemovedArea * .01f)
                    Debug.LogError(trim.Object + ": the coplanar trim expected " + trim.Before + " triangles becoming " + trim.After + " with "
                        + trim.RemovedArea + " m2 removed; the model gives " + before + " becoming " + after + " with " + removed.ToString("F4") + " m2.");
            }
        }

        /// <summary>A face other faces give way to; with <see cref="EitherSide"/>, faces turned the other way give way too.</summary>
        public readonly struct Cutter
        {
            public readonly (Vector3 a, Vector3 b, Vector3 c) Face;
            public readonly bool EitherSide;
            public Cutter((Vector3 a, Vector3 b, Vector3 c) face, bool eitherSide) { Face = face; EitherSide = eitherSide; }
        }

        /// <summary>Every triangle of an object in the model's space.</summary>
        private static IEnumerable<(Vector3 a, Vector3 b, Vector3 c)> ModelFaces(MeshFilter filter)
        {
            var mesh = filter.sharedMesh;
            var matrix = filter.transform.localToWorldMatrix;
            var model = mesh.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToArray();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                    yield return (model[triangles[index]], model[triangles[index + 1]], model[triangles[index + 2]]);
            }
        }

        /// <summary>Faces closer than this to the same plane are drawn on top of each other (as in the surface audit).</summary>
        private const float PlaneTolerance = .004f;

        /// <summary>
        /// Removes from a mesh the parts of its faces that lie in the plane of a cutter face and overlap it, so only the cutter
        /// is drawn there. A face that is partly covered is split into the uncovered convex pieces; every new corner takes the
        /// attributes of the face it came from at that spot (normal, tangent, colour and all UV channels), so the texture and
        /// the lightmap layout do not move. With <paramref name="self"/> set, each face also gives way to the earlier faces of
        /// the same mesh (of either side when it is true). Submeshes keep their order. Returns the triangle counts before and
        /// after and the area removed, in m².
        /// </summary>
        public static (int before, int after, float removed) TrimCoplanar(Mesh mesh, Matrix4x4 toModel, List<Cutter> cutters, bool? self)
        {
            var vertices = new List<Vector3>(mesh.vertices);
            var normals = new List<Vector3>(mesh.normals);
            var tangents = new List<Vector4>(mesh.tangents);
            var colors = new List<Color32>(mesh.colors32);
            int original = vertices.Count;
            var channels = new List<List<Vector4>>();
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>();
                mesh.GetUVs(channel, values);
                channels.Add(values.Count == original ? values : null);
            }
            var model = vertices.Select(vertex => toModel.MultiplyPoint3x4(vertex)).ToArray();
            var earlier = new List<Cutter>();
            int before = 0, after = 0;
            double removed = 0;
            var submeshes = new List<List<int>>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var source = mesh.GetTriangles(submesh);
                var result = new List<int>(source.Length);
                for (int index = 0; index + 2 < source.Length; index += 3)
                {
                    before++;
                    int i0 = source[index], i1 = source[index + 1], i2 = source[index + 2];
                    Vector3 a = model[i0], b = model[i1], c = model[i2];
                    var pieces = Uncovered(a, b, c, self.HasValue ? cutters.Concat(earlier) : cutters, out double area, out double left);
                    if (self.HasValue) earlier.Add(new Cutter((a, b, c), self.Value));
                    if (pieces == null) { result.Add(i0); result.Add(i1); result.Add(i2); after++; continue; }
                    removed += area - left;
                    foreach (var piece in pieces)
                        for (int corner = 1; corner + 1 < piece.Count; corner++)
                        {
                            foreach (var weights in new[] { piece[0], piece[corner], piece[corner + 1] })
                            {
                                result.Add(vertices.Count);
                                Blend(weights, i0, i1, i2, vertices, normals, tangents, colors, channels);
                            }
                            after++;
                        }
                }
                submeshes.Add(result);
            }
            if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            if (normals.Count == vertices.Count) mesh.SetNormals(normals);
            if (tangents.Count == vertices.Count) mesh.SetTangents(tangents);
            if (colors.Count == vertices.Count) mesh.SetColors(colors);
            for (int channel = 0; channel < channels.Count; channel++)
                if (channels[channel] != null) mesh.SetUVs(channel, channels[channel]);
            for (int submesh = 0; submesh < submeshes.Count; submesh++) mesh.SetTriangles(submeshes[submesh], submesh, false);
            mesh.RecalculateBounds();
            return (before, after, (float)removed);
        }

        /// <summary>Adds one vertex with the attributes of a face at the given barycentric weights.</summary>
        private static void Blend(Vector3 weights, int i0, int i1, int i2, List<Vector3> vertices, List<Vector3> normals, List<Vector4> tangents,
            List<Color32> colors, List<List<Vector4>> channels)
        {
            vertices.Add(vertices[i0] * weights.x + vertices[i1] * weights.y + vertices[i2] * weights.z);
            if (normals.Count > i2)
                normals.Add((normals[i0] * weights.x + normals[i1] * weights.y + normals[i2] * weights.z).normalized);
            if (tangents.Count > i2)
            {
                var tangent = (Vector3)(tangents[i0] * weights.x + tangents[i1] * weights.y + tangents[i2] * weights.z);
                tangents.Add(new Vector4(tangent.normalized.x, tangent.normalized.y, tangent.normalized.z, tangents[i0].w));
            }
            if (colors.Count > i2)
                colors.Add((Color)colors[i0] * weights.x + (Color)colors[i1] * weights.y + (Color)colors[i2] * weights.z);
            foreach (var values in channels)
                values?.Add(values[i0] * weights.x + values[i1] * weights.y + values[i2] * weights.z);
        }

        /// <summary>
        /// The parts of a triangle that no coplanar cutter covers, as convex polygons of barycentric weights, or null when no
        /// cutter overlaps it. Areas are in m².
        /// </summary>
        private static List<List<Vector3>> Uncovered(Vector3 a, Vector3 b, Vector3 c, IEnumerable<Cutter> cutters,
            out double area, out double left)
        {
            var cross = Vector3.Cross(b - a, c - a);
            area = cross.magnitude * .5;
            left = area;
            if (area < 1e-8) return null;
            var normal = cross.normalized;
            var x = (b - a).normalized;
            var y = Vector3.Cross(normal, x);
            Vector2 Flat(Vector3 point) => new Vector2(Vector3.Dot(point - a, x), Vector3.Dot(point - a, y));
            Vector2 fa = Vector2.zero, fb = Flat(b), fc = Flat(c);
            var low = Vector2.Min(fa, Vector2.Min(fb, fc));
            var high = Vector2.Max(fa, Vector2.Max(fb, fc));
            var covers = new List<List<Vector2>>();
            foreach (var entry in cutters)
            {
                var cutter = entry.Face;
                var other = Vector3.Cross(cutter.b - cutter.a, cutter.c - cutter.a);
                if (other.sqrMagnitude < 1e-12f) continue;
                float facing = Vector3.Dot(normal, other.normalized);
                if (Mathf.Abs(facing) < .9995f || !entry.EitherSide && facing < 0) continue;
                if (Mathf.Abs(Vector3.Dot(cutter.a - a, normal)) > PlaneTolerance || Mathf.Abs(Vector3.Dot(cutter.b - a, normal)) > PlaneTolerance
                    || Mathf.Abs(Vector3.Dot(cutter.c - a, normal)) > PlaneTolerance) continue;
                var polygon = new List<Vector2> { Flat(cutter.a), Flat(cutter.b), Flat(cutter.c) };
                var cutLow = Vector2.Min(polygon[0], Vector2.Min(polygon[1], polygon[2]));
                var cutHigh = Vector2.Max(polygon[0], Vector2.Max(polygon[1], polygon[2]));
                if (cutLow.x >= high.x || cutLow.y >= high.y || cutHigh.x <= low.x || cutHigh.y <= low.y) continue;
                if (Signed(polygon) < 0) polygon.Reverse();
                covers.Add(polygon);
            }
            if (covers.Count == 0) return null;
            MergeConvex(covers);
            var pieces = new List<List<Vector2>> { new List<Vector2> { fa, fb, fc } };
            bool cut = false;
            foreach (var cover in covers)
            {
                var next = new List<List<Vector2>>();
                foreach (var piece in pieces)
                {
                    var outside = Subtract(piece, cover);
                    if (outside == null) { next.Add(piece); continue; }
                    cut = true;
                    next.AddRange(outside);
                }
                pieces = next;
            }
            if (!cut) return null;
            left = pieces.Sum(piece => Math.Abs(Signed(piece)));
            float twice = fb.x * fc.y - fc.x * fb.y;
            Vector3 Weights(Vector2 point)
            {
                float wb = (point.x * fc.y - fc.x * point.y) / twice;
                float wc = (fb.x * point.y - point.x * fb.y) / twice;
                return new Vector3(1 - wb - wc, wb, wc);
            }
            return pieces.Select(piece => piece.Select(Weights).ToList()).ToList();
        }

        /// <summary>Joins pairs of covering triangles that share an edge into one convex quad, so a covered rectangle cuts once.</summary>
        private static void MergeConvex(List<List<Vector2>> covers)
        {
            for (int i = 0; i < covers.Count; i++)
                for (int j = i + 1; j < covers.Count; j++)
                {
                    if (covers[i].Count != 3 || covers[j].Count != 3) continue;
                    var shared = covers[i].Where(corner => covers[j].Any(other => (other - corner).sqrMagnitude < 1e-10f)).ToList();
                    if (shared.Count != 2) continue;
                    var apart = covers[j].First(corner => shared.All(other => (other - corner).sqrMagnitude >= 1e-10f));
                    // Insert the far corner of the second triangle between the shared corners of the first.
                    var quad = new List<Vector2>(covers[i]);
                    for (int k = 0; k < 3; k++)
                    {
                        var p = quad[k];
                        var q = quad[(k + 1) % 3];
                        bool edge = shared.Any(s => (s - p).sqrMagnitude < 1e-10f) && shared.Any(s => (s - q).sqrMagnitude < 1e-10f);
                        if (!edge) continue;
                        quad.Insert(k + 1, apart);
                        break;
                    }
                    if (quad.Count != 4 || !Convex(quad)) continue;
                    covers[i] = quad;
                    covers.RemoveAt(j);
                    j = i;
                }
        }

        private static bool Convex(List<Vector2> polygon)
        {
            for (int k = 0; k < polygon.Count; k++)
                if (Side(polygon[k], polygon[(k + 1) % polygon.Count], polygon[(k + 2) % polygon.Count]) < -1e-7f) return false;
            return true;
        }

        /// <summary>
        /// A convex polygon minus a convex cover, as convex pieces; null when they do not overlap by a measurable area. Both
        /// polygons wind counter-clockwise.
        /// </summary>
        private static List<List<Vector2>> Subtract(List<Vector2> piece, List<Vector2> cover)
        {
            var inside = piece;
            for (int edge = 0; edge < cover.Count && inside.Count >= 3; edge++)
                inside = Clip(inside, cover[edge], cover[(edge + 1) % cover.Count], true);
            if (inside.Count < 3 || Math.Abs(Signed(inside)) < 1e-6) return null;
            var outside = new List<List<Vector2>>();
            var remaining = piece;
            for (int edge = 0; edge < cover.Count && remaining.Count >= 3; edge++)
            {
                Vector2 p = cover[edge], q = cover[(edge + 1) % cover.Count];
                var beyond = Clip(remaining, p, q, false);
                if (beyond.Count >= 3 && Math.Abs(Signed(beyond)) > 1e-7) outside.Add(beyond);
                remaining = Clip(remaining, p, q, true);
            }
            return outside;
        }

        /// <summary>The part of a convex polygon to the left of (or, with <paramref name="left"/> false, to the right of) the line p→q.</summary>
        private static List<Vector2> Clip(List<Vector2> polygon, Vector2 p, Vector2 q, bool left)
        {
            var result = new List<Vector2>();
            float Signed(Vector2 point) => left ? Side(p, q, point) : -Side(p, q, point);
            for (int k = 0; k < polygon.Count; k++)
            {
                var current = polygon[k];
                var previous = polygon[(k + polygon.Count - 1) % polygon.Count];
                float now = Signed(current), before = Signed(previous);
                bool inside = now >= 0, wasInside = before >= 0;
                if (inside != wasInside) result.Add(previous + (current - previous) * (before / (before - now)));
                if (inside) result.Add(current);
            }
            return result;
        }

        private static float Side(Vector2 p, Vector2 q, Vector2 point) => (q.x - p.x) * (point.y - p.y) - (q.y - p.y) * (point.x - p.x);

        private static double Signed(List<Vector2> polygon)
        {
            double sum = 0;
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var b = polygon[(k + 1) % polygon.Count];
                sum += (double)a.x * b.y - (double)b.x * a.y;
            }
            return sum * .5;
        }

        /// <summary>
        /// Turns a frame of flat, axis-aligned strips whose ends overlap at the corners into mitred joints: at each corner both
        /// strips run to the outer corner along their outer edge and stop at the inner corner along their inner edge, so they
        /// meet on the diagonal, once, and the notch the overlap left at the outer corner is filled. The colour UVs of a moved
        /// corner continue the strip's own mapping; the lightmap UVs stay as delivered (the bands take probe lighting).
        /// Returns the number of strips and of strip ends mitred.
        /// </summary>
        public static (int strips, int mitred) MitreBands(Mesh mesh, Matrix4x4 toModel)
        {
            var vertices = mesh.vertices;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var fromModel = toModel.inverse;
            var model = vertices.Select(vertex => toModel.MultiplyPoint3x4(vertex)).ToArray();
            var strips = Islands(mesh, model).Where(island => island.Count >= 4).Select(island => new Strip(island, model)).ToList();
            int mitred = 0;
            var moved = (Vector3[])model.Clone();
            var maps = strips.ToDictionary(strip => strip, strip => strip.UvMap(model, uvs));
            foreach (var strip in strips)
            {
                // Long axis 0 = x, 1 = z.
                int along = strip.Size(0) >= strip.Size(1) ? 0 : 1, across = 1 - along;
                foreach (bool high in new[] { false, true })
                {
                    float end = high ? strip.Max[along] : strip.Min[along];
                    var partner = strips.FirstOrDefault(other => other != strip && other.Size(across) > other.Size(along)
                        && other.Min[along] - 1e-4f <= end && end <= other.Max[along] + 1e-4f
                        && other.Min[across] < strip.Max[across] && strip.Min[across] < other.Max[across]);
                    if (partner == null) continue;
                    float middle = (strip.Min[along] + strip.Max[along]) / 2;
                    bool partnerLow = Mathf.Abs(partner.Min[along] - middle) < Mathf.Abs(partner.Max[along] - middle);
                    float outerAlong = partnerLow ? partner.Max[along] : partner.Min[along];
                    float innerAlong = partnerLow ? partner.Min[along] : partner.Max[along];
                    float partnerMiddle = (partner.Min[across] + partner.Max[across]) / 2;
                    bool innerLow = Mathf.Abs(strip.Min[across] - partnerMiddle) < Mathf.Abs(strip.Max[across] - partnerMiddle);
                    float innerAcross = innerLow ? strip.Min[across] : strip.Max[across];
                    var map = maps[strip];
                    foreach (int index in strip.Corners.Where(index => Mathf.Abs(Get(model[index], along) - end) < 1e-4f))
                    {
                        bool inner = Mathf.Abs(Get(model[index], across) - innerAcross) < 1e-4f;
                        moved[index] = Set(moved[index], along, inner ? innerAlong : outerAlong);
                        if (map != null) uvs[index] = map(moved[index]);
                    }
                    mitred++;
                }
            }
            for (int index = 0; index < vertices.Length; index++) vertices[index] = fromModel.MultiplyPoint3x4(moved[index]);
            mesh.vertices = vertices;
            if (uvs.Count == vertices.Length) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return (strips.Count, mitred);
        }

        private static float Get(Vector3 point, int axis) => axis == 0 ? point.x : point.z;
        private static Vector3 Set(Vector3 point, int axis, float value) => axis == 0 ? new Vector3(value, point.y, point.z) : new Vector3(point.x, point.y, value);

        /// <summary>One flat strip of a band: its corners and its extent in plan (x, z).</summary>
        private sealed class Strip
        {
            public readonly List<int> Corners;
            public readonly float[] Min, Max;

            public Strip(List<int> corners, Vector3[] model)
            {
                Corners = corners;
                Min = new[] { corners.Min(index => model[index].x), corners.Min(index => model[index].z) };
                Max = new[] { corners.Max(index => model[index].x), corners.Max(index => model[index].z) };
            }

            public float Size(int axis) => Max[axis] - Min[axis];

            /// <summary>The strip's colour mapping as a function of plan position, from three corners that span it.</summary>
            public Func<Vector3, Vector2> UvMap(Vector3[] model, List<Vector2> uvs)
            {
                if (uvs.Count != model.Length) return null;
                int o = Corners[0];
                int p = Corners.OrderByDescending(index => (model[index] - model[o]).sqrMagnitude).First();
                int q = Corners.OrderByDescending(index => Mathf.Abs(Flat(model[p] - model[o]).x * Flat(model[index] - model[o]).y
                    - Flat(model[p] - model[o]).y * Flat(model[index] - model[o]).x)).First();
                Vector2 e1 = Flat(model[p] - model[o]), e2 = Flat(model[q] - model[o]);
                float det = e1.x * e2.y - e2.x * e1.y;
                if (Mathf.Abs(det) < 1e-8f) return null;
                Vector2 u1 = uvs[p] - uvs[o], u2 = uvs[q] - uvs[o];
                Vector2 origin = Flat(model[o]), start = uvs[o];
                return point =>
                {
                    var d = Flat(point) - origin;
                    float s = (d.x * e2.y - e2.x * d.y) / det, t = (e1.x * d.y - d.x * e1.y) / det;
                    return start + u1 * s + u2 * t;
                };
            }

            private static Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);
        }

        /// <summary>
        /// Moves the inner end face of each listed rafter rigidly along the rafter's own slope, from the delivered end to the
        /// repaired one. A rafter is a connected piece of the mesh above the minimum height, running along z and no wider than
        /// a beam in x. Its section, normals and outer end stay as delivered; the colour UVs of the moved corners continue the
        /// rafter's own mapping, so the wood keeps its scale. Returns the number of rafters moved.
        /// </summary>
        public static int ExtendRafters(Mesh mesh, Matrix4x4 toModel, MuseumMeshRepairs.RafterObject rafters)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var fromModel = toModel.inverse;
            var model = vertices.Select(vertex => toModel.MultiplyPoint3x4(vertex)).ToArray();
            int moved = 0;
            foreach (var island in Islands(mesh, model))
            {
                float minY = island.Min(index => model[index].y);
                float minX = island.Min(index => model[index].x), maxX = island.Max(index => model[index].x);
                float inner = island.Min(index => Mathf.Abs(model[index].z)), outer = island.Max(index => Mathf.Abs(model[index].z));
                if (minY < rafters.MinimumHeight || maxX - minX > .2f || outer - inner < 3) continue;
                if (Mathf.Abs((minX + maxX) / 2) > rafters.MaximumAbsX || Mathf.Abs(inner - rafters.DeliveredEnd) > .02f) continue;
                float sign = Mathf.Sign(model[island[0]].z);
                // The top edge gives the slope: the highest corner at each end.
                int topInner = island.Where(index => Mathf.Abs(Mathf.Abs(model[index].z) - inner) < .005f).OrderByDescending(index => model[index].y).First();
                int topOuter = island.Where(index => Mathf.Abs(Mathf.Abs(model[index].z) - outer) < .005f).OrderByDescending(index => model[index].y).First();
                var along = model[topOuter] - model[topInner];
                float length = Mathf.Abs(along.z);
                float distance = rafters.DeliveredEnd - rafters.RepairedEnd;
                var shift = -along * (distance / length);
                // The end face is square to the rafter, so its corners lie within a few centimetres of the delivered end.
                var end = island.Where(index => Mathf.Abs(model[index].z) < inner + .1f).ToList();
                var original = model.ToArray();
                foreach (int index in end)
                {
                    int partner = -1;
                    if (uvs.Count == vertices.Length && normals.Length == vertices.Length)
                    {
                        var wanted = original[index] + along;
                        float best = .01f;
                        foreach (int candidate in island)
                        {
                            if (Vector3.Dot(normals[candidate], normals[index]) < .99f) continue;
                            float gap = Vector3.Distance(original[candidate], wanted);
                            if (gap < best) { best = gap; partner = candidate; }
                        }
                    }
                    if (partner >= 0) uvs[index] += (uvs[index] - uvs[partner]) * (distance / length);
                    model[index] += shift;
                    vertices[index] = fromModel.MultiplyPoint3x4(model[index]);
                }
                if (Mathf.Sign(model[end[0]].z) != sign) throw new InvalidOperationException("A rafter repair crossed the centre line.");
                moved++;
            }
            mesh.vertices = vertices;
            if (uvs.Count == vertices.Length) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return moved;
        }

        /// <summary>
        /// Lowers the bottom face of each listed post from the delivered height to the repaired one, so a door post that was
        /// modelled to stand on a stone plinth reaches the floor where the doorway has no plinth. The bottom face is every
        /// corner at the delivered height within 0.12 m, in plan, of a listed position; plan coordinates are matched by their
        /// size, since the importer may mirror an axis. Section, normals and top stay as delivered; the colour UVs of the
        /// lowered corners continue the post's own mapping up the same side. Returns the number of posts lowered.
        /// </summary>
        public static int GroundPosts(Mesh mesh, Matrix4x4 toModel, MuseumMeshRepairs.PostObject posts)
        {
            const float Reach = .12f;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var fromModel = toModel.inverse;
            var model = vertices.Select(vertex => toModel.MultiplyPoint3x4(vertex)).ToArray();
            float distance = posts.DeliveredBottom - posts.RepairedBottom;
            bool Near(Vector3 point, float[] spot) => Mathf.Abs(Mathf.Abs(point.x) - Mathf.Abs(spot[0])) < Reach
                && Mathf.Abs(Mathf.Abs(point.z) - Mathf.Abs(spot[1])) < Reach;
            int grounded = 0;
            var claimed = new HashSet<int>();
            foreach (var spot in posts.Plan)
            {
                // Mirrored or not, each listed post is the one on its own side of the centre line.
                var bottom = Enumerable.Range(0, model.Length).Where(index => !claimed.Contains(index)
                    && Mathf.Abs(model[index].y - posts.DeliveredBottom) < .01f && Near(model[index], spot)).ToList();
                if (bottom.Count == 0) continue;
                float side = Mathf.Sign(spot[0]);
                var own = bottom.Where(index => Mathf.Sign(model[index].x) == side).ToList();
                if (own.Count == 0) own = bottom.Where(index => Mathf.Sign(model[index].x) == -side).ToList();
                if (own.Count == 0) continue;
                var column = Enumerable.Range(0, model.Length).Where(index => Near(model[index], spot)
                    && Mathf.Sign(model[index].x) == Mathf.Sign(model[own[0]].x)).ToList();
                foreach (int index in own)
                {
                    claimed.Add(index);
                    if (uvs.Count == vertices.Length && normals.Length == vertices.Length)
                    {
                        // The highest corner straight above on the same side face gives the way the texture runs.
                        int partner = column.Where(candidate => candidate != index && Vector3.Dot(normals[candidate], normals[index]) > .99f
                                && new Vector2(model[candidate].x - model[index].x, model[candidate].z - model[index].z).magnitude < .005f
                                && model[candidate].y > model[index].y + .5f)
                            .OrderByDescending(candidate => model[candidate].y).DefaultIfEmpty(-1).First();
                        if (partner >= 0)
                            uvs[index] += (uvs[index] - uvs[partner]) * (distance / (model[partner].y - model[index].y));
                    }
                    model[index] += Vector3.down * distance;
                    vertices[index] = fromModel.MultiplyPoint3x4(model[index]);
                }
                grounded++;
            }
            mesh.vertices = vertices;
            if (uvs.Count == vertices.Length) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return grounded;
        }

        /// <summary>Connected pieces of a mesh; corners closer than a millimetre belong together.</summary>
        private static List<List<int>> Islands(Mesh mesh, Vector3[] model)
        {
            var parent = Enumerable.Range(0, model.Length).ToArray();
            int Find(int index) { while (parent[index] != index) index = parent[index] = parent[parent[index]]; return index; }
            void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
            var seen = new Dictionary<Vector3Int, int>();
            for (int index = 0; index < model.Length; index++)
            {
                var key = Vector3Int.RoundToInt(model[index] * 1000);
                if (seen.TryGetValue(key, out var first)) Join(first, index); else seen[key] = index;
            }
            var triangles = mesh.triangles;
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                Join(triangles[index], triangles[index + 1]);
                Join(triangles[index], triangles[index + 2]);
            }
            return Enumerable.Range(0, model.Length).GroupBy(Find).Select(group => group.ToList()).ToList();
        }

        /// <summary>Returns the delivered triangle count and how many of them repeated an earlier face.</summary>
        public static (int delivered, int coincident) MakeTwoSided(Mesh mesh)
        {
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var channels = new List<List<Vector4>>();
            for (int channel = 0; channel < 8; channel++)
            {
                var values = new List<Vector4>();
                mesh.GetUVs(channel, values);
                channels.Add(values.Count == positions.Length ? values : null);
            }
            var colors = mesh.colors32;
            var newPositions = new List<Vector3>(positions);
            var newNormals = new List<Vector3>(normals);
            var newTangents = new List<Vector4>(tangents);
            var newColors = new List<Color32>(colors);
            var newChannels = channels.Select(values => values != null ? new List<Vector4>(values) : null).ToList();
            var back = new Dictionary<int, int>();
            int Back(int index)
            {
                if (back.TryGetValue(index, out var copy)) return copy;
                copy = newPositions.Count;
                newPositions.Add(positions[index]);
                if (normals.Length > 0) newNormals.Add(-normals[index]);
                // The back face mirrors the surface frame, so the tangent's handedness turns with it.
                if (tangents.Length > 0) newTangents.Add(new Vector4(tangents[index].x, tangents[index].y, tangents[index].z, -tangents[index].w));
                if (colors.Length > 0) newColors.Add(colors[index]);
                for (int channel = 0; channel < newChannels.Count; channel++)
                    newChannels[channel]?.Add(channels[channel][index]);
                back[index] = copy;
                return copy;
            }

            int delivered = 0, coincident = 0;
            var submeshes = new List<int[]>();
            var seen = new HashSet<(Vector3Int, Vector3Int, Vector3Int)>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var source = mesh.GetTriangles(submesh);
                var result = new List<int>(source.Length * 2);
                for (int index = 0; index + 2 < source.Length; index += 3)
                {
                    delivered++;
                    int a = source[index], b = source[index + 1], c = source[index + 2];
                    if (!seen.Add(Key(positions[a], positions[b], positions[c]))) { coincident++; continue; }
                    result.Add(a); result.Add(b); result.Add(c);
                    result.Add(Back(a)); result.Add(Back(c)); result.Add(Back(b));
                }
                submeshes.Add(result.ToArray());
            }
            if (newPositions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(newPositions);
            if (normals.Length > 0) mesh.SetNormals(newNormals);
            if (tangents.Length > 0) mesh.SetTangents(newTangents);
            if (colors.Length > 0) mesh.SetColors(newColors);
            for (int channel = 0; channel < newChannels.Count; channel++)
                if (newChannels[channel] != null) mesh.SetUVs(channel, newChannels[channel]);
            for (int submesh = 0; submesh < submeshes.Count; submesh++) mesh.SetTriangles(submeshes[submesh], submesh, false);
            mesh.RecalculateBounds();
            return (delivered, coincident);
        }

        /// <summary>The same three corners in any order, to a tenth of a millimetre, are the same face.</summary>
        private static (Vector3Int, Vector3Int, Vector3Int) Key(Vector3 a, Vector3 b, Vector3 c)
        {
            var corners = new[] { Quantize(a), Quantize(b), Quantize(c) }
                .OrderBy(corner => corner.x).ThenBy(corner => corner.y).ThenBy(corner => corner.z).ToArray();
            return (corners[0], corners[1], corners[2]);
        }

        private static Vector3Int Quantize(Vector3 point)
            => new Vector3Int(Mathf.RoundToInt(point.x * 10000), Mathf.RoundToInt(point.y * 10000), Mathf.RoundToInt(point.z * 10000));
    }
}
