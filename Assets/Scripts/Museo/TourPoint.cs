using System;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum PointState { Undeclared, Empty, Available, Focused, Active }

    [RequireComponent(typeof(PointAnchor))]
    public sealed class TourPoint : MonoBehaviour
    {
        [SerializeField] private Renderer statusRenderer;
        private MaterialPropertyBlock colorProperties;
        public PointAnchor Anchor => GetComponent<PointAnchor>();
        public PointContractV1 Content { get; private set; }
        public PointState State { get; private set; }
        /// <summary>The published guide stands here; it is not a cultural element list.</summary>
        public bool IsGuide { get; private set; }
        public bool HasContent => Content != null && Content.elements != null && Content.elements.Length > 0;
        public event Action<TourPoint> Changed;

        public void Configure(Renderer indicator) => statusRenderer = indicator;

        public void Bind(PointContractV1 content)
        {
            Content = content;
            SetState(content == null ? PointState.Undeclared : HasContent ? PointState.Available : PointState.Empty);
        }

        public void MarkGuide(bool guide)
        {
            if (IsGuide == guide) return;
            IsGuide = guide;
            SetState(State);
        }

        public bool Supports(string method) => HasContent && Array.IndexOf(Content.activation, method) >= 0;

        public void SetState(PointState state)
        {
            State = !HasContent && state != PointState.Undeclared ? PointState.Empty : state;
            if (statusRenderer != null)
            {
                colorProperties ??= new MaterialPropertyBlock();
                var color = State == PointState.Active ? new Color(0.9f, 0.62f, 0.22f)
                    : State == PointState.Focused ? new Color(0.95f, 0.85f, 0.45f)
                    : State == PointState.Available ? new Color(0.25f, 0.65f, 0.5f)
                    : IsGuide ? new Color(0.35f, 0.5f, 0.85f)
                    : new Color(0.28f, 0.3f, 0.3f);
                statusRenderer.GetPropertyBlock(colorProperties);
                colorProperties.SetColor("_BaseColor", color);
                colorProperties.SetColor("_Color", color);
                statusRenderer.SetPropertyBlock(colorProperties);
            }
            Changed?.Invoke(this);
        }
    }
}
