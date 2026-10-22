using System;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public enum MuseumVignette { Off, Soft, Strong }

    /// <summary>
    /// Comfort choices of the visit: the vignette that narrows the view while moving on a headset, the height of the
    /// eyes, and the sizes of the subtitles and of the reading on a stand. The values themselves are few and named, so a
    /// saved preference is always one the menu can show.
    /// </summary>
    public static class MuseumComfort
    {
        /// <summary>Sizes offered for the subtitles and for the text of a record, relative to the shipped size.</summary>
        public static readonly float[] SubtitleScales = { .85f, 1, 1.25f };
        public static readonly float[] ReadingScales = { 1, 1.15f, 1.3f };
        /// <summary>Metres the eyes can be raised or lowered on a headset.</summary>
        public const float MaxHeightOffset = .25f;
        /// <summary>Metres between two steps of the height, so a slider stops at whole centimetres.</summary>
        public const float HeightStep = .05f;

        public static MuseumVignette? ParseVignette(string value)
            => value == "off" ? MuseumVignette.Off : value == "soft" ? MuseumVignette.Soft : value == "strong" ? MuseumVignette.Strong : (MuseumVignette?)null;
        public static string Name(MuseumVignette vignette)
            => vignette == MuseumVignette.Off ? "off" : vignette == MuseumVignette.Strong ? "strong" : "soft";
        /// <summary>How dark the edge of the view becomes at full movement, from 0 to 1.</summary>
        public static float Intensity(MuseumVignette vignette)
            => vignette == MuseumVignette.Off ? 0 : vignette == MuseumVignette.Strong ? .9f : .6f;
        public static string TextKey(MuseumVignette vignette)
            => vignette == MuseumVignette.Off ? "vignette_off" : vignette == MuseumVignette.Strong ? "vignette_strong" : "vignette_soft";
        public static MuseumVignette NextVignette(MuseumVignette vignette)
            => vignette == MuseumVignette.Off ? MuseumVignette.Soft : vignette == MuseumVignette.Soft ? MuseumVignette.Strong : MuseumVignette.Off;

        public static bool IsScale(float[] scales, float value)
        {
            if (!MuseumExperienceConfiguration.Finite(value)) return false;
            foreach (var scale in scales) if (Mathf.Abs(scale - value) < .001f) return true;
            return false;
        }

        /// <summary>The size after the current one, wrapping around to the first.</summary>
        public static float NextScale(float[] scales, float current)
        {
            for (int index = 0; index < scales.Length; index++)
                if (Mathf.Abs(scales[index] - current) < .001f) return scales[(index + 1) % scales.Length];
            return scales[0];
        }

        /// <summary>The word for a size: smaller than the shipped size, the shipped size, larger, or much larger.</summary>
        public static string ScaleKey(float scale)
            => scale < .999f ? "size_small" : scale < 1.001f ? "size_normal" : scale < 1.2f ? "size_large" : "size_larger";

        /// <summary>The word for a size among those offered: the shipped size is "normal", each step up is one word larger.</summary>
        public static string ScaleKey(float[] scales, float scale)
        {
            var words = new[] { "size_small", "size_normal", "size_large", "size_larger" };
            int normal = 0;
            for (int index = 0; index < scales.Length; index++) if (Mathf.Abs(scales[index] - 1) < .001f) normal = index;
            for (int index = 0; index < scales.Length; index++)
                if (Mathf.Abs(scales[index] - scale) < .001f) return words[Mathf.Clamp(1 + index - normal, 0, words.Length - 1)];
            return "size_normal";
        }

        /// <summary>Whole centimetres of a height offset, for its value text.</summary>
        public static int Centimetres(float metres) => Mathf.RoundToInt(metres * 100);
    }

    /// <summary>
    /// A veil before the eyes, clear at its centre and dark at its edge, that closes in a little while the visitor walks
    /// or turns with the stick and opens again when they stop: it is what keeps most people comfortable in a headset. It
    /// is part of the world, like the veil of the entry, because a screen effect is not seen by a headset. When the
    /// visitor chose no vignette it is never drawn.
    /// </summary>
    public sealed class MuseumComfortVignette : MonoBehaviour
    {
        /// <summary>Seconds the veil takes to close when movement starts and to open when it stops.</summary>
        public const float CloseSeconds = .14f, OpenSeconds = .3f;
        private Canvas canvas;
        private Image veil;
        private float level;
        private bool moving;

        /// <summary>Darkness of the edge at full movement, from 0 to 1; 0 switches the veil off.</summary>
        public float Intensity { get; set; } = MuseumComfort.Intensity(MuseumVignette.Soft);
        /// <summary>How far the veil is closed this moment, from 0 to 1.</summary>
        public float Level => level;
        public bool Drawn => canvas != null && canvas.enabled;

        public void Configure(Transform head)
        {
            var holder = new GameObject("ComfortVignette", typeof(RectTransform), typeof(Canvas));
            holder.transform.SetParent(head, false);
            holder.transform.localPosition = Vector3.forward * (MuseumXrEntry.VeilDistance + .01f);
            canvas = holder.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = short.MaxValue - 1;
            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(100, 100);
            rect.localScale = Vector3.one * .02f;
            var image = new GameObject("Veil", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(holder.transform, false);
            var imageRect = (RectTransform)image.transform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
            veil = image.GetComponent<Image>();
            veil.sprite = MuseumUiSprites.Radial();
            veil.raycastTarget = false;
            veil.color = new Color(0, 0, 0, 0);
            canvas.enabled = false;
        }

        /// <summary>Tells the veil whether the visitor moves this frame.</summary>
        public void Set(bool value) => moving = value;

        /// <summary>Advances the veil; the spatial host calls it each frame, and tests call it directly.</summary>
        public void Step(float elapsed)
        {
            if (veil == null || !MuseumExperienceConfiguration.Finite(elapsed)) return;
            float target = Intensity > 0 && moving ? 1 : 0;
            level = Mathf.MoveTowards(level, target, Mathf.Max(0, elapsed) / (target > level ? CloseSeconds : OpenSeconds));
            float alpha = level * Mathf.Clamp01(Intensity);
            veil.color = new Color(0, 0, 0, alpha);
            canvas.enabled = alpha > .001f;
        }

        private void LateUpdate() => Step(Time.unscaledDeltaTime);

        private void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
    }
}
