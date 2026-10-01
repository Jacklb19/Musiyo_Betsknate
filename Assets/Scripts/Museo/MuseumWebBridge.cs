using System;
using System.Runtime.InteropServices;
using MusiyoBetsknate.Museo;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumWebBridge : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourLoader loader;
        private DesktopVisitorController visitor;
        private bool applying;
        private string pendingQuery;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MusiyoConfirmSelection(string tour, string point, string element);
        [DllImport("__Internal")]
        private static extern void MusiyoClearSelection(string tour);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var runtime = FindAnyObjectByType<TourRuntime>();
            if (runtime != null && runtime.GetComponent<MuseumWebBridge>() == null)
                runtime.gameObject.AddComponent<MuseumWebBridge>();
            WebGLInput.captureAllKeyboardInput = false;
        }
#endif

        private void Awake()
        {
            interaction = GetComponent<MuseumInteraction>();
            loader = GetComponent<TourLoader>();
            visitor = FindAnyObjectByType<DesktopVisitorController>();
        }

        private void OnEnable()
        {
            if (loader == null || interaction == null) return;
            loader.Changed += OnLoaded;
            interaction.Changed += NotifySelection;
        }

        private void OnDisable()
        {
            if (loader != null) loader.Changed -= OnLoaded;
            if (interaction != null) interaction.Changed -= NotifySelection;
        }

        private void OnLoaded()
        {
            if (loader.State != TourLoadState.Ready) return;
            if (pendingQuery != null) ApplySelection(pendingQuery);
            else if (Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var page)) ApplySelection(page.Query.TrimStart('?'));
        }

        public void ApplySelection(string query)
        {
            pendingQuery = query;
            if (interaction == null || loader == null || loader.State != TourLoadState.Ready) return;
            var tour = Parameter(query, "tour");
            var key = Parameter(query, "point");
            var slug = Parameter(query, "element");
            if (!string.IsNullOrEmpty(tour) && tour != interaction.Runtime.TourKey) return;
            if (string.IsNullOrEmpty(key))
            {
                applying = true;
                try { interaction.Close(); } finally { applying = false; }
                return;
            }
            if (!WebTourBridge.TryResolve(interaction.Runtime.Contract, key, slug, out _)) return;
            var point = interaction.Runtime.FindPoint(key);
            if (point == null) return;
            if (interaction.ActivePoint == point && (string.IsNullOrEmpty(slug)
                || interaction.SelectedElement?.slug == slug))
            {
                // Return an automatically selected single item to the page after startup.
                if (string.IsNullOrEmpty(slug) && interaction.SelectedElement != null) NotifySelection();
                return;
            }
            applying = true;
            try
            {
                if (interaction.State == InteractionState.Paused) interaction.SetPaused(false);
                if (visitor != null && !visitor.FocusPoint(point.Anchor)) return;
                if (!interaction.Activate(point, ActivationSource.DeepLink)) return;
                if (!string.IsNullOrEmpty(slug))
                    interaction.SelectElement(Array.FindIndex(point.Content.elements, element => element.slug == slug));
            }
            finally { applying = false; }
            NotifySelection();
        }

        private void NotifySelection()
        {
            if (applying || loader.State != TourLoadState.Ready || interaction.State == InteractionState.Paused) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (interaction.ActivePoint != null)
                MusiyoConfirmSelection(interaction.Runtime.TourKey, interaction.ActivePoint.Anchor.Key, interaction.SelectedElement?.slug);
            else MusiyoClearSelection(interaction.Runtime.TourKey);
#endif
        }

        public static string Parameter(string query, string name)
        {
            foreach (var part in (query ?? "").TrimStart('?').Split('&'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length == 2 && pair[0] == name)
                {
                    try { return Uri.UnescapeDataString(pair[1].Replace('+', ' ')); }
                    catch (UriFormatException) { return null; }
                }
            }
            return null;
        }
    }
}
