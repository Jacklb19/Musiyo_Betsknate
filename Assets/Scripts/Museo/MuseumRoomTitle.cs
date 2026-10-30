using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The room's name the first time the visitor enters it (owner, 28/10/2026: «que entres por ejemplo a la primera sala y
    /// te diga, bienvenido…»; «los nombres de la API tal como están»): its name as the museum service gives it and where it
    /// stands in the visit, for about three seconds, then it fades. A room entered again is not announced again.
    /// It stands in the upper third, away from the subtitles at the bottom: at the top of the screen on a computer or in
    /// the browser, and on a headset about two metres ahead, a little above the eyes, following the gaze gently. It waits
    /// while a record is open, a narration's subtitles run or the companion speaks, and is then shown smaller if they go
    /// on; it never shows over a menu, at the entry or during the credits. Anything else that needs the visitor's attention
    /// first, such as a first-visit tutorial, can keep it waiting with <see cref="Hold"/> and <see cref="Release"/>.
    /// </summary>
    public sealed class MuseumRoomTitle : MonoBehaviour
    {
        public const float FadeInSeconds = .45f, HoldSeconds = 3, FadeOutSeconds = .9f;
        /// <summary>Seconds the visit has been open before a title shows, so it does not arrive with the way in.</summary>
        public const float SettleSeconds = 1;
        /// <summary>Seconds a title waits for a record, a narration or the companion before it shows smaller anyway.</summary>
        public const float BusyWaitSeconds = 6;
        public const float CompactScale = .72f;
        public const float ShrinkSeconds = .3f;
        /// <summary>On a screen: distance from the top edge, in interface units.</summary>
        public const float ScreenTop = 40;
        public const float NameSize = 40, PositionSize = 19;
        /// <summary>On a headset: metres ahead of the eyes and above them, metres per interface unit, and how fast it follows.</summary>
        public const float SpatialDistance = 2, SpatialRise = .22f, SpatialUnit = .0018f, SpatialFollow = 3;
        /// <summary>Nearest a wall in front of the visitor may push the title, in metres.</summary>
        public const float SpatialNearest = .8f;

        private Func<string> currentRoom;
        private Func<IReadOnlyList<RoomContractV1>> rooms;
        private MuseumVisitFlow flow;
        private MuseumInteraction interaction;
        private MuseumWayfinding wayfinding;
        private MuseumNarration narration;
        private MuseumArrival arrival;
        private MuseumGuideVoice guideVoice;
        private MuseumCompanion companion;
        private float nextCompanionSearch;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<object> holds = new HashSet<object>();
        private string pending;
        private float pendingSince;
        private float exploringSince = -1;
        private float shownAt;
        private RectTransform card;
        private CanvasGroup group;
        private TMP_Text nameText, positionText;
        private Canvas spatialCanvas;
        private Transform head;
        private float now;

        /// <summary>The room being announced, or null.</summary>
        public string ShownRoomKey { get; private set; }
        public bool Showing => ShownRoomKey != null;
        /// <summary>True when the title was shown smaller because the visitor was busy with something else.</summary>
        public bool Compact { get; private set; }
        public float Alpha => group != null ? group.alpha : 0;
        public RectTransform Card => card;
        public TMP_Text NameText => nameText;
        public TMP_Text PositionText => positionText;
        /// <summary>The canvas that carries the title on a headset, or null on a screen.</summary>
        public Canvas SpatialCanvas => spatialCanvas;
        /// <summary>True while something keeps the titles waiting.</summary>
        public bool Held => holds.Count > 0;
        /// <summary>
        /// Whether the visitor is busy with something a title must not cover: a record open, a narration's subtitles or the
        /// companion or the guide speaking. Replaceable for tests.
        /// </summary>
        public Func<bool> Busy { get; set; }
        /// <summary>Raised when a room's title appears, with the room's key.</summary>
        public event Action<string> Announced;

        public void Configure(MuseumWayfinding orientation, MuseumVisitFlow visit, MuseumInteraction state, MuseumNarration voice, TMP_Text template)
        {
            wayfinding = orientation;
            Configure(() => orientation != null ? orientation.CurrentRoomKey : null,
                () => orientation != null ? orientation.Rooms : Array.Empty<RoomContractV1>(), visit, state, voice, template);
        }

        public void Configure(Func<string> current, Func<IReadOnlyList<RoomContractV1>> list, MuseumVisitFlow visit, MuseumInteraction state,
            MuseumNarration voice, TMP_Text template)
        {
            currentRoom = current;
            rooms = list;
            flow = visit;
            interaction = state;
            narration = voice;
            Busy ??= DefaultBusy;
            if (card == null) Build(template);
        }

        /// <summary>Keeps every title waiting until the same owner releases it; a title on screen leaves at once and comes back after.</summary>
        public void Hold(object owner)
        {
            if (owner == null || !holds.Add(owner) || !Showing) return;
            Withdraw();
        }

        public void Release(object owner)
        {
            if (owner != null) holds.Remove(owner);
        }

        /// <summary>
        /// True when the room's title will say it is there, or is saying it: the first entry into the room. The orientation's
        /// «Has llegado a…» would repeat it then.
        /// </summary>
        public bool Announces(string roomKey) => !string.IsNullOrEmpty(roomKey) && (ShownRoomKey == roomKey || !seen.Contains(roomKey) && Named(roomKey));

        /// <summary>On a headset the title stands on a canvas of its own in the room, ahead of the eyes, not on the screen's.</summary>
        public void UseSpatial(Camera eyes)
        {
            if (eyes == null || card == null || spatialCanvas != null) return;
            head = eyes.transform;
            var root = new GameObject("SpatialRoomTitle", typeof(RectTransform), typeof(Canvas));
            spatialCanvas = root.GetComponent<Canvas>();
            spatialCanvas.renderMode = RenderMode.WorldSpace;
            spatialCanvas.worldCamera = eyes;
            // Under the subtitles' canvas, which is drawn last of all.
            spatialCanvas.sortingOrder = 45;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(1400, 300);
            rect.localScale = Vector3.one * SpatialUnit;
            card.SetParent(rect, false);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = Vector2.zero;
            root.SetActive(false);
        }

        private void Build(TMP_Text template)
        {
            card = new GameObject("RoomTitle", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            card.SetParent(transform, false);
            card.anchorMin = card.anchorMax = new Vector2(.5f, 1);
            card.pivot = new Vector2(.5f, 1);
            card.anchoredPosition = new Vector2(0, -ScreenTop);
            var surface = card.GetComponent<Image>();
            MuseumHudSkin.Pill(surface, .84f);
            group = card.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 14, 18);
            layout.spacing = 0;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = card.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            positionText = Line(template, "RoomTitlePosition", PositionSize, MuseumHudSkin.Accent, FontStyles.UpperCase);
            positionText.characterSpacing = 8;
            nameText = Line(template, "RoomTitleName", NameSize, MuseumHudSkin.Text, FontStyles.Bold);
            card.gameObject.SetActive(false);
        }

        private TMP_Text Line(TMP_Text template, string name, float size, Color color, FontStyles style)
        {
            TMP_Text text;
            if (template != null)
            {
                text = Instantiate(template, card);
                for (int index = text.transform.childCount - 1; index >= 0; index--)
                {
                    var child = text.transform.GetChild(index).gameObject;
                    if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                }
            }
            else text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            text.transform.SetParent(card, false);
            text.name = name;
            text.text = "";
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchoredPosition = Vector2.zero;
            return text;
        }

        private void Update() => Tick(Time.unscaledTime);

        /// <summary>One moment of the title's life at the given time; Update calls it with the real time, tests with their own.</summary>
        public void Tick(float time)
        {
            float step = Mathf.Max(0, time - now);
            now = time;
            bool exploring = flow == null || flow.Phase == VisitPhase.Exploring && !flow.BlocksInput;
            if (!exploring) exploringSince = -1;
            else if (exploringSince < 0) exploringSince = now;
            // The room the visitor stands in, if it was never announced; leaving it before its title shows forgets it.
            string room = currentRoom?.Invoke();
            string wanted = !string.IsNullOrEmpty(room) && !seen.Contains(room) && room != ShownRoomKey && Named(room) ? room : null;
            if (wanted != pending) { pending = wanted; pendingSince = now; }
            bool free = exploring && !Held && (interaction == null || interaction.State != InteractionState.Paused)
                && (wayfinding == null || !wayfinding.MenuOpen) && !ArrivalVisible();
            if (Showing)
            {
                if (!free) { Withdraw(); return; }
                float age = now - shownAt;
                float total = FadeInSeconds + HoldSeconds + FadeOutSeconds;
                group.alpha = age < FadeInSeconds ? Mathf.SmoothStep(0, 1, age / FadeInSeconds)
                    : age < FadeInSeconds + HoldSeconds ? 1 : Mathf.SmoothStep(1, 0, (age - FadeInSeconds - HoldSeconds) / FadeOutSeconds);
                // A record or a narration that starts while the title shows sends it smaller, out of their way.
                if (!Compact && Busy != null && Busy()) Compact = true;
                float scale = Mathf.MoveTowards(card.localScale.x, Compact ? CompactScale : 1, step * (1 - CompactScale) / ShrinkSeconds);
                card.localScale = Vector3.one * scale;
                if (age >= total) Hide();
                return;
            }
            if (pending == null || !free || now - exploringSince < SettleSeconds) return;
            bool busy = Busy != null && Busy();
            if (!busy) Show(pending, false);
            else if (now - pendingSince >= BusyWaitSeconds) Show(pending, true);
        }

        private bool Named(string room)
        {
            var list = rooms?.Invoke();
            if (list == null) return false;
            for (int index = 0; index < list.Count; index++)
                if (list[index].key == room) return !string.IsNullOrWhiteSpace(list[index].name);
            return false;
        }

        private void Show(string room, bool compact)
        {
            var list = rooms();
            int index = 0;
            while (index < list.Count && list[index].key != room) index++;
            seen.Add(room);
            pending = null;
            ShownRoomKey = room;
            Compact = compact;
            shownAt = now;
            nameText.text = list[index].name.Trim();
            positionText.text = MuseumInterfaceText.Format("room_title_position", index + 1, list.Count);
            card.localScale = Vector3.one * (compact ? CompactScale : 1);
            group.alpha = 0;
            card.gameObject.SetActive(true);
            card.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(card);
            if (spatialCanvas != null)
            {
                spatialCanvas.gameObject.SetActive(true);
                Follow(true);
            }
            Announced?.Invoke(room);
        }

        /// <summary>A title cut short before it was read comes back at the next free moment; one nearly read does not.</summary>
        private void Withdraw()
        {
            if (now - shownAt < FadeInSeconds + HoldSeconds * .5f) seen.Remove(ShownRoomKey);
            Hide();
        }

        private void Hide()
        {
            ShownRoomKey = null;
            Compact = false;
            if (group != null) group.alpha = 0;
            if (card != null) card.gameObject.SetActive(false);
            if (spatialCanvas != null) spatialCanvas.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (Showing && spatialCanvas != null) Follow(false);
        }

        /// <summary>Ahead of the eyes along the level gaze and a little above them, catching up gently; closer before a wall.</summary>
        private void Follow(bool snap)
        {
            if (head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();
            float distance = SpatialDistance;
            if (Physics.Raycast(head.position, forward, out var hit, SpatialDistance, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(SpatialNearest, hit.distance - .15f);
            var target = head.position + forward * distance + Vector3.up * SpatialRise;
            var place = spatialCanvas.transform;
            place.position = snap ? target : Vector3.Lerp(place.position, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * SpatialFollow));
            var facing = Vector3.ProjectOnPlane(place.position - head.position, Vector3.up);
            if (facing.sqrMagnitude > .0001f) place.rotation = Quaternion.LookRotation(facing, Vector3.up);
        }

        private bool ArrivalVisible()
        {
            if (arrival == null && interaction != null) arrival = interaction.GetComponent<MuseumArrival>();
            return arrival != null && arrival.Visible;
        }

        private bool DefaultBusy()
        {
            if (interaction != null && interaction.State != InteractionState.Exploration) return true;
            if (narration != null && !string.IsNullOrEmpty(narration.Subtitle)) return true;
            if (guideVoice == null && interaction != null) guideVoice = interaction.GetComponent<MuseumGuideVoice>();
            if (guideVoice != null && guideVoice.Speaking) return true;
            if (companion == null && now >= nextCompanionSearch)
            {
                companion = FindAnyObjectByType<MuseumCompanion>();
                nextCompanionSearch = now + 2;
            }
            return companion != null && companion.Speaking;
        }

        private void OnDestroy()
        {
            if (spatialCanvas != null) Destroy(spatialCanvas.gameObject);
        }
    }
}
