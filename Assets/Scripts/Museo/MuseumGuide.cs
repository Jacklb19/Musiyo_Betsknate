using System;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Guide presence at the published guide anchor. Questions need the backend guide service; until then the limitation is stated.</summary>
    [RequireComponent(typeof(TourRuntime), typeof(MuseumInteraction))]
    public sealed class MuseumGuide : MonoBehaviour
    {
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform visitor;
        private PointAnchor anchor;
        private bool connected;
        public GuideContractV1 Guide { get { Connect(); return runtime.Guide; } }
        public bool Present => Guide != null && anchor != null;
        public bool InRange { get; private set; }
        public bool Open { get { Connect(); return interaction.State == InteractionState.GuideQuestion; } }
        public string Name => string.IsNullOrEmpty(Guide?.name) ? MuseumInterfaceText.Get("guide_name") : Guide.name;
        public string Prompt => !Present || !InRange || interaction.State != InteractionState.Exploration ? ""
            : MuseumInterfaceText.Format("guide_prompt", Name);
        public string Message => Guide == null ? ""
            : Guide.available
                ? MuseumInterfaceText.Get("guide_not_connected")
                : MuseumInterfaceText.Get("guide_unavailable");
        public event Action Changed;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void OnEnable() => Connect();
        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            runtime.Changed -= Bind;
            interaction.Changed -= Notify;
        }
        private void Update() => Refresh();
        private void Notify() => Changed?.Invoke();

        private void Connect()
        {
            if (connected) return;
            connected = true;
            runtime = GetComponent<TourRuntime>();
            interaction = GetComponent<MuseumInteraction>();
            runtime.Changed += Bind;
            interaction.Changed += Notify;
            Bind();
        }

        private void Bind()
        {
            anchor = runtime.FindAnchor(runtime.Guide?.key);
            InRange = false;
            Refresh();
            Changed?.Invoke();
        }

        public void Refresh()
        {
            Connect();
            bool inRange = false;
            if (Present && visitor != null)
            {
                var offset = anchor.transform.position - visitor.position;
                offset.y = 0;
                inRange = offset.sqrMagnitude <= anchor.ActivationRadius * anchor.ActivationRadius;
            }
            if (inRange == InRange) return;
            InRange = inRange;
            Changed?.Invoke();
        }

        public bool Ask()
        {
            Refresh();
            return Present && InRange && interaction.OpenGuide();
        }

        public void Close() { if (Open) interaction.Back(); }
    }
}
