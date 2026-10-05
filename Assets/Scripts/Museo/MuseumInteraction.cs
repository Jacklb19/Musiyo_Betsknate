using System;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum InteractionState { Exploration, PointFocus, ElementSelected, Reading, ModelExamination, GuideQuestion, Paused }
    public enum ActivationSource { Keyboard, Proximity, Gaze, DeepLink }

    [RequireComponent(typeof(TourRuntime))]
    public sealed class MuseumInteraction : MonoBehaviour
    {
        private InteractionState previousState;
        private InteractionState detailReturnState = InteractionState.ElementSelected;
        private bool connected;
        public InteractionState State { get; private set; }
        public TourPoint ActivePoint { get; private set; }
        public ElementSummaryContractV1 SelectedElement { get; private set; }
        public ActivationSource LastSource { get; private set; }
        public bool BlocksMovement => State == InteractionState.Reading || State == InteractionState.ModelExamination
            || State == InteractionState.GuideQuestion || State == InteractionState.Paused;
        public event Action Changed;
        public event Action<ElementSummaryContractV1> ElementChanged;
        public TourRuntime Runtime => GetComponent<TourRuntime>();

        private void OnEnable() => Connect();
        private void OnDisable()
        {
            if (connected) Runtime.Changed -= OnTourChanged;
            connected = false;
        }

        private void Connect()
        {
            if (connected) return;
            Runtime.Changed += OnTourChanged;
            connected = true;
        }
        private void OnTourChanged() => Close();

        public bool Activate(TourPoint point, ActivationSource source)
        {
            Connect();
            if (State == InteractionState.Paused || point == null || !point.HasContent
                || Runtime.Contract == null || Runtime.FindPoint(point.Anchor.Key) != point) return false;
            string method = source == ActivationSource.Proximity ? "proximity" : source == ActivationSource.Gaze ? "gaze" : "keyboard";
            if (source != ActivationSource.DeepLink && !point.Supports(method)) return false;
            if (ActivePoint == point && (State == InteractionState.PointFocus || State == InteractionState.ElementSelected)) return true;
            ResetSelection();
            ActivePoint = point;
            LastSource = source;
            point.SetState(PointState.Active);
            State = InteractionState.PointFocus;
            if (point.Content.elements.Length == 1) SelectElement(0);
            else Changed?.Invoke();
            return true;
        }

        public bool SelectElement(int index)
        {
            if (ActivePoint == null || !ActivePoint.HasContent
                || (State != InteractionState.PointFocus && State != InteractionState.ElementSelected)
                || index < 0 || index >= ActivePoint.Content.elements.Length) return false;
            SelectedElement = ActivePoint.Content.elements[index];
            State = InteractionState.ElementSelected;
            ElementChanged?.Invoke(SelectedElement);
            Changed?.Invoke();
            return true;
        }

        public bool OpenDetail()
        {
            if (SelectedElement == null || (State != InteractionState.ElementSelected && State != InteractionState.ModelExamination)) return false;
            detailReturnState = State;
            State = InteractionState.Reading;
            Changed?.Invoke();
            return true;
        }

        public bool OpenModel()
        {
            if (SelectedElement == null || State != InteractionState.ElementSelected) return false;
            State = InteractionState.ModelExamination;
            Changed?.Invoke();
            return true;
        }

        public void CancelModel()
        {
            if (detailReturnState == InteractionState.ModelExamination) detailReturnState = InteractionState.ElementSelected;
            if (previousState == InteractionState.ModelExamination) previousState = InteractionState.ElementSelected;
            if (State != InteractionState.ModelExamination) return;
            State = InteractionState.ElementSelected;
            Changed?.Invoke();
        }

        public void Back()
        {
            if (State == InteractionState.Paused) { SetPaused(false); return; }
            if (State == InteractionState.Reading)
            { State = detailReturnState; Changed?.Invoke(); return; }
            if (State == InteractionState.ModelExamination)
            { State = InteractionState.ElementSelected; Changed?.Invoke(); return; }
            Close();
        }

        public void SetPaused(bool paused)
        {
            if (paused && State != InteractionState.Paused)
            { previousState = State; State = InteractionState.Paused; Changed?.Invoke(); }
            else if (!paused && State == InteractionState.Paused)
            { State = previousState; Changed?.Invoke(); }
        }

        public void Close()
        {
            ResetSelection();
            State = InteractionState.Exploration;
            Changed?.Invoke();
        }

        private void ResetSelection()
        {
            if (ActivePoint != null) ActivePoint.SetState(PointState.Available);
            ActivePoint = null;
            SelectedElement = null;
            detailReturnState = InteractionState.ElementSelected;
            ElementChanged?.Invoke(null);
        }
    }
}
