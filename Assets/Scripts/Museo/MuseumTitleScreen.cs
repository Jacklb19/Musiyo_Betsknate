using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Turns the entry menu into a title screen when the visit opens on the hearth set: no card behind it, the museum's
    /// name large across the top of a column on the left, the choices as a floating list marked by a thin line of the
    /// accent colour, darkness gathering on the left, and the fire's warm light reaching the menu and breathing with the
    /// flames. Every other menu keeps its own look. It only restyles at runtime; the menu prefab stays as authored.
    /// </summary>
    public sealed class MuseumTitleScreen : MonoBehaviour
    {
        public const float TitleSize = 62;
        public const float ChoiceSize = 27;
        private MuseumMenuView view;
        private MuseumVisitFlow flow;
        private MuseumEntryBackdrop backdrop;
        private Image shade;
        private Image glow;
        private Color glowColor;
        private readonly List<(Selectable choice, Image marker, TMP_Text label)> choices = new List<(Selectable, Image, TMP_Text)>();
        private Color textColor;
        private Color accentColor;
        private Color mutedColor;

        /// <summary>True while the title look is in use: the entry menu is open over the hearth.</summary>
        public bool Showing => shade != null && shade.gameObject.activeSelf;

        public void Configure(MuseumMenuView menuView, MuseumVisitFlow visitFlow, MuseumEntryBackdrop entryBackdrop)
        {
            view = menuView;
            flow = visitFlow;
            backdrop = entryBackdrop;
            if (view == null || backdrop == null || backdrop.Kind != EntryBackdropKind.Hearth) { enabled = false; return; }
            var settings = MuseumExperienceConfiguration.Current;
            textColor = MuseumExperienceConfiguration.ColorValue(settings.TextColor);
            accentColor = MuseumExperienceConfiguration.ColorValue(settings.AccentColor);
            mutedColor = MuseumExperienceConfiguration.ColorValue(settings.MutedTextColor);
            var page = view.Main;
            Unbind(page.gameObject);
            if (page.TryGetComponent<Image>(out var card)) card.color = Color.clear;
            foreach (Transform child in page.transform)
                if (child.name.StartsWith("PresentationEdge") || child.name.StartsWith("PresentationWarmEdge")) child.gameObject.SetActive(false);
            Style(page.transform);
            foreach (var choice in new Selectable[] { page.Primary, page.Settings, page.Controls, page.Retry, page.Catalog })
                if (choice != null) Choice(choice);
            var backdropColor = MuseumExperienceConfiguration.ColorValue(settings.BackdropColor);
            backdropColor.a = 1;
            shade = Layer("TitleShade", Gradient(new[] { (0f, .9f), (.34f, .78f), (.58f, .25f), (.78f, 0f) }), backdropColor, 0);
            glowColor = Color.Lerp(accentColor, new Color(1, .45f, .15f), .35f);
            glow = Layer("TitleFireGlow", Gradient(new[] { (.12f, 0f), (.34f, .55f), (.5f, .7f), (.66f, .35f), (.88f, 0f) }), glowColor, 1);
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            bool show = flow != null && flow.Phase != VisitPhase.Exploring && backdrop.Shown && view.Main.gameObject.activeInHierarchy;
            if (shade.gameObject.activeSelf != show) { shade.gameObject.SetActive(show); glow.gameObject.SetActive(show); }
            if (!show) return;
            // Both layers dissolve with the menu on the way in.
            float presence = view.Overlay.alpha;
            var dark = shade.color;
            dark.a = presence;
            shade.color = dark;
            // The firelight on the menu rises and falls with the flames.
            var tint = glowColor;
            tint.a = (.05f + .08f * backdrop.FireLevel) * presence;
            glow.color = tint;
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var (choice, marker, label) in choices)
            {
                bool active = choice.gameObject == selected;
                if (marker.enabled != active) marker.enabled = active;
                label.color = active ? accentColor : choice.IsInteractable() ? textColor : mutedColor;
            }
        }

        /// <summary>Configuration bindings would put the card colours back each time the page opens.</summary>
        private static void Unbind(GameObject root)
        {
            foreach (var binding in root.GetComponentsInChildren<MuseumMenuThemeBinding>(true))
                if (binding.ColorToken == MuseumMenuColorToken.Panel || binding.BindSelectableColors) Destroy(binding);
        }

        private void Style(Transform page)
        {
            foreach (var label in page.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.GetComponentInParent<Selectable>() != null) continue;
                switch (label.name)
                {
                    case "PresentationKicker":
                        label.color = accentColor;
                        label.characterSpacing = 18;
                        break;
                    case "Title":
                        label.fontStyle = FontStyles.UpperCase;
                        label.characterSpacing = 6;
                        label.color = textColor;
                        // The name keeps one line across the column, as large as the column allows.
                        label.textWrappingMode = TextWrappingModes.NoWrap;
                        label.overflowMode = TextOverflowModes.Overflow;
                        label.enableAutoSizing = true;
                        label.fontSizeMin = 34;
                        label.fontSizeMax = TitleSize;
                        break;
                    case "Description":
                        label.color = mutedColor;
                        break;
                }
            }
            var rule = page.Find("Viewport/Content/PresentationRule");
            if (rule != null && rule.TryGetComponent<Image>(out var line))
            {
                var tint = accentColor;
                tint.a = .7f;
                line.color = tint;
                if (rule.TryGetComponent<LayoutElement>(out var size)) size.preferredWidth = 120;
            }
        }

        /// <summary>A choice reads as text on the dark; the one in focus is marked by a short line and the accent colour.</summary>
        private void Choice(Selectable choice)
        {
            if (choice.TryGetComponent<Image>(out var surface)) surface.color = Color.white;
            var colors = choice.colors;
            var hover = accentColor;
            hover.a = .1f;
            var pressed = accentColor;
            pressed.a = .22f;
            colors.normalColor = colors.disabledColor = Color.clear;
            colors.highlightedColor = colors.selectedColor = hover;
            colors.pressedColor = pressed;
            colors.fadeDuration = .12f;
            choice.colors = colors;
            var label = choice.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.fontSize = ChoiceSize;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.characterSpacing = 2;
                label.margin = new Vector4(26, 0, 0, 0);
            }
            var marker = new GameObject("TitleMarker", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            marker.transform.SetParent(choice.transform, false);
            var rect = marker.rectTransform;
            rect.anchorMin = new Vector2(0, .2f);
            rect.anchorMax = new Vector2(0, .8f);
            rect.pivot = new Vector2(0, .5f);
            rect.sizeDelta = new Vector2(4, 0);
            rect.anchoredPosition = Vector2.zero;
            marker.color = accentColor;
            marker.raycastTarget = false;
            marker.enabled = false;
            choices.Add((choice, marker, label));
        }

        /// <summary>A full-screen layer behind the menu column, drawn from a horizontal gradient.</summary>
        private Image Layer(string name, Sprite gradient, Color color, int order)
        {
            var overlay = view.Overlay.transform;
            var layer = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            layer.transform.SetParent(overlay, false);
            layer.transform.SetSiblingIndex(order);
            var rect = layer.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            layer.sprite = gradient;
            layer.color = color;
            layer.raycastTarget = false;
            return layer;
        }

        /// <summary>A white sprite whose alpha follows the given (position, alpha) stops from left to right.</summary>
        private static Sprite Gradient((float at, float alpha)[] stops)
        {
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TitleGradient" };
            for (int x = 0; x < width; x++)
            {
                float t = x / (width - 1f), alpha = stops[stops.Length - 1].alpha;
                if (t <= stops[0].at) alpha = stops[0].alpha;
                for (int index = 1; index < stops.Length; index++)
                    if (t >= stops[index - 1].at && t <= stops[index].at)
                    {
                        float k = Mathf.SmoothStep(0, 1, (t - stops[index - 1].at) / Mathf.Max(.0001f, stops[index].at - stops[index - 1].at));
                        alpha = Mathf.Lerp(stops[index - 1].alpha, stops[index].alpha, k);
                        break;
                    }
                texture.SetPixel(x, 0, new Color(1, 1, 1, alpha));
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(.5f, .5f));
        }
    }
}
