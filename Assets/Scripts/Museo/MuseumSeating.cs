using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Marks any object as a seat, for seats added after this was written: its position is the middle of the seat's top,
    /// its forward is where a seated visitor looks and its right runs along a bench of <see cref="Length"/> metres.
    /// </summary>
    public sealed class MuseumSeat : MonoBehaviour
    {
        [SerializeField] private float length;
        public float Length => length;
        public void Configure(float seatLength) => length = Mathf.Max(0, seatLength);
    }

    /// <summary>One place to sit: the top of its seat, the line along it where one can sit, and where a seated visitor looks.</summary>
    public sealed class SeatSpot
    {
        public string Name;
        /// <summary>Middle of the seat's top.</summary>
        public Vector3 Top;
        /// <summary>Half the seat's length along it, in world space; zero for a stool.</summary>
        public Vector3 Along;
        /// <summary>Where a seated visitor looks, flat.</summary>
        public Vector3 Facing;
        /// <summary>The seat is in the hearth, where sitting begins the conversation with its guide.</summary>
        public bool Hearth;

        /// <summary>The point of the seat nearest a spot on the floor.</summary>
        public Vector3 Nearest(Vector3 spot)
        {
            if (Along.sqrMagnitude < 1e-6f) return Top;
            var axis = Along.normalized;
            float reach = Mathf.Max(0, Along.magnitude - .3f);
            float along = Mathf.Clamp(Vector3.Dot(spot - Top, axis), -reach, reach);
            return Top + axis * along;
        }
    }

    /// <summary>
    /// Sitting down anywhere in the museum: on the plank benches, the stools and the hearth's blanketed benches, found when
    /// the visit runs (so seats moved later still work) or marked with <see cref="MuseumSeat"/>. Near a seat a small
    /// «Sentarte» appears; choosing it (pointer, the right stick's press on a headset, a key on a computer) lowers the view
    /// to a seated person's eyes and holds walking. On a headset the view only goes down, never turns; on a computer it
    /// also turns to where the seat looks. Moving the stick or pressing the same control stands the visitor up where they
    /// were. The visitor's view height choice still applies. In the hearth, sitting begins the conversation with its guide.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumSeating : MonoBehaviour
    {
        /// <summary>A seat is offered within this distance of the visitor's feet.</summary>
        public const float Reach = 1.3f;
        /// <summary>Eyes of a seated person above the seat, in metres.</summary>
        public const float SeatedEyes = .72f;
        private const float RescanSeconds = 4;
        public const string HearthRoom = "room.hearth";
        public const string FirePiece = "Fogon_Piedras_Shinyac";
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private DesktopVisitorController desktop;
        private MuseumXrHost xr;
        private CharacterController body;
        private Camera head;
        private List<SeatSpot> seats = new List<SeatSpot>();
        private float rescanAt;
        private Canvas prompt;
        private RectTransform promptSurface;
        private Button sit, stand;
        private TMP_Text hint;
        private bool registered;
        private Vector3 standingSpot;
        private float cameraDrop;
        private bool moveArmed;
        private float seatedAt;

        public IReadOnlyList<SeatSpot> Seats => seats;
        /// <summary>The seat offered near the visitor, or null.</summary>
        public SeatSpot Offered { get; private set; }
        public SeatSpot Current { get; private set; }
        public bool Seated => Current != null;
        /// <summary>Where the visitor sits, on the seat.</summary>
        public Vector3 SeatPoint { get; private set; }
        public bool PromptShown => prompt != null && prompt.gameObject.activeSelf;
        public Button SitButton => sit;
        public Button StandButton => stand;
        public RectTransform PromptSurface => promptSurface;
        /// <summary>What a seated visitor in the hearth looks at besides the fire; the hearth guide sets it.</summary>
        public Func<Vector3?> HearthFocus { get; set; }
        public event Action<SeatSpot> SatDown;
        public event Action<SeatSpot> StoodUp;

        private void Start()
        {
            interaction = GetComponent<MuseumInteraction>();
            flow = GetComponent<MuseumVisitFlow>();
            desktop = FindAnyObjectByType<DesktopVisitorController>(FindObjectsInactive.Include);
            body = desktop != null ? desktop.GetComponent<CharacterController>() : null;
            BuildPrompt();
        }

        /// <summary>Finds every seat in the loaded museum.</summary>
        public static List<SeatSpot> FindSeats()
        {
            var found = new List<SeatSpot>();
            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var fireRenderer = renderers.FirstOrDefault(item => item.name == FirePiece);
            Vector3? fire = fireRenderer != null ? fireRenderer.bounds.center : (Vector3?)null;
            var rooms = FindObjectsByType<RoomAnchor>(FindObjectsSortMode.None);
            var hearth = rooms.FirstOrDefault(room => room.Key == HearthRoom);
            Vector3 Toward(Vector3 from, Vector3 to) { var flat = to - from; flat.y = 0; return flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward; }
            Vector3 RoomFacing(Vector3 spot)
            {
                if (fire.HasValue && hearth != null && hearth.Contains(spot)) return Toward(spot, fire.Value);
                var room = rooms.FirstOrDefault(item => item.Contains(spot));
                return room != null ? Toward(spot, room.transform.position) : Vector3.forward;
            }
            // The plank benches: one box collider each, its forward where the seated look.
            foreach (var bench in FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(item => item.name == "Bench"))
            {
                var t = bench.transform;
                found.Add(new SeatSpot
                {
                    Name = bench.name, Top = t.TransformPoint(bench.center + Vector3.up * bench.size.y * .5f),
                    Along = t.right * bench.size.x * .5f * t.lossyScale.x, Facing = Toward(Vector3.zero, t.forward)
                });
            }
            // The stools, wherever they stand.
            foreach (var stool in renderers.Where(item => item.name.Contains("_Banco_")))
            {
                var bounds = stool.bounds;
                var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                found.Add(new SeatSpot { Name = stool.name, Top = top, Facing = RoomFacing(top) });
            }
            // The hearth's benches under their blankets, facing the fire.
            foreach (var blanket in renderers.Where(item => item.name.StartsWith("Fogon_Manta_")))
            {
                var bounds = blanket.bounds;
                var top = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                var along = bounds.size.x >= bounds.size.z ? Vector3.right * bounds.extents.x : Vector3.forward * bounds.extents.z;
                found.Add(new SeatSpot { Name = blanket.name, Top = top, Along = along, Facing = fire.HasValue ? Toward(top, fire.Value) : Vector3.forward });
            }
            foreach (var marked in FindObjectsByType<MuseumSeat>(FindObjectsSortMode.None))
                found.Add(new SeatSpot
                {
                    Name = marked.name, Top = marked.transform.position, Along = marked.transform.right * marked.Length * .5f,
                    Facing = Toward(Vector3.zero, marked.transform.forward)
                });
            foreach (var seat in found) seat.Hearth = hearth != null && hearth.Contains(seat.Top);
            return found;
        }

        private Camera Head()
        {
            if (head != null && head.isActiveAndEnabled) return head;
            if (desktop != null) head = desktop.GetComponentsInChildren<Camera>().FirstOrDefault(camera => camera.isActiveAndEnabled);
            if (head == null) head = Camera.main;
            if (prompt != null) prompt.worldCamera = head;
            return head;
        }

        private Vector3 Feet()
        {
            var eyes = Head() != null ? head.transform.position : desktop.transform.position;
            return new Vector3(eyes.x, desktop.transform.position.y, eyes.z);
        }

        private bool CanSit => flow != null && flow.Phase == VisitPhase.Exploring && !flow.BlocksInput && !interaction.BlocksMovement
            && !interaction.HoldsVisitor && interaction.State != InteractionState.Paused && interaction.State != InteractionState.ModelExamination
            && interaction.GetComponent<MuseumModelGrab>()?.Held != true;

        private void Update()
        {
            if (desktop == null) return;
            if (xr == null) xr = FindAnyObjectByType<MuseumXrHost>();
            if (!Seated && Time.unscaledTime >= rescanAt)
            {
                rescanAt = Time.unscaledTime + RescanSeconds;
                seats = FindSeats();
            }
            var input = MuseumInput.Current;
            if (Seated)
            {
                // Standing up: the control pressed again, or the stick moved once it was let go after sitting down.
                var move = input.Move.enabled ? input.Move.ReadValue<Vector2>() : Vector2.zero;
                if (move.sqrMagnitude < .04f) moveArmed = true;
                bool leaving = input.Sit.WasPressedThisFrame() && Time.unscaledTime - seatedAt > .25f
                    || moveArmed && move.sqrMagnitude > .36f || flow != null && flow.Phase != VisitPhase.Exploring;
                if (leaving) Stand();
                Refresh();
                return;
            }
            var feet = Feet();
            Offered = CanSit ? seats.Where(seat => Mathf.Abs(seat.Top.y - feet.y) is > .2f and < .8f)
                .Select(seat => (seat, distance: Flat(seat.Nearest(feet) - feet).magnitude))
                .Where(item => item.distance <= Reach).OrderBy(item => item.distance).Select(item => item.seat).FirstOrDefault() : null;
            if (Offered != null && input.Sit.WasPressedThisFrame()) Sit(Offered);
            Refresh();
        }

        /// <summary>Sits the visitor on a seat: there, lower, still.</summary>
        public bool Sit(SeatSpot seat)
        {
            if (seat == null || Seated || desktop == null || !CanSit) return false;
            var feet = Feet();
            standingSpot = feet;
            SeatPoint = seat.Nearest(feet);
            float floor = desktop.transform.position.y;
            var target = new Vector3(SeatPoint.x, floor, SeatPoint.z);
            if (body != null) body.enabled = false;
            desktop.transform.position += target - feet;
            float eyes = Head() != null ? head.transform.position.y - floor : 1.6f;
            float drop = Mathf.Max(0, eyes - (SeatPoint.y - floor + SeatedEyes));
            if (xr != null && xr.Ready)
                // On a headset the view only goes down; a visitor already sitting in the room keeps their own height.
                xr.SeatDrop = drop;
            else
            {
                cameraDrop = drop;
                if (head != null) head.transform.localPosition -= Vector3.up * drop;
                var look = seat.Facing;
                var focus = seat.Hearth ? HearthFocus?.Invoke() : null;
                // In the hearth, between the fire the seat faces and its guide.
                if (focus.HasValue) look = (look + Flat(focus.Value - SeatPoint).normalized).normalized;
                if (look.sqrMagnitude > .01f) desktop.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            desktop.Seated = true;
            Current = seat;
            moveArmed = false;
            seatedAt = Time.unscaledTime;
            Physics.SyncTransforms();
            SatDown?.Invoke(seat);
            Refresh();
            return true;
        }

        /// <summary>Stands the visitor up where they stood before sitting down.</summary>
        public void Stand()
        {
            if (!Seated) return;
            var seat = Current;
            var feet = Feet();
            desktop.transform.position += new Vector3(standingSpot.x, desktop.transform.position.y, standingSpot.z) - feet;
            if (xr != null) xr.SeatDrop = 0;
            if (cameraDrop > 0 && head != null) head.transform.localPosition += Vector3.up * cameraDrop;
            cameraDrop = 0;
            if (body != null) body.enabled = true;
            desktop.Seated = false;
            Current = null;
            Physics.SyncTransforms();
            StoodUp?.Invoke(seat);
            Refresh();
        }

        private static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);

        private void BuildPrompt()
        {
            prompt = MuseumWorldUi.Canvas("SeatPrompt", transform, new Vector2(420, 210), Head());
            var panel = MuseumWorldUi.Surface("Panel", prompt.transform, MuseumWorldUi.Panel).rectTransform;
            promptSurface = panel;
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = new Vector2(420, 0);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Button Make(string name, string key, UnityEngine.Events.UnityAction action)
            {
                var made = MuseumWorldUi.Choice(name, panel, MuseumInterfaceText.Get(key), 26, action, true);
                made.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
                return made;
            }
            sit = Make("Sit", "seat_sit", () => Sit(Offered));
            stand = Make("Stand", "seat_stand", Stand);
            hint = MuseumWorldUi.Label("Hint", panel, "", 18, MuseumWorldUi.Muted);
            hint.alignment = TextAlignmentOptions.Center;
            prompt.gameObject.SetActive(false);
        }

        /// <summary>The prompt floats over the offered seat; once seated, the way to stand stays for a few seconds.</summary>
        private void Refresh()
        {
            if (prompt == null) return;
            if (!registered && interaction.TryGetComponent<MuseumLecternDisplay>(out var display))
            {
                display.AddCue(promptSurface);
                registered = true;
            }
            bool seatedHint = Seated && Time.unscaledTime - seatedAt < 6;
            bool shown = Offered != null && !Seated || seatedHint;
            prompt.gameObject.SetActive(shown);
            if (!shown) return;
            sit.gameObject.SetActive(!Seated);
            stand.gameObject.SetActive(Seated);
            string text = MuseumInterfaceText.Get(Seated ? "seat_stand_hint" : "seat_sit_hint");
            if (hint.text != text) hint.text = text;
            var eyes = Head() != null ? head.transform.position : Feet() + Vector3.up * 1.6f;
            Vector3 spot;
            if (Seated)
            {
                var forward = Flat(head != null ? head.transform.forward : Vector3.forward).normalized;
                spot = eyes + forward * .9f + Vector3.down * .25f;
            }
            else spot = Offered.Nearest(Feet()) + Vector3.up * .55f;
            prompt.transform.position = spot;
            prompt.transform.rotation = MuseumWorldUi.Facing(spot, eyes);
        }

        private void OnDisable()
        {
            if (Seated) Stand();
        }
    }
}
