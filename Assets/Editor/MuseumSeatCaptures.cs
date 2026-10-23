using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MusiyoBetsknate.Museum;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Pictures of every seat of the museum (plank benches, stools, the hearth's benches and any marked seat) and of the
    /// entrance gate, with the world built as a headset build builds it, so the seats and the gate can be judged before and
    /// after a change. Each seat is seen from its front and from above, and a list says where it stands and what is near
    /// it. Writes to MUSIYO_CAPTURE_DIR (or TestResults/seats) with MUSIYO_CAPTURE_LABEL in the names; the scene is never
    /// saved. Batch, with graphics: -executeMethod MusiyoBetsknate.Editor.MuseumSeatCaptures.Capture.
    /// </summary>
    public static class MuseumSeatCaptures
    {
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/seats";
        private static string Label => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_LABEL") is string label && label.Length > 0
            ? label : "now";

        public static void Capture()
        {
            Directory.CreateDirectory(Folder);
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            try
            {
                // The same steps as a build, in its order.
                MuseumWorldDressing.Apply(scene, true);
                MuseumSurfaceProfiles.Apply(scene, "quest");
                MuseumFoliageBatch.Combine(scene, true);
                MuseumAtmosphere.Current.Apply(true);
                Physics.SyncTransforms();
                var seats = MuseumSeating.FindSeats();
                var views = new List<(string name, Vector3 eye, Vector3 target)>();
                var list = new StringBuilder();
                int index = 0;
                foreach (var seat in seats.OrderBy(item => item.Top.z).ThenBy(item => item.Top.x))
                {
                    string name = FormattableString.Invariant($"{index++:D2}_{seat.Name}_{seat.Top.x:F1}_{seat.Top.z:F1}").Replace(' ', '_');
                    views.Add(("seat_" + name + "_front", seat.Top + seat.Facing * 2.4f + Vector3.up * 1.25f, seat.Top + Vector3.down * .1f));
                    views.Add(("seat_" + name + "_above", seat.Top + Vector3.up * 5.5f - seat.Facing * .6f, seat.Top));
                    list.AppendLine(FormattableString.Invariant(
                        $"{name}: top {seat.Top.x:F2} {seat.Top.y:F2} {seat.Top.z:F2}, facing {seat.Facing.x:F2} {seat.Facing.z:F2}, length {seat.Along.magnitude * 2:F2} m, hearth {seat.Hearth}, {Surroundings(seat)}"));
                }
                // The doorway of the instruments room, from the corridor and from inside.
                views.Add(("instruments_door_from_corridor", new Vector3(14.5f, 1.6f, -6.5f), new Vector3(20.5f, .6f, -9.4f)));
                views.Add(("instruments_door_from_inside", new Vector3(22.5f, 1.6f, -3.2f), new Vector3(18.5f, .6f, -9.6f)));
                var gate = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(item => item.name.StartsWith("Portada_")).Select(item => item.bounds).ToList();
                if (gate.Count > 0)
                {
                    var bounds = gate[0];
                    foreach (var item in gate) bounds.Encapsulate(item);
                    var middle = bounds.center;
                    list.AppendLine(FormattableString.Invariant($"gate: centre {middle.x:F2} {middle.y:F2} {middle.z:F2}, size {bounds.size.x:F2} {bounds.size.y:F2} {bounds.size.z:F2}"));
                    var floor = new Vector3(middle.x, 0, middle.z);
                    views.Add(("gate_far", floor + new Vector3(-1.2f, 1.65f, -9), floor + Vector3.up * 2.6f));
                    views.Add(("gate_near", floor + new Vector3(-.6f, 1.65f, -4.2f), floor + Vector3.up * 2.7f));
                    views.Add(("gate_side", floor + new Vector3(4.5f, 1.65f, -3.5f), floor + Vector3.up * 2.2f));
                    views.Add(("gate_under", floor + new Vector3(.2f, 1.65f, -1.4f), floor + new Vector3(0, 3.1f, .2f)));
                    views.Add(("gate_from_inside", floor + new Vector3(.8f, 1.65f, 5), floor + Vector3.up * 2.4f));
                }
                File.WriteAllText(Path.Combine(Folder, Label + "_seats.txt"), list.ToString(), new UTF8Encoding(false));
                MuseumViewCapture.Render(Path.Combine(Folder, Label), views, 62, 1280, 760, .05f, 600);
                Debug.Log("Seat pictures written: " + views.Count + " views, " + seats.Count + " seats.");
            }
            finally { EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single); }
        }

        /// <summary>What stands close to a seat: walls and pieces within half a metre, and whether its top is over a floor.</summary>
        private static string Surroundings(SeatSpot seat)
        {
            var near = Physics.OverlapBox(seat.Top + Vector3.down * .2f, new Vector3(Mathf.Max(.3f, seat.Along.magnitude) + .25f, .2f, .45f),
                    Quaternion.LookRotation(seat.Facing, Vector3.up), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Select(item => item.name).Where(name => name != "Bench" && !name.Contains("_Banco_") && !name.StartsWith("Fogon_Manta_"))
                .Distinct().ToList();
            bool floor = Physics.Raycast(seat.Top + Vector3.down * .05f, Vector3.down, out var hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            return FormattableString.Invariant($"floor {(floor ? hit.collider.name + " " + (seat.Top.y - hit.point.y).ToString("F2") + " m below" : "none")}, near [{string.Join(", ", near)}]");
        }
    }
}
