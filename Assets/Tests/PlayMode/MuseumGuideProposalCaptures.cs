using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the proposed forms for the two guides, for the owner to choose one: each proposal for the companion, from
    /// the front and on the path, and its larger pair for the hearth guide by the fire. Explicit: it asserts nothing about
    /// the design and writes to MUSIYO_CAPTURE_DIR or TestResults/proposals.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumGuideProposalCaptures
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static string Folder => Environment.GetEnvironmentVariable("MUSIYO_CAPTURE_DIR") is string folder && folder.Length > 0
            ? folder : "TestResults/proposals";

        /// <summary>The proposals: a name for the files and the form, as it would be written in MuseumGuideFigures.json.</summary>
        private static readonly (string name, GuideFigureDefinition form)[] Proposals =
        {
            ("1-ovillo-sayo", new GuideFigureDefinition { Kind = GuideFigureDefinition.Yarn, SayoColor = "#EDE2C3", StripeColor = "#1F1B17", StripeCount = 18, StripeWidth = .14f }),
            ("2-ovillo-cabuya", new GuideFigureDefinition { Kind = GuideFigureDefinition.Yarn, SayoColor = "#CDB282", StripeColor = "#9A7E52", StripeCount = 30, StripeWidth = .3f }),
            ("3-jigra-cabuya", new GuideFigureDefinition { Kind = GuideFigureDefinition.Jigra, SayoColor = "#C6A874", StripeColor = "#7E6440", StripeCount = 22, StripeWidth = .28f }),
            ("4-madera", new GuideFigureDefinition { Kind = GuideFigureDefinition.Wood, HeadColor = "#8E6A45", SayoColor = "#EDE2C3" }),
        };

        private static GuideFigureDefinition Larger(GuideFigureDefinition form) => new GuideFigureDefinition
        {
            Kind = form.Kind, SayoColor = form.SayoColor, StripeColor = form.StripeColor, StripeCount = form.StripeCount + 8,
            StripeWidth = form.StripeWidth, HeadColor = form.HeadColor, Diameter = .5f, FloatHeight = .95f
        };

        private static IEnumerator Shot(string name, Vector3 eye, Vector3 target, float fov)
        {
            yield return null;
            const int width = 1440, height = 900;
            var holder = new GameObject("CaptureCamera");
            var camera = holder.AddComponent<Camera>();
            camera.fieldOfView = fov;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 400;
            holder.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
            var main = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .FirstOrDefault(other => other != camera && other.isActiveAndEnabled && other.targetTexture == null);
            if (main != null) { camera.clearFlags = main.clearFlags; camera.backgroundColor = main.backgroundColor; camera.cullingMask = main.cullingMask; }
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            UnityEngine.Object.Destroy(holder);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheProposedGuideForms()
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            loader.enabled = false;
            flow.EnterImmediately();
            var companion = UnityEngine.Object.FindAnyObjectByType<MuseumCompanion>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var body = visitor.GetComponent<CharacterController>();
            body.enabled = false;
            visitor.transform.position = new Vector3(0, .05f, -50);
            body.enabled = true;
            for (float waited = 0; waited < 4; waited += Time.unscaledDeltaTime) yield return null;
            // The companion's own figure steps aside; each proposal floats where it stands.
            companion.Figure.gameObject.SetActive(false);
            var path = companion.transform.position;
            var eyes = visitor.GetComponentInChildren<Camera>().transform.position;
            var toVisitor = new Vector3(eyes.x - path.x, 0, eyes.z - path.z).normalized;
            var side = Vector3.Cross(Vector3.up, toVisitor);

            var guide = UnityEngine.Object.FindObjectsByType<PointAnchor>(FindObjectsSortMode.None).First(anchor => anchor.Key == "guia.principal");
            var place = MuseumHearthInterviewer.PlaceFor(guide);
            var stones = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).First(item => item.name == "Fogon_Piedras_Shinyac");
            var fire = stones.bounds.center;
            var benchEnd = place;
            var bench = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == "Fogon_Manta_Norte");
            if (bench != null)
            {
                var bounds = bench.bounds;
                var along = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
                benchEnd = new Vector3(bounds.center.x, place.y, bounds.center.z) + along * Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * .5f - .35f, 0, .75f);
            }
            var interviewer = flow.GetComponent<MuseumHearthInterviewer>();
            var lines = new System.Collections.Generic.List<string>();
            foreach (var (name, form) in Proposals)
            {
                var holder = new GameObject("Proposal_" + name).transform;
                holder.SetPositionAndRotation(path, Quaternion.LookRotation(toVisitor, Vector3.up));
                var figure = GuideFigure.Build(holder, form);
                var ball = path + Vector3.up * form.FloatHeight;
                for (float waited = 0; waited < .3f; waited += Time.unscaledDeltaTime) yield return null;
                yield return Shot(name + "_frente", ball + toVisitor * .9f + side * .25f + Vector3.up * .05f, ball, 45);
                yield return Shot(name + "_sendero", eyes, ball + side * .3f, 70);
                UnityEngine.Object.Destroy(holder.gameObject);

                var pair = new GameObject("Proposal_" + name + "_fogon").transform;
                var front = new Vector3(fire.x - benchEnd.x, 0, fire.z - benchEnd.z).normalized;
                pair.SetPositionAndRotation(benchEnd, Quaternion.LookRotation(front, Vector3.up));
                var large = Larger(form);
                GuideFigure.Build(pair, large);
                if (interviewer != null && interviewer.Figure != null) interviewer.Figure.gameObject.SetActive(false);
                var held = benchEnd + Vector3.up * large.FloatHeight;
                for (float waited = 0; waited < .3f; waited += Time.unscaledDeltaTime) yield return null;
                // From across the fire, at a standing visitor's eyes, the guide beyond the flames.
                var viewer = new Vector3(fire.x, place.y + 1.6f, fire.z) + front * 1.6f + Vector3.Cross(Vector3.up, front) * .6f;
                yield return Shot(name + "_fogon", viewer, held, 70);
                UnityEngine.Object.Destroy(pair.gameObject);
                lines.Add(name + ": " + form.Kind + " " + form.SayoColor + " / " + form.StripeColor);
            }
            File.WriteAllLines(Path.Combine(Folder, "formas.txt"), lines, new System.Text.UTF8Encoding(false));
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif
    }
}
