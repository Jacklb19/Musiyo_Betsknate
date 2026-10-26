using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Tabs on the header line of a point's screen for a point whose card holds several records, named after their
    /// communities (owner, 25/10/2026: «Kamëntsá» and «Inga» at the welcome's meanings of the names), instead of arrows and
    /// «1 de 2». A tab is chosen with the same pointer as the rest of the screen; the chosen one is marked.
    /// </summary>
    public sealed class MuseumRecordTabs : MonoBehaviour
    {
        public const float TabHeight = 40, TabPadding = 22, TabTextSize = 19, Spacing = 8;
        private RectTransform row;
        private TMP_Text style;
        private Action<int> choose;
        private readonly List<(Button button, TMP_Text caption, Image surface, int record)> tabs = new List<(Button, TMP_Text, Image, int)>();
        private string shownKey;

        public RectTransform Row => row;
        public IReadOnlyList<(Button button, TMP_Text caption, Image surface, int record)> Tabs => tabs;
        /// <summary>Record index of the tab marked as chosen, or -1.</summary>
        public int Chosen { get; private set; } = -1;

        /// <summary>Builds the row on the top left of <paramref name="panel"/>, at <paramref name="inset"/> from its corner.</summary>
        public void Configure(RectTransform panel, Vector2 inset, TMP_Text captionStyle, Action<int> chooseRecord)
        {
            style = captionStyle;
            choose = chooseRecord;
            row = new GameObject("RecordTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement)).GetComponent<RectTransform>();
            row.SetParent(panel, false);
            row.GetComponent<LayoutElement>().ignoreLayout = true;
            row.anchorMin = row.anchorMax = row.pivot = new Vector2(0, 1);
            row.anchoredPosition = inset;
            row.sizeDelta = new Vector2(400, MuseumNarrationButtons.RowHeight);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            row.gameObject.SetActive(false);
        }

        /// <summary>Shows the tabs of a point, or hides them where the card holds a single record.</summary>
        public void Show(string pointKey, IReadOnlyList<int> records, IReadOnlyList<string> names, int chosen)
        {
            if (row == null) return;
            bool shown = records != null && records.Count >= 2;
            if (row.gameObject.activeSelf != shown) row.gameObject.SetActive(shown);
            if (!shown) { shownKey = null; Chosen = -1; return; }
            string key = pointKey + "|" + string.Join("|", names);
            if (key != shownKey)
            {
                shownKey = key;
                foreach (var tab in tabs)
                    if (Application.isPlaying) Destroy(tab.button.gameObject); else DestroyImmediate(tab.button.gameObject);
                tabs.Clear();
                for (int index = 0; index < records.Count; index++) tabs.Add(Tab(names[index], records[index]));
            }
            Chosen = chosen;
            var accent = MuseumHudSkin.Accent;
            foreach (var tab in tabs)
            {
                bool on = tab.record == chosen;
                tab.surface.color = on ? new Color(accent.r, accent.g, accent.b, .32f) : new Color(1, 1, 1, .06f);
                tab.caption.color = on ? MuseumHudSkin.Text : MuseumHudSkin.Muted;
                tab.caption.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private (Button, TMP_Text, Image, int) Tab(string name, int record)
        {
            var item = new GameObject("Tab", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            item.transform.SetParent(row, false);
            var surface = item.GetComponent<Image>();
            surface.sprite = MuseumUiSprites.Rounded(MuseumHudSkin.ChoiceRadius);
            surface.type = Image.Type.Sliced;
            var button = item.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => choose?.Invoke(record));
            var caption = Instantiate(style, item.transform);
            caption.name = "Caption";
            caption.richText = false;
            caption.text = name;
            caption.fontSize = TabTextSize;
            caption.alignment = TextAlignmentOptions.Center;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            caption.raycastTarget = false;
            if (caption.TryGetComponent<LayoutElement>(out var captionLayout)) captionLayout.ignoreLayout = true;
            var rect = caption.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            caption.ForceMeshUpdate();
            // As wide as its name, within a sensible limit, so a long title cannot spread over the header.
            float width = Mathf.Clamp(caption.preferredWidth + TabPadding * 2, 90, 260);
            ((RectTransform)item.transform).sizeDelta = new Vector2(width, TabHeight);
            var layout = item.GetComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = TabHeight;
            return (button, caption, surface, record);
        }
    }
}
