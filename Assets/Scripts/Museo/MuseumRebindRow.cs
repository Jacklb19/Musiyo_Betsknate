using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Samples.RebindUI;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// One line of the controls page. The capture itself is the Input System's Rebinding UI sample;
    /// this row adds the museum's texts, the keyboard-only rule and the conflict check.
    /// </summary>
    [RequireComponent(typeof(Button), typeof(RebindActionUI))]
    public sealed class MuseumRebindRow : MonoBehaviour
    {
        public TMP_Text Action;
        public TMP_Text Key;
        [Tooltip("Shown while this row waits for a key and hidden otherwise.")]
        public GameObject Listening;
        private MuseumInput input;
        private MuseumInputBinding binding;
        private RebindActionUI capture;
        private InputActionReference reference;
        private Action<string> report;
        private string previousPath;
        private bool waiting;
        public MuseumInputBinding Binding => binding;
        public bool Capturing => capture != null && capture.ongoingRebind != null;

        public void ValidateReferences()
        {
            if (Action == null || Key == null || Listening == null || !Action.transform.IsChildOf(transform)
                || !Key.transform.IsChildOf(transform) || !Listening.transform.IsChildOf(transform) || Listening == gameObject)
                throw new InvalidOperationException(name + ": a rebinding row needs its action label, key label and listening mark.");
        }

        /// <summary>Connects the row to one key of the shared actions. Messages for the visitor go to the page through report.</summary>
        public void Configure(MuseumInput owner, MuseumInputBinding target, Action<string> status)
        {
            Release();
            input = owner; binding = target; report = status;
            capture = GetComponent<RebindActionUI>();
            reference = InputActionReference.Create(target.Action);
            capture.actionReference = reference;
            capture.bindingId = target.Action.bindings[target.Index].id.ToString();
            // The sample shows this object for the duration of a capture and hides it when the capture ends.
            capture.rebindOverlay = Listening;
            Listening.SetActive(false);
            capture.startRebindEvent.AddListener(OnCaptureStarted);
            capture.stopRebindEvent.AddListener(OnCaptureStopped);
            GetComponent<Button>().onClick.AddListener(Begin);
            Refresh();
        }

        public void Refresh()
        {
            if (binding == null) return;
            Action.text = MuseumInterfaceText.Get(binding.LabelKey);
            Key.text = MuseumInputLabels.Path(binding.Path);
        }

        public void Begin()
        {
            if (binding == null || input.Capturing) return;
            previousPath = binding.Path;
            // Every map goes silent first, so the key being chosen neither walks nor moves the focus.
            input.BeginCapture();
            waiting = true;
            // If the capture cannot start, the museum must not be left deaf.
            try { capture.StartInteractiveRebind(); }
            finally { if (!Capturing) Cancel(); }
        }

        /// <summary>Ends a capture without a new key, also when the sample has already dropped its operation.</summary>
        public void Cancel()
        {
            if (!waiting) return;
            if (Capturing) { capture.ongoingRebind.Cancel(); return; }
            waiting = false;
            if (Listening != null) Listening.SetActive(false);
            input.EndCapture();
            Refresh();
        }

        private void OnCaptureStarted(RebindActionUI source, InputActionRebindingExtensions.RebindingOperation operation)
        {
            operation.WithControlsHavingToMatchPath("<Keyboard>")
                .WithCancelingThrough(MuseumInputBindings.CancelPath)
                .WithTimeout(MuseumExperienceConfiguration.Current.RebindTimeoutSeconds);
            Key.text = MuseumInterfaceText.Get("rebind_waiting");
            report?.Invoke(MuseumInterfaceText.Format("rebind_prompt", MuseumInterfaceText.Get(binding.LabelKey),
                MuseumInputLabels.Path(MuseumInputBindings.CancelPath)));
        }

        private void OnCaptureStopped(RebindActionUI source, InputActionRebindingExtensions.RebindingOperation operation)
        {
            waiting = false;
            string chosen = operation.completed ? binding.Path : null;
            // The sample has already written the key. Put the previous one back and let the owner validate the change.
            if (string.Equals(previousPath, binding.DefaultPath, StringComparison.OrdinalIgnoreCase))
                binding.Action.RemoveBindingOverride(binding.Index);
            else binding.Action.ApplyBindingOverride(binding.Index, previousPath);
            input.EndCapture();
            string action = MuseumInterfaceText.Get(binding.LabelKey);
            if (chosen == null) report?.Invoke(MuseumInterfaceText.Get("rebind_cancelled"));
            else
                switch (input.Bindings.TryRebind(binding, chosen, out var conflict))
                {
                    case MuseumRebindResult.Applied:
                        report?.Invoke(MuseumInterfaceText.Format("rebind_done", action, MuseumInputLabels.Path(chosen)));
                        break;
                    case MuseumRebindResult.Conflict:
                        report?.Invoke(MuseumInterfaceText.Format("rebind_conflict", MuseumInputLabels.Path(chosen), MuseumInterfaceText.Get(conflict)));
                        break;
                    default:
                        report?.Invoke(MuseumInterfaceText.Get("rebind_unsupported"));
                        break;
                }
            Refresh();
        }

        private void Release()
        {
            if (capture != null)
            {
                Cancel();
                capture.startRebindEvent.RemoveListener(OnCaptureStarted);
                capture.stopRebindEvent.RemoveListener(OnCaptureStopped);
                capture.actionReference = null;
                GetComponent<Button>().onClick.RemoveListener(Begin);
            }
            if (reference != null)
            {
                if (Application.isPlaying) Destroy(reference);
                else DestroyImmediate(reference);
            }
            reference = null; binding = null;
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Release();
    }
}
