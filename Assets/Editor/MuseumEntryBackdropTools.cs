using System;
using System.Collections.Generic;
using System.IO;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Editor
{
    /// <summary>The glow material of the entry sets, and reviewable pictures of every entry backdrop and framing.</summary>
    public static class MuseumEntryBackdropTools
    {
        public const string MaterialPath = "Assets/_Musiyo/Resources/" + MuseumEntryBackdrop.GlowMaterial + ".mat";
        public const string TexturePath = "Assets/_Musiyo/UI/Textures/EntrySoftDot.png";
        public const string CaptureFolder = "TestResults/entry-backdrops";

        /// <summary>A soft round dot drawn here, and an additive particle material that uses it.</summary>
        [MenuItem("Musiyo/Entry/Create Backdrop Glow Material")]
        public static void CreateGlowMaterial()
        {
            const int size = 64;
            var dot = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = new Vector2(x + .5f - size * .5f, y + .5f - size * .5f).magnitude / (size * .5f);
                    float alpha = Mathf.Pow(Mathf.Clamp01(1 - distance), 2.2f);
                    dot.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, dot.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(dot);
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = MuseumEntryBackdrop.GlowMaterial };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1);
            // Additive: light adds up where flames overlap and never darkens what is behind.
            material.SetFloat("_Blend", 2);
            BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Renders each backdrop with the real entry menu over it, and the start and end of the glide into the museum.
        /// The mask set is shown with a neutral grey stand-in: no mask is loaded or pictured here.
        /// </summary>
        [MenuItem("Musiyo/Entry/Capture Backdrop Proposals")]
        public static void CaptureProposals()
        {
            EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            Directory.CreateDirectory(CaptureFolder);
            foreach (var existing in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) existing.gameObject.SetActive(false);
            var host = new GameObject("BackdropPreview");
            var backdrop = host.AddComponent<MuseumEntryBackdrop>();
            var camera = new GameObject("BackdropPreviewCamera").AddComponent<Camera>();
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 500;
            var view = MenuOver(camera);
            var input = MuseumInput.CreateDefault();
            try
            {
                foreach (EntryBackdropKind kind in Enum.GetValues(typeof(EntryBackdropKind)))
                {
                    if (kind == EntryBackdropKind.Museum)
                    {
                        var viewpoint = UnityEngine.Object.FindAnyObjectByType<MuseumEntryViewpoint>();
                        Place(camera, viewpoint.transform, viewpoint.FieldOfView);
                        Render(camera, view, true, "backdrop_museum");
                        continue;
                    }
                    backdrop.Configure(kind);
                    if (kind == EntryBackdropKind.Mask) StandIn(host.transform.Find("EntryBackdrop_Mask/MaskAnchor"));
                    Place(camera, backdrop.CameraPose, MuseumEntryView.BackdropFieldOfView);
                    backdrop.Show(true, camera);
                    // Particles only advance while their set is shown.
                    foreach (var system in host.GetComponentsInChildren<ParticleSystem>(true)) system.Simulate(4, true, true);
                    Render(camera, view, true, "backdrop_" + kind.ToString().ToLowerInvariant());
                    Render(camera, view, false, "backdrop_" + kind.ToString().ToLowerInvariant() + "_bare");
                    backdrop.Show(false, camera);
                }
                backdrop.Configure(EntryBackdropKind.Museum);
                var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
                var eyes = visitor.GetComponentInChildren<Camera>(true).transform;
                foreach (var (name, eye, target) in Framings(eyes))
                {
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                    camera.fieldOfView = 50;
                    Render(camera, view, false, "framing_" + name);
                }
                Debug.Log("Entry backdrop proposals saved in " + CaptureFolder + ".");
            }
            finally
            {
                input.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }

        /// <summary>Candidate starts of the glide: the current anchor and a centred view along the path, plus the visitor's eyes.</summary>
        public static IEnumerable<(string name, Vector3 eye, Vector3 target)> Framings(Transform eyes)
        {
            var viewpoint = UnityEngine.Object.FindAnyObjectByType<MuseumEntryViewpoint>().transform;
            yield return ("current_start", viewpoint.position, viewpoint.position + viewpoint.forward * 10);
            var portal = new Vector3(eyes.position.x, 2.4f, eyes.position.z + 30);
            yield return ("centred_start", eyes.position + new Vector3(0, .6f, -6), portal);
            yield return ("high_start", eyes.position + new Vector3(0, 1.4f, -8), portal + Vector3.down * .4f);
            yield return ("visitor_eyes", eyes.position, eyes.position + eyes.forward * 10);
        }

        private static void Place(Camera camera, Transform pose, float field)
        {
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.fieldOfView = field;
        }

        /// <summary>A plain grey oval where a published mask would float, so the lighting can be judged.</summary>
        private static void StandIn(Transform anchor)
        {
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            standIn.name = "NeutralStandIn";
            UnityEngine.Object.DestroyImmediate(standIn.GetComponent<Collider>());
            standIn.transform.SetParent(anchor, false);
            standIn.transform.localScale = new Vector3(.3f, .4f, .14f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.55f, .55f, .55f));
            material.SetFloat("_Smoothness", .35f);
            standIn.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static MuseumMenuView MenuOver(Camera camera)
        {
            var canvas = new GameObject("BackdropPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceCamera;
            canvas.GetComponent<Canvas>().worldCamera = camera;
            canvas.GetComponent<Canvas>().planeDistance = .3f;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var view = UnityEngine.Object.Instantiate(Resources.Load<MuseumMenuView>(MuseumMenuView.ResourceName), canvas.transform, false);
            view.ValidateReferences();
            foreach (var label in view.GetComponentsInChildren<MuseumInterfaceLabel>(true)) label.RefreshText();
            foreach (var theme in view.GetComponentsInChildren<MuseumMenuThemeBinding>(true)) theme.Apply();
            view.Main.Title.text = MuseumInterfaceText.Get("museum_menu_title");
            view.Main.Description.text = MuseumInterfaceText.Get("menu_description");
            foreach (var page in new MuseumMenuPageView[] { view.Pause, view.Settings, view.Controls }) page.gameObject.SetActive(false);
            view.Main.Retry.gameObject.SetActive(false);
            view.Main.Catalog.gameObject.SetActive(false);
            view.Transition.gameObject.SetActive(false);
            view.Welcome.gameObject.SetActive(false);
            view.Overlay.alpha = 1;
            view.Overlay.GetComponent<Image>().color = MuseumExperienceConfiguration.ColorValue(MuseumExperienceConfiguration.Current.EntryBackdropColor);
            return view;
        }

        private static void Render(Camera camera, MuseumMenuView view, bool withMenu, string name)
        {
            const int width = 1440, height = 900;
            view.gameObject.SetActive(withMenu);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            var canvas = view.GetComponentInParent<Canvas>(true);
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
            Canvas.ForceUpdateCanvases();
            if (withMenu) view.GetComponent<MuseumMenuLayout>().Apply();
            Canvas.ForceUpdateCanvases();
            bool asynchronous = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            camera.Render();
            ShaderUtil.allowAsyncCompilation = asynchronous;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            File.WriteAllBytes(Path.Combine(CaptureFolder, name + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
