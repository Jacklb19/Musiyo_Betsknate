using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Batch entry points of the world work: apply every step in order, and picture fixed views with the screen and the
    /// headset material sets so changes can be compared. Pictures go to TestResults/world (local, ignored).
    /// </summary>
    public static class MuseumWorldBatch
    {
        public const string CaptureFolder = "TestResults/world";

        public static readonly (string name, Vector3 eye, Vector3 target)[] Views =
        {
            ("arrival_path", new Vector3(0, 1.65f, -50), new Vector3(0, 1.2f, -40)),
            ("arrival_planters", new Vector3(1.1f, 1.45f, -45.5f), new Vector3(1.7f, .3f, -42.5f)),
            ("welcome_lagoon", new Vector3(-3.5f, 1.65f, -24), new Vector3(0, .3f, -20)),
            ("welcome_lagoon_low", new Vector3(2.5f, 1.2f, -16.5f), new Vector3(0, .2f, -20.5f)),
            ("corridor_rafters", new Vector3(-.5f, 1.65f, -11), new Vector3(3, 3.9f, -8.5f)),
            ("corridor_south_east", new Vector3(0, 1.65f, -10.2f), new Vector3(10, 3.6f, -11.4f)),
            ("patio_sky", new Vector3(0, 1.65f, -3), new Vector3(0, 8, 6)),
            ("patio_planters", new Vector3(-3, 1.6f, 3), new Vector3(-1.5f, .5f, 6.3f)),
            ("exterior_sky", new Vector3(0, 1.65f, -58), new Vector3(0, 6, -20)),
            ("overlook_sky", new Vector3(-6.8f, 1.65f, 18.5f), new Vector3(-30, 4, 18.5f)),
        };

        /// <summary>
        /// The joins the owner reported on the headset (entrance, welcome room to characters, floor band corners) and the
        /// patio stones, at eye height and low, so faces that fight in depth show as noise.
        /// </summary>
        public static readonly (string name, Vector3 eye, Vector3 target)[] JoinViews =
        {
            ("entrance_threshold", new Vector3(.4f, 1.6f, -29.5f), new Vector3(0, .03f, -26.1f)),
            ("entrance_threshold_low", new Vector3(-1.6f, .9f, -28.4f), new Vector3(.3f, .03f, -26)),
            ("welcome_exit_floor", new Vector3(.5f, 1.6f, -17.6f), new Vector3(0, .1f, -13)),
            ("welcome_exit_floor_low", new Vector3(-1.4f, .8f, -16.2f), new Vector3(.6f, 0, -13.8f)),
            ("characters_south_door", new Vector3(-.8f, 1.6f, -14.6f), new Vector3(1.55f, 1.3f, -11.35f)),
            ("characters_band_corner", new Vector3(-12.5f, 1.5f, -10.6f), new Vector3(-10.7f, 0, -8.7f)),
            ("instruments_band_corner", new Vector3(18.6f, 1.6f, -6.6f), new Vector3(16, 0, -9.1f)),
            ("instruments_band_corner_north", new Vector3(22.6f, 1.6f, -2.6f), new Vector3(25.1f, 0, .1f)),
            ("patio_stones", new Vector3(-2.6f, 1.6f, -5.4f), new Vector3(-.4f, 0, -2.3f)),
        };

        public static void JoinsBefore() => Joins("before");
        public static void JoinsAfter() => Joins("after");

        /// <summary>
        /// Pictures the join views with the headset set and the visitor camera's clip planes, and audits the coplanar
        /// overlaps of the saved scene, into TestResults/world/joins; the scene is never saved here.
        /// </summary>
        public static void Joins(string label)
        {
            string folder = Path.Combine(CaptureFolder, "joins");
            Directory.CreateDirectory(folder);
            MuseumSurfaceAudit.Run(false, Path.Combine(folder, "audit-" + label));
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            MuseumSurfaceProfiles.Apply(scene, "quest");
            MuseumFoliageBatch.Combine(scene, true);
            MuseumAtmosphere.Current.Apply(true);
            MuseumViewCapture.Render(Path.Combine(folder, label), JoinViews, 60, near: .3f, far: 1000);
            EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            Debug.Log("Museum join pictures and audit written: " + label + ".");
        }

        public static void ApplyAll()
        {
            MuseumAtmosphereTools.CreateSky();
            MuseumDeliveryAssets.ApplyMeshRepairs();
            MuseumDeliveryAssets.ApplySurfaceFinish();
            MuseumSetDressing.Run();
            MuseumWorldSetup.Run();
            AssetDatabase.SaveAssets();
            Debug.Log("Museum world steps applied.");
        }

        /// <summary>
        /// Upper bounds per room for the headset set: triangles and drawn submeshes of every enabled renderer whose centre
        /// lies in the room (outside every room counts as exterior), after the plants are combined. It ignores culling and
        /// occlusion, so it overstates what one view draws; it is no measure of frame time.
        /// </summary>
        public static void Budget()
        {
            Directory.CreateDirectory(CaptureFolder);
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            MuseumSurfaceProfiles.Apply(scene, "quest");
            MuseumFoliageBatch.Combine(scene, true);
            var rooms = Object.FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None);
            var totals = new System.Collections.Generic.SortedDictionary<string, (int triangles, int draws, int dressingTriangles)>();
            var dressingRoot = GameObject.Find(MuseumSetDressing.SceneRoot)?.transform;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                // A combined plant mesh spans its area; it is counted where most of it stands.
                string key = "exterior";
                foreach (var room in rooms) if (room.Contains(renderer.bounds.center)) { key = room.Key; break; }
                int triangles = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) triangles += (int)mesh.GetIndexCount(submesh) / 3;
                bool dressing = dressingRoot != null && renderer.transform.IsChildOf(dressingRoot);
                totals.TryGetValue(key, out var total);
                totals[key] = (total.triangles + triangles, total.draws + mesh.subMeshCount, total.dressingTriangles + (dressing ? triangles : 0));
            }
            var report = new System.Text.StringBuilder("area,triangles,drawn_submeshes,set_dressing_triangles\n");
            foreach (var entry in totals) report.AppendLine(entry.Key + "," + entry.Value.triangles + "," + entry.Value.draws + "," + entry.Value.dressingTriangles);
            File.WriteAllText(Path.Combine(CaptureFolder, "budget_quest.csv"), report.ToString());
            Debug.Log("Museum world budget (headset set, upper bounds):\n" + report);
            EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
        }

        /// <summary>Places the plants, music and companion in the saved scene again (assets already built).</summary>
        public static void ApplyScene()
        {
            MuseumSetDressing.Run();
            MuseumWorldSetup.Run();
        }

        public static void CaptureBefore() => Capture("before");
        public static void CaptureAfter() => Capture("after");

        /// <summary>Pictures the saved scene with the web and the quest sets; the scene is never saved here.</summary>
        public static void Capture(string label)
        {
            Directory.CreateDirectory(CaptureFolder);
            foreach (var (profile, headset) in new[] { ("web", false), ("quest", true) })
            {
                var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
                MuseumSurfaceProfiles.Apply(scene, profile);
                MuseumFoliageBatch.Combine(scene, headset);
                MuseumAtmosphere.Current.Apply(headset);
                MuseumViewCapture.Render(Path.Combine(CaptureFolder, label + "_" + profile), Views, 60);
            }
            EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            Debug.Log("Museum world pictures written: " + label + ".");
        }
    }
}
