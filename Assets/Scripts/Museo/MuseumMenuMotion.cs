using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The motion of the menus (coordination/design-tokens-20261018.json: 160 ms for an interaction, 400 ms for a
    /// reveal). A page that opens breathes in from a little smaller and fades in, and its choices follow one after
    /// another, a few hundredths apart; nothing moves on a page that is already open. Everything runs on unscaled time,
    /// so it also plays while the visit is paused, and it never changes the authored layout: the scale ends at one.
    /// </summary>
    public sealed class MuseumMenuMotion : MonoBehaviour
    {
        public const float RevealSeconds = .4f;
        public const float InteractionSeconds = .16f;
        /// <summary>Delay between one choice and the next on the way in.</summary>
        public const float StaggerSeconds = .045f;
        /// <summary>Scale a page starts from while it appears; it settles at its authored size.</summary>
        public const float StartScale = .97f;
        private sealed class Reveal
        {
            public MuseumMenuPageView Page;
            public CanvasGroup Group;
            public RectTransform Rect;
            public readonly List<CanvasGroup> Items = new List<CanvasGroup>();
            public float Time;
            public bool Done;
        }
        private readonly Dictionary<MuseumMenuPageView, Reveal> reveals = new Dictionary<MuseumMenuPageView, Reveal>();
        private MuseumMenuView view;
        private MuseumMenuPageView shown;

        /// <summary>True while a reveal is still playing, so a test can wait for it.</summary>
        public bool Animating { get; private set; }

        public void Configure(MuseumMenuView menuView) => view = menuView;

        /// <summary>Starts the reveal of a page from its first frame; a page already shown keeps still.</summary>
        public void Show(MuseumMenuPageView page)
        {
            if (page == null || page == shown) return;
            shown = page;
            Begin(page);
        }

        /// <summary>Forgets the page shown, so the next opening of any page reveals it again.</summary>
        public void Hide() => shown = null;

        private void Begin(MuseumMenuPageView page)
        {
            if (!reveals.TryGetValue(page, out var reveal))
            {
                var group = page.GetComponent<CanvasGroup>();
                if (group == null) group = page.gameObject.AddComponent<CanvasGroup>();
                reveal = new Reveal { Page = page, Group = group };
                reveal.Rect = (RectTransform)page.transform;
                reveals[page] = reveal;
            }
            reveal.Items.Clear();
            foreach (var choice in page.GetComponentsInChildren<Selectable>(false))
            {
                // Rows of the controls page are many: they rise together as one, under the page.
                if (choice.GetComponentInParent<MuseumRebindRow>(true) != null) continue;
                var item = choice.GetComponent<CanvasGroup>();
                if (item == null) item = choice.gameObject.AddComponent<CanvasGroup>();
                item.ignoreParentGroups = false;
                reveal.Items.Add(item);
            }
            reveal.Time = 0;
            reveal.Done = false;
            Apply(reveal);
        }

        private void Update()
        {
            bool any = false;
            foreach (var reveal in reveals.Values)
            {
                if (reveal.Done || reveal.Page == null) continue;
                if (!reveal.Page.gameObject.activeInHierarchy) { Finish(reveal); continue; }
                reveal.Time += Time.unscaledDeltaTime;
                Apply(reveal);
                any = true;
            }
            Animating = any;
        }

        private void Apply(Reveal reveal)
        {
            float progress = Mathf.Clamp01(reveal.Time / RevealSeconds);
            float eased = 1 - Mathf.Pow(1 - progress, 3);
            reveal.Group.alpha = eased;
            // The layout owns the page's place and size; only its scale breathes in, and it ends exactly at one.
            if (reveal.Rect != null) reveal.Rect.localScale = Vector3.one * Mathf.Lerp(StartScale, 1, eased);
            for (int index = 0; index < reveal.Items.Count; index++)
            {
                var item = reveal.Items[index];
                if (item == null) continue;
                float own = Mathf.Clamp01((reveal.Time - index * StaggerSeconds) / (RevealSeconds * .7f));
                item.alpha = 1 - Mathf.Pow(1 - own, 2);
            }
            float last = RevealSeconds + reveal.Items.Count * StaggerSeconds;
            if (reveal.Time >= last) Finish(reveal);
        }

        private static void Finish(Reveal reveal)
        {
            reveal.Done = true;
            if (reveal.Group != null) reveal.Group.alpha = 1;
            if (reveal.Rect != null) reveal.Rect.localScale = Vector3.one;
            foreach (var item in reveal.Items) if (item != null) item.alpha = 1;
        }

        /// <summary>Ends every reveal at once, leaving the pages exactly as authored.</summary>
        public void Complete()
        {
            foreach (var reveal in reveals.Values) Finish(reveal);
            Animating = false;
        }
    }
}
