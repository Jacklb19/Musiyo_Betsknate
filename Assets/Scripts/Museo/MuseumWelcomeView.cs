using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumWelcomeView : MonoBehaviour
    {
        public TMP_Text Title;
        public TMP_Text Body;
        public Button Dismiss;

        public void ValidateReferences()
        {
            if (Title == null || Body == null || Dismiss == null
                || !Title.transform.IsChildOf(transform) || !Body.transform.IsChildOf(transform)
                || !Dismiss.transform.IsChildOf(transform))
                throw new InvalidOperationException(name + ": welcome view needs local Title, Body and Dismiss references.");
        }
    }
}
