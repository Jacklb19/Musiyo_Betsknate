using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    [RequireComponent(typeof(TMP_Text))]
    public sealed class MuseumInterfaceLabel : MonoBehaviour
    {
        [Tooltip("Neutral text key in MuseumInterfaceText_es.json. Published content is supplied by the controller.")]
        public string TextKey;
        private void OnEnable() => RefreshText();
        public void RefreshText() => GetComponent<TMP_Text>().text = MuseumInterfaceText.Get(TextKey);
    }
}
