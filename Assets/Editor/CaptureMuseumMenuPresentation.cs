using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using MusiyoBetsknate.Museum;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Renders the real menu prefabs over the saved museum; these are Editor previews, not player acceptance.</summary>
    public static class CaptureMuseumMenuPresentation
    {
        public static void Capture()
        {
            EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                existing.gameObject.SetActive(false);
            var viewpoint = UnityEngine.Object.FindAnyObjectByType<MuseumEntryViewpoint>();
            var camera = new GameObject("PresentationPreviewCamera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(viewpoint.transform.position, viewpoint.transform.rotation);
            camera.fieldOfView = 45;
            camera.nearClipPlane = .1f; camera.farClipPlane = 500;
            var canvas = new GameObject("PresentationPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceCamera;
            canvas.GetComponent<Canvas>().worldCamera = camera;
            canvas.GetComponent<Canvas>().planeDistance = .5f;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var view = UnityEngine.Object.Instantiate(Resources.Load<MuseumMenuView>(MuseumMenuView.ResourceName), canvas.transform, false);
            view.ValidateReferences();
            foreach (var label in view.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.RefreshText();
            foreach (var theme in view.GetComponentsInChildren<MuseumMenuThemeBinding>(true)) theme.Apply();
            view.Main.Title.text = MuseumInterfaceText.Get("museum_menu_title");
            view.Main.Description.text = MuseumInterfaceText.Get("menu_description");
            view.Pause.Title.text = MuseumInterfaceText.Get("pause");
            view.Pause.Description.text = MuseumInterfaceText.Get("pause_menu_description");
            view.Settings.Title.text = MuseumInterfaceText.Get("settings");
            view.Settings.Description.text = MuseumInterfaceText.Get("settings_description");
            view.Controls.Title.text = MuseumInterfaceText.Get("controls");
            view.Controls.Description.text = MuseumInterfaceText.Get("controls_description");
            view.Transition.gameObject.SetActive(false); view.Welcome.gameObject.SetActive(false);
            view.Overlay.alpha = 1;
            var input = MuseumInput.CreateDefault();
            try
            {
                foreach (var definition in input.Bindings.All)
                {
                    var row = UnityEngine.Object.Instantiate(view.Controls.RowPrefab, view.Controls.Bindings, false);
                    row.Configure(input, definition, _ => { });
                }
                Directory.CreateDirectory("TestResults/menu-previews");
                foreach (var page in new[] { view.Main, view.Pause, view.Settings, view.Controls })
                    Render(view, page, camera, 1440, 900, page.Kind.ToString().ToLowerInvariant());
                Render(view, view.Main, camera, 720, 900, "main-compact");
                Debug.Log("Actual authored menu Editor previews saved in TestResults/menu-previews.");
            }
            finally { input.Dispose(); }
        }
        private static void Render(MuseumMenuView view, MuseumMenuPageView page, Camera camera, int width, int height, string name)
        {
            foreach (var current in new[] { view.Main, view.Pause, view.Settings, view.Controls }) current.gameObject.SetActive(current == page);
            view.Main.Retry.gameObject.SetActive(false); view.Main.Catalog.gameObject.SetActive(false);
            view.Pause.Retry.gameObject.SetActive(false); view.Pause.Catalog.gameObject.SetActive(false);
            var backdrop = view.Overlay.GetComponent<Image>();
            backdrop.color = MuseumExperienceConfiguration.ColorValue(page == view.Main
                ? MuseumExperienceConfiguration.Current.EntryBackdropColor : MuseumExperienceConfiguration.Current.BackdropColor);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            var canvas = view.GetComponentInParent<Canvas>();
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            camera.pixelRect = new Rect(0, 0, width, height);
            Canvas.ForceUpdateCanvases();
            view.GetComponent<MuseumMenuLayout>().Apply();
            Canvas.ForceUpdateCanvases();
            page.Scroll.verticalNormalizedPosition = 1;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes("TestResults/menu-previews/" + name + ".png", texture.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
