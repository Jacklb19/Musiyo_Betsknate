using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the two guides and the hearth, for review. Explicit: it asserts nothing about the design and runs only
    /// when named, writing to MUSIYO_CAPTURE_DIR or TestResults/guides. No content service is needed.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumGuideCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/guides";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }

        private static System.Net.HttpListener service;

        /// <summary>A local service that only says the guides' voice is configured; everything else is not found.</summary>
        private static string StartVoiceService()
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var capabilities = File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", "voice_capabilities_external.json"));
            service = new System.Net.HttpListener();
            service.Prefixes.Add("http://127.0.0.1:" + port + "/");
            service.Start();
            var listener = service;
            void Receive(IAsyncResult pending)
            {
                System.Net.HttpListenerContext context;
                try { context = listener.EndGetContext(pending); listener.BeginGetContext(Receive, null); }
                catch (Exception) { return; }
                try
                {
                    bool voice = context.Request.Url.AbsolutePath == "/api/v1/guide/voice-capabilities";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(voice ? capabilities : "{\"detail\":\"Not Found\"}");
                    context.Response.StatusCode = voice ? 200 : 404;
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    context.Response.OutputStream.Close();
                }
                catch (Exception) { }
            }
            service.BeginGetContext(Receive, null);
            return "http://127.0.0.1:" + port + "/api/v1";
        }

        /// <summary>A microphone that records nothing, so no device or person is needed for the pictures.</summary>
        private sealed class SilentRecorder : IGuideVoiceRecorder
        {
            private bool recording;
            public bool Available => true;
            public bool Recording => recording;
            public float Elapsed => recording ? 3 : 0;
            public bool AwaitingPermission => false;
            public GuideRecorderStart Begin(int maxSeconds) { recording = true; return GuideRecorderStart.Started; }
            public byte[] End() { recording = false; return null; }
            public void Discard() => recording = false;
            public void Dispose() { }
        }

        public override void TearDown()
        {
            try { service?.Stop(); service?.Close(); } catch (Exception) { }
            service = null;
            MuseumInput.Replace(null);
            base.TearDown();
        }

        /// <summary>Renders a camera of its own at a chosen eye and target, with every world screen in view.</summary>
        private static IEnumerator Shot(string name, Vector3 eye, Vector3 target, float fov = 70)
        {
            yield return null;
            const int width = 1440, height = 900;
            var holder = new GameObject("CaptureCamera");
            var camera = holder.AddComponent<Camera>();
            camera.fieldOfView = fov;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            holder.transform.position = eye;
            holder.transform.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
            var main = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(other => other != camera && other.isActiveAndEnabled && other.targetTexture == null);
            if (main != null)
            {
                camera.clearFlags = main.clearFlags;
                camera.backgroundColor = main.backgroundColor;
                camera.cullingMask = main.cullingMask;
            }
            var target2 = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = target2;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target2;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target2.Release();
            UnityEngine.Object.Destroy(target2);
            UnityEngine.Object.Destroy(holder);
        }

        private static IEnumerator OpenMuseum()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = StartVoiceService();
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            // No retry of the unreachable service may clear the tour applied below while the pictures are taken.
            loader.enabled = false;
            var runtime = flow.GetComponent<TourRuntime>();
            RoomContractV1 Room(string key, int order) => new RoomContractV1 { key = key, name = key, order = order, points = new PointContractV1[0] };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[] { Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3),
                    Room("room.hearth", 4), Room("room.overlook", 5) },
                guide = new GuideContractV1 { key = "guia.principal", name = "Guía", room_key = "room.hearth", available = true },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
        }

        /// <summary>
        /// Every piece within reach of the fire, one per line, for the review of the hearth: its name, centre and size, how
        /// far its lowest point floats above what lies under it, and the pieces whose volumes cross it. Architecture is left out.
        /// </summary>
        private static void WriteHearthInventory(Vector3 fire, float floor)
        {
            string[] structure = { "Piso", "Muro", "Techo", "Estructura", "Pilar", "Friso", "Faja", "Fleco", "Umbral", "Pasaje", "Sala_" };
            var pieces = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(item => item.enabled && item.gameObject.activeInHierarchy && !(item is ParticleSystemRenderer) && !(item is LineRenderer)
                    && item.GetComponentInParent<Canvas>() == null && !structure.Any(word => item.name.Contains(word)))
                .Where(item => new Vector2(item.bounds.center.x - fire.x, item.bounds.center.z - fire.z).magnitude < 6.5f
                    && item.bounds.center.y < floor + 3.5f && item.bounds.size.magnitude < 8)
                .OrderBy(item => item.name).ToList();
            var lines = new System.Collections.Generic.List<string>
            {
                "Fogón: piezas a menos de 6,5 m del fuego (sin arquitectura). Distancias en metros; «flota» = hueco bajo su punto más bajo.",
            };
            foreach (var item in pieces)
            {
                var bounds = item.bounds;
                float gap = float.NaN;
                foreach (var hit in Physics.RaycastAll(new Vector3(bounds.center.x, bounds.min.y + .02f, bounds.center.z), Vector3.down, 3,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).OrderBy(hit => hit.distance))
                {
                    if (hit.collider.transform.IsChildOf(item.transform)) continue;
                    gap = hit.distance - .02f;
                    break;
                }
                var crossing = pieces.Where(other => other != item && bounds.Intersects(other.bounds) && Overlap(bounds, other.bounds) > .3f)
                    .Select(other => other.name).Distinct();
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} | centro ({1:F2}, {2:F2}, {3:F2}) | tamaño ({4:F2} × {5:F2} × {6:F2}) | flota {7} | se cruza con: {8}",
                    item.name, bounds.center.x - fire.x, bounds.min.y - floor, bounds.center.z - fire.z, bounds.size.x, bounds.size.y, bounds.size.z,
                    float.IsNaN(gap) ? "sin suelo debajo" : gap.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), string.Join(", ", crossing)));
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder, "hearth_inventory.txt"), lines, new System.Text.UTF8Encoding(false));
        }

        /// <summary>Share of the smaller volume that lies inside the other.</summary>
        private static float Overlap(Bounds first, Bounds second)
        {
            var min = Vector3.Max(first.min, second.min);
            var max = Vector3.Min(first.max, second.max);
            var size = Vector3.Max(Vector3.zero, max - min);
            float smaller = Mathf.Min(first.size.x * first.size.y * first.size.z, second.size.x * second.size.y * second.size.z);
            return smaller > 1e-6f ? size.x * size.y * size.z / smaller : 0;
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheGuidesAndTheHearth()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return OpenMuseum();
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var runtime = flow.GetComponent<TourRuntime>();
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            void Place(Vector3 spot) { body.enabled = false; visitor.transform.position = spot; body.enabled = true; }
            Place(new Vector3(0, .05f, -50));
            for (float waited = 0; waited < 4; waited += Time.unscaledDeltaTime) yield return null;
            var at = companion.transform.position;
            var side = companion.transform.right;
            yield return Shot("companion_front", at + companion.transform.forward * 2.4f + Vector3.up * 1.5f, at + Vector3.up * 1.0f, 50);
            yield return Shot("companion_side", at + side * 2.4f + Vector3.up * 1.4f, at + Vector3.up * .95f, 50);
            yield return Shot("companion_visitor_view", visitor.transform.position + Vector3.up * 1.65f, at + Vector3.up * 1.1f);

            // The companion's own screen for a spoken question: the notice, then listening.
            var voice = companion.Voice;
            voice.Recorder = new SilentRecorder();
            for (float waited = 0; !voice.Listens && waited < 10; waited += Time.unscaledDeltaTime) yield return null;
            var eye = visitor.GetComponentInChildren<Camera>().transform;
            for (float waited = 0; waited < 1; waited += Time.unscaledDeltaTime) yield return null;
            yield return Shot("companion_voice_invite", eye.position, eye.position + eye.forward * 2 + Vector3.left * .35f, 70);
            if (voice.Talk())
            {
                for (float waited = 0; waited < .6f; waited += Time.unscaledDeltaTime) yield return null;
                yield return Shot("companion_voice_notice", eye.position, eye.position + eye.forward * 2 + eye.right * -.4f + Vector3.down * .3f, 60);
                voice.Accept();
                for (float waited = 0; waited < .6f; waited += Time.unscaledDeltaTime) yield return null;
                yield return Shot("companion_voice_listening", eye.position, eye.position + eye.forward * 2 + eye.right * -.4f + Vector3.down * .3f, 60);
                voice.End();
            }

            // The scene's own anchor: the tour may still be retrying its unreachable service.
            var anchor = UnityEngine.Object.FindObjectsByType<PointAnchor>(FindObjectsSortMode.None).First(item => item.Key == "guia.principal");
            var hearth = anchor.transform.position;
            var stones = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == "Fogon_Piedras_Shinyac");
            var fire = stones != null ? stones.bounds.center : hearth;
            Place(hearth + (hearth - fire).normalized * 6);
            for (float waited = 0; waited < 1.5f; waited += Time.unscaledDeltaTime) yield return null;
            // From inside the room, at eye height, around the fire; then from above with the roof put aside for the picture.
            for (int view = 0; view < 6; view++)
            {
                var direction = Quaternion.Euler(0, view * 60, 0) * Vector3.forward;
                yield return Shot("hearth_" + view, new Vector3(fire.x, hearth.y + 1.6f, fire.z) + direction * 2.2f,
                    new Vector3(fire.x, hearth.y + .5f, fire.z) - direction * 1.5f, 80);
            }
            var roofs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(item => item.enabled && (item.name.Contains("Techo") || item.name.Contains("Fleco"))).ToList();
            foreach (var roof in roofs) roof.enabled = false;
            yield return Shot("hearth_top", new Vector3(fire.x, hearth.y + 6.5f, fire.z) - Vector3.forward * .01f, new Vector3(fire.x, hearth.y, fire.z), 75);
            foreach (var roof in roofs) roof.enabled = true;
            yield return Shot("hearth_arrival", visitor.transform.position + Vector3.up * 1.65f, fire + Vector3.up * .9f, 70);
            WriteHearthInventory(fire, hearth.y);
            var interviewer = flow.GetComponent<MuseumHearthInterviewer>();
            if (interviewer != null && interviewer.Figure != null)
            {
                var place = interviewer.Place;
                var toward = anchor.transform.position - place;
                toward.y = 0;
                toward = toward.sqrMagnitude > .01f ? toward.normalized : Vector3.forward;
                Place(anchor.transform.position + toward * (anchor.ActivationRadius + 3));
                for (float waited = 0; waited < 1.5f; waited += Time.unscaledDeltaTime) yield return null;
                yield return Shot("interviewer_invitation", visitor.transform.position + Vector3.up * 1.65f, place + Vector3.up * 1.4f, 70);
                yield return Shot("interviewer_close", place + toward * 2.2f + Vector3.up * 1.55f, place + Vector3.up * 1.0f, 55);
            }
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
