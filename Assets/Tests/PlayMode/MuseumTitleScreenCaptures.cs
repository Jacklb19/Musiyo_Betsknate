using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MusiyoBetsknate.Museum;
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
    /// Title screen proposals: the entry menu over the hearth set, in each palette under review. Explicit: pictures and a
    /// dump of the menu hierarchy go to TestResults/title-screen. The shipped configuration is restored afterwards.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class MuseumTitleScreenCaptures : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/title-screen";

        /// <summary>Palettes under review: backdrop, entry backdrop, panel, text, accent, selected, pointer, muted, secondary.</summary>
        private static readonly Dictionary<string, string[]> Palettes = new Dictionary<string, string[]>
        {
            // The shared tokens of 18/10/2026 are the shipped palette; others can be added here for comparison.
            { "tokens", null },
        };

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        private static IEnumerator Shot(string name)
        {
            yield return null;
            const int width = 1600, height = 900;
            Camera view = null;
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.isActiveAndEnabled && camera.targetTexture == null && (view == null || camera.depth > view.depth)) view = camera;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var overlays = new List<Canvas>();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    overlays.Add(canvas);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = view;
                    canvas.planeDistance = view.nearClipPlane + .05f;
                }
            Canvas.ForceUpdateCanvases();
            view.targetTexture = target;
            view.Render();
            view.targetTexture = null;
            foreach (var canvas in overlays) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            Object.Destroy(image);
            target.Release();
            Object.Destroy(target);
        }

        private static void Apply(MuseumExperienceConfiguration settings, string[] palette)
        {
            settings.BackdropColor = palette[0]; settings.EntryBackdropColor = palette[1]; settings.PanelColor = palette[2];
            settings.TextColor = palette[3]; settings.AccentColor = palette[4]; settings.SelectedColor = palette[5];
            settings.PointerColor = palette[6]; settings.MutedTextColor = palette[7]; settings.SecondaryAccentColor = palette[8];
        }

        private static string[] Read(MuseumExperienceConfiguration settings) => new[]
        {
            settings.BackdropColor, settings.EntryBackdropColor, settings.PanelColor, settings.TextColor, settings.AccentColor,
            settings.SelectedColor, settings.PointerColor, settings.MutedTextColor, settings.SecondaryAccentColor,
        };

        private static void Dump(Transform item, StringBuilder text, int depth)
        {
            var line = new string(' ', depth * 2) + item.name + (item.gameObject.activeSelf ? "" : " (off)");
            if (item.TryGetComponent<Image>(out var image)) line += " Image#" + ColorUtility.ToHtmlStringRGBA(image.color);
            if (item.TryGetComponent<TMP_Text>(out var label)) line += " Text[" + label.fontSize + "] \"" + label.text.Replace("\n", " ").Substring(0, Mathf.Min(40, label.text.Length)) + "\"";
            if (item.TryGetComponent<Selectable>(out _)) line += " Selectable";
            if (item is RectTransform rect) line += " rect=" + rect.rect.size;
            text.AppendLine(line);
            foreach (Transform child in item) Dump(child, text, depth + 1);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator CaptureTheTitleScreenInEachPalette()
        {
            var settings = MuseumExperienceConfiguration.Current;
            string backdrop = settings.EntryBackdrop;
            var shipped = Read(settings);
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            try
            {
                foreach (var palette in Palettes)
                {
                    Apply(settings, palette.Value ?? shipped);
                    settings.EntryBackdrop = "hearth";
                    yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                        new LoadSceneParameters(LoadSceneMode.Additive));
                    var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
                    flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
                    var loader = flow.GetComponent<TourLoader>();
                    for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot(palette.Key + "_1_title");
                    if (palette.Value == null)
                    {
                        var text = new StringBuilder();
                        var menu = Object.FindAnyObjectByType<MuseumMenuView>();
                        Dump(menu.transform, text, 0);
                        File.WriteAllText(Path.Combine(Folder, "menu-hierarchy.txt"), text.ToString());
                    }
                    var view = Object.FindAnyObjectByType<MuseumMenuView>();
                    view.Main.Settings.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot(palette.Key + "_2_settings");
                    view.Settings.Back.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(.6f);
                    view.Main.Controls.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(1.6f);
                    yield return Shot(palette.Key + "_2b_controls");
                    view.Controls.Back.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(.6f);
                    flow.BeginVisit();
                    yield return new WaitForSecondsRealtime(MuseumExperienceConfiguration.Current.EntryFadeSeconds * .3f);
                    yield return Shot(palette.Key + "_3_way_in");
                    flow.EnterImmediately();
                    flow.GetComponent<MuseumArrival>()?.Dismiss();
                    flow.GetComponent<MuseumInteraction>().SetPaused(true);
                    yield return new WaitForSecondsRealtime(1.4f);
                    yield return Shot(palette.Key + "_4_pause_in_museum");
                    view.Pause.Settings.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Shot(palette.Key + "_5_settings_in_museum");
                    var scene = SceneManager.GetSceneByPath(MuseumScene);
                    yield return SceneManager.UnloadSceneAsync(scene);
                    yield return null;
                }
            }
            finally
            {
                Apply(settings, shipped);
                settings.EntryBackdrop = backdrop;
            }
        }
#endif
    }
}
