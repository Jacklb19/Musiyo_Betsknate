using System;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Menu, the way in, the visit, the question at the end of the route (<see cref="Asking"/>) and the closing credits by
    /// the hearth (<see cref="Ending"/>), after which the title menu comes back.
    /// </summary>
    public enum VisitPhase { Menu, Entering, Exploring, Asking, Ending }
    public enum EntryTransition { Fade, Glide }

    /// <summary>Presentation flow independent of the shared tour interaction contract.</summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumVisitFlow : MonoBehaviour
    {
        public VisitPhase Phase { get; private set; } = VisitPhase.Menu;
        public bool HasStarted { get; private set; }
        public bool BlocksInput => Phase != VisitPhase.Exploring;
        public float TransitionProgress { get; private set; }
        /// <summary>How the current entry is shown: a fade, or a camera glide when the entry view can offer one.</summary>
        public EntryTransition Transition { get; private set; }
        public float TransitionSeconds => Transition == EntryTransition.Glide
            // From a scenic backdrop the menu first goes dark, then the glide starts at the museum.
            ? MuseumExperienceConfiguration.Current.EntryGlideSeconds + (Entry != null && Entry.BackdropActive
                ? MuseumExperienceConfiguration.Current.EntryFadeSeconds : 0)
            : MuseumExperienceConfiguration.Current.EntryFadeSeconds;
        /// <summary>Entry staging installed at runtime by the HUD; without it every entry is a fade.</summary>
        public MuseumEntryView Entry { get; set; }
        private int consumedFrame = -1;
        /// <summary>True on the frame a menu closed, so its Enter or Escape cannot also reach the museum.</summary>
        public bool InputConsumedThisFrame => consumedFrame == Time.frameCount;
        public void ConsumeInputThisFrame() => consumedFrame = Time.frameCount;
        /// <summary>Menu presenter installed at runtime by the HUD; it may consume Back before the visit does.</summary>
        public MuseumMenuHud Menu { get; set; }
        /// <summary>True on the menu that follows the closing credits, so it can appear gently over the fire.</summary>
        public bool AfterEnding { get; private set; }
        public event Action Changed;
        private void Update() => Advance(Time.unscaledDeltaTime);

        public void BeginVisit()
        {
            if (Phase != VisitPhase.Menu) return;
            Transition = Entry != null && Entry.CanGlide ? EntryTransition.Glide : EntryTransition.Fade;
            Phase = VisitPhase.Entering;
            TransitionProgress = 0;
            AfterEnding = false;
            Changed?.Invoke();
        }
        public void Advance(float elapsed)
        {
            if (Phase != VisitPhase.Entering || !MuseumExperienceConfiguration.Finite(elapsed) || elapsed < 0) return;
            TransitionProgress = Mathf.Clamp01(TransitionProgress + elapsed / TransitionSeconds);
            if (TransitionProgress < 1) return;
            EnterImmediately();
        }
        public void EnterImmediately()
        {
            Phase = VisitPhase.Exploring;
            HasStarted = true;
            TransitionProgress = 1;
            AfterEnding = false;
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

        /// <summary>The visit stops while the visitor is asked whether to end it.</summary>
        public bool Ask()
        {
            if (Phase != VisitPhase.Exploring) return false;
            GetComponent<MuseumWayfinding>()?.CloseMenu();
            Phase = VisitPhase.Asking;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The visitor chose to go on: the visit resumes where it was.</summary>
        public void KeepExploring()
        {
            if (Phase != VisitPhase.Asking) return;
            Phase = VisitPhase.Exploring;
            ConsumeInputThisFrame();
            Changed?.Invoke();
        }

        /// <summary>The visitor chose to end the visit: the museum goes dark and the credits follow.</summary>
        public bool BeginEnding()
        {
            if (Phase != VisitPhase.Asking && Phase != VisitPhase.Exploring) return false;
            GetComponent<MuseumWayfinding>()?.CloseMenu();
            Phase = VisitPhase.Ending;
            ConsumeInputThisFrame();
            Changed?.Invoke();
            return true;
        }

        /// <summary>After the credits the title menu returns over the fire, and a new visit starts from the beginning.</summary>
        public void FinishEnding()
        {
            if (Phase != VisitPhase.Ending) return;
            // The phase changes first, so nothing on the way reads the closing point or the pause as a pause menu.
            Phase = VisitPhase.Menu;
            HasStarted = false;
            AfterEnding = true;
            var interaction = GetComponent<MuseumInteraction>();
            interaction.Close();
            interaction.SetPaused(true);
            ConsumeInputThisFrame();
            Changed?.Invoke();
        }
    }
}
