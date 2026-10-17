using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Takes one closed piece out of a delivered mesh that merges several, such as one pedestal of the row the characters
    /// room delivers as a single object: every triangle lying wholly inside a box goes. Vertices, texture and lightmap
    /// coordinates of the rest stay as they are.
    /// </summary>
    public static class MuseumMeshCut
    {
        /// <summary>Removes the triangles whose three corners lie inside <paramref name="box"/>, given in the mesh's own space.</summary>
        public static int RemoveInside(Mesh mesh, Bounds box) => RemoveInside(mesh, Matrix4x4.identity, box);

        /// <summary>Removes the triangles whose three corners, carried by <paramref name="toSpace"/>, lie inside <paramref name="box"/>.</summary>
        public static int RemoveInside(Mesh mesh, Matrix4x4 toSpace, Bounds box)
        {
            if (mesh == null) return 0;
            var vertices = mesh.vertices;
            var inside = new bool[vertices.Length];
            for (int index = 0; index < vertices.Length; index++) inside[index] = box.Contains(toSpace.MultiplyPoint3x4(vertices[index]));
            int removed = 0;
            var kept = new List<int>();
            var submeshes = new List<int[]>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                kept.Clear();
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    if (inside[triangles[index]] && inside[triangles[index + 1]] && inside[triangles[index + 2]]) { removed++; continue; }
                    kept.Add(triangles[index]); kept.Add(triangles[index + 1]); kept.Add(triangles[index + 2]);
                }
                submeshes.Add(kept.ToArray());
            }
            if (removed == 0) return 0;
            for (int submesh = 0; submesh < submeshes.Count; submesh++) mesh.SetTriangles(submeshes[submesh], submesh, false);
            mesh.RecalculateBounds();
            return removed;
        }
    }
}
