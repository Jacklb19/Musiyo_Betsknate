using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// No caption of a point's card and of the cues beside its piece spills out of its place, at the usual reading size
    /// and at the largest one (owner, 25/10/2026: texts overflowing their buttons and labels).
    /// </summary>
    public sealed class MuseumCardOverflowPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Pieces = "punto.personajes.pedestal_4";
        private const string Records = "punto.bienvenida.significados";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>
        /// Captions in the museum's own space that do not fit their place: a single line wider than its box, wrapped text
        /// taller than its box, or a caption spilling out of its button. Text that scrolls is left to its scroll.
        /// </summary>
        public static List<string> Overflowing()
        {
            Canvas.ForceUpdateCanvases();
            var found = new List<string>();
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text) || text.text == ".") continue;
                var canvas = text.canvas != null ? text.canvas.rootCanvas : null;
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;
                if (text.GetComponentInParent<CanvasGroup>() is CanvasGroup group && group.alpha <= 0) continue;
                text.ForceMeshUpdate();
                var box = text.rectTransform.rect;
                string name = Path(text.transform) + " «" + text.text + "»";
                bool scrolls = text.GetComponentInParent<ScrollRect>() is ScrollRect scroll && scroll.content != null
                    && text.transform.IsChildOf(scroll.content);
                if (text.enableAutoSizing && text.fontSize > text.fontSizeMin + .01f) continue;
                if (text.textWrappingMode == TextWrappingModes.NoWrap)
                {
                    if (text.preferredWidth > box.width + 2) found.Add(name + $": {text.preferredWidth:0} wide in {box.width:0}");
                }
                else if (!scrolls && text.GetPreferredValues(text.text, box.width, 0).y > box.height + 2)
                    found.Add(name + $": {text.GetPreferredValues(text.text, box.width, 0).y:0} tall in {box.height:0}");
                var button = text.GetComponentInParent<Button>();
                if (button != null && button.transform != text.transform)
                {
                    var outer = (RectTransform)button.transform;
                    var bounds = text.textBounds;
                    if (bounds.size.x <= 0) continue;
                    Vector3 Corner(Vector3 local) => outer.InverseTransformPoint(text.rectTransform.TransformPoint(local));
                    var low = Corner(bounds.min);
                    var high = Corner(bounds.max);
                    var rect = outer.rect;
                    if (low.x < rect.xMin - 2 || high.x > rect.xMax + 2 || low.y < rect.yMin - 2 || high.y > rect.yMax + 2)
                        found.Add(name + ": out of its button");
                }
            }
            return found;
        }

        private static string Path(Transform item)
        {
            var names = new List<string>();
            for (var step = item; step != null && names.Count < 4; step = step.parent) names.Add(step.name);
            names.Reverse();
            return string.Join("/", names);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator NoCaptionOfTheCardOrTheCuesSpillsOutAtAnyReadingSize()
        {
            InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService();
            var cube = Convert.FromBase64String(RemoteModelPlayTests.Cube);
            // Long titles, as the published masks have, so the cues and the label meet real lengths.
            var masks = new[] { ("mask-animal", "Máscara de Sanjuán: figura animal"), ("mask-whistle", "Máscara de Sanjuán: silbido"),
                ("mask-smooth", "Máscara de Sanjuán: sin arrugas") };
            foreach (var (slug, title) in masks) service.AddModel(slug, title, cube);
            service.AddRecord("character", SyntheticMuseumService.TextRecord("character", "Sanjuanes"));
            service.AddRecord("meanings-k", SyntheticMuseumService.TextRecord("meanings-k", "Nombres y significados"));
            service.AddRecord("meanings-i", SyntheticMuseumService.TextRecord("meanings-i", "Atun Puncha"));
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var interaction = flow.GetComponent<MuseumInteraction>();
            var presenter = flow.GetComponent<MuseumModelPresenter>();
            var grab = flow.GetComponent<MuseumModelGrab>();
            var display = flow.GetComponent<MuseumLecternDisplay>();
            var hud = UnityEngine.Object.FindAnyObjectByType<MuseumHud>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            var view = visitor.GetComponentInChildren<Camera>().transform;
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            yield return Until(() => loader.State != TourLoadState.Loading, 20);
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1, new PointContractV1
                    {
                        key = Records, name = "Significados de los nombres", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[]
                        {
                            new ElementSummaryContractV1 { slug = "meanings-k", title = "Nombres y significados", community = "Kamëntsá" },
                            new ElementSummaryContractV1 { slug = "meanings-i", title = "Atun Puncha", community = "Inga" },
                        }
                    }),
                    Room("room.characters", 2, new PointContractV1
                    {
                        key = Pieces, name = "Sanjuanes", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "character", title = "Sanjuanes" } }
                            .Concat(masks.Select(mask => new ElementSummaryContractV1 { slug = mask.Item1, title = mask.Item2, has_3d_model = true })).ToArray()
                    }),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
            var preferences = (MuseumVisitorPreferences)typeof(MuseumHud).GetField("preferences", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
            var problems = new List<string>();
            try
            {
                foreach (float scale in new[] { MuseumComfort.ReadingScales.Min(), MuseumComfort.ReadingScales.Max() })
                {
                    preferences.SetReadingScale(scale);
                    // A point whose card holds two records, as tabs.
                    var records = runtime.FindPoint(Records);
                    var stand = display.Find(records);
                    Assert.That(stand != null ? visitor.FocusPoint(records.Anchor, stand.ViewingSpot(1.5f), stand.ViewTarget) : visitor.FocusPoint(records.Anchor), Is.True);
                    yield return Until(() => interaction.ActivePoint == records, 5);
                    yield return Until(() => display.Openness >= 1, 3);
                    yield return null;
                    problems.AddRange(Overflowing().Select(item => $"x{scale} tabs: {item}"));

                    // A point whose card stays on the character, with masks stepping beside the piece and a label under it.
                    var pieces = runtime.FindPoint(Pieces);
                    stand = display.Find(pieces);
                    Assert.That(stand != null ? visitor.FocusPoint(pieces.Anchor, stand.ViewingSpot(1.5f), stand.ViewTarget) : visitor.FocusPoint(pieces.Anchor), Is.True);
                    yield return Until(() => interaction.ActivePoint == pieces, 5);
                    yield return Until(() => presenter.State == ModelLoadState.Ready, 15);
                    yield return new WaitForSecondsRealtime(MuseumModelPresenter.RevealSeconds + .2f);
                    yield return Until(() => display.Openness >= 1, 3);
                    // Held, the piece shows its label and the cue on handling it.
                    Press(mouse.rightButton);
                    Assert.That(grab.Take(view.position, view.rotation, true, true), Is.True);
                    grab.Follow(view.position, view.rotation, 1);
                    var label = UnityEngine.Object.FindAnyObjectByType<MuseumPieceLabel>();
                    label.Advance(1);
                    yield return null;
                    problems.AddRange(Overflowing().Select(item => $"x{scale} pieces: {item}"));
                    Release(mouse.rightButton);
                    yield return null;
                    yield return Until(() => !grab.Returning, 3);
                }
            }
            finally { preferences.SetReadingScale(1); }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
#endif
    }
}
