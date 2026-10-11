using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Derives the input context from the state of the visit, in one place, for every component that reads input.</summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumInputDirector : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private MuseumVisitFlow flow;
        private MuseumWayfinding wayfinding;

        /// <summary>Returns the director of a visit, adding it on first use, with the context already applied.</summary>
        public static MuseumInputDirector Ensure(MuseumInteraction state)
        {
            if (!state.TryGetComponent<MuseumInputDirector>(out var director))
                director = state.gameObject.AddComponent<MuseumInputDirector>();
            director.Apply();
            return director;
        }

        public MuseumInputContext Desired
        {
            get
            {
                Watch();
                if (flow != null && flow.BlocksInput || interaction.State == InteractionState.Paused) return MuseumInputContext.Menu;
                if (wayfinding != null && wayfinding.MenuOpen) return MuseumInputContext.Panel;
                if (interaction.State == InteractionState.ModelExamination) return MuseumInputContext.Examine;
                return interaction.BlocksMovement || interaction.HoldsVisitor ? MuseumInputContext.Panel : MuseumInputContext.Visit;
            }
        }

        /// <summary>Applies the context now. State changes call it at once; each frame repeats it as a safeguard.</summary>
        public void Apply()
        {
            var input = MuseumInput.Current;
            var events = EventSystem.current;
            if (events != null && events.TryGetComponent<InputSystemUIInputModule>(out var module)) input.BindUi(module);
            input.SetContext(Desired);
        }

        /// <summary>The flow and the orientation menu are added while the visit is assembled, so they are picked up when they appear.</summary>
        private void Watch()
        {
            if (interaction == null)
            {
                interaction = GetComponent<MuseumInteraction>();
                interaction.Changed += Apply;
            }
            if (flow == null && TryGetComponent(out flow)) flow.Changed += Apply;
            if (wayfinding == null && TryGetComponent(out wayfinding)) wayfinding.Changed += Apply;
        }

        private void OnEnable() => Apply();
        private void Update() => Apply();
        private void OnDestroy()
        {
            if (interaction != null) interaction.Changed -= Apply;
            if (flow != null) flow.Changed -= Apply;
            if (wayfinding != null) wayfinding.Changed -= Apply;
        }
    }
}
