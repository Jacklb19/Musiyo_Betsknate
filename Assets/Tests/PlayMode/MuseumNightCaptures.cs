using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of every room by day and by night for the owner's review, the inventory of the museum's lamps by room,
    /// and what the night costs in the Editor (lights, draws, render time). Explicit: it writes to TestResults/night and,
    /// for the second round (the new lamps and the moon), to TestResults/night-r2. MUSIYO_LIVE_API, when set, brings the
    /// exhibits onto their stands.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumNightCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/night";
        private const string RoundTwo = "TestResults/night-r2";
        private static readonly string[] LampWords = { "farol", "lamp", "lantern", "lumin", "candil", "vela", "antorch", "torch", "bombill" };
        private static readonly string[] RoomOrder = { "room.arrival", "room.welcome", "room.characters", "room.instruments", "room.hearth", "room.overlook" };
        /// <summary>A Quest 3 eye: its pixels and its vertical field of view.</summary>
        private const int EyeWidth = 2064, EyeHeight = 2208;
        private const float EyeFieldOfView = 96;
        private MuseumVisitFlow flow;
        private MuseumNight night;
        private DesktopVisitorController visitor;
        private Camera camera;
        private RoomAnchor[] rooms;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static double Shot(Camera view, string folder, string name, int width = 1600, int height = 1000, Rect? crop = null)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            view.targetTexture = target;
            var clock = Stopwatch.StartNew();
            view.Render();
            clock.Stop();
            view.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var area = crop ?? new Rect(0, 0, width, height);
            var image = new Texture2D((int)area.width, (int)area.height, TextureFormat.RGB24, false);
            image.ReadPixels(area, 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
            return clock.Elapsed.TotalMilliseconds;
        }

        private static string RoomOf(Vector3 position, RoomAnchor[] rooms)
            => rooms.FirstOrDefault(room => room.Contains(position))?.Key ?? "(fuera de las salas)";

        /// <summary>Where each room is seen from: inside it, at eye height, looking across it.</summary>
        private static (Vector3 eye, Vector3 target) View(RoomAnchor room)
        {
            var centre = room.transform.position;
            var size = room.HalfSize;
            switch (room.Key)
            {
                // From the path, towards the portal and the lanterns.
                case "room.arrival": return (centre + new Vector3(0, 1.6f, -size.y * .55f), centre + new Vector3(0, 1.2f, size.y * .4f));
                // From the overlook's railing out over the valley and the sky.
                case "room.overlook": return (centre + new Vector3(1.5f, 1.6f, -1.5f), centre + new Vector3(-6, 2.2f, 6));
                default:
                    var back = room.transform.TransformPoint(new Vector3(0, 0, -size.y * .7f));
                    return (back + Vector3.up * 1.6f, centre + Vector3.up * 1.1f);
            }
        }

        /// <summary>A spectator camera like the visitor's, standing at an eye and looking at a target.</summary>
        private Camera Spectator(Vector3 eye, Vector3 target, string name = "NightCaptureCamera")
        {
            var spectator = new GameObject(name).AddComponent<Camera>();
            spectator.CopyFrom(camera);
            spectator.enabled = false;
            spectator.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            return spectator;
        }

        private void Stand(Vector3 eye, Vector3 target)
        {
            var flat = Vector3.ProjectOnPlane(target - eye, Vector3.up);
            visitor.PlaceAt(new Vector3(eye.x, eye.y - 1.6f, eye.z), Quaternion.LookRotation(flat.sqrMagnitude > .01f ? flat : Vector3.forward));
        }

        private IEnumerator OpenMuseum(List<string> notes)
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            var api = Environment.GetEnvironmentVariable("MUSIYO_LIVE_API");
            if (!string.IsNullOrEmpty(api))
            {
                var settings = new UnityEditor.SerializedObject(loader);
                settings.FindProperty("apiBase").stringValue = api;
                settings.ApplyModifiedPropertiesWithoutUndo();
                loader.Reload();
                yield return Until(() => loader.State == TourLoadState.Ready, 240);
                notes.Add("api: " + api + " -> " + loader.State);
            }
            else notes.Add("api: none (rooms without their exhibits)");
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            flow.GetComponent<MuseumArrival>()?.Dismiss();
            night = flow.GetComponent<MuseumNight>();
            Assert.That(night, Is.Not.Null, "The visit carries the night.");
            visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            camera = visitor.GetComponentInChildren<Camera>();
            rooms = UnityEngine.Object.FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None).OrderBy(room => Array.IndexOf(RoomOrder, room.Key)).ToArray();
            // The exhibits come to their stands while the visitor walks; a moment is enough for those near the eye.
            yield return new WaitForSecondsRealtime(5);
        }

        /// <summary>Each room by day and by night with what a frame costs in the Editor, as one note per picture.</summary>
        private IEnumerator EveryRoom(string folder, List<string> notes)
        {
            notes.Add("");
            notes.Add("== Capturas y coste (Editor, 1600x1000) ==");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var passes = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            for (int index = 0; index < rooms.Length; index++)
            {
                var room = rooms[index];
                var (eye, target) = View(room);
                Stand(eye, target);
                var spectator = Spectator(eye, target);
                foreach (var dark in new[] { false, true })
                {
                    night.Set(dark, 0);
                    // The room's own ambient settles in its blend time.
                    yield return new WaitForSecondsRealtime(2);
                    yield return null;
                    double milliseconds = 0;
                    for (int frame = 0; frame < 3; frame++) { milliseconds = Shot(spectator, folder, (index + 1) + "_" + room.Key.Replace("room.", "") + (dark ? "_noche" : "_dia")); yield return null; }
                    notes.Add(room.Key + (dark ? " noche" : " día") + ": render " + milliseconds.ToString("F1") + " ms, draws " + draws.LastValue
                        + ", batches " + batches.LastValue + ", setpass " + passes.LastValue + ", interior " + night.Indoor.ToString("F2"));
                }
                UnityEngine.Object.Destroy(spectator.gameObject);
            }
        }

#if UNITY_EDITOR
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureEveryRoomByDayAndByNight()
        {
            Directory.CreateDirectory(Folder);
            var notes = new List<string>();
            yield return OpenMuseum(notes);

            // The lamps: by the night's materials, and by any name or material that sounds like a lamp.
            notes.Add("");
            notes.Add("== Inventario de lámparas ==");
            night.Set(false, 0);
            foreach (var (renderer, index, emission) in night.Lamps)
                notes.Add("lámpara (material " + MuseumNight.BaseName(renderer.sharedMaterials[index].name) + "): " + Hierarchy(renderer.transform)
                    + " · sala " + RoomOf(renderer.bounds.center, rooms) + " · emisión de día " + emission);
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var words = (renderer.name + " " + string.Join(" ", renderer.sharedMaterials.Where(item => item != null).Select(item => item.name))).ToLowerInvariant();
                if (night.Lamps.Any(lamp => lamp.renderer == renderer) || !LampWords.Any(words.Contains)) continue;
                notes.Add("parecido a lámpara (no encendido): " + Hierarchy(renderer.transform) + " [" + words + "] activo=" + renderer.gameObject.activeInHierarchy
                    + " visible=" + renderer.enabled + " · sala " + RoomOf(renderer.bounds.center, rooms));
            }
            foreach (var room in rooms)
                notes.Add("sala " + room.Key + ": " + night.Lamps.Count(lamp => room.Contains(lamp.renderer.bounds.center)) + " lámparas");
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(light => light.isActiveAndEnabled).ToArray();
            notes.Add("luces en tiempo real activas: " + lights.Length + " (" + string.Join(", ", lights.Select(light => light.type + " " + light.name
                + (light.type == LightType.Directional ? "" : " alcance " + light.range.ToString("F1") + " · sala " + RoomOf(light.transform.position, rooms)))) + ")");

            yield return EveryRoom(Folder, notes);

            // The proposal, off by default: night falls by itself at the overlook at the end of the visit.
            var overlook = rooms.FirstOrDefault(room => room.Key == "room.overlook");
            if (overlook != null)
            {
                night.Set(false, 0);
                yield return null;
                var (eye, target) = View(overlook);
                var spectator = Spectator(eye, target, "NightfallCamera");
                Stand(eye, target);
                night.AutoNightAtOverlook = true;
                foreach (var mark in new[] { 0f, .35f, .65f, 1f })
                {
                    yield return Until(() => night.Level >= mark, 20);
                    Shot(spectator, Folder, "propuesta_anochecer_mirador_" + Mathf.RoundToInt(mark * 100));
                }
                notes.Add("");
                notes.Add("propuesta mirador: anochece sola en " + MuseumNightAtmosphere.Current.AutoNightSeconds + " s, automática=" + night.Automatic
                    + " (desactivada en MuseumNight.json: auto_night_at_overlook=false)");
                night.AutoNightAtOverlook = false;
                UnityEngine.Object.Destroy(spectator.gameObject);
            }
            night.Set(false, 0);
            File.WriteAllLines(Path.Combine(Folder, "notes.txt"), notes);
        }

        /// <summary>
        /// The second round (owner, 30/10/2026): the lamps of the welcome, instruments and overlook rooms by day and by
        /// night; the moon from the overlook and from the arrival, and on a headset's eye at its own pixels; where in the
        /// overlook's view the moon stands clear of the forest; and the cost again, room by room.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureTheNewLampsAndTheMoon()
        {
            Directory.CreateDirectory(RoundTwo);
            var notes = new List<string>();
            yield return OpenMuseum(notes);
            var settings = MuseumNightAtmosphere.Current;

            // The new lamps, each seen from the room's side of it, by day and by night.
            notes.Add("");
            notes.Add("== Lámparas nuevas ==");
            var built = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == "MuseumNightLamps");
            Assert.That(built, Is.Not.Null, "The new lamps are built with the world.");
            foreach (Transform lamp in built)
            {
                var glass = lamp.GetComponentsInChildren<Renderer>().First(renderer => renderer.sharedMaterials.Any(material => material != null
                    && MuseumNight.BaseName(material.name) == "PathLantern_Glass"));
                var room = rooms.FirstOrDefault(item => item.Contains(glass.bounds.center));
                var inward = room != null ? Vector3.ProjectOnPlane(room.transform.position - lamp.position, Vector3.up) : Vector3.right;
                if (room != null && room.Key == "room.overlook") inward = Vector3.right;
                inward = inward.sqrMagnitude > .01f ? inward.normalized : Vector3.right;
                var eye = lamp.position + inward * 2.6f + Vector3.up * 1.6f + Vector3.Cross(Vector3.up, inward) * .6f;
                var target = lamp.position + Vector3.up * .8f;
                Stand(eye, target);
                var spectator = Spectator(eye, target);
                foreach (var dark in new[] { false, true })
                {
                    night.Set(dark, 0);
                    yield return new WaitForSecondsRealtime(2);
                    for (int frame = 0; frame < 2; frame++) { Shot(spectator, RoundTwo, lamp.name.Replace("NightLamp_", "lampara_") + (dark ? "_noche" : "_dia")); yield return null; }
                }
                UnityEngine.Object.Destroy(spectator.gameObject);
                notes.Add(lamp.name + ": pie " + lamp.position.ToString("F2") + ", vidrio a " + glass.bounds.center.y.ToString("F2") + " m, sala "
                    + (room != null ? room.Key : "(fuera de las salas)") + ", colisionadores " + lamp.GetComponentsInChildren<Collider>().Length);
            }
            night.Set(true, 0);
            notes.Add("lámparas encendidas de noche: " + night.Lamps.Count + "; brillo en muro: " + night.WallGlows + "; reflejo de luna en agua: " + night.MoonGlints
                + "; luces en tiempo real activas: " + UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(light => light.isActiveAndEnabled));

            // The moon: where it stands, how big a headset eye sees it and what it costs.
            var moon = settings.MoonDirection();
            var face = MuseumNight.MoonFace();
            float pixelsPerDegree = EyeHeight / EyeFieldOfView;
            notes.Add("");
            notes.Add("== Luna ==");
            notes.Add("dirección " + moon.ToString("F3") + " (acimut " + settings.MoonAzimuth + "°, altura " + settings.MoonElevation + "°), diámetro " + settings.MoonSize + "°");
            notes.Add("ojo del Quest 3: " + EyeWidth + "x" + EyeHeight + " px en ~" + EyeFieldOfView + "° vertical = " + pixelsPerDegree.ToString("F1")
                + " px/°; la luna ocupa ~" + (settings.MoonSize * pixelsPerDegree).ToString("F0") + " px; su cara es de " + face.width + " px ("
                + (face.width / (settings.MoonSize * pixelsPerDegree)).ToString("F1") + " texeles por píxel, con mipmaps), el borde lo calcula el shader por píxel");
            notes.Add("memoria de la cara de la luna: " + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(face) + " bytes (R8 " + face.width + "x" + face.height
                + " con mipmaps, " + face.mipmapCount + " niveles); el cielo usa la textura de estrellas del menú ya cargada: "
                + (RenderSettings.skybox.GetTexture("_MainTex") == MuseumEntryBackdrop.NightSky()?.GetTexture("_MainTex")) + "; shader " + RenderSettings.skybox.shader.name);
            var sun = RenderSettings.sun;
            notes.Add("luz direccional de noche: desde " + (-sun.transform.forward).ToString("F3") + ", intensidad " + sun.intensity.ToString("F2"));

            // Where the overlook sees open sky: a narrow look in each direction, sky cleared to a colour nothing else has.
            var overlook = rooms.First(room => room.Key == "room.overlook");
            var bench = UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Where(item => item.name == "Bench")
                .OrderBy(item => Vector3.Distance(item.transform.position, new Vector3(-16.3f, 0, 19.35f))).FirstOrDefault();
            var benchAt = bench != null ? bench.transform.position : new Vector3(-16.3f, overlook.transform.position.y, 19.35f);
            var standing = new Vector3(benchAt.x + 1.6f, overlook.transform.position.y + 1.6f, benchAt.z);
            notes.Add("");
            notes.Add("== Cielo libre desde el Mirador (de pie junto a la banca, " + standing.ToString("F1") + "): fila = altura, columna = acimut ==");
            var probe = Spectator(standing, standing + Vector3.left, "MoonSurveyCamera");
            probe.clearFlags = CameraClearFlags.SolidColor;
            probe.backgroundColor = Color.magenta;
            probe.fieldOfView = 2;
            var survey = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32);
            probe.targetTexture = survey;
            var read = new Texture2D(8, 8, TextureFormat.RGB24, false);
            var azimuths = Enumerable.Range(0, 13).Select(step => 234f + step * 6).ToArray();
            notes.Add("       " + string.Join(" ", azimuths.Select(value => value.ToString("000"))));
            for (float elevation = 38; elevation >= 14; elevation -= 3)
            {
                var row = new List<string>();
                foreach (var azimuth in azimuths)
                {
                    var direction = Quaternion.Euler(-elevation, azimuth, 0) * Vector3.forward;
                    probe.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                    probe.Render();
                    RenderTexture.active = survey;
                    read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                    RenderTexture.active = null;
                    bool open = read.GetPixels().All(pixel => pixel.r > .9f && pixel.g < .1f && pixel.b > .9f);
                    row.Add(open ? " . " : " # ");
                }
                notes.Add(elevation.ToString("00") + "°   " + string.Join(" ", row));
            }
            notes.Add("(. cielo libre, # tapado por bosque, palmas o el propio museo)");
            probe.targetTexture = null;
            survey.Release();
            UnityEngine.Object.Destroy(survey);
            UnityEngine.Object.Destroy(read);
            UnityEngine.Object.Destroy(probe.gameObject);

            // The moon over the valley: standing and seated at the overlook, on a screen and on a headset's eye.
            yield return new WaitForSecondsRealtime(1);
            var flat = Vector3.ProjectOnPlane(moon, Vector3.up).normalized;
            var seated = new Vector3(benchAt.x, overlook.transform.position.y + 1.15f, benchAt.z);
            foreach (var (name, eye) in new[] { ("luna_desde_mirador_de_pie", standing), ("luna_desde_mirador_sentado", seated) })
            {
                Stand(eye, eye + flat);
                // Looking a little up from level, as one does at a moon over a landscape.
                var spectator = Spectator(eye, eye + flat + Vector3.up * Mathf.Tan(12 * Mathf.Deg2Rad));
                Shot(spectator, RoundTwo, name);
                UnityEngine.Object.Destroy(spectator.gameObject);
            }
            {
                Stand(standing, standing + flat);
                var spectator = Spectator(standing, standing + flat, "HeadsetEyeCamera");
                spectator.fieldOfView = EyeFieldOfView;
                spectator.aspect = EyeWidth / (float)EyeHeight;
                Shot(spectator, RoundTwo, "luna_visor_mirador_ojo", EyeWidth, EyeHeight);
                var viewport = spectator.WorldToViewportPoint(standing + moon * 500);
                var at = new Vector2(viewport.x * EyeWidth, viewport.y * EyeHeight);
                float above = Mathf.Atan((viewport.y - .5f) * 2 * Mathf.Tan(EyeFieldOfView * .5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
                notes.Add("en el ojo del visor (mirando al frente sin inclinar la cabeza) la luna queda en " + at.ToString("F0") + " de " + EyeWidth + "x" + EyeHeight
                    + ": " + above.ToString("F0") + "° por encima del centro de la vista");
                const int crop = 360;
                var area = new Rect(Mathf.Clamp(at.x - crop / 2, 0, EyeWidth - crop), Mathf.Clamp(at.y - crop / 2, 0, EyeHeight - crop), crop, crop);
                Shot(spectator, RoundTwo, "luna_visor_detalle_pixeles_reales", EyeWidth, EyeHeight, area);
                UnityEngine.Object.Destroy(spectator.gameObject);
            }
            // From the arrival's path, turning to where the moon stands.
            var arrival = rooms.First(room => room.Key == "room.arrival");
            var path = View(arrival).eye;
            Stand(path, path + flat);
            var fromPath = Spectator(path, path + flat + Vector3.up * Mathf.Tan(14 * Mathf.Deg2Rad));
            Shot(fromPath, RoundTwo, "luna_desde_llegada");
            UnityEngine.Object.Destroy(fromPath.gameObject);
            notes.Add("reflejo en la laguna del mirador: " + string.Join(", ", settings.MoonGlints.Select(glint => "(" + glint.X + ", " + glint.Z + ")")));

            yield return EveryRoom(RoundTwo, notes);
            night.Set(false, 0);
            File.WriteAllLines(Path.Combine(RoundTwo, "notes.txt"), notes);
        }
#endif

        private static string Hierarchy(Transform item)
        {
            var parts = new List<string>();
            for (; item != null; item = item.parent) parts.Insert(0, item.name);
            return string.Join("/", parts);
        }
    }
}
