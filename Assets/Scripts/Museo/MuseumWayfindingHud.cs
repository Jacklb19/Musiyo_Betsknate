using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Optional orientation. It has no permanent button or legend on the screen: it opens from its key or from the pause
    /// menu, and its status shows only while it guides the visitor or for a moment after it changes.
    /// </summary>
    public sealed class MuseumWayfindingHud : MonoBehaviour
    {
        /// <summary>Seconds a change of orientation status, such as an arrival, stays on screen.</summary>
        private const float StatusSeconds = 6;
        private MuseumWayfinding guide;
        private MuseumInteraction interaction;
        private Button template;
        private string shownStatus;
        private float statusUntil;
        private GameObject menu;
        private TMP_Text legend;
        private TMP_Text title;
        private Transform choices;
        private bool wasOpen;
        private RectTransform legendPill;
        private MuseumInput input;
        private readonly System.Collections.Generic.List<(TMP_Text caption, string textKey)> keyedCaptions = new();
        public void Configure(MuseumWayfinding wayfinding, MuseumInteraction state, Button buttonTemplate, TMP_Text textTemplate)
        {
            guide = wayfinding;
            interaction = state;
            template = buttonTemplate;
            legend = Instantiate(textTemplate, transform);
            legend.name = "WayfindingStatus";
            legend.raycastTarget = false;
            legend.alignment = TextAlignmentOptions.TopLeft;
            legend.fontSize = 24;
            var legendRect = legend.rectTransform;
            legendRect.anchorMin = legendRect.anchorMax = new Vector2(0, 1);
            legendRect.pivot = new Vector2(0, 1);
            legendRect.anchoredPosition = new Vector2(24, -110);
            legendRect.sizeDelta = new Vector2(1200, 80);
            menu = new GameObject("WayfindingMenu", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            menu.transform.SetParent(transform, false);
            var menuRect = (RectTransform)menu.transform;
            menuRect.anchorMin = menuRect.anchorMax = new Vector2(0, 1);
            menuRect.pivot = new Vector2(0, 1);
            menuRect.anchoredPosition = new Vector2(24, -110);
            menuRect.sizeDelta = new Vector2(630, 690);
            MuseumHudSkin.Surface(menu.GetComponent<Image>());
            legend.color = MuseumHudSkin.Text;
            legendPill = MuseumHudSkin.PillBehind(legend);
            var layout = menu.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            title = Instantiate(textTemplate, menu.transform);
            title.name = "WayfindingTitle";
            title.alignment = TextAlignmentOptions.TopLeft;
            title.fontSize = 25;
            title.color = MuseumHudSkin.Text;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
            choices = new GameObject("RoomChoices", typeof(RectTransform), typeof(VerticalLayoutGroup)).transform;
            choices.SetParent(menu.transform, false);
            var choiceLayout = choices.GetComponent<VerticalLayoutGroup>();
            choiceLayout.spacing = 8;
            choiceLayout.childControlHeight = true;
            choiceLayout.childForceExpandHeight = false;
            KeyedButton("wayfinding_follow", menu.transform, guide.FollowSuggested);
            KeyedButton("wayfinding_stop", menu.transform, () => { guide.StopGuidance(); guide.CloseMenu(); });
            KeyedButton("wayfinding_close", menu.transform, guide.CloseMenu);
            guide.Changed += Refresh;
            interaction.Changed += Refresh;
            input = MuseumInput.Current;
            input.BindingsChanged += RefreshKeys;
            shownStatus = guide.Status;
            Refresh();
        }
        /// <summary>True while the orientation status is on screen: while guiding, or briefly after it changed.</summary>
        public bool StatusVisible => legend != null && legend.gameObject.activeSelf;
        private bool StatusWanted => guide.TargetRoomKey != null || Time.unscaledTime < statusUntil;
        private void Update()
        {
            if (legend != null && legend.gameObject.activeSelf && !StatusWanted) Refresh();
        }
        /// <summary>Captions that name a key are created from their text key and follow reassignments.</summary>
        private Button KeyedButton(string textKey, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var button = ActionButton(MuseumInterfaceText.Get(textKey), parent, action);
            keyedCaptions.Add((button.GetComponentInChildren<TMP_Text>(), textKey));
            return button;
        }
        private void RefreshKeys()
        {
            foreach (var (caption, textKey) in keyedCaptions)
                if (caption != null) caption.text = MuseumInterfaceText.Get(textKey);
        }
        private Button ActionButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
        {
            var button = Instantiate(template, parent);
            button.name = "WayfindingAction";
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            button.GetComponentInChildren<TMP_Text>().text = label;
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 48;
            MuseumHudSkin.Choice(button);
            return button;
        }
        private void Refresh()
        {
            if (guide == null || menu == null) return;
            if (guide.Status != shownStatus)
            {
                shownStatus = guide.Status;
                statusUntil = Time.unscaledTime + StatusSeconds;
            }
            menu.SetActive(guide.MenuOpen);
            legend.gameObject.SetActive(!guide.MenuOpen && !interaction.BlocksMovement && StatusWanted);
            legend.text = MuseumInterfaceText.Format("wayfinding_legend", guide.CurrentRoomName, string.IsNullOrEmpty(guide.Status) ? MuseumInterfaceText.Get("wayfinding_free") : guide.Status);
            MuseumHudSkin.Fit(legendPill, legend);
            title.text = guide.Rooms.Count == 0 ? MuseumInterfaceText.Format("wayfinding_title_empty", guide.CurrentRoomName)
                : MuseumInterfaceText.Format("wayfinding_title", guide.CurrentRoomName);
            if (guide.MenuOpen)
            {
                foreach (Transform child in choices) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                Button first = null;
                foreach (var room in guide.Rooms)
                {
                    var key = room.key;
                    var button = ActionButton(room.name, choices, () => guide.SelectRoom(key));
                    if (first == null) first = button;
                }
                if (!wasOpen && first != null && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
            wasOpen = guide.MenuOpen;
        }
        private void OnDestroy()
        {
            if (guide != null) guide.Changed -= Refresh;
            if (interaction != null) interaction.Changed -= Refresh;
            if (input != null) input.BindingsChanged -= RefreshKeys;
        }
    }
}
