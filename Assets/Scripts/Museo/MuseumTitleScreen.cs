using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The look of every menu of the visit, given at runtime over the authored prefabs, which stay as they are. On a
    /// screen each page is a floating column on the left, with darkness gathering behind it and the world (the fire of the
    /// entry, or the museum while the visit is paused) kept in view on the right; the title page over the hearth takes
    /// the fire's warm light, breathing with the flames. In a headset, or on a narrow window, the pages keep a rounded
    /// card. Choices read as text on the dark, the one in focus marked by a short line of the accent colour that slides
    /// in; sliders are a thin line with a soft dot and show their value; while the museum's content loads a light sweeps
    /// under the title. Colours and faces come from the shared tokens (coordination/design-tokens-20261018.json).
    /// </summary>
    public sealed class MuseumTitleScreen : MonoBehaviour
    {
        public const float TitleSize = 62;
        public const float PageTitleSize = 46;
        public const float ChoiceSize = 27;
        public const float HeadingSize = 13;
        /// <summary>Height over which a scrolling list fades out at its edges.</summary>
        public const int ListFade = 40;
        public const int CardRadius = 22;
        /// <summary>Width of the focus mark and how far a focused label slides to the right.</summary>
        public const float MarkWidth = 4, FocusSlide = 8;
        private sealed class Look
        {
            public Selectable Choice;
            public Image Marker;
            public TMP_Text Label;
            public TMP_Text Value;
            public float Amount;
            public float LabelLeft;
        }
        private MuseumMenuView view;
        private MuseumVisitFlow flow;
        private MuseumEntryBackdrop backdrop;
        private MuseumMenuHud hud;
        private MuseumMenuLayout layout;
        private Image shade;
        private Image glow;
        private Color glowColor;
        private readonly List<Look> looks = new List<Look>();
        private readonly List<Image> cards = new List<Image>();
        private readonly Dictionary<Slider, (TMP_Text value, Func<float, string> format)> sliderValues = new Dictionary<Slider, (TMP_Text, Func<float, string>)>();
        private Color textColor;
        private Color accentColor;
        private Color mutedColor;
        private Color panelColor;
        private RectTransform loadingLine;
        private RectTransform loadingSweep;
        private TMP_Text loadingText;
        private float loadingTime;

        /// <summary>True while the title look is in use: the entry menu is open over the hearth.</summary>
        public bool Showing => shade != null && shade.gameObject.activeSelf && glow != null && glow.gameObject.activeSelf;
        /// <summary>True while the darkness behind the floating column is drawn: a menu is open on a screen.</summary>
        public bool Shaded => shade != null && shade.gameObject.activeSelf;
        /// <summary>True while the loading light sweeps under the title.</summary>
        public bool Loading => loadingLine != null && loadingLine.gameObject.activeSelf;

        public void Configure(MuseumMenuView menuView, MuseumVisitFlow visitFlow, MuseumEntryBackdrop entryBackdrop)
        {
            view = menuView;
            flow = visitFlow;
            backdrop = entryBackdrop;
            hud = GetComponent<MuseumMenuHud>();
            if (view == null) { enabled = false; return; }
            layout = view.GetComponent<MuseumMenuLayout>();
            var settings = MuseumExperienceConfiguration.Current;
            textColor = MuseumExperienceConfiguration.ColorValue(settings.TextColor);
            accentColor = MuseumExperienceConfiguration.ColorValue(settings.AccentColor);
            mutedColor = MuseumExperienceConfiguration.ColorValue(settings.MutedTextColor);
            panelColor = MuseumExperienceConfiguration.ColorValue(settings.PanelColor);
            looks.Clear();
            cards.Clear();
            sliderValues.Clear();
            foreach (var page in new[] { view.Main, view.Pause, view.Settings, view.Controls })
            {
                Unbind(page.gameObject);
                StylePage(page);
            }
            StyleWelcome(view.Welcome);
            // A list longer than its column fades out at its edge instead of cutting a line in half.
            foreach (var scrolled in new[] { view.Main, view.Pause, view.Settings, view.Controls })
                if (scrolled.Scroll != null && scrolled.Scroll.viewport != null && scrolled.Scroll.viewport.TryGetComponent<RectMask2D>(out var mask))
                    mask.softness = new Vector2Int(0, ListFade);
            var backdropColor = MuseumExperienceConfiguration.ColorValue(settings.BackdropColor);
            backdropColor.a = 1;
            shade = Layer("TitleShade", MuseumUiSprites.Horizontal("shade", new[] { (0f, .94f), (.38f, .86f), (.6f, .32f), (.82f, 0f) }), backdropColor, 0);
            glowColor = Color.Lerp(accentColor, new Color(1, .45f, .15f), .35f);
            glow = Layer("TitleFireGlow", MuseumUiSprites.Horizontal("glow", new[] { (.12f, 0f), (.34f, .55f), (.5f, .7f), (.66f, .35f), (.88f, 0f) }), glowColor, 1);
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (view == null || shade == null) return;
            bool compact = layout != null && layout.Compact;
            MarkFocus();
            bool overlayShown = view.Overlay.gameObject.activeInHierarchy;
            bool hearth = backdrop != null && backdrop.Kind == EntryBackdropKind.Hearth && backdrop.Shown
                && flow != null && flow.Phase != VisitPhase.Exploring;
            bool showShade = overlayShown && !compact;
            bool showGlow = showShade && hearth;
            if (shade.gameObject.activeSelf != showShade) shade.gameObject.SetActive(showShade);
            if (glow.gameObject.activeSelf != showGlow) glow.gameObject.SetActive(showGlow);
            // On a card the page carries its own surface; on the shade it stands on the dark.
            var card = panelColor;
            card.a = compact ? panelColor.a : 0;
            foreach (var surface in cards) if (surface != null && surface.color != card) surface.color = card;
            UpdateLoading();
            if (!showShade) return;
            // Both layers dissolve with the menu on the way in.
            float presence = view.Overlay.alpha;
            var dark = shade.color;
            dark.a = presence;
            shade.color = dark;
            if (!showGlow) return;
            // The firelight on the menu rises and falls with the flames.
            var tint = glowColor;
            tint.a = (.05f + .08f * backdrop.FireLevel) * presence;
            glow.color = tint;
        }

        /// <summary>The light under the title sweeps while the museum's content loads, and the loading text breathes.</summary>
        private void UpdateLoading()
        {
            if (loadingLine == null) return;
            bool loading = hud != null && hud.Loading && view.Main.gameObject.activeInHierarchy;
            if (loadingLine.gameObject.activeSelf != loading) loadingLine.gameObject.SetActive(loading);
            if (loadingText != null)
            {
                var color = mutedColor;
                if (loading) color.a = .6f + .4f * (.5f + .5f * Mathf.Sin(loadingTime * 2.4f));
                loadingText.color = color;
            }
            if (!loading) { loadingTime = 0; return; }
            loadingTime += Time.unscaledDeltaTime;
            float width = loadingLine.rect.width, sweep = Mathf.Min(120, width * .3f);
            float t = Mathf.Repeat(loadingTime / 1.6f, 1);
            float eased = t < .5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
            loadingSweep.sizeDelta = new Vector2(sweep, 0);
            loadingSweep.anchoredPosition = new Vector2(Mathf.Lerp(-sweep, width, eased), 0);
        }

        /// <summary>
        /// The choice in focus, on any page, carries the accent line and colour. The line slides in and the label moves
        /// with it over the interaction time of the tokens, so the focus is seen to travel rather than jump.
        /// </summary>
        private void MarkFocus()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            float step = Time.unscaledDeltaTime / MuseumMenuMotion.InteractionSeconds;
            foreach (var look in looks)
            {
                var choice = look.Choice;
                if (choice == null || !choice.gameObject.activeInHierarchy) continue;
                bool active = choice.gameObject == selected;
                look.Amount = Mathf.MoveTowards(look.Amount, active ? 1 : 0, step);
                if (look.Marker != null)
                {
                    bool visible = look.Amount > .001f;
                    if (look.Marker.enabled != visible) look.Marker.enabled = visible;
                    look.Marker.rectTransform.sizeDelta = new Vector2(MarkWidth * look.Amount, 0);
                }
                var rest = choice.IsInteractable() ? textColor : mutedColor;
                if (look.Label != null)
                {
                    look.Label.color = Color.Lerp(rest, accentColor, look.Amount);
                    var margin = look.Label.margin;
                    margin.x = look.LabelLeft + FocusSlide * look.Amount;
                    look.Label.margin = margin;
                }
                if (look.Value != null) look.Value.color = Color.Lerp(mutedColor, accentColor, look.Amount);
            }
        }

        /// <summary>A slider shows its value as text; the caller decides the words for it.</summary>
        public void FormatSlider(Slider slider, Func<float, string> format)
        {
            if (slider == null || format == null || !sliderValues.TryGetValue(slider, out var entry)) return;
            sliderValues[slider] = (entry.value, format);
            if (entry.value != null) entry.value.text = format(slider.value);
        }

        /// <summary>Text shown for a slider's value, or null when the slider carries none.</summary>
        public string SliderValueText(Slider slider)
            => slider != null && sliderValues.TryGetValue(slider, out var entry) && entry.value != null ? entry.value.text : null;

        /// <summary>Removes a component now in the Editor and at the end of the frame in play, as Unity requires.</summary>
        internal static void Discard(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
        /// <summary>Configuration bindings would put the card colours back each time the page opens.</summary>
        private static void Unbind(GameObject root)
        {
            foreach (var binding in root.GetComponentsInChildren<MuseumMenuThemeBinding>(true))
                if (binding.ColorToken == MuseumMenuColorToken.Panel || binding.BindSelectableColors) Discard(binding);
        }

        private void StylePage(MuseumMenuPageView page)
        {
            if (page.TryGetComponent<Image>(out var card))
            {
                card.sprite = MuseumUiSprites.Rounded(CardRadius);
                card.type = Image.Type.Sliced;
                card.pixelsPerUnitMultiplier = 1;
                card.color = Color.clear;
                cards.Add(card);
            }
            foreach (Transform child in page.transform)
                if (child.name.StartsWith("PresentationEdge") || child.name.StartsWith("PresentationWarmEdge")) child.gameObject.SetActive(false);
            foreach (var label in page.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.GetComponentInParent<Selectable>() != null) continue;
                switch (label.name)
                {
                    case "PresentationKicker":
                        label.color = accentColor;
                        label.fontSize = HeadingSize + 1;
                        label.characterSpacing = 14;
                        label.fontStyle = FontStyles.UpperCase;
                        break;
                    case "Title":
                        label.fontStyle = FontStyles.UpperCase;
                        label.color = textColor;
                        label.textWrappingMode = TextWrappingModes.NoWrap;
                        label.overflowMode = TextOverflowModes.Overflow;
                        if (page.Kind == MuseumMenuPageKind.Main)
                        {
                            // The name keeps one line across the column, as large as the column allows.
                            label.characterSpacing = 6;
                            label.enableAutoSizing = true;
                            label.fontSizeMin = 34;
                            label.fontSizeMax = TitleSize;
                        }
                        else
                        {
                            label.characterSpacing = 4;
                            label.enableAutoSizing = false;
                            label.fontSize = PageTitleSize;
                        }
                        break;
                    case "Description":
                        label.color = mutedColor;
                        if (page.Kind == MuseumMenuPageKind.Main) loadingText = label;
                        break;
                    case "Heading":
                        label.color = accentColor;
                        label.fontSize = HeadingSize;
                        label.characterSpacing = 10;
                        label.fontStyle = FontStyles.UpperCase | FontStyles.Bold;
                        break;
                }
            }
            var rule = page.transform.Find("Viewport/Content/PresentationRule");
            if (rule != null && rule.TryGetComponent<Image>(out var line))
            {
                // The authored binding would paint the rule in the second accent again each time the page opens.
                foreach (var binding in rule.GetComponents<MuseumMenuThemeBinding>()) Discard(binding);
                var tint = accentColor;
                tint.a = .7f;
                line.color = tint;
                if (rule.TryGetComponent<LayoutElement>(out var size)) { size.preferredWidth = 120; size.preferredHeight = 2; }
            }
            if (page.Kind == MuseumMenuPageKind.Main) LoadingLine(page);
            ScrollBar(page);
            foreach (var button in page.GetComponentsInChildren<Button>(true))
            {
                var row = button.GetComponentInParent<MuseumRebindRow>(true);
                if (row != null) ChoiceRow(button, row);
                else Choice(button);
            }
            foreach (var slider in page.GetComponentsInChildren<Slider>(true)) StyleSlider(slider);
        }

        /// <summary>
        /// A thin line at the right edge of a page that scrolls, with the visible part in the accent colour, so a page
        /// longer than the screen says so and can be dragged; it hides itself when everything fits. The wheel and the
        /// keyboard scroll as before. Longer pages are also allowed to stand taller on a tall screen.
        /// </summary>
        private void ScrollBar(MuseumMenuPageView page)
        {
            var scroll = page.Scroll;
            if (scroll == null || scroll.viewport == null) return;
            scroll.scrollSensitivity = 40;
            if (page.Kind != MuseumMenuPageKind.Main && page.TryGetComponent<LayoutElement>(out var size) && size.preferredHeight < 1000)
                size.preferredHeight = 1000;
            var previous = page.transform.Find("ScrollLine");
            if (previous != null) Discard(previous.gameObject);
            var bar = new GameObject("ScrollLine", typeof(RectTransform), typeof(Image), typeof(Scrollbar)).GetComponent<Scrollbar>();
            bar.transform.SetParent(page.transform, false);
            var track = bar.GetComponent<Image>();
            track.sprite = MuseumUiSprites.Rounded(2);
            track.type = Image.Type.Sliced;
            var quiet = textColor;
            quiet.a = .12f;
            track.color = quiet;
            track.raycastTarget = true;
            var rect = (RectTransform)bar.transform;
            var viewport = scroll.viewport;
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, .5f);
            rect.offsetMin = new Vector2(-4 - 14, viewport.offsetMin.y);
            rect.offsetMax = new Vector2(-14, viewport.offsetMax.y);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            handle.transform.SetParent(bar.transform, false);
            handle.sprite = MuseumUiSprites.Rounded(2);
            handle.type = Image.Type.Sliced;
            var tint = accentColor;
            tint.a = .7f;
            handle.color = tint;
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.one;
            handle.rectTransform.offsetMin = handle.rectTransform.offsetMax = Vector2.zero;
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            var colors = bar.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = colors.selectedColor = colors.pressedColor = Color.white;
            bar.colors = colors;
            // The line is not a stop for the keyboard: focus moves among the choices only.
            var navigation = bar.navigation;
            navigation.mode = Navigation.Mode.None;
            bar.navigation = navigation;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// <summary>The arrival card: the same rounded surface and faces as the menus, its one choice a quiet button.</summary>
        private void StyleWelcome(MuseumWelcomeView welcome)
        {
            if (welcome == null) return;
            if (welcome.TryGetComponent<Image>(out var card))
            {
                card.sprite = MuseumUiSprites.Rounded(CardRadius);
                card.type = Image.Type.Sliced;
                card.color = panelColor;
            }
            foreach (var binding in welcome.GetComponentsInChildren<MuseumMenuThemeBinding>(true))
                if (binding.ColorToken == MuseumMenuColorToken.Panel || binding.BindSelectableColors) Discard(binding);
            if (welcome.Title != null) { welcome.Title.color = textColor; welcome.Title.fontStyle |= FontStyles.UpperCase; welcome.Title.characterSpacing = 3; }
            if (welcome.Body != null) welcome.Body.color = textColor;
            if (welcome.Dismiss != null) Choice(welcome.Dismiss, true);
        }

        /// <summary>A thin line under the title whose light sweeps across while the museum's content is on its way.</summary>
        private void LoadingLine(MuseumMenuPageView page)
        {
            var content = page.Scroll != null ? page.Scroll.content : null;
            if (content == null || page.Description == null) return;
            var previous = content.Find("LoadingLine");
            if (previous != null) Discard(previous.gameObject);
            var line = new GameObject("LoadingLine", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            line.transform.SetParent(content, false);
            line.transform.SetSiblingIndex(page.Description.transform.GetSiblingIndex() + 1);
            var track = textColor;
            track.a = .14f;
            line.color = track;
            line.raycastTarget = false;
            line.GetComponent<LayoutElement>().preferredHeight = 2;
            line.GetComponent<LayoutElement>().minHeight = 2;
            loadingLine = line.rectTransform;
            var mask = line.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            var sweep = new GameObject("Sweep", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            sweep.transform.SetParent(line.transform, false);
            sweep.color = accentColor;
            sweep.raycastTarget = false;
            loadingSweep = sweep.rectTransform;
            loadingSweep.anchorMin = Vector2.zero;
            loadingSweep.anchorMax = new Vector2(0, 1);
            loadingSweep.pivot = new Vector2(0, .5f);
            loadingSweep.sizeDelta = new Vector2(90, 0);
            loadingLine.gameObject.SetActive(false);
        }

        /// <summary>A choice reads as text on the dark; the one in focus is marked by a short line and the accent colour.</summary>
        private void Choice(Selectable choice, bool primary = false)
        {
            if (choice.TryGetComponent<Image>(out var surface))
            {
                surface.sprite = MuseumUiSprites.Rounded(10);
                surface.type = Image.Type.Sliced;
                surface.color = Color.white;
            }
            var colors = choice.colors;
            var hover = accentColor;
            hover.a = .1f;
            var pressed = accentColor;
            pressed.a = .22f;
            var rest = accentColor;
            rest.a = primary ? .16f : 0;
            colors.normalColor = rest;
            colors.disabledColor = Color.clear;
            colors.highlightedColor = colors.selectedColor = hover;
            colors.pressedColor = pressed;
            colors.fadeDuration = MuseumMenuMotion.InteractionSeconds * .75f;
            choice.colors = colors;
            var look = new Look { Choice = choice };
            TMP_Text label = null, value = null;
            foreach (var text in choice.GetComponentsInChildren<TMP_Text>(true))
            {
                // The look owns the size and colour of a caption; an authored binding would put them back on enable.
                foreach (var binding in text.GetComponents<MuseumMenuThemeBinding>()) Discard(binding);
                if (text.name == "Value") value = text;
                else if (label == null) label = text;
            }
            if (label != null)
            {
                label.fontSize = ChoiceSize;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.characterSpacing = 2;
                // Every choice reads with the same firm weight, whichever button it was made from.
                label.fontStyle = (label.fontStyle | FontStyles.Bold) & ~FontStyles.UpperCase;
                label.margin = new Vector4(26, 0, value != null ? 200 : 0, 0);
                look.LabelLeft = 26;
            }
            if (value != null)
            {
                value.fontSize = ChoiceSize * .8f;
                value.alignment = TextAlignmentOptions.MidlineRight;
                value.fontStyle = FontStyles.Normal;
                value.color = mutedColor;
                value.margin = new Vector4(0, 0, 18, 0);
            }
            look.Label = label;
            look.Value = value;
            look.Marker = Marker(choice.transform);
            looks.Add(look);
            if (choice.GetComponent<MuseumMenuChoice>() == null) choice.gameObject.AddComponent<MuseumMenuChoice>();
        }

        /// <summary>One line of the controls page: the action on the left, its key on the right in the accent colour.</summary>
        private void ChoiceRow(Button button, MuseumRebindRow row)
        {
            if (button.TryGetComponent<Image>(out var surface))
            {
                surface.sprite = MuseumUiSprites.Rounded(10);
                surface.type = Image.Type.Sliced;
                surface.color = Color.white;
            }
            var colors = button.colors;
            var hover = accentColor;
            hover.a = .1f;
            colors.normalColor = new Color(1, 1, 1, .035f);
            colors.highlightedColor = colors.selectedColor = hover;
            var pressed = accentColor;
            pressed.a = .22f;
            colors.pressedColor = pressed;
            colors.disabledColor = Color.clear;
            colors.fadeDuration = MuseumMenuMotion.InteractionSeconds * .75f;
            button.colors = colors;
            if (row.Action != null) { row.Action.color = textColor; row.Action.fontStyle |= FontStyles.Bold; }
            if (row.Key != null) row.Key.color = accentColor;
            var look = new Look { Choice = button, Label = row.Action, LabelLeft = row.Action != null ? row.Action.margin.x : 0, Marker = Marker(button.transform) };
            looks.Add(look);
            if (button.GetComponent<MuseumMenuChoice>() == null) button.gameObject.AddComponent<MuseumMenuChoice>();
        }

        private Image Marker(Transform parent)
        {
            var marker = new GameObject("TitleMarker", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            marker.transform.SetParent(parent, false);
            var rect = marker.rectTransform;
            rect.anchorMin = new Vector2(0, .2f);
            rect.anchorMax = new Vector2(0, .8f);
            rect.pivot = new Vector2(0, .5f);
            rect.sizeDelta = new Vector2(0, 0);
            rect.anchoredPosition = Vector2.zero;
            marker.sprite = MuseumUiSprites.Rounded(2);
            marker.type = Image.Type.Sliced;
            marker.color = accentColor;
            marker.raycastTarget = false;
            marker.enabled = false;
            return marker;
        }

        /// <summary>
        /// A slider is a thin quiet line with the chosen part in the accent colour and a soft dot for a handle, and its
        /// value stands at the right end of its caption.
        /// </summary>
        private void StyleSlider(Slider slider)
        {
            if (slider == null) return;
            var colors = slider.colors;
            colors.normalColor = textColor;
            colors.highlightedColor = colors.selectedColor = accentColor;
            colors.pressedColor = Color.Lerp(accentColor, Color.white, .3f);
            colors.disabledColor = mutedColor;
            colors.fadeDuration = MuseumMenuMotion.InteractionSeconds * .75f;
            slider.colors = colors;
            var track = slider.transform.Find("Track") as RectTransform;
            if (track != null && track.TryGetComponent<Image>(out var trackImage))
            {
                var quiet = textColor;
                quiet.a = .2f;
                trackImage.color = quiet;
                trackImage.sprite = MuseumUiSprites.Rounded(2);
                trackImage.type = Image.Type.Sliced;
                track.sizeDelta = new Vector2(track.sizeDelta.x, 4);
                if (slider.fillRect == null)
                {
                    var area = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
                    area.SetParent(slider.transform, false);
                    area.SetSiblingIndex(track.GetSiblingIndex() + 1);
                    area.anchorMin = track.anchorMin; area.anchorMax = track.anchorMax;
                    area.pivot = track.pivot; area.anchoredPosition = track.anchoredPosition; area.sizeDelta = track.sizeDelta;
                    var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    fill.transform.SetParent(area, false);
                    fill.rectTransform.anchorMin = Vector2.zero;
                    fill.rectTransform.anchorMax = Vector2.one;
                    fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                    fill.sprite = MuseumUiSprites.Rounded(2);
                    fill.type = Image.Type.Sliced;
                    fill.color = accentColor;
                    fill.raycastTarget = false;
                    slider.fillRect = fill.rectTransform;
                }
            }
            var handle = slider.handleRect;
            if (handle != null)
            {
                handle.sizeDelta = new Vector2(22, 22);
                if (handle.TryGetComponent<Image>(out var dot))
                {
                    dot.sprite = MuseumUiSprites.Dot();
                    dot.type = Image.Type.Simple;
                    dot.preserveAspect = true;
                }
            }
            TMP_Text caption = null;
            foreach (var text in slider.GetComponentsInChildren<TMP_Text>(true))
                if (text.name != "Value") { caption = text; break; }
            if (caption == null) return;
            caption.color = textColor;
            var previous = slider.transform.Find("Value");
            if (previous != null) Discard(previous.gameObject);
            var value = Instantiate(caption, slider.transform);
            value.name = "Value";
            foreach (var binding in value.GetComponents<MuseumInterfaceLabel>()) Discard(binding);
            foreach (var binding in value.GetComponents<MuseumMenuThemeBinding>()) Discard(binding);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.color = mutedColor;
            value.fontSize = caption.fontSize * .9f;
            value.raycastTarget = false;
            var rect = value.rectTransform;
            var source = caption.rectTransform;
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
            rect.anchoredPosition = source.anchoredPosition; rect.sizeDelta = source.sizeDelta;
            Func<float, string> percent = amount => Mathf.RoundToInt(Mathf.InverseLerp(slider.minValue, slider.maxValue, amount) * 100) + " %";
            if (slider.name == "HeightOffset")
                percent = amount => MuseumInterfaceText.Format("height_value", MuseumComfort.Centimetres(amount));
            sliderValues[slider] = (value, percent);
            value.text = percent(slider.value);
            slider.onValueChanged.AddListener(amount =>
            {
                if (sliderValues.TryGetValue(slider, out var entry) && entry.value != null) entry.value.text = entry.format(amount);
            });
            var look = new Look { Choice = slider, Label = caption, LabelLeft = caption.margin.x, Marker = Marker(slider.transform), Value = value };
            looks.Add(look);
            if (slider.GetComponent<MuseumMenuChoice>() == null) slider.gameObject.AddComponent<MuseumMenuChoice>();
        }

        /// <summary>A full-screen layer behind the menu column, drawn from a horizontal gradient.</summary>
        private Image Layer(string name, Sprite gradient, Color color, int order)
        {
            var overlay = view.Overlay.transform;
            var previous = overlay.Find(name);
            if (previous != null) Discard(previous.gameObject);
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
            layer.gameObject.SetActive(false);
            return layer;
        }
    }

    /// <summary>
    /// A choice that takes the focus when the pointer, the mouse or the controller ray, comes over it, so hovering and
    /// keyboard focus are one and the same mark; the interface sounds a soft tick when the focus moves.
    /// </summary>
    public sealed class MuseumMenuChoice : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            var current = EventSystem.current;
            if (current == null || !TryGetComponent<Selectable>(out var choice) || !choice.IsInteractable()) return;
            var group = GetComponentInParent<CanvasGroup>();
            if (group != null && !group.interactable) return;
            if (current.currentSelectedGameObject == gameObject) return;
            current.SetSelectedGameObject(gameObject, eventData);
            if (Application.isPlaying) MuseumUiSounds.Current.Play(MuseumUiSound.Focus);
        }
    }
}
