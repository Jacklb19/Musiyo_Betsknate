using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumMenuView : MonoBehaviour
    {
        public const string ResourceName = "MuseumMenuRoot";
        public CanvasGroup Overlay;
        public CanvasGroup Transition;
        public MuseumMenuPageView Main;
        public MuseumMenuPageView Pause;
        public MuseumMenuPageView Settings;
        public MuseumMenuPageView Controls;
        public MuseumWelcomeView Welcome;

        public void ValidateReferences()
        {
            if (Overlay == null || Transition == null || Main == null || Pause == null
                || Settings == null || Controls == null || Welcome == null)
                throw new InvalidOperationException(name + ": incomplete menu prefab. Check overlay, transition and all page references.");
            if (GetComponentsInChildren<Canvas>(true).Length != 0 || GetComponentsInChildren<EventSystem>(true).Length != 0
                || GetComponentsInChildren<BaseInputModule>(true).Length != 0)
                throw new InvalidOperationException(name + ": menu prefabs must reuse the HUD Canvas and EventSystem.");
            foreach (var page in new[] { Main, Pause, Settings, Controls })
                if (!page.transform.IsChildOf(Overlay.transform))
                    throw new InvalidOperationException(name + ": menu page must be inside Overlay.");
            if (!Overlay.transform.IsChildOf(transform) || !Transition.transform.IsChildOf(transform)
                || !Welcome.transform.IsChildOf(transform))
                throw new InvalidOperationException(name + ": menu references must belong to this prefab.");
            Main.ValidateReferences(MuseumMenuPageKind.Main);
            Pause.ValidateReferences(MuseumMenuPageKind.Pause);
            Settings.ValidateReferences(MuseumMenuPageKind.Settings);
            Controls.ValidateReferences(MuseumMenuPageKind.Controls);
            Welcome.ValidateReferences();
        }
    }
}
