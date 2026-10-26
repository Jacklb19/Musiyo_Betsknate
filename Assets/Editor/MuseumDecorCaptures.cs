using System;
using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Pictures of the decoration of the research's PART A, room by room, with the world built as a headset build builds
    /// it. With MUSIYO_DECOR=off the same views show the museum before the decoration. Writes to MUSIYO_CAPTURE_DIR (or
    /// TestResults/decor) with MUSIYO_CAPTURE_LABEL in the names; the scene is never saved.
    /// Batch, with graphics: -executeMethod MusiyoBetsknate.Editor.MuseumDecorCaptures.Capture.
    /// </summary>
    public static class MuseumDecorCaptures
    {
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0 ? folder : "TestResults/decor";
        private static string Label => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_LABEL") is string label && label.Length > 0 ? label : "now";

        public static readonly (string name, Vector3 eye, Vector3 target)[] Views =
        {
            ("1_jajan", new Vector3(1.0f, 1.65f, -30.6f), new Vector3(5.2f, .4f, -37f)),
            ("1_jajan_gallinas", new Vector3(2.9f, 1.3f, -33.6f), new Vector3(5.2f, .15f, -39f)),
            ("1_frijol", new Vector3(-.9f, 1.65f, -35.5f), new Vector3(-4.2f, 1.1f, -39f)),
            ("1_lena_ramos", new Vector3(-.4f, 1.65f, -57.2f), new Vector3(-3f, .3f, -60.2f)),
            ("1_palmas", new Vector3(0, 1.65f, -50f), new Vector3(-34f, 14f, -62f)),
            ("2_telar", new Vector3(.6f, 1.6f, -17.6f), new Vector3(-2.5f, .9f, -15.2f)),
            ("2_petalos_chicha", new Vector3(-2.6f, 1.6f, -21.6f), new Vector3(-5f, .4f, -21.2f)),
            ("2_rincon_inga", new Vector3(2.8f, 1.55f, -20.6f), new Vector3(5.1f, .4f, -20.6f)),
            ("2_totora", new Vector3(3.6f, 1.65f, -23.6f), new Vector3(0, .6f, -20f)),
            ("3_castillo_preparacion", new Vector3(1.4f, 1.65f, .4f), new Vector3(3.3f, .25f, 4.1f)),
            ("3_banderas", new Vector3(-.8f, 1.65f, .6f), new Vector3(-3.1f, 1.1f, 3.7f)),
            ("3_banderas_rincon", new Vector3(-4.9f, 1.65f, 2.4f), new Vector3(-8.3f, 1.1f, 6.8f)),
            ("3_tallador", new Vector3(6f, 1.65f, -3.2f), new Vector3(8.2f, .75f, -3f)),
            ("3_coronas", new Vector3(-10.9f, 1.55f, 5.3f), new Vector3(-12.3f, .45f, 5.4f)),
            ("4_instrumentos", new Vector3(20.6f, 1.45f, -7.6f), new Vector3(20.9f, .45f, -9.4f)),
            ("4_taller", new Vector3(23.4f, 1.6f, -5.6f), new Vector3(25.3f, .5f, -7f)),
            ("4_miniaturas", new Vector3(20.7f, 1.55f, -1.1f), new Vector3(20.7f, 1.3f, .5f)),
            ("5_alacena_comida", new Vector3(1.3f, 1.6f, 17.6f), new Vector3(3f, .7f, 16.4f)),
            ("5_cuyes", new Vector3(-1.1f, 1.4f, 18.9f), new Vector3(-3.1f, .1f, 19.6f)),
            ("5_jigra", new Vector3(1.6f, 1.6f, 17.2f), new Vector3(.3f, 1.15f, 18.3f)),
            ("6_mirador", new Vector3(-11.5f, 1.65f, 18.5f), new Vector3(-40f, 5f, 18.5f)),
            ("6_mirador_banca", new Vector3(-13.6f, 1.6f, 17.6f), new Vector3(-16.2f, .4f, 19.35f)),
            ("6_mirador_laderas", new Vector3(-16.4f, 1.65f, 18.5f), new Vector3(-250f, 45f, 10f)),
            ("6_sentado_en_banca", new Vector3(-16.25f, 1.2f, 19.35f), new Vector3(-60f, -2f, 19.35f)),
        };

        public static void Capture()
        {
            Directory.CreateDirectory(Folder);
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            try
            {
                MuseumWorldDressing.Apply(scene, true);
                MuseumSurfaceProfiles.Apply(scene, "quest");
                MuseumFoliageBatch.Combine(scene, true);
                MuseumAtmosphere.Current.Apply(true);
                MuseumViewCapture.Render(Path.Combine(Folder, Label), Views, 62, 1280, 760, .05f, 900);
                Debug.Log("Decoration pictures written: " + Views.Length + " views.");
            }
            finally { EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single); }
        }
    }
}
