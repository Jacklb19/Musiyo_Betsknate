using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MusiyoBetsknate.Museum;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Authors neutral presentation in the reusable menu prefabs, without scene or content changes.</summary>
    public static class ConfigureMuseumMenuPresentation
    {
        [MenuItem("Musiyo/Configure Museum Menu Presentation")]
        public static void Configure()
        {
            foreach (string name in new[] { "MuseumMainMenu", "MuseumPauseMenu", "MuseumSettingsMenu", "MuseumControlsMenu" })
                StylePage("Assets/_Musiyo/UI/Menu/" + name + ".prefab");
            StyleRow();
            const string path = "Assets/_Musiyo/UI/Resources/MuseumMenuRoot.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<MuseumMenuView>();
                foreach (var group in view.Overlay.GetComponents<LayoutGroup>()) group.enabled = false;
                foreach (var fitter in view.Overlay.GetComponents<ContentSizeFitter>()) fitter.enabled = false;
                foreach (var group in view.Main.transform.parent.GetComponents<LayoutGroup>()) group.enabled = false;
                if (root.GetComponent<MuseumMenuLayout>() == null) root.AddComponent<MuseumMenuLayout>();
                view.ValidateReferences();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("Museum menu presentation authored; no scene or licensed asset changed.");
        }
        private static void StylePage(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var page = root.GetComponent<MuseumMenuPageView>();
                var size = root.GetComponent<LayoutElement>();
                size.preferredWidth = page.Kind == MuseumMenuPageKind.Controls ? 840 : page.Kind == MuseumMenuPageKind.Settings ? 700 : 620;
                size.preferredHeight = page.Kind == MuseumMenuPageKind.Controls ? 940 : page.Kind == MuseumMenuPageKind.Settings ? 850 : 800;
                var panel = root.GetComponent<Image>() ?? root.AddComponent<Image>();
                panel.raycastTarget = true;
                Theme(root, MuseumMenuColorToken.Panel);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.raycastTarget = false;
                    text.fontStyle = FontStyles.Normal;
                    text.textWrappingMode = TextWrappingModes.Normal;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                    text.margin = new Vector4(6, 4, 6, 4);
                }
                page.Title.fontStyle = FontStyles.Bold;
                page.Title.characterSpacing = -.3f;
                var titleLayout = page.Title.GetComponent<LayoutElement>();
                if (titleLayout != null) { titleLayout.minHeight = 92; titleLayout.preferredHeight = 108; }
                Theme(page.Description.gameObject, MuseumMenuColorToken.MutedText);
                var content = page.Scroll.content;
                var layout = content.GetComponent<VerticalLayoutGroup>();
                if (layout != null) { layout.spacing = 14; layout.padding = new RectOffset(4, 4, 6, 12); }
                page.Scroll.viewport.offsetMin = new Vector2(38, page.Back != null ? 112 : 38);
                page.Scroll.viewport.offsetMax = new Vector2(-38, -38);
                foreach (var button in root.GetComponentsInChildren<Button>(true)) StyleButton(button, button == page.Primary);
                foreach (var slider in root.GetComponentsInChildren<Slider>(true))
                {
                    var handle = slider.handleRect;
                    if (handle != null)
                    {
                        handle.anchorMin = new Vector2(handle.anchorMin.x, .5f);
                        handle.anchorMax = new Vector2(handle.anchorMax.x, .5f);
                        handle.sizeDelta = new Vector2(26, 28);
                    }
                }
                if (page.Back != null)
                {
                    var back = (RectTransform)page.Back.transform;
                    back.SetParent(root.transform, false);
                    back.anchorMin = Vector2.zero; back.anchorMax = Vector2.right;
                    back.pivot = new Vector2(.5f, 0);
                    back.anchoredPosition = new Vector2(0, 26); back.sizeDelta = new Vector2(-76, 62);
                }
                AddStripe(root.transform, "PresentationEdge", MuseumMenuColorToken.Accent, 3, 0);
                AddStripe(root.transform, "PresentationWarmEdge", MuseumMenuColorToken.SecondaryAccent, 3, 3);
                var kicker = CreateLabel("PresentationKicker", content, page.Title.font, "menu_kicker", 15, MuseumMenuColorToken.Accent);
                kicker.transform.SetAsFirstSibling();
                kicker.characterSpacing = 3;
                var oldRule = content.Find("PresentationRule");
                if (oldRule != null) UnityEngine.Object.DestroyImmediate(oldRule.gameObject);
                var rule = new GameObject("PresentationRule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                rule.transform.SetParent(content, false);
                rule.transform.SetSiblingIndex(2);
                rule.GetComponent<LayoutElement>().preferredHeight = 1;
                rule.GetComponent<Image>().raycastTarget = false;
                Theme(rule, MuseumMenuColorToken.SecondaryAccent);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void StyleRow()
        {
            const string path = "Assets/_Musiyo/UI/Menu/MuseumRebindRow.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true)) StyleButton(button, false);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.raycastTarget = false;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void StyleButton(Button button, bool primary)
        {
            var image = button.GetComponent<Image>();
            if (image != null) image.color = Color.white;
            var theme = button.GetComponent<MuseumMenuThemeBinding>() ?? button.gameObject.AddComponent<MuseumMenuThemeBinding>();
            theme.ColorToken = MuseumMenuColorToken.None;
            theme.BindSelectableColors = true;
            theme.PrimaryAction = primary;
            var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            size.minHeight = primary ? 76 : 62;
            size.preferredHeight = primary ? 76 : 62;
            var caption = button.GetComponentInChildren<TMP_Text>(true);
            if (caption != null)
            {
                caption.alignment = TextAlignmentOptions.MidlineLeft;
                caption.margin = new Vector4(22, 6, 18, 6);
                caption.fontStyle = primary ? FontStyles.Bold : FontStyles.Normal;
                Theme(caption.gameObject, primary ? MuseumMenuColorToken.Panel : MuseumMenuColorToken.Text);
            }
            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.65f, .74f, .61f, .28f);
            outline.effectDistance = new Vector2(1, -1);
            theme.Apply();
        }
        private static void AddStripe(Transform parent, string name, MuseumMenuColorToken token, float width, float offset)
        {
            var previous = parent.Find(name);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(offset, 0); rect.sizeDelta = new Vector2(width, 0);
            obj.GetComponent<Image>().raycastTarget = false;
            Theme(obj, token);
        }
        private static TMP_Text CreateLabel(string name, Transform parent, TMP_FontAsset font, string key, float size, MuseumMenuColorToken token)
        {
            var previous = parent.Find(name);
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.raycastTarget = false;
            obj.GetComponent<LayoutElement>().preferredHeight = 26;
            obj.AddComponent<MuseumInterfaceLabel>().TextKey = key;
            text.text = MuseumInterfaceText.Get(key);
            Theme(obj, token);
            return text;
        }
        private static void Theme(GameObject obj, MuseumMenuColorToken token)
        {
            var theme = obj.GetComponent<MuseumMenuThemeBinding>() ?? obj.AddComponent<MuseumMenuThemeBinding>();
            theme.ColorToken = token;
            theme.Apply();
        }
    }
}
