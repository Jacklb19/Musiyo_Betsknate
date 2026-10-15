using UnityEngine;
using UnityEngine.EventSystems;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Tells the reading follower when the visitor drags or wheels the panel themselves.</summary>
    public sealed class ReadingScrollWatcher : MonoBehaviour, IBeginDragHandler, IScrollHandler
    {
        public MuseumNarrationReading Reading { get; set; }
        public void OnBeginDrag(PointerEventData eventData) => Reading?.Suspend();
        public void OnScroll(PointerEventData eventData) => Reading?.Suspend();
    }
}
