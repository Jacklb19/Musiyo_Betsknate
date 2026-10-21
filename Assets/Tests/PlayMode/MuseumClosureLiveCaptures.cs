using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Pictures of the closing work against a running museum API (MUSIYO_LIVE_API, e.g. http://127.0.0.1:8030/api/v1):
    /// the governor's stand, the arrows on Matachín's own record, the hearth questions and the guide's voice end to end
    /// with the API's engines, the recorded question coming from MUSIYO_LIVE_QUESTION_WAV (16 kHz mono PCM16). Skipped
    /// without them. Pictures and a summary go to TestResults/closure-live; nothing is published or kept elsewhere.
    /// </summary>
    public sealed class MuseumClosureLiveCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/closure-live";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        /// <summary>Gives back the recorded question from a file, as the microphone would.</summary>
        private sealed class FileRecorder : IGuideVoiceRecorder
        {
            private readonly byte[] wav;
            private bool recording;
            public FileRecorder(byte[] recorded) => wav = recorded;
            public bool Available => true;
            public bool Recording => recording;
            public float Elapsed => recording ? 2 : 0;
            public bool AwaitingPermission => false;
            public GuideRecorderStart Begin(int maxSeconds) { recording = true; return GuideRecorderStart.Started; }
            public byte[] End() { recording = false; return wav; }
            public void Discard() => recording = false;
            public void Dispose() { }
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static void Render(Camera camera, string name)
        {
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheClosingWorkAgainstTheLiveApi()
        {
            var api = Environment.GetEnvironmentVariable("MUSIYO_LIVE_API");
            var questionPath = Environment.GetEnvironmentVariable("MUSIYO_LIVE_QUESTION_WAV");
            if (string.IsNullOrEmpty(api)) Assert.Ignore("Set MUSIYO_LIVE_API to a running museum API.");
            Directory.CreateDirectory(Folder);
            var summary = new List<string> { "api: " + api };
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = api;
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            loader.Reload();
            yield return Until(() => loader.State == TourLoadState.Ready, 30);
            Assert.That(loader.State, Is.EqualTo(TourLoadState.Ready), loader.Status);
            MuseumAtmosphere.Current.Apply();
            flow.EnterImmediately();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var guide = flow.GetComponent<MuseumGuide>();
            var quiz = flow.GetComponent<MuseumHearthQuiz>();
            var voice = flow.GetComponent<MuseumGuideVoice>();
            var view = UnityEngine.Object.FindAnyObjectByType<MuseumGuideView>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var camera = visitor.GetComponentInChildren<Camera>();
            string Screen() => string.Join("\n", display.Surface.GetComponentsInChildren<TMP_Text>(false).Select(text => text.text));
            var stands = UnityEngine.Object.FindObjectsByType<PointLectern>(FindObjectsSortMode.None);

            // The governor's stand, when the API declares its point.
            var governor = runtime.FindPoint("punto.personajes.gobernador");
            summary.Add("governor_declared: " + (governor != null && governor.State != PointState.Undeclared));
            if (governor != null && governor.State != PointState.Undeclared)
            {
                var stand = stands.Single(candidate => candidate.Point == governor.Anchor);
                Assert.That(visitor.FocusPoint(governor.Anchor, stand.ViewingSpot(1.3f), stand.ViewTarget), Is.True);
                yield return Until(() => Screen().Contains("Crédito:"), 15);
                yield return Until(() => display.Openness >= 1, 3);
                yield return new WaitForSecondsRealtime(.5f);
                Render(camera, "1_gobernador");
                File.WriteAllText(Path.Combine(Folder, "1_gobernador.txt"), Screen());
                summary.Add("governor_screen_has_source_and_credit: " + (Screen().Contains("Fuente:") && Screen().Contains("Crédito:")));
                summary.Add("governor_model_requests_after_open: " + presenter.State);
            }

            // Matachín: the arrows stand by the pedestal on the point's own record, and its mask comes to the pedestal.
            var matachin = runtime.FindPoint("punto.personajes.pedestal_1");
            if (matachin != null && matachin.HasContent)
            {
                var stand = stands.FirstOrDefault(candidate => candidate.Point == matachin.Anchor);
                Assert.That(stand != null ? visitor.FocusPoint(matachin.Anchor, stand.ViewingSpot(1.4f), stand.ViewTarget)
                    : visitor.FocusPoint(matachin.Anchor), Is.True);
                yield return Until(() => interaction.ActivePoint == matachin, 5);
                yield return null;
                yield return null;
                var carousel = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(item => item.name == "PieceCarousel").gameObject;
                summary.Add("matachin_selected: " + interaction.SelectedElement?.slug);
                summary.Add("matachin_arrows_on_own_record_at_once: " + carousel.activeInHierarchy);
                yield return Until(() => presenter.State == ModelLoadState.Ready, 45);
                summary.Add("matachin_piece: " + presenter.State + " " + presenter.ModelSlug);
                yield return Until(() => display.Openness >= 1, 3);
                yield return new WaitForSecondsRealtime(.6f);
                Render(camera, "2_matachin_registro_comun");
            }

            // By the hearth: the conversation about the visit.
            var anchor = runtime.GuidePoint.Anchor;
            Assert.That(visitor.FocusPoint(anchor), Is.True);
            yield return Until(() => interaction.State == InteractionState.Exploration && guide.InRange, 5);
            Assert.That(guide.Ask(), Is.True, interaction.State + " in range " + guide.InRange);
            yield return Until(() => quiz.Available && voice.Client.CapabilityState != VoiceCapabilityState.Loading, 20);
            yield return Until(() => display.Openness >= 1, 3);
            yield return new WaitForSecondsRealtime(.4f);
            Render(camera, "3_guia_fogon");
            summary.Add("quiz_questions: " + quiz.Count);
            if (quiz.Begin())
            {
                yield return new WaitForSecondsRealtime(.4f);
                Render(camera, "4_encuesta_pregunta");
                summary.Add("quiz_first: " + quiz.Current.text + " | " + string.Join(" / ", quiz.Current.options.Select(option => option.text)));
                Assert.That(quiz.Choose(0), Is.True);
                yield return Until(() => quiz.Explanation != null || quiz.Client.AnswerState == GuideQuizAnswerState.Failed, 15);
                summary.Add("quiz_answer: " + quiz.Client.AnswerState + " correct=" + quiz.Explanation?.correct);
                yield return new WaitForSecondsRealtime(.4f);
                Render(camera, "5_encuesta_explicacion");
                File.WriteAllText(Path.Combine(Folder, "5_encuesta_explicacion.txt"), Screen());
                quiz.Finish();
                yield return null;
                guide.Close();
                yield return null;
                Assert.That(guide.Ask(), Is.True);
                yield return Until(() => display.Openness >= 1, 3);
            }

            // The guide's voice with the API's engines.
            summary.Add("voice_capabilities: " + voice.Client.CapabilityState + " " + voice.Client.Capabilities?.transcription + "/"
                + voice.Client.Capabilities?.synthesis + " " + voice.Client.Capabilities?.processing + " ["
                + string.Join(",", voice.Client.Capabilities?.providers ?? new string[0]) + "]");
            if (!string.IsNullOrEmpty(questionPath) && File.Exists(questionPath))
            {
                voice.Recorder = new FileRecorder(File.ReadAllBytes(questionPath));
                yield return Until(() => voice.Offered, 10);
                summary.Add("voice_offered: " + voice.Offered);
                if (voice.Offered)
                {
                    Assert.That(voice.Talk(), Is.True);
                    yield return new WaitForSecondsRealtime(.3f);
                    Render(camera, "6_voz_aviso");
                    summary.Add("voice_notice: " + view.VoiceText.text);
                    Assert.That(voice.Accept(), Is.True);
                    Assert.That(voice.StopTalking(), Is.True);
                    yield return Until(() => voice.Stage != GuideVoiceStage.Transcribing, 60);
                    summary.Add("voice_stage_after_transcription: " + voice.Stage + " " + voice.ProblemKey);
                    yield return new WaitForSecondsRealtime(.3f);
                    Render(camera, "7_voz_transcripcion");
                    if (voice.Stage == GuideVoiceStage.Review)
                    {
                        summary.Add("voice_transcript: " + voice.Client.Transcript);
                        Assert.That(voice.Confirm(), Is.True);
                        yield return Until(() => !guide.Client.Busy, 60);
                        yield return null;
                        summary.Add("voice_answer: " + guide.Client.State + " speech=" + guide.Client.SpeechStatus + " citations="
                            + guide.Citations.Count + " speaking=" + voice.Speaking + " rate=" + (voice.Speaker != null && voice.Speaker.clip != null ? voice.Speaker.clip.frequency : 0)
                            + " seconds=" + (voice.Speaker != null && voice.Speaker.clip != null ? voice.Speaker.clip.length.ToString("F1") : "0"));
                        yield return new WaitForSecondsRealtime(.5f);
                        Render(camera, "8_voz_respuesta");
                        File.WriteAllText(Path.Combine(Folder, "8_voz_respuesta.txt"), Screen());
                    }
                }
            }
            File.WriteAllLines(Path.Combine(Folder, "summary.txt"), summary);
        }
#endif
    }
}
