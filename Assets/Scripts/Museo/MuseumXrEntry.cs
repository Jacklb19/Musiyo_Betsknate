using UnityEngine;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The entry of the visit on a headset. While the entry menu is open the visitor stands in the scenic set of the entry
    /// (the hearth in the dark), facing it as the desktop camera does; choosing to explore darkens the view with a veil
    /// held just in front of the eyes, carries the visitor to where the visit begins, and clears the view again. A screen
    /// fade cannot do this on a headset, so the veil is part of the world, drawn before everything else in sight.
    /// </summary>
    public sealed class MuseumXrEntry : MonoBehaviour
    {
        /// <summary>Distance of the veil in front of the eyes; within the near reach of every view, beyond the near plane.</summary>
        public const float VeilDistance = .18f;
        private MuseumVisitFlow flow;
        private MuseumEntryBackdrop backdrop;
        private Transform visitor;
        private Transform head;
        private Camera view;
        private Canvas veil;
        private Image veilImage;
        private Vector3 museumPosition;
        private Quaternion museumRotation;
        private bool inSet;
        private bool placed;
        private System.Func<bool> headTracked;

        /// <summary>True while the visitor stands in the entry set instead of the museum.</summary>
        public bool InSet => inSet;
        /// <summary>0 clear, 1 completely dark.</summary>
        public float Veil => veilImage != null && veil.enabled ? veilImage.color.a : 0;

        public void Configure(MuseumVisitFlow visitFlow, MuseumEntryBackdrop entryBackdrop, Transform visitorRoot, Camera headCamera,
            System.Func<bool> tracked)
        {
            headTracked = tracked;
            flow = visitFlow;
            backdrop = entryBackdrop;
            visitor = visitorRoot;
            view = headCamera;
            head = headCamera.transform;
            var holder = new GameObject("EntryVeil", typeof(RectTransform), typeof(Canvas));
            holder.transform.SetParent(head, false);
            holder.transform.localPosition = Vector3.forward * VeilDistance;
            veil = holder.GetComponent<Canvas>();
            veil.renderMode = RenderMode.WorldSpace;
            veil.sortingOrder = short.MaxValue;
            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(100, 100);
            // A square of two metres at this distance covers far more than any headset's field of view.
            rect.localScale = Vector3.one * .02f;
            var image = new GameObject("Dark", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(holder.transform, false);
            var imageRect = (RectTransform)image.transform;
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
            veilImage = image.GetComponent<Image>();
            veilImage.raycastTarget = false;
            veilImage.color = new Color(0, 0, 0, 0);
            veil.enabled = false;
        }

        /// <summary>
        /// Moves between the set and the museum for the current moment of the flow. True when the visitor was moved, so an
        /// open menu is placed again in front of them. The spatial host calls it every frame.
        /// </summary>
        public bool Step()
        {
            if (flow == null) return false;
            bool moved = false;
            bool scenic = backdrop != null && backdrop.Active;
            float share = MuseumExperienceConfiguration.Current.EntryMenuFadeShare;
            bool menu = flow.Phase == VisitPhase.Menu;
            bool leaving = flow.Phase == VisitPhase.Entering && flow.TransitionProgress < share;
            if (scenic && (menu || leaving)) moved = EnterSet();
            else moved = LeaveSet();
            float dark = 0;
            if (flow.Phase == VisitPhase.Entering)
            {
                float progress = flow.TransitionProgress;
                dark = progress < share ? Mathf.SmoothStep(0, 1, progress / share) : Mathf.SmoothStep(1, 0, (progress - share) / (1 - share));
            }
            veilImage.color = new Color(0, 0, 0, dark);
            veil.enabled = dark > .001f;
            return moved;
        }

        /// <summary>
        /// The visitor stands where the desktop camera of the set stands and faces the way it looks, so the menu opens on
        /// the left of the fire and the fire burns on the right. This happens once, as soon as the head is tracked: the set
        /// never turns with the head afterwards.
        /// </summary>
        private bool EnterSet()
        {
            if (!inSet)
            {
                museumPosition = visitor.position;
                museumRotation = visitor.rotation;
                inSet = true;
                placed = false;
                backdrop.Show(true, view);
            }
            if (placed || headTracked != null && !headTracked()) return false;
            placed = true;
            var pose = backdrop.CameraPose;
            float yaw = pose.eulerAngles.y;
            var feet = new Vector3(pose.position.x, MuseumEntryBackdrop.Origin.y, pose.position.z);
            // The head may stand anywhere in the play area: the set is placed under the head, not under the origin.
            var headOffset = head.position - visitor.position;
            headOffset.y = 0;
            var rotation = Quaternion.Euler(0, yaw - (head.eulerAngles.y - visitor.eulerAngles.y), 0);
            var target = feet - rotation * Quaternion.Inverse(visitor.rotation) * headOffset;
            visitor.SetPositionAndRotation(target, rotation);
            Physics.SyncTransforms();
            return true;
        }

        private bool LeaveSet()
        {
            if (!inSet) return false;
            inSet = false;
            placed = false;
            backdrop?.Show(false, view);
            visitor.SetPositionAndRotation(museumPosition, museumRotation);
            Physics.SyncTransforms();
            return true;
        }

        private void OnDestroy()
        {
            LeaveSet();
            if (veil != null) Destroy(veil.gameObject);
        }
    }
}
