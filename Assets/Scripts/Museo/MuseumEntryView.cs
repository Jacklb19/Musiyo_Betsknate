using System;
using UnityEngine;
using UnityEngine.XR;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Stages the entry menu in front of the museum. A second camera at the entry viewpoint shows the architecture behind
    /// the menu and glides to the visitor's eyes when the visit begins. The visitor's own camera is never moved, and with
    /// a headset nothing is staged: there the entry is a fade.
    /// </summary>
    [RequireComponent(typeof(MuseumVisitFlow))]
    public sealed class MuseumEntryView : MonoBehaviour
    {
        private MuseumVisitFlow flow;
        private MuseumExperienceConfiguration configuration;
        private Camera visitorCamera;
        private Transform visitorBody;
        private MuseumEntryViewpoint viewpoint;
        private Camera stage;
        private bool hidVisitorCamera;
        private readonly RaycastHit[] obstacles = new RaycastHit[8];

        /// <summary>A headset owns its view. Tests replace this probe.</summary>
        public Func<bool> HeadsetActive = () => XRSettings.isDeviceActive;
        public bool Available => visitorCamera != null && viewpoint != null && !HeadsetActive();
        public bool Staged => stage != null && stage.enabled;
        public Camera StageCamera => stage;
        /// <summary>Scenic set shown behind the menu instead of the museum, when one is chosen.</summary>
        public MuseumEntryBackdrop Backdrop { get; private set; }
        public bool BackdropActive => Available && Backdrop != null && Backdrop.Active;
        public const float BackdropFieldOfView = 40;

        public void Configure(Camera visitorView, Transform visitor, MuseumEntryViewpoint entryViewpoint, MuseumEntryBackdrop backdrop = null)
        {
            Backdrop = backdrop;
            Release();
            flow = GetComponent<MuseumVisitFlow>();
            configuration = MuseumExperienceConfiguration.Current;
            visitorCamera = visitorView;
            visitorBody = visitor;
            viewpoint = entryViewpoint;
            flow.Entry = this;
            Apply();
        }

        /// <summary>The glide is offered only for a short, unobstructed move; any other entry goes through the fade.</summary>
        public bool CanGlide
        {
            get
            {
                if (!Available) return false;
                var from = viewpoint.transform.position;
                var path = visitorCamera.transform.position - from;
                float distance = path.magnitude;
                if (distance > configuration.EntryGlideMaxDistance) return false;
                if (distance < .001f) return true;
                Physics.SyncTransforms();
                int count = Physics.RaycastNonAlloc(from, path / distance, obstacles, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int index = 0; index < count; index++)
                    if (visitorBody == null || !obstacles[index].transform.IsChildOf(visitorBody)) return false;
                return true;
            }
        }

        private void LateUpdate() => Apply();

        /// <summary>Places the stage camera for the current moment of the flow, or hands the view back to the visitor.</summary>
        public void Apply()
        {
            if (flow == null) return;
            bool gliding = flow.Phase == VisitPhase.Entering && flow.Transition == EntryTransition.Glide;
            float share = configuration.EntryMenuFadeShare;
            // A fading entry changes view while the screen is dark.
            bool show = Available && (flow.Phase == VisitPhase.Menu || gliding
                || flow.Phase == VisitPhase.Entering && flow.TransitionProgress < share);
            if (!show) { Release(); return; }
            if (stage == null) CreateStage();
            // A scenic backdrop stays until the screen is dark; the glide then starts at the museum viewpoint.
            bool backdrop = BackdropActive;
            if (backdrop && (flow.Phase == VisitPhase.Menu || flow.TransitionProgress < share))
            {
                stage.transform.SetPositionAndRotation(Backdrop.CameraPose.position, Backdrop.CameraPose.rotation);
                stage.fieldOfView = BackdropFieldOfView;
                Backdrop.Show(true, stage);
                Show();
                return;
            }
            Backdrop?.Show(false, stage);
            float travel = !gliding ? 0 : backdrop ? Mathf.SmoothStep(0, 1, (flow.TransitionProgress - share) / (1 - share))
                : Mathf.SmoothStep(0, 1, flow.TransitionProgress);
            var from = viewpoint.transform;
            var to = visitorCamera.transform;
            stage.transform.SetPositionAndRotation(Vector3.Lerp(from.position, to.position, travel),
                Quaternion.Slerp(from.rotation, to.rotation, travel));
            stage.fieldOfView = Mathf.Lerp(viewpoint.FieldOfView, visitorCamera.fieldOfView, travel);
            Show();
        }

        private void Show()
        {
            stage.enabled = true;
            if (visitorCamera.enabled) { visitorCamera.enabled = false; hidVisitorCamera = true; }
        }

        /// <summary>A copy of the visitor's camera, so both render alike when the view is handed over; it carries nothing else.</summary>
        private void CreateStage()
        {
            var copy = Instantiate(visitorCamera.gameObject, transform);
            copy.name = "EntryStageCamera";
            copy.tag = "Untagged";
            for (int index = copy.transform.childCount - 1; index >= 0; index--) Discard(copy.transform.GetChild(index).gameObject);
            foreach (var component in copy.GetComponents<Component>())
            {
                if (component is Transform || component is Camera || component.GetType().Name == "UniversalAdditionalCameraData") continue;
                // A second listener must not exist even for the frame its removal may take.
                if (component is Behaviour behaviour) behaviour.enabled = false;
                Discard(component);
            }
            stage = copy.GetComponent<Camera>();
            stage.enabled = false;
        }

        private void Release()
        {
            Backdrop?.Show(false, stage);
            if (stage != null) stage.enabled = false;
            if (hidVisitorCamera && visitorCamera != null) visitorCamera.enabled = true;
            hidVisitorCamera = false;
        }

        private static void Discard(UnityEngine.Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDisable() => Release();
        private void OnDestroy()
        {
            Release();
            if (flow != null && flow.Entry == this) flow.Entry = null;
            if (stage != null) Discard(stage.gameObject);
        }
    }
}
