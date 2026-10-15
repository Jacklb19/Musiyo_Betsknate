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
            return repairs;
        }

        /// <summary>Triangles an object carries after import.</summary>
        public int ExpectedTriangles(string name, int delivered)
            => Cards.Objects.TryGetValue(name, out var card) ? card.Repaired : delivered;

        /// <summary>A material whose every user carries explicit back faces draws one side only.</summary>
        public bool DrawsOneSide(string material) => material == Cards.Material;
    }

    /// <summary>
    /// Gives the listed thin cards of the museum model one consistent face per side: coincident faces are kept once and
    /// every face gets an explicit back face with the reversed normal, so lighting is right from both sides.
    /// </summary>
    public sealed class MuseumDeliveryMeshRepair : AssetPostprocessor
    {
        /// <summary>Raise when the repair changes, so every machine reimports the model.</summary>
        public override uint GetVersion() => 2;

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
            }
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
