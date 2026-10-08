using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumWayfindingHud : MonoBehaviour
    {
        private MuseumWayfinding guide;
        private MuseumInteraction interaction;
        private Button template;
        private Button open;
        private GameObject menu;
        private TMP_Text legend;
        private TMP_Text title;
        private Transform choices;
        private bool wasOpen;
        public void Configure(MuseumWayfinding wayfinding, MuseumInteraction state, Button buttonTemplate, TMP_Text textTemplate)
        {
            guide = wayfinding;
            interaction = state;
            template = buttonTemplate;
            open = ActionButton(MuseumInterfaceText.Get("wayfinding_open"), transform, guide.ToggleMenu);
            var rect = open.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(-396, -20);
            rect.sizeDelta = new Vector2(240, 48);
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
            menu.GetComponent<Image>().color = new Color(.06f, .12f, .11f, .97f);
            var layout = menu.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            title = Instantiate(textTemplate, menu.transform);
            title.name = "WayfindingTitle";
            title.alignment = TextAlignmentOptions.TopLeft;
            title.fontSize = 25;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
            choices = new GameObject("RoomChoices", typeof(RectTransform), typeof(VerticalLayoutGroup)).transform;
            choices.SetParent(menu.transform, false);
            var choiceLayout = choices.GetComponent<VerticalLayoutGroup>();
            choiceLayout.spacing = 8;
            choiceLayout.childControlHeight = true;
            choiceLayout.childForceExpandHeight = false;
            ActionButton(MuseumInterfaceText.Get("wayfinding_follow"), menu.transform, guide.FollowSuggested);
            ActionButton(MuseumInterfaceText.Get("wayfinding_stop"), menu.transform, () => { guide.StopGuidance(); guide.CloseMenu(); });
            ActionButton(MuseumInterfaceText.Get("wayfinding_close"), menu.transform, guide.CloseMenu);
            guide.Changed += Refresh;
            interaction.Changed += Refresh;
            Refresh();
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
            return button;
        }
        private void Refresh()
        {
            if (guide == null || menu == null) return;
            open.interactable = !interaction.BlocksMovement;
            menu.SetActive(guide.MenuOpen);
            legend.gameObject.SetActive(!guide.MenuOpen && !interaction.BlocksMovement);
            legend.text = MuseumInterfaceText.Format("wayfinding_legend", guide.CurrentRoomName, string.IsNullOrEmpty(guide.Status) ? MuseumInterfaceText.Get("wayfinding_free") : guide.Status);
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
        }
    }
}
