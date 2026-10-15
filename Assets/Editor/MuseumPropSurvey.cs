using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Splits grouped set-dressing meshes of the delivered museum into their separate pieces (connected islands), so each
    /// lantern, plant or stalk has a place, a height and a footprint. It reads the scene and writes a report.
    /// </summary>
    public static class MuseumPropSurvey
    {
        public const string OutputFolder = "TestResults/prop-survey";
        public static readonly string[] Groups =
        {
            "Sendero_Faroles_Luz", "Sendero_Faroles_Postes", "Jajan_Maiz", "Jajan_Maiz_Espigas", "Patio_Maiz",
            "Patio_Maiz_Espigas", "Patio_Plantas_Medicinales", "Jajan_Plantas_Medicinales",
        };

        public struct Piece
        {
            public Vector3 Base;
            public Vector3 Size;
            public int Triangles;
        }

        /// <summary>World-space pieces of one renderer's mesh; corners closer than a millimetre are joined.</summary>
        public static List<Piece> Pieces(MeshRenderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var matrix = renderer.localToWorldMatrix;
            var world = mesh.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToArray();
            var canonical = new Dictionary<Vector3Int, int>();
            var parent = new int[world.Length];
            for (int index = 0; index < world.Length; index++)
            {
                var key = Vector3Int.RoundToInt(world[index] * 1000);
                parent[index] = canonical.TryGetValue(key, out var first) ? first : index;
                if (!canonical.ContainsKey(key)) canonical[key] = index;
            }
            int Find(int index) { while (parent[index] != index) index = parent[index] = parent[parent[index]]; return index; }
            var triangles = mesh.triangles;
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int a = Find(triangles[index]), b = Find(triangles[index + 1]), c = Find(triangles[index + 2]);
                parent[b] = a; parent[Find(c)] = a;
            }
            var groups = new Dictionary<int, (Bounds bounds, int triangles)>();
            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int root = Find(triangles[index]);
                var bounds = groups.TryGetValue(root, out var found) ? found.bounds : new Bounds(world[triangles[index]], Vector3.zero);
                for (int corner = 0; corner < 3; corner++) bounds.Encapsulate(world[triangles[index + corner]]);
                groups[root] = (bounds, (found.triangles) + 1);
            }
            return groups.Values.Select(group => new Piece
            {
                Base = new Vector3(group.bounds.center.x, group.bounds.min.y, group.bounds.center.z),
                Size = group.bounds.size, Triangles = group.triangles,
            }).OrderBy(piece => piece.Base.z).ThenBy(piece => piece.Base.x).ToList();
        }

        [MenuItem("Musiyo/Museum Delivery/Survey Set Dressing")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            Directory.CreateDirectory(OutputFolder);
            var report = new StringBuilder("group,piece,base_x,base_y,base_z,size_x,size_y,size_z,triangles\n");
            var views = new List<(string, Vector3, Vector3)>();
            foreach (var name in Groups)
            {
                var renderer = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                    .FirstOrDefault(candidate => candidate.name == name);
                if (renderer == null) { Debug.LogWarning("Missing " + name); continue; }
                var pieces = Pieces(renderer);
                for (int index = 0; index < pieces.Count; index++)
                {
                    var piece = pieces[index];
                    report.AppendLine(FormattableString.Invariant(
                        $"{name},{index},{piece.Base.x:F3},{piece.Base.y:F3},{piece.Base.z:F3},{piece.Size.x:F3},{piece.Size.y:F3},{piece.Size.z:F3},{piece.Triangles}"));
                }
                Debug.Log(name + ": " + pieces.Count + " pieces, " + pieces.Sum(piece => piece.Triangles) + " triangles.");
                var sample = pieces[pieces.Count / 2];
                var target = sample.Base + Vector3.up * Mathf.Max(.4f, sample.Size.y * .6f);
                var eye = target + new Vector3(1.6f, .5f, -2.2f) * Mathf.Max(1, sample.Size.y);
                views.Add((name, eye, target));
            }
            File.WriteAllText(Path.Combine(OutputFolder, "pieces.csv"), report.ToString());
            MusiyoBetsknate.Museum.MuseumAtmosphere.Current.Apply();
            MuseumViewCapture.Render(Path.Combine(OutputFolder, "close"), views, 45);
        }
    }
}
