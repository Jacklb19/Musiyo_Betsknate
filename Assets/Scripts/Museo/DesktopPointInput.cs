using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MusiyoBetsknate.Museum
{
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class DesktopPointInput : MonoBehaviour
    {
        [SerializeField] private Transform visitor;
        [SerializeField] private Camera viewCamera;
        private MuseumInteraction interaction;
        private TourPoint keyboardPoint;
        private readonly DwellActivation proximityDwell = new DwellActivation();
        private readonly DwellActivation gazeDwell = new DwellActivation();
        public TourPoint FocusedPoint => keyboardPoint;
        public float GazeProgress => gazeDwell.Progress;

        public void Configure(Transform visitorTransform, Camera camera)
        { visitor = visitorTransform; viewCamera = camera; }

        private void Awake() => interaction = GetComponent<MuseumInteraction>();
        private void OnEnable()
        {
            interaction = GetComponent<MuseumInteraction>();
            GetComponent<TourRuntime>().Changed += ResetFocus;
            interaction.Changed += SuppressActivePoint;
        }
        private void OnDisable()
        {
            GetComponent<TourRuntime>().Changed -= ResetFocus;
            interaction.Changed -= SuppressActivePoint;
        }

        private void SuppressActivePoint()
        {
            if (interaction.ActivePoint == null) return;
            var key = interaction.ActivePoint.Anchor.Key;
            proximityDwell.SuppressUntilExit(key);
            gazeDwell.SuppressUntilExit(key);
        }

        private void ResetFocus()
        {
            keyboardPoint = null;
            proximityDwell.Step(null, false, 0, 1);
            gazeDwell.Step(null, false, 0, 1);
        }

        public void Cycle(int direction)
        {
            if (interaction == null) interaction = GetComponent<MuseumInteraction>();
            var candidates = KeyboardCandidates();
            if (candidates.Count == 0) { ResetFocus(); return; }
            int index = candidates.IndexOf(keyboardPoint);
            if (keyboardPoint != null && keyboardPoint != interaction.ActivePoint)
                keyboardPoint.SetState(PointState.Available);
            index = index < 0 ? direction < 0 ? candidates.Count - 1 : 0
                : (index + (direction < 0 ? -1 : 1) + candidates.Count) % candidates.Count;
            keyboardPoint = candidates[index];
            if (keyboardPoint != interaction.ActivePoint) keyboardPoint.SetState(PointState.Focused);
        }

        private List<TourPoint> KeyboardCandidates()
        {
            var result = new List<TourPoint>();
            RoomAnchor currentRoom = null;
            float nearest = float.PositiveInfinity;
            if (visitor != null)
                foreach (var room in GetComponent<TourRuntime>().GetComponentsInChildren<RoomAnchor>())
                {
                    if (room.Contains(visitor.position)) { currentRoom = room; break; }
                    var distance = (room.transform.position - visitor.position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; currentRoom = room; }
                }
            foreach (var point in GetComponent<TourRuntime>().OrderedPoints)
                if (point.Supports("keyboard") && (currentRoom == null || point.GetComponentInParent<RoomAnchor>() == currentRoom))
                    result.Add(point);
            return result;
        }

        public void Confirm()
        {
            if (interaction.State == InteractionState.Exploration)
                interaction.Activate(keyboardPoint, ActivationSource.Keyboard);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    interaction.SetPaused(interaction.State != InteractionState.Paused);
                if (keyboard.backspaceKey.wasPressedThisFrame) interaction.Back();
                if (interaction.State == InteractionState.Paused) return;
                if (interaction.State == InteractionState.Exploration && keyboard.tabKey.wasPressedThisFrame)
                    Cycle(keyboard.shiftKey.isPressed ? -1 : 1);
                if (keyboard.enterKey.wasPressedThisFrame) Confirm();
                if (keyboard.fKey.wasPressedThisFrame) interaction.OpenDetail();
                if (keyboard.xKey.wasPressedThisFrame) GetComponent<MuseumModelPresenter>()?.Examine();
                if (keyboard.digit1Key.wasPressedThisFrame) interaction.SelectElement(0);
                if (keyboard.digit2Key.wasPressedThisFrame) interaction.SelectElement(1);
                if (keyboard.digit3Key.wasPressedThisFrame) interaction.SelectElement(2);
            }
#endif
            if (visitor == null || viewCamera == null || interaction.Runtime.Contract == null) return;
            if (interaction.State != InteractionState.Exploration) return;
            TourPoint nearestPoint = null;
            TourPoint gazePoint = null;
            float nearestDistance = float.PositiveInfinity;
            float gazeAngle = float.PositiveInfinity;
            foreach (var point in interaction.Runtime.OrderedPoints)
            {
                if (!point.HasContent) continue;
                var distance = (point.Anchor.transform.position - visitor.position).sqrMagnitude;
                if (point.Supports("proximity") && PointActivation.IsInRange(point, visitor.position) && distance < nearestDistance)
                { nearestPoint = point; nearestDistance = distance; }
                var angle = Vector3.Angle(viewCamera.transform.forward, point.Anchor.LookTarget.position - viewCamera.transform.position);
                if (point.Supports("gaze") && angle < gazeAngle && PointActivation.IsVisible(point, viewCamera.transform, 4, 6))
                { gazePoint = point; gazeAngle = angle; }
            }
#if ENABLE_INPUT_SYSTEM
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && nearestPoint != null)
            { interaction.Activate(nearestPoint, ActivationSource.Proximity); return; }
#endif
            bool facing = nearestPoint != null && PointActivation.IsVisible(nearestPoint, viewCamera.transform, 4, 60);
            if (proximityDwell.Step(nearestPoint != null ? nearestPoint.Anchor.Key : null, facing, Time.unscaledDeltaTime, .8f))
            { interaction.Activate(nearestPoint, ActivationSource.Proximity); return; }
            if (gazeDwell.Step(gazePoint != null ? gazePoint.Anchor.Key : null, gazePoint != null, Time.unscaledDeltaTime, 1.2f))
                interaction.Activate(gazePoint, ActivationSource.Gaze);
        }
    }
}
