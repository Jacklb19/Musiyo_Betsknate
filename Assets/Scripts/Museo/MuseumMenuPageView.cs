using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public enum MuseumMenuPageKind { Main, Pause, Settings, Controls }

    /// <summary>Serialized presentation references. Layout and navigation order belong to the prefab.</summary>
    public sealed class MuseumMenuPageView : MonoBehaviour
    {
        public MuseumMenuPageKind Kind;
        public TMP_Text Title;
        public TMP_Text Description;
        public Button Primary;
        public Button Settings;
        public Button Controls;
        public Button MainMenu;
        public Button Retry;
        public Button Catalog;
        public Button Back;
        public Button Reset;
        public Button Subtitles;
        public Slider Sensitivity;
        public Slider Volume;
        public TMP_Text Instructions;
        public ScrollRect Scroll;

        public void ValidateReferences(MuseumMenuPageKind expected)
        {
            if (Kind != expected) throw new InvalidOperationException(name + ": unexpected menu page kind.");
            Require(Title, nameof(Title)); Require(Description, nameof(Description)); Require(Scroll, nameof(Scroll));
            Require(Scroll.viewport, "Scroll.viewport"); Require(Scroll.content, "Scroll.content");
            if (Kind == MuseumMenuPageKind.Main || Kind == MuseumMenuPageKind.Pause)
            {
                Require(Primary, nameof(Primary)); Require(Settings, nameof(Settings));
                Require(Controls, nameof(Controls)); Require(Retry, nameof(Retry)); Require(Catalog, nameof(Catalog));
                if (Kind == MuseumMenuPageKind.Pause) Require(MainMenu, nameof(MainMenu));
            }
            else
            {
                Require(Back, nameof(Back));
                if (Kind == MuseumMenuPageKind.Settings)
                {
                    Require(Sensitivity, nameof(Sensitivity)); Require(Volume, nameof(Volume));
                    Require(Subtitles, nameof(Subtitles)); Require(Reset, nameof(Reset));
                }
                else Require(Instructions, nameof(Instructions));
            }
            foreach (var button in GetComponentsInChildren<Button>(true))
                if (button.GetComponentInChildren<TMP_Text>(true) == null)
                    throw new InvalidOperationException(name + ": button " + button.name + " has no text label.");
        }

        private void Require(Component value, string field)
        {
            if (value == null || !value.transform.IsChildOf(transform))
                throw new InvalidOperationException(name + ": missing or external menu reference " + field + ".");
        }
    }
}
