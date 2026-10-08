using System;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum VisitPhase { Menu, Entering, Exploring }

    /// <summary>Presentation flow independent of the shared tour interaction contract.</summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumVisitFlow : MonoBehaviour
    {
        public VisitPhase Phase { get; private set; } = VisitPhase.Menu;
        public bool HasStarted { get; private set; }
        public bool BlocksInput => Phase != VisitPhase.Exploring;
        public float TransitionProgress { get; private set; }
        private int consumedFrame = -1;
        /// <summary>True on the frame a menu closed, so its Enter or Escape cannot also reach the museum.</summary>
        public bool InputConsumedThisFrame => consumedFrame == Time.frameCount;
        public void ConsumeInputThisFrame() => consumedFrame = Time.frameCount;
        /// <summary>Menu presenter installed at runtime by the HUD; it may consume Back before the visit does.</summary>
        public MuseumMenuHud Menu { get; set; }
        public event Action Changed;
        private void Update() => Advance(Time.unscaledDeltaTime);

        public void BeginVisit()
        {
            if (Phase != VisitPhase.Menu) return;
            Phase = VisitPhase.Entering;
            TransitionProgress = 0;
            Changed?.Invoke();
        }
        public void Advance(float elapsed)
        {
            if (Phase != VisitPhase.Entering || !MuseumExperienceConfiguration.Finite(elapsed) || elapsed < 0) return;
            TransitionProgress = Mathf.Clamp01(TransitionProgress + elapsed / MuseumExperienceConfiguration.Current.EntryFadeSeconds);
            if (TransitionProgress < 1) return;
            EnterImmediately();
        }
        public void EnterImmediately()
        {
            Phase = VisitPhase.Exploring;
            HasStarted = true;
            TransitionProgress = 1;
            GetComponent<MuseumInteraction>().SetPaused(false);
            Changed?.Invoke();
        }
        public void ReturnToMenu()
        {
            if (Phase != VisitPhase.Exploring) return;
            GetComponent<MuseumWayfinding>()?.CloseMenu();
            GetComponent<MuseumInteraction>().SetPaused(true);
            Phase = VisitPhase.Menu;
            Changed?.Invoke();
        }
    }
}
