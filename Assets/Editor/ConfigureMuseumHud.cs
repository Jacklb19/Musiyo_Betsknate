using System;
using System.Collections.Generic;
using System.IO;
using MusiyoBetsknate.Museum;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MusiyoBetsknate.Editor
{
    public static class ConfigureMuseumHud
    {
        private static TMP_FontAsset font;
        private static readonly Color Background = new Color(.06f, .12f, .11f, .96f);

        [MenuItem("Musiyo/Configure Museum Interface")]
        public static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != BuildMuseumBlockout.ScenePath || scene.isDirty)
                throw new InvalidOperationException("The saved museum scene must be active and clean.");
            var runtime = UnityEngine.Object.FindAnyObjectByType<TourRuntime>();
            if (runtime == null || runtime.GetComponent<MuseumInteraction>() == null)
                throw new InvalidOperationException("Configure the museum interaction before the interface.");
            if (runtime.transform.Find("MuseumInterface") != null)
                throw new InvalidOperationException("Museum interface already exists; no objects were replaced.");
            Directory.CreateDirectory("Assets/_Musiyo/UI");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Musiyo/UI/MuseumSans.asset");
            if (font == null)
            {
                font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>("Assets/_Musiyo/UI/Fonts/MuseumSans.ttf"));
                if (font == null) throw new InvalidOperationException("Could not create the museum interface font.");
                font.name = "MuseumSans";
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                AssetDatabase.CreateAsset(font, "Assets/_Musiyo/UI/MuseumSans.asset");
                AssetDatabase.AddObjectToAsset(font.material, font);
                foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
                if (!font.TryAddCharacters("ëñšÿáéíóúüËÑŠŸÁÉÍÓÚÜABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 ·:.,…—()/%+-", out var missing))
                    throw new InvalidOperationException("Missing interface characters: " + missing);
            }
            var fontSettings = new SerializedObject(font);
            fontSettings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            fontSettings.ApplyModifiedProperties();
            font.material.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Musiyo/UI/Shaders/TMP_SDF-Mobile.shader");
            var canvasRoot = Rect("MuseumInterface", runtime.transform);
            var canvas = Undo.AddComponent<Canvas>(canvasRoot.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = Undo.AddComponent<CanvasScaler>(canvasRoot.gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            Undo.AddComponent<GraphicRaycaster>(canvasRoot.gameObject);

            var top = Rect("Header", canvasRoot);
            Anchor(top, new Vector2(0,1), new Vector2(1,1), new Vector2(.5f,1), Vector2.zero, new Vector2(0,100));
            Image(top, Background);
            var title = Text("Title", top, "Musiyo Bëtsknaté", 30);
            Stretch(title.rectTransform, new Vector2(24,54), new Vector2(-430,-12));
            var status = Text("Status", top, "", 22);
            Stretch(status.rectTransform, new Vector2(24,8), new Vector2(-430,-54));
            var pause = Button("Pause", top, "Pausa");
            Anchor(pause.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one, new Vector2(-24,-20), new Vector2(160,52));
            var retry = Button("Retry", top, "Reintentar");
            Anchor(retry.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one, new Vector2(-200,-20), new Vector2(180,52));

            var instructions = Rect("Controls", canvasRoot);
            Anchor(instructions, Vector2.zero, Vector2.right, new Vector2(.5f,0), Vector2.zero, new Vector2(0,112));
            Image(instructions, Background);
            var controls = Text("ControlText", instructions, "WASD o flechas: caminar · Mayús: avanzar más rápido · Clic: mirar con el ratón · IJKL: mirar con teclado\nTab / Mayús+Tab: seleccionar punto · Enter o E: activar · F: ficha · Retroceso: cerrar · Esc: pausa", 22);
            Stretch(controls.rectTransform, new Vector2(24,16), new Vector2(-24,-16));
            var focus = Text("Focus", canvasRoot, ".", 26);
            Anchor(focus.rectTransform, new Vector2(.5f,.5f), new Vector2(.5f,.5f), new Vector2(.5f,.5f), Vector2.zero, new Vector2(560,80));
            focus.alignment = TextAlignmentOptions.Center;

            var panel = Rect("ContentPanel", canvasRoot);
            Anchor(panel, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24,-124), new Vector2(540,740));
            Image(panel, Background);
            var viewport = Rect("Viewport", panel);
            Stretch(viewport, new Vector2(20,88), new Vector2(-20,-20));
            Undo.AddComponent<RectMask2D>(viewport.gameObject);
            var content = Rect("Content", viewport);
            Anchor(content, Vector2.up, Vector2.one, new Vector2(.5f,1), Vector2.zero, Vector2.zero);
            var group = Undo.AddComponent<VerticalLayoutGroup>(content.gameObject);
            group.spacing = 12;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            Undo.AddComponent<ContentSizeFitter>(content.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = Undo.AddComponent<ScrollRect>(panel.gameObject);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            var panelText = Text("ContentText", content, "", 26);
            var minimum = Undo.AddComponent<LayoutElement>(panelText.gameObject);
            minimum.minHeight = 100;
            var list = Rect("ElementList", content);
            var listLayout = Undo.AddComponent<VerticalLayoutGroup>(list.gameObject);
            listLayout.spacing = 10;
            listLayout.childControlWidth = true;
            listLayout.childControlHeight = true;
            listLayout.childForceExpandHeight = false;
            var template = Button("ElementTemplate", list, "");
            Undo.AddComponent<LayoutElement>(template.gameObject).preferredHeight = 64;
            template.gameObject.SetActive(false);
            var close = Button("Close", panel, "Volver");
            Anchor(close.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(20,20), new Vector2(230,52));
            var detail = Button("ReadDetail", panel, "Leer ficha");
            Anchor(detail.GetComponent<RectTransform>(), Vector2.right, Vector2.right, Vector2.right, new Vector2(-20,20), new Vector2(240,52));

            var sliderRect = Rect("MouseSensitivity", canvasRoot);
            Anchor(sliderRect, Vector2.one, Vector2.one, Vector2.one, new Vector2(-24,-884), new Vector2(540,70));
            Image(sliderRect, Background);
            var sliderLabel = Text("SensitivityLabel", sliderRect, "Sensibilidad del ratón", 22);
            Anchor(sliderLabel.rectTransform, Vector2.up, Vector2.up, Vector2.up, new Vector2(16,-8), new Vector2(500,26));
            var slider = Undo.AddComponent<Slider>(sliderRect.gameObject);
            slider.minValue = .02f;
            slider.maxValue = 1;
            var track = Rect("Track", sliderRect);
            Anchor(track, Vector2.zero, Vector2.right, new Vector2(.5f,0), new Vector2(0,16), new Vector2(-40,8));
            Image(track, new Color(.5f,.6f,.55f));
            var fill = Rect("Fill", track);
            Stretch(fill, Vector2.zero, Vector2.zero);
            Image(fill, new Color(.95f,.82f,.5f));
            var handle = Rect("Handle", track);
            handle.sizeDelta = new Vector2(20,26);
            slider.targetGraphic = Image(handle, Color.white);
            slider.handleRect = handle;
            slider.fillRect = fill;
            slider.direction = Slider.Direction.LeftToRight;

            var eventRoot = new GameObject("MuseumEventSystem");
            Undo.RegisterCreatedObjectUndo(eventRoot, "Create museum event system");
            eventRoot.transform.SetParent(canvasRoot, false);
            Undo.AddComponent<EventSystem>(eventRoot);
            var module = Undo.AddComponent<InputSystemUIInputModule>(eventRoot);
            module.AssignDefaultActions();
            AssetDatabase.CreateAsset(module.actionsAsset, "Assets/_Musiyo/UI/MuseumUIActions.asset");
            var references = new HashSet<InputActionReference> {
                module.point, module.leftClick, module.rightClick, module.middleClick, module.move,
                module.submit, module.cancel, module.scrollWheel, module.trackedDevicePosition, module.trackedDeviceOrientation
            };
            foreach (var reference in references)
                if (reference != null && !AssetDatabase.Contains(reference)) AssetDatabase.AddObjectToAsset(reference, module.actionsAsset);

            var hud = Undo.AddComponent<MuseumHud>(canvasRoot.gameObject);
            var serialized = new SerializedObject(hud);
            Set(serialized, "loader", runtime.GetComponent<TourLoader>());
            Set(serialized, "interaction", runtime.GetComponent<MuseumInteraction>());
            Set(serialized, "pointInput", runtime.GetComponent<DesktopPointInput>());
            Set(serialized, "statusText", status);
            Set(serialized, "focusText", focus);
            Set(serialized, "panelText", panelText);
            Set(serialized, "panel", panel.gameObject);
            Set(serialized, "elementList", list);
            Set(serialized, "elementTemplate", template);
            Set(serialized, "retryButton", retry);
            Set(serialized, "closeButton", close);
            Set(serialized, "pauseButton", pause);
            Set(serialized, "detailButton", detail);
            Set(serialized, "sensitivitySlider", slider);
            Set(serialized, "visitor", runtime.GetComponentInChildren<DesktopVisitorController>());
            serialized.ApplyModifiedProperties();
            panel.gameObject.SetActive(false);
            retry.gameObject.SetActive(false);
            sliderRect.gameObject.SetActive(false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Museum Canvas, keyboard UI, scrolling detail, controls, and diacritic font configured.");
        }

        private static void Set(SerializedObject serialized, string name, UnityEngine.Object value)
            => serialized.FindProperty(name).objectReferenceValue = value;

        private static RectTransform Rect(string name, Transform parent)
        {
            var created = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(created, "Create museum interface");
            created.transform.SetParent(parent, false);
            return (RectTransform)created.transform;
        }

        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position, Vector2 size)
        { rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size; }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }

        private static Image Image(RectTransform rect, Color color)
        { var image = Undo.AddComponent<Image>(rect.gameObject); image.color = color; return image; }

        private static TMP_Text Text(string name, Transform parent, string label, float size)
        {
            var text = Undo.AddComponent<TextMeshProUGUI>(Rect(name, parent).gameObject);
            text.font = font;
            text.fontSize = size;
            text.text = label;
            text.richText = false;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button Button(string name, Transform parent, string label)
        {
            var rect = Rect(name, parent);
            var background = Image(rect, new Color(.25f,.38f,.3f));
            var button = Undo.AddComponent<Button>(rect.gameObject);
            button.targetGraphic = background;
            var colors = button.colors;
            colors.selectedColor = new Color(.95f,.82f,.5f);
            colors.highlightedColor = new Color(.75f,.85f,.75f);
            button.colors = colors;
            var text = Text("Label", rect, label, 24);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.rectTransform, new Vector2(12,6), new Vector2(-12,-6));
            return button;
        }
    }
}
