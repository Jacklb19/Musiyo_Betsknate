using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The visit has no permanent screen bars. The old title header (title, status, pause and retry) and the key list
    /// stay hidden: pausing, retrying, orientation and the list of keys live in the menus, and the screen only shows
    /// contextual prompts when something calls for them.
    /// </summary>
    public sealed class MuseumHudPresence : MonoBehaviour
    {
        private GameObject header;
        private GameObject controls;

        /// <summary>True while either bar is shown; it never is during a visit.</summary>
        public bool BarsVisible => header != null && header.activeSelf || controls != null && controls.activeSelf;

        public void Configure(MuseumVisitFlow visit, MuseumInteraction state, TourLoader tour, RectTransform headerBar, RectTransform controlBar)
        {
            header = headerBar != null ? headerBar.gameObject : null;
            controls = controlBar != null ? controlBar.gameObject : null;
            Hide();
        }

        /// <summary>Another component may reactivate a bar while it rebuilds the interface; it goes away again at once.</summary>
        private void LateUpdate() => Hide();

        private void Hide()
        {
            if (header != null && header.activeSelf) header.SetActive(false);
            if (controls != null && controls.activeSelf) controls.SetActive(false);
        }
    }
}
