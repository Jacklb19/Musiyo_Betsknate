using System;
using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Pictures of the outdoor world, the lagoon, the tree of life, the plants, the maize, the walls, the entrance and the
    /// benches with the headset set, without and with the world of <see cref="MuseumWorldDressing"/>, so a change can be
    /// shown to the owner before it is kept. Pictures go to TestResults/world (local, ignored); the scene is never saved.
    /// </summary>
    public static class MuseumWorldCaptures
    {
        public const string Folder = "TestResults/world";

        public static readonly (string name, Vector3 eye, Vector3 target)[] Views =
        {
            ("agua", new Vector3(-3.5f, 1.65f, -24), new Vector3(0, .3f, -20)),
            ("agua_baja", new Vector3(2.5f, 1.2f, -16.5f), new Vector3(0, .2f, -20.5f)),
            ("arbol_vida", new Vector3(0, 1.65f, -25.4f), new Vector3(0, 3.8f, -20)),
            ("arbol_vida_copa", new Vector3(2.6f, 1.6f, -16.8f), new Vector3(0, 5, -20)),
            ("entrada_camino", new Vector3(0, 1.65f, -58), new Vector3(0, 1.6f, -40)),
            ("entrada_portada", new Vector3(-1.2f, 1.65f, -50), new Vector3(0, 2.6f, -43)),
            ("bosque_oeste", new Vector3(0, 1.65f, -55), new Vector3(-40, 5, -25)),
            ("bosque_este", new Vector3(0, 1.65f, -50), new Vector3(60, 4, -45)),
            ("vista_aerea", new Vector3(24, 24, -52), new Vector3(0, 0, -12)),
            ("plantas_camino", new Vector3(1.1f, 1.45f, -45.5f), new Vector3(1.7f, .3f, -42.5f)),
            ("plantas_patio", new Vector3(-3, 1.6f, 3), new Vector3(-1.5f, .5f, 6.3f)),
            ("maiz_lejos", new Vector3(-1, 1.65f, -60), new Vector3(4, .8f, -42)),
            ("maiz_patio", new Vector3(3, 1.65f, -6), new Vector3(-2, .8f, 6)),
            ("paredes_bienvenida", new Vector3(2, 1.65f, -22), new Vector3(-6, 2.2f, -18)),
            ("paredes_corredor", new Vector3(-.5f, 1.65f, -10.5f), new Vector3(-10, 2.2f, -10.5f)),
            ("paredes_fogon", new Vector3(0, 1.65f, 14), new Vector3(0, 2.2f, 22)),
            ("paredes_instrumentos", new Vector3(18, 1.65f, -4.5f), new Vector3(26, 2.2f, -4.5f)),
            ("patio_horizonte", new Vector3(0, 1.65f, -3), new Vector3(0, 8, 6)),
            ("mirador", new Vector3(-6.8f, 1.65f, 18.5f), new Vector3(-30, 4, 18.5f)),
        };

        /// <summary>The museum's front door, its outer walls and the exits, to judge how the building meets the ground.</summary>
        public static readonly (string name, Vector3 eye, Vector3 target)[] SurroundingViews =
        {
            ("planta", new Vector3(0, 95, -16), new Vector3(0, 0, -15.9f)),
            ("puerta_lejos", new Vector3(0, 1.65f, -38), new Vector3(0, 1.8f, -26)),
            ("puerta_cerca", new Vector3(-2.5f, 1.65f, -31), new Vector3(0, 1.4f, -26)),
            ("fachada_este", new Vector3(16, 1.65f, -30), new Vector3(8, 1.5f, -16)),
            ("fachada_oeste", new Vector3(-17, 1.65f, -30), new Vector3(-8, 1.5f, -16)),
            ("lado_instrumentos", new Vector3(34, 1.65f, -16), new Vector3(24, 1.5f, -6)),
            ("lado_personajes", new Vector3(-24, 1.65f, -8), new Vector3(-12, 1.5f, 0)),
            ("fondo_fogon", new Vector3(10, 1.65f, 34), new Vector3(0, 1.5f, 20)),
            ("patio_bancas", new Vector3(-6, 1.65f, -7), new Vector3(2, 1, 4)),
            ("bancas_bienvenida", new Vector3(-2.5f, 1.65f, -24.5f), new Vector3(5, .5f, -20)),
            ("bancas_corredor_oeste", new Vector3(-11, 1.65f, -9.5f), new Vector3(-12.3f, .4f, 0)),
            ("bancas_corredor_norte", new Vector3(1, 1.65f, 7.5f), new Vector3(-6, .4f, 10.3f)),
            ("banca_instrumentos", new Vector3(20.5f, 1.65f, -3.5f), new Vector3(20.5f, .4f, -9.4f)),
            ("plazoleta_banca", new Vector3(1.5f, 1.65f, -31.5f), new Vector3(-3, .4f, -29.8f)),
        };

        private static void World(Scene scene) => MuseumWorldDressing.Apply(scene, true);

        public static void Before() => Capture("antes", null);
        public static void After() => Capture("despues", World);
        public static void SurroundingsBefore() => Capture("contorno_antes", null, SurroundingViews);
        public static void SurroundingsAfter() => Capture("contorno_despues", World, SurroundingViews);

        public static void All()
        {
            Before();
            After();
            SurroundingsBefore();
            SurroundingsAfter();
        }

        /// <summary>
        /// Opens the saved scene with the headset set, lets the change alter it in memory, pictures every view and
        /// opens the saved scene again so nothing of the change stays.
        /// </summary>
        public static void Capture(string label, Action<Scene> change, (string name, Vector3 eye, Vector3 target)[] views = null)
        {
            Directory.CreateDirectory(Folder);
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            // Same order as a build: the world first, then the headset set swaps the delivered materials it borrows.
            change?.Invoke(scene);
            MuseumSurfaceProfiles.Apply(scene, "quest");
            MuseumFoliageBatch.Combine(scene, true);
            MuseumAtmosphere.Current.Apply(true);
            MuseumViewCapture.Render(Path.Combine(Folder, label), views ?? Views, 60, 1280, 720, .1f, 1000);
            EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            Debug.Log("Museum world pictures written: " + label + ".");
        }
    }
}
