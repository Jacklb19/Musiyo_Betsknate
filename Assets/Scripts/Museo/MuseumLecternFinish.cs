using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Colour and finish of the reading stands (owner's decision D1 of 22/10/2026): the stand in a warm dark wood and its
    /// board in the night of the museum's palette. The stands keep the geometry of the museum model; their materials
    /// change at runtime, from copies of the delivered ones, so the shader the build already carries is the one used.
    /// Each stand also takes a larger reading board over its own (owner, 23/10/2026: "haz el atril un poco más grande"),
    /// on the same slope, where the page of the record lies inside the board's edges as on a real lectern. Its size and
    /// the colours are in the museum experience configuration.
    /// </summary>
    public static class MuseumLecternFinish
    {
        /// <summary>Material of the stand model's reading surface.</summary>
        public const string BoardMaterial = "Lectern_Board";
        public const string WoodName = "LecternWood", BoardName = "LecternBoardFace";
        public const float WoodSmoothness = .28f, BoardSmoothness = .42f;
        public const string ReadingBoardName = "ReadingBoard";
        /// <summary>Thickness of the reading board and how far its wooden rim reaches past the board's edges, in metres.</summary>
        public const float BoardThickness = .03f, RimWidth = .025f;
        /// <summary>How far in front of the stand's own board the face of the reading board lies, in metres.</summary>
        public const float FaceOffset = .012f;
        private static readonly Dictionary<(Material source, bool board), Material> finishes = new Dictionary<(Material, bool), Material>();

        /// <summary>The visit is seen through a headset; the reading boards are larger on a screen otherwise.</summary>
        public static bool Spatial => MuseumInput.Current.Spatial;

        /// <summary>
        /// Gives a stand its finish; a stand already finished is left as it is, but for its reading board, which is built
        /// again at the size of a headset or a screen when the visit moves from one to the other.
        /// </summary>
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
            BuildReadingBoard(stand, settings);
        }

        /// <summary>
        /// The reading board's face: the middle of its lower edge, its axes (right across it, up its slope, forward into
        /// it, away from the reader) and its size. Its lower edge is the lower edge of the stand's own board.
        /// </summary>
        public static (Vector3 bottom, Quaternion rotation, Vector2 size) ReadingFace(PointLectern stand, MuseumExperienceConfiguration settings)
        {
            var board = stand.Board;
            var rotation = Quaternion.LookRotation(board.forward, board.up);
            return (board.position - board.forward * FaceOffset, rotation, settings.LecternBoard(Spatial));
        }

        /// <summary>The page that lies on the reading board, inside its margin: the middle of its lower edge and its size.</summary>
        public static (Vector3 bottom, Quaternion rotation, Vector2 size) Page(PointLectern stand, MuseumExperienceConfiguration settings)
        {
            var (bottom, rotation, size) = ReadingFace(stand, settings);
            var up = rotation * Vector3.up;
            var forward = rotation * Vector3.forward;
            float margin = settings.LecternBoardMargin;
            return (bottom + up * margin - forward * settings.LecternScreenGap, rotation, size - Vector2.one * (margin * 2));
        }

        private static void BuildReadingBoard(PointLectern stand, MuseumExperienceConfiguration settings)
        {
            if (stand.Board == null) return;
            var (bottom, rotation, size) = ReadingFace(stand, settings);
            var built = stand.transform.Find(ReadingBoardName);
            if (built != null)
            {
                var builtFace = built.Find("Face");
                if (builtFace != null && Mathf.Abs(builtFace.lossyScale.x - size.x) < .001f) return;
                built.SetParent(null);
                if (Application.isPlaying) Object.Destroy(built.gameObject); else Object.DestroyImmediate(built.gameObject);
            }
            var up = rotation * Vector3.up;
            var forward = rotation * Vector3.forward;
            Material face = null, wood = null;
            foreach (var renderer in stand.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (material.name.StartsWith(BoardName)) face ??= material;
                    else if (material.name.StartsWith(WoodName)) wood ??= material;
                }
            var root = new GameObject(ReadingBoardName).transform;
            root.SetPositionAndRotation(bottom, rotation);
            var middle = bottom + up * (size.y * .5f) + forward * (BoardThickness * .5f);
            Plate(root, "Face", middle, rotation, new Vector3(size.x, size.y, BoardThickness), face);
            Plate(root, "Rim", middle + forward * (BoardThickness * .3f), rotation,
                new Vector3(size.x + RimWidth * 2, size.y + RimWidth * 2, BoardThickness * .7f), wood);
            // The stand's scale is uniform, so the board keeps its size in metres under it.
            root.SetParent(stand.transform, true);
        }

        private static void Plate(Transform parent, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            var collider = plate.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            plate.transform.SetPositionAndRotation(position, rotation);
            plate.transform.localScale = size;
            plate.transform.SetParent(parent, true);
            if (material != null) plate.GetComponent<MeshRenderer>().sharedMaterial = material;
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
