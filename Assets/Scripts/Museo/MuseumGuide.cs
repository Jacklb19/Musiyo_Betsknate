using System;
using System.Collections.Generic;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Guide presence at the published guide anchor and the visitor's conversation with it. Questions go to the shared
    /// guide service only where the tour publishes the guide as available; the service's answer decides the rest.
    /// </summary>
    [RequireComponent(typeof(TourRuntime), typeof(MuseumInteraction))]
    public sealed class MuseumGuide : MonoBehaviour
    {
        private TourRuntime runtime;
        private MuseumInteraction interaction;
        private Transform visitor;
        private PointAnchor anchor;
        private bool connected;
        private bool typing;
        public GuideContractV1 Guide { get { Connect(); return runtime.Guide; } }
        public bool Present => Guide != null && anchor != null;
        public bool InRange { get; private set; }
        public bool Open { get { Connect(); return interaction.State == InteractionState.GuideQuestion; } }
        public string Name => string.IsNullOrEmpty(Guide?.name) ? MuseumInterfaceText.Get("guide_name") : Guide.name;
        public string Prompt => !Present || !InRange || interaction.State != InteractionState.Exploration ? ""
            : MuseumInterfaceText.Format("guide_prompt", Name);
        /// <summary>
        /// Publication metadata of the tour: whether visitors are offered the guide at all. It says nothing about whether
        /// the service is up or has evidence for a question; only an answer tells that.
        /// </summary>
        public bool Offered => Guide?.available == true;
        /// <summary>The conversation; created with the guide and reused for every question.</summary>
        public GuideQuestionClient Client { get { Connect(); return client; } }
        private GuideQuestionClient client;
        /// <summary>True while the visitor writes a question; walking and every shortcut are silent meanwhile.</summary>
        public bool Typing
        {
            get => typing;
            set
            {
                if (typing == value) return;
                typing = value;
                Changed?.Invoke();
            }
        }
        public string Message
        {
            get
            {
                if (Guide == null) return "";
                if (!Offered) return MuseumInterfaceText.Get("guide_unavailable");
                switch (Client.State)
                {
                    case GuideQuestionState.Sending: return MuseumInterfaceText.Get("guide_sending");
                    case GuideQuestionState.Answered: return MuseumInterfaceText.Get("guide_answered");
                    case GuideQuestionState.Abstained: return MuseumInterfaceText.Get("guide_abstained");
                    case GuideQuestionState.ProviderUnavailable: return MuseumInterfaceText.Get("guide_provider_unavailable");
                    case GuideQuestionState.Invalid: return MuseumInterfaceText.Get("guide_invalid");
                    case GuideQuestionState.Rejected: return MuseumInterfaceText.Get("guide_rejected");
                    case GuideQuestionState.RateLimited:
                        return Client.RetryWaitRemaining > 0
                            ? MuseumInterfaceText.Format("guide_rate_limited", Client.RetryWaitRemaining)
                            : MuseumInterfaceText.Get("guide_rate_limit_over");
                    case GuideQuestionState.TimedOut: return MuseumInterfaceText.Get("guide_timed_out");
                    case GuideQuestionState.Unreachable: return MuseumInterfaceText.Get("guide_unreachable");
                    case GuideQuestionState.ServiceError: return MuseumInterfaceText.Get("guide_service_error");
                    case GuideQuestionState.Incompatible: return MuseumInterfaceText.Get("guide_incompatible");
                    default:
                        return string.IsNullOrWhiteSpace(Client.Question) ? MuseumInterfaceText.Get("guide_ready")
                            : MuseumInterfaceText.Get("guide_cancelled");
                }
            }
        }
        public event Action Changed;
        /// <summary>Raised when the visitor asks to write, so the conversation view can take the keyboard.</summary>
        public event Action TypingRequested;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void OnEnable() => Connect();
        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            runtime.Changed -= Bind;
            interaction.Changed -= OnInteractionChanged;
            client.Changed -= Notify;
            client.Dispose();
            client = null;
            typing = false;
        }
        private void Update()
        {
            Refresh();
            client?.Poll();
        }
        private void Notify() => Changed?.Invoke();

        private void Connect()
        {
            if (connected) return;
            connected = true;
            runtime = GetComponent<TourRuntime>();
            interaction = GetComponent<MuseumInteraction>();
            client = new GuideQuestionClient(ApiBase, () => Time.realtimeSinceStartupAsDouble);
            runtime.Changed += Bind;
            interaction.Changed += OnInteractionChanged;
            client.Changed += Notify;
            Bind();
        }

        /// <summary>The questions go to the same API as the tour; no second address exists.</summary>
        private string ApiBase()
        {
            var loader = GetComponent<TourLoader>();
            return loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);
        }

        /// <summary>Leaving the conversation, pausing or ending the visit abandons the question in flight; its text stays.</summary>
        private void OnInteractionChanged()
        {
            if (interaction.State != InteractionState.GuideQuestion)
            {
                Typing = false;
                client.Cancel();
            }
            Changed?.Invoke();
        }

        private void Bind()
        {
            anchor = runtime.FindAnchor(runtime.Guide?.key);
            InRange = false;
            client.Clear();
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
                float radius = anchor.ActivationRadius + (Open ? MuseumExperienceConfiguration.Current.PointLeaveMargin : 0);
                inRange = offset.sqrMagnitude <= radius * radius;
            }
            if (inRange == InRange) return;
            InRange = inRange;
            // Walking away from the guide ends the conversation, the way a reading stand folds its screen.
            if (!inRange && Open && visitor != null) Close();
            Changed?.Invoke();
        }

        /// <summary>Opens the conversation from free exploration within reach of the guide.</summary>
        public bool Ask()
        {
            Refresh();
            return Present && InRange && interaction.OpenGuide();
        }

        /// <summary>Asks the conversation view for the keyboard, as the guide key does while the conversation is open.</summary>
        public bool RequestTyping()
        {
            if (!Open || !Offered || MuseumInput.Current.Spatial) return false;
            TypingRequested?.Invoke();
            return true;
        }

        /// <summary>Sends the visitor's question. Nothing is sent where the guide is not offered.</summary>
        public bool Send(string question)
        {
            if (!Open || !Offered) return false;
            client.Ask(question);
            return true;
        }

        public bool Retry() => Open && Offered && client.Retry();

        public void Close() { if (Open) interaction.Back(); }

        /// <summary>
        /// Where a cited record stands in this museum, if the loaded tour includes it. A citation outside the tour is shown
        /// by its title only, without promising a place in the museum.
        /// </summary>
        public TourPoint Locate(GuideCitationContractV1 citation)
        {
            Connect();
            if (citation == null) return null;
            foreach (var point in runtime.OrderedPoints)
                if (point.HasContent)
                    foreach (var element in point.Content.elements)
                        if (element.slug == citation.element_slug) return point;
            return null;
        }

        /// <summary>Room key of a tour point, for orientation toward a cited record.</summary>
        public string RoomOf(TourPoint point)
        {
            var room = point != null ? point.GetComponentInParent<RoomAnchor>() : null;
            return room != null ? room.Key : null;
        }

        public IReadOnlyList<GuideCitationContractV1> Citations
            => (IReadOnlyList<GuideCitationContractV1>)Client.Response?.citations ?? Array.Empty<GuideCitationContractV1>();
    }
}
