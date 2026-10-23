using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Colour and finish of the reading stands (owner's decision D1 of 22/10/2026): the stand in a warm dark wood and its
    /// board in the night of the museum's palette, under the screen that rests on it. The stands keep the geometry of the
    /// museum model; only their materials change, at runtime, from copies of the delivered ones, so the shader the build
    /// already carries is the one used. The colours are in the museum experience configuration.
    /// </summary>
    public static class MuseumLecternFinish
    {
        /// <summary>Material of the stand model's reading surface.</summary>
        public const string BoardMaterial = "Lectern_Board";
        public const string WoodName = "LecternWood", BoardName = "LecternBoardFace";
        public const float WoodSmoothness = .28f, BoardSmoothness = .42f;
        private static readonly Dictionary<(Material source, bool board), Material> finishes = new Dictionary<(Material, bool), Material>();

        /// <summary>Gives a stand its finish; a stand already finished is left as it is.</summary>
        public static void Apply(PointLectern stand)
        {
            if (stand == null) return;
            var settings = MuseumExperienceConfiguration.Current;
            foreach (var renderer in stand.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    var source = materials[index];
                    if (source == null || source.name.StartsWith(WoodName) || source.name.StartsWith(BoardName)) continue;
                    bool board = source.name.StartsWith(BoardMaterial);
                    materials[index] = Finish(source, board,
                        MuseumExperienceConfiguration.ColorValue(board ? settings.LecternBoardColor : settings.LecternWoodColor));
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static Material Finish(Material source, bool board, Color colour)
        {
            if (finishes.TryGetValue((source, board), out var finished) && finished != null) return finished;
            finished = new Material(source) { name = board ? BoardName : WoodName };
            finished.SetColor("_BaseColor", colour);
            finished.SetColor("_Color", colour);
            finished.SetFloat("_Smoothness", board ? BoardSmoothness : WoodSmoothness);
            finished.SetFloat("_Glossiness", board ? BoardSmoothness : WoodSmoothness);
            finishes[(source, board)] = finished;
            return finished;
        }
    }
}
