using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Combines the plants of the set dressing into one mesh per area and material when the museum scene enters Play mode
    /// or a build, so a hundred plants cost a handful of draws on the headset. Each vertex carries its plant's data for
    /// the foliage shader: red is the height within the plant (0 at the foot, 1 at the top), green a value proper to the
    /// plant. The saved scene keeps the separate plants; only the processed copy is combined.
    /// </summary>
    public sealed class MuseumFoliageBatch : IProcessSceneWithReport
    {
        /// <summary>Plants south of this line stand along the arrival path; the others in the courtyard planters.</summary>
        public const float ArrivalEdgeZ = -26;
        public static readonly string[] PlantNames = { MuseumSetDressing.MaizeName, MuseumSetDressing.LeavesName, MuseumSetDressing.FlowerName };

        public int callbackOrder => 5;

        public void OnProcessScene(Scene scene, BuildReport report)
            => Combine(scene, report != null && report.summary.platform == BuildTarget.Android);

        /// <summary>Combines the plants of an open scene; returns the number of combined meshes made.</summary>
        public static int Combine(Scene scene, bool headset)
        {
            var root = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(item => item.name == MuseumSetDressing.SceneRoot);
            if (root == null) return 0;
            var plants = root.Cast<Transform>().Where(child => PlantNames.Contains(child.name) && child.gameObject.activeSelf).ToList();
            var parts = new Dictionary<(int area, Material material), Part>();
            for (int index = 0; index < plants.Count; index++)
            {
                var plant = plants[index];
                var renderers = plant.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) continue;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float tone = Hash(index);
                int area = plant.position.z < ArrivalEdgeZ ? 0 : 1;
                foreach (var renderer in renderers)
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var materials = renderer.sharedMaterials;
                    for (int submesh = 0; submesh < mesh.subMeshCount && submesh < materials.Length; submesh++)
                    {
                        var material = Material(materials[submesh], headset);
                        if (!parts.TryGetValue((area, material), out var part)) parts[(area, material)] = part = new Part();
                        part.Add(mesh, submesh, renderer.localToWorldMatrix, bounds.min.y, Mathf.Max(bounds.size.y, .01f), tone);
                    }
                }
            }
            int made = 0;
            foreach (var entry in parts.OrderBy(entry => entry.Key.area).ThenBy(entry => entry.Key.material.name))
            {
                var combined = new GameObject("Foliage_" + (entry.Key.area == 0 ? "Arrival" : "Courtyard") + "_" + entry.Key.material.name);
                combined.transform.SetParent(root, false);
                combined.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                combined.transform.localScale = Vector3.one;
                combined.AddComponent<MeshFilter>().sharedMesh = entry.Value.Build(combined.name);
                var renderer = combined.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = entry.Key.material;
                renderer.receiveGI = ReceiveGI.LightProbes;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                made++;
            }
            foreach (var plant in plants) Object.DestroyImmediate(plant.gameObject);
            return made;
        }

        /// <summary>The headset build uses the material's headset twin where one exists.</summary>
        private static Material Material(Material material, bool headset)
        {
            if (!headset || material == null) return material;
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path)) return material;
            var twin = AssetDatabase.LoadAssetAtPath<Material>(path.Substring(0, path.Length - ".mat".Length) + MuseumSetDressing.HeadsetSuffix + ".mat");
            return twin != null ? twin : material;
        }

        private static float Hash(int index) => Mathf.Repeat(Mathf.Sin(index * 12.9898f + 4.1414f) * 43758.5453f, 1);

        private sealed class Part
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector4> tangents = new List<Vector4>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Color32> colors = new List<Color32>();
            private readonly List<int> triangles = new List<int>();

            public void Add(Mesh mesh, int submesh, Matrix4x4 toWorld, float foot, float height, float tone)
            {
                var sourcePositions = mesh.vertices;
                var sourceNormals = mesh.normals;
                var sourceTangents = mesh.tangents;
                var sourceUvs = mesh.uv;
                var normalMatrix = toWorld.inverse.transpose;
                var used = new Dictionary<int, int>();
                foreach (int index in mesh.GetTriangles(submesh))
                {
                    if (!used.TryGetValue(index, out int copy))
                    {
                        copy = positions.Count;
                        used[index] = copy;
                        var position = toWorld.MultiplyPoint3x4(sourcePositions[index]);
                        positions.Add(position);
                        normals.Add(sourceNormals.Length > 0 ? normalMatrix.MultiplyVector(sourceNormals[index]).normalized : Vector3.up);
                        if (sourceTangents.Length > 0)
                        {
                            var tangent = toWorld.MultiplyVector(sourceTangents[index]).normalized;
                            tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, sourceTangents[index].w));
                        }
                        else tangents.Add(new Vector4(1, 0, 0, 1));
                        uvs.Add(sourceUvs.Length > 0 ? sourceUvs[index] : Vector2.zero);
                        float rise = Mathf.Clamp01((position.y - foot) / height);
                        colors.Add(new Color32((byte)Mathf.RoundToInt(rise * 255), (byte)Mathf.RoundToInt(tone * 255), 0, 255));
                    }
                    triangles.Add(copy);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetTangents(tangents);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
