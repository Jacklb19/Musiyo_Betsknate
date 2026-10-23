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
                var list = new StringBuilder();
                // Doorways and passages, to judge whether a seat stands in a way through.
                foreach (var way in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(item => item.name.StartsWith("Pasaje_") || item.name.StartsWith("Umbral_") || item.name.Contains("Puerta")).OrderBy(item => item.name))
                    list.AppendLine(FormattableString.Invariant(
                        $"way {way.name}: centre {way.bounds.center.x:F2} {way.bounds.center.z:F2}, size {way.bounds.size.x:F2} {way.bounds.size.z:F2}"));
                // The entrance gate is pictured whole, roof and sign included, before anything is left out.
                var gateViews = new List<(string name, Vector3 eye, Vector3 target)>();
                var gate = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(item => item.name.StartsWith("Portada_")).Select(item => item.bounds).ToList();
                if (gate.Count > 0)
                {
                    var bounds = gate[0];
                    foreach (var item in gate) bounds.Encapsulate(item);
                    var middle = bounds.center;
                    list.AppendLine(FormattableString.Invariant($"gate: centre {middle.x:F2} {middle.y:F2} {middle.z:F2}, size {bounds.size.x:F2} {bounds.size.y:F2} {bounds.size.z:F2}"));
                    var floor = new Vector3(middle.x, 0, middle.z);
                    gateViews.Add(("gate_far", floor + new Vector3(-1.2f, 1.65f, -9), floor + Vector3.up * 2.6f));
                    gateViews.Add(("gate_near", floor + new Vector3(-.6f, 1.65f, -4.2f), floor + Vector3.up * 2.7f));
                    gateViews.Add(("gate_side", floor + new Vector3(4.5f, 1.65f, -3.5f), floor + Vector3.up * 2.2f));
                    gateViews.Add(("gate_under", floor + new Vector3(.2f, 1.65f, -1.4f), floor + new Vector3(0, 3.1f, .2f)));
                    gateViews.Add(("gate_from_inside", floor + new Vector3(.8f, 1.65f, 5), floor + Vector3.up * 2.4f));
                }
                MuseumViewCapture.Render(Path.Combine(Folder, Label), gateViews, 62, 1280, 760, .05f, 600);
                // Roofs, beams and the tops of walls hide the seats from above; the pictures leave them out.
                var floors = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(item => item.name.Contains("_Piso")).ToList();
                foreach (var high in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (high.bounds.min.y > 2.2f) high.enabled = false;
                var views = new List<(string name, Vector3 eye, Vector3 target)>();
                int index = 0;
                foreach (var seat in seats.OrderBy(item => item.Top.z).ThenBy(item => item.Top.x))
                {
                    string name = FormattableString.Invariant($"{index++:D2}_{seat.Name}_{seat.Top.x:F1}_{seat.Top.z:F1}").Replace(' ', '_');
                    views.Add(("seat_" + name + "_front", seat.Top + seat.Facing * 1.7f + Vector3.up * 1.2f, seat.Top + Vector3.down * .15f));
                    views.Add(("seat_" + name + "_above", seat.Top + Vector3.up * 6.5f - seat.Facing * 1.2f, seat.Top));
                    list.AppendLine(FormattableString.Invariant(
                        $"{name}: top {seat.Top.x:F2} {seat.Top.y:F2} {seat.Top.z:F2}, facing {seat.Facing.x:F2} {seat.Facing.z:F2}, length {seat.Along.magnitude * 2:F2} m, hearth {seat.Hearth}, {Surroundings(seat)}"));
                }
                // Every room from above, its floors whole.
                foreach (var room in floors.GroupBy(item => item.name.Substring(0, item.name.IndexOf("_Piso"))))
                {
                    var bounds = room.First().bounds;
                    foreach (var item in room) bounds.Encapsulate(item.bounds);
                    float reach = Mathf.Max(bounds.size.x, bounds.size.z);
                    views.Add(("room_" + room.Key, bounds.center + new Vector3(0, reach * .95f + 3, -reach * .25f), bounds.center));
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
