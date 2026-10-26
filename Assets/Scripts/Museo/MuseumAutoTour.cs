using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace MusiyoBetsknate.Museum
{
    public enum AutoTourPhase { Off, Walking, AtStop, Paused }

    /// <summary>
    /// Optional, slow walk through the published points in the order of the tour, for visitors who prefer to watch rather
    /// than steer. It walks the museum's navigation mesh with the visitor's own character controller, so walls, the
    /// lagoon's rim and pedestals block it as they block walking. It keeps to the paths and floors: from the maize, the
    /// lagoon's shore or anywhere off a path it first goes back to the nearest path, and never cuts across the maize. It never turns the visitor or the head: the gaze stays
    /// free. At each point it waits while the narration plays. Any walking input stops it and hands control back at once;
    /// menus, pauses, screen panels and a held piece only hold it where it is.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumAutoTour : MonoBehaviour
    {
        /// <summary>Longest wait at a point whose narration never finishes loading.</summary>
        private const float LoadingWaitSeconds = 30;
        private const float CornerReach = .2f;
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumWayfinding wayfinding;
        private MuseumNarration narration;
        private MuseumModelGrab grab;
        private CharacterController body;
        private Transform viewer;
        private readonly List<TourPoint> stops = new List<TourPoint>();
        private Vector3[] corners = Array.Empty<Vector3>();
        private int stopIndex;
        private int corner;
        private int failures;
        private float speed;
        private float waited;
        private float stuck;
        private Vector3 progressMark;
        private AutoTourPhase resumePhase;
        /// <summary>A closing message stays this long and then clears.</summary>
        private const float ClosingMessageSeconds = 8;
        private float statusUntil = float.PositiveInfinity;
        public AutoTourPhase Phase { get; private set; }
        public bool Active => Phase != AutoTourPhase.Off;
        /// <summary>True while the tour carries the visitor; other systems should not move or block the visitor meanwhile.</summary>
        public bool Moving => Phase == AutoTourPhase.Walking && !Holding;
        public TourPoint CurrentStop => Active && stopIndex < stops.Count ? stops[stopIndex] : null;
        public IReadOnlyList<TourPoint> Stops => stops;
        /// <summary>Visitor-facing line about the tour, or empty when there is nothing to say.</summary>
        public string Status { get; private set; } = "";
        public event Action Changed;

        public void Configure(CharacterController visitorBody, Transform viewPoint)
        {
            body = visitorBody;
            viewer = viewPoint;
        }

        private void Awake() => interaction = GetComponent<MuseumInteraction>();

        private void Watch()
        {
            if (flow == null) flow = GetComponent<MuseumVisitFlow>();
            if (wayfinding == null) wayfinding = GetComponent<MuseumWayfinding>();
            if (narration == null) narration = GetComponent<MuseumNarration>();
            if (grab == null) grab = GetComponent<MuseumModelGrab>();
        }

        /// <summary>Menus, pauses, screen panels, a held piece or typing hold the tour where it is without ending it.</summary>
        public bool Holding
        {
            get
            {
                Watch();
                var input = MuseumInput.Current;
                return flow != null && flow.BlocksInput || interaction.BlocksMovement || interaction.HoldsVisitor
                    || wayfinding?.MenuOpen == true || grab?.Held == true || input.Context != MuseumInputContext.Visit;
            }
        }

        /// <summary>Published points the tour visits, in the tour's order: those with content that open by proximity.</summary>
        public static List<TourPoint> PlanStops(IReadOnlyList<TourPoint> ordered)
        {
            var result = new List<TourPoint>();
            foreach (var point in ordered)
                if (point != null && point.HasContent && !point.IsGuide && point.Supports("proximity")) result.Add(point);
            return result;
        }

        /// <summary>First stop at or after the room the visitor is in, so the tour continues from here instead of the start.</summary>
        public static int FirstStop(IReadOnlyList<TourPoint> stops, IReadOnlyList<string> roomOrder, string currentRoom)
        {
            int room = currentRoom == null ? -1 : IndexOf(roomOrder, currentRoom);
            if (room < 0) return 0;
            for (int index = 0; index < stops.Count; index++)
            {
                var owner = stops[index].GetComponentInParent<RoomAnchor>();
                if (owner != null && IndexOf(roomOrder, owner.Key) >= room) return index;
            }
            return 0;
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            for (int index = 0; index < values.Count; index++) if (values[index] == value) return index;
            return -1;
        }

        public bool Begin()
        {
            Watch();
            if (body == null || wayfinding == null) return false;
            if (interaction.Runtime.Contract == null)
            {
                SetStatus(MuseumInterfaceText.Get("auto_tour_unavailable"));
                statusUntil = Time.realtimeSinceStartup + ClosingMessageSeconds;
                return false;
            }
            stops.Clear();
            stops.AddRange(PlanStops(interaction.Runtime.OrderedPoints));
            if (stops.Count == 0) { Stop("auto_tour_finished"); return false; }
            var roomOrder = new List<string>();
            foreach (var room in interaction.Runtime.Contract.rooms) roomOrder.Add(room.key);
            var here = interaction.Runtime.RoomAt(Feet());
            stopIndex = FirstStop(stops, roomOrder, here?.key);
            failures = 0;
            speed = 0;
            Pulse();
            if (!PlanTo(stopIndex)) return false;
            return true;
        }

        public void Pause()
        {
            if (Phase != AutoTourPhase.Walking && Phase != AutoTourPhase.AtStop) return;
            resumePhase = Phase;
            Phase = AutoTourPhase.Paused;
            speed = 0;
            Pulse();
            SetStatus(MuseumInterfaceText.Get("auto_tour_paused"));
        }

        public void Resume()
        {
            if (Phase != AutoTourPhase.Paused) return;
            Phase = resumePhase;
            ResetProgress();
            Pulse();
            SetStatus(StatusLine());
        }

        /// <summary>Ends the tour; the visitor has full control again at once.</summary>
        public void Stop(string statusKey = null)
        {
            bool wasActive = Active;
            Phase = AutoTourPhase.Off;
            speed = 0;
            corners = Array.Empty<Vector3>();
            if (wasActive) Pulse();
            SetStatus(statusKey != null ? MuseumInterfaceText.Get(statusKey) : "");
            statusUntil = Time.realtimeSinceStartup + ClosingMessageSeconds;
        }

        /// <summary>Starts, pauses or resumes, as the shared action does.</summary>
        public void Toggle()
        {
            if (Phase == AutoTourPhase.Off) Begin();
            else if (Phase == AutoTourPhase.Paused) Resume();
            else Pause();
        }

        private void Update()
        {
            var input = MuseumInput.Current;
            Watch();
            if (Phase == AutoTourPhase.Off && Status.Length > 0 && Time.realtimeSinceStartup > statusUntil) SetStatus("");
            if (flow != null && flow.InputConsumedThisFrame) return;
            if (input.Capturing || input.CaptureEndedThisFrame) return;
            // Steering by hand takes over immediately, whatever the tour was doing.
            if (Active && input.Move.enabled && input.Move.ReadValue<Vector2>().sqrMagnitude > .04f) { Stop(); return; }
            if (input.AutoTour.WasPressedThisFrame() && (flow == null || !flow.BlocksInput)) Toggle();
            Step(Time.deltaTime);
        }

        /// <summary>Advances the tour by one time step; tests call it directly.</summary>
        public void Step(float elapsed)
        {
            if (Phase == AutoTourPhase.Off || Phase == AutoTourPhase.Paused || body == null) return;
            if (!MuseumExperienceConfiguration.Finite(elapsed) || elapsed <= 0) return;
            if (Holding) { speed = 0; ResetProgress(); return; }
            if (Phase == AutoTourPhase.Walking) Walk(elapsed);
            else WaitAtStop(elapsed);
        }

        private Vector3 Feet() => body.transform.TransformPoint(body.center) - Vector3.up * (body.height * .5f);

        private bool PlanTo(int index)
        {
            Watch();
            while (index < stops.Count)
            {
                if (TryRoute(stops[index], out corners))
                {
                    stopIndex = index;
                    corner = 0;
                    Phase = AutoTourPhase.Walking;
                    ResetProgress();
                    SetStatus(StatusLine());
                    return true;
                }
                // A point without a walkable approach is skipped; two in a row end the tour honestly.
                if (++failures >= 2) break;
                index++;
            }
            Stop(failures == 0 ? "auto_tour_finished" : "auto_tour_blocked");
            return false;
        }

        /// <summary>
        /// Shortest walkable route along the paths to a spot inside the point's radius: in front of its reading stand, or
        /// around it. Off-path ground counts several times its length (see <see cref="MuseumWayfinding.TryPathAlongPaths"/>).
        /// </summary>
        private bool TryRoute(TourPoint stop, out Vector3[] route)
        {
            route = Array.Empty<Vector3>();
            var anchor = stop.Anchor;
            var settings = MuseumExperienceConfiguration.Current;
            var spots = new List<Vector3>();
            var display = GetComponent<MuseumLecternDisplay>();
            var stand = display != null ? display.Find(stop) : null;
            if (stand != null) spots.Add(stand.ViewingSpot(Mathf.Min(settings.ViewingDistance(MuseumInput.Current.Spatial), anchor.ActivationRadius * .7f)));
            float ring = Mathf.Min(anchor.ActivationRadius * .6f, 1.4f);
            for (int step = 0; step < 8; step++)
                spots.Add(anchor.transform.position + Quaternion.Euler(0, step * 45, 0) * -anchor.transform.forward * ring);
            float best = float.PositiveInfinity;
            var start = Feet();
            foreach (var spot in spots)
            {
                if (!wayfinding.TryPathAlongPaths(start, spot, out var candidate)) continue;
                // The end must lie inside the point's radius, where the visitor's arrival opens it.
                if (anchor.DistanceTo(candidate[candidate.Length - 1]) > anchor.ActivationRadius * .85f) continue;
                float length = 0;
                for (int index = 1; index < candidate.Length; index++) length += Vector3.Distance(candidate[index - 1], candidate[index]);
                if (length < best) { best = length; route = candidate; }
            }
            return route.Length > 0;
        }

        private void Walk(float elapsed)
        {
            var settings = MuseumExperienceConfiguration.Current;
            var feet = Feet();
            while (corner < corners.Length && Planar(corners[corner] - feet).magnitude < CornerReach) corner++;
            if (corner >= corners.Length) { Arrive(); return; }
            var offset = Planar(corners[corner] - feet);
            speed = Mathf.MoveTowards(speed, settings.AutoTourSpeed, settings.AutoTourAcceleration * elapsed);
            var step = offset.normalized * Mathf.Min(speed * elapsed, offset.magnitude);
            body.Move(step);
            if ((Feet() - progressMark).sqrMagnitude > .2f * .2f) ResetProgress();
            else if ((stuck += elapsed) > settings.AutoTourStuckSeconds)
            {
                // Something solid stands in the way: try the next point rather than pushing against it.
                failures++;
                speed = 0;
                PlanTo(stopIndex + 1);
            }
        }

        private void Arrive()
        {
            Phase = AutoTourPhase.AtStop;
            speed = 0;
            waited = 0;
            failures = 0;
            SetStatus(StatusLine());
        }

        private void WaitAtStop(float elapsed)
        {
            waited += elapsed;
            var stop = CurrentStop;
            var settings = MuseumExperienceConfiguration.Current;
            if (waited < settings.AutoTourStopSeconds) return;
            // The narration of the point is heard to the end; a visitor who paused it is still listening or reading.
            bool here = narration != null && stop != null && interaction.ActivePoint == stop;
            if (here && (narration.State == NarrationState.Playing || narration.State == NarrationState.Paused && !narration.PausedByDistance)) return;
            if (here && narration.State == NarrationState.Loading && waited < LoadingWaitSeconds) return;
            if (stopIndex + 1 >= stops.Count) { Stop("auto_tour_finished"); return; }
            PlanTo(stopIndex + 1);
        }

        private void ResetProgress()
        {
            stuck = 0;
            if (body != null) progressMark = Feet();
        }

        private string StatusLine()
        {
            if (Phase == AutoTourPhase.AtStop && CurrentStop?.Content != null)
                return MuseumInterfaceText.Format("auto_tour_waiting", CurrentStop.Content.name);
            return MuseumInterfaceText.Get("auto_tour_moving");
        }

        private void SetStatus(string value)
        {
            Status = value ?? "";
            Changed?.Invoke();
        }

        private static Vector3 Planar(Vector3 value) { value.y = 0; return value; }

        private static void Pulse()
        {
            if (!MuseumInput.Current.Spatial) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.isValid) device.SendHapticImpulse(0, .2f, .04f);
        }

        private void OnDisable() => Stop();
    }
}
