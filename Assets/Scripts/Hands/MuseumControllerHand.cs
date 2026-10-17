using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The visitor's hand drawn around a controller: a copy of the tracked hand model, taken over from hand tracking and
    /// held at the controller, whose index finger closes with the trigger and whose other fingers close with the grip.
    /// </summary>
    public sealed class MuseumControllerHand : MonoBehaviour
    {
        /// <summary>Degrees each joint of a finger bends when its control is fully pressed.</summary>
        public const float IndexCurl = 62, GripCurl = 72, ThumbCurl = 28;
        /// <summary>How far the fingers stay closed around the controller with nothing pressed.</summary>
        public const float Rest = .3f;
        /// <summary>
        /// Where the wrist of a right hand sits from the controller's pointer, in the pointer's frame: behind it, a little
        /// below and to the outside, so the palm closes on the handle. A left hand mirrors it.
        /// </summary>
        public static readonly Vector3 WristFromPointer = new Vector3(.03f, -.025f, -.11f);
        /// <summary>Roll of the hand around the handle: the back of the hand turns outward, as when holding a controller.</summary>
        public const float Roll = 70;
        private readonly List<(Transform joint, Quaternion rest, Vector3 axis, int finger)> joints = new List<(Transform, Quaternion, Vector3, int)>();
        private Transform wrist;
        private Quaternion turn;
        private bool right;

        /// <summary>0 index, 1 thumb, 2 the other fingers.</summary>
        private static readonly (XRHandJointID first, XRHandJointID last, int finger)[] Fingers =
        {
            (XRHandJointID.IndexProximal, XRHandJointID.IndexDistal, 0),
            (XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, 1),
            (XRHandJointID.MiddleProximal, XRHandJointID.MiddleDistal, 2),
            (XRHandJointID.RingProximal, XRHandJointID.RingDistal, 2),
            (XRHandJointID.LittleProximal, XRHandJointID.LittleDistal, 2),
        };

        /// <summary>A copy of a tracked hand prefab under <paramref name="parent"/>, ready to be held; null when it has no skeleton.</summary>
        public static MuseumControllerHand Create(GameObject prefab, bool right, Transform parent)
        {
            if (prefab == null) return null;
            var item = Instantiate(prefab, parent, false);
            item.name = right ? "RightHandOnController" : "LeftHandOnController";
            var hand = item.AddComponent<MuseumControllerHand>();
            if (hand.Take(right)) return hand;
            Destroy(item);
            return null;
        }

        /// <summary>
        /// Takes the hand model over from hand tracking and measures, in its rest pose, the axis each finger joint bends
        /// about and the hand's own frame. False when the model has no skeleton.
        /// </summary>
        private bool Take(bool rightHand)
        {
            right = rightHand;
            var driver = GetComponentInChildren<XRHandSkeletonDriver>(true);
            if (driver == null) return false;
            var references = new Dictionary<XRHandJointID, Transform>();
            foreach (var reference in driver.jointTransformReferences)
                if (reference.jointTransform != null) references[reference.xrHandJointID] = reference.jointTransform;
            // The model no longer follows tracked fingers; the controller places it.
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != this && behaviour.GetType().Namespace != null && behaviour.GetType().Namespace.StartsWith("UnityEngine.XR.Hands"))
                    behaviour.enabled = false;
            // Tracking shows the hand mesh only while the hand is tracked; held at a controller it is always shown.
            foreach (var mesh in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                mesh.gameObject.SetActive(true);
                mesh.enabled = true;
                mesh.updateWhenOffscreen = true;
            }
            if (!references.TryGetValue(XRHandJointID.Wrist, out wrist) || !references.TryGetValue(XRHandJointID.IndexProximal, out var index)
                || !references.TryGetValue(XRHandJointID.MiddleProximal, out var middle)
                || !references.TryGetValue(XRHandJointID.LittleProximal, out var little)) return false;
            // Across the knuckles and along the hand: for a right hand this points out of the back of the hand, for a
            // left hand out of the palm.
            var cross = Vector3.Cross(index.position - wrist.position, little.position - wrist.position).normalized;
            var back = rightHand ? cross : -cross;
            foreach (var (first, last, finger) in Fingers)
            {
                for (var id = first; id <= last; id++)
                {
                    if (!references.TryGetValue(id, out var joint) || !references.TryGetValue(id + 1, out var next)) continue;
                    var along = (next.position - joint.position).normalized;
                    // Turning about this axis brings the finger toward the palm.
                    var across = Vector3.Cross(along, -back).normalized;
                    if (across.sqrMagnitude < .5f) continue;
                    joints.Add((joint, joint.localRotation, Quaternion.Inverse(joint.rotation) * across, finger));
                }
            }
            // The hand's own frame in its rest pose: fingers forward, the back of the hand up. A held controller has the
            // fingers along its pointer and the back of the hand up, rolled slightly inward.
            var handFrame = Quaternion.LookRotation((middle.position - wrist.position).normalized, back);
            turn = Quaternion.Euler(0, 0, rightHand ? -Roll : Roll) * Quaternion.Inverse(handFrame) * transform.rotation;
            return joints.Count > 0;
        }

        /// <summary>Holds the hand at a controller pose given in its parent's space, wrist behind the pointer.</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            transform.localRotation = rotation * turn;
            transform.localPosition = Vector3.zero;
            var parent = transform.parent;
            var wristOffset = parent != null ? parent.InverseTransformPoint(wrist.position) : wrist.position;
            var offset = right ? WristFromPointer : new Vector3(-WristFromPointer.x, WristFromPointer.y, WristFromPointer.z);
            transform.localPosition = position + rotation * offset - wristOffset;
        }

        /// <summary>Closes the fingers for the trigger and grip values of the controller, from 0 to 1.</summary>
        public void Pose(float trigger, float grip)
        {
            foreach (var (joint, rest, axis, finger) in joints)
            {
                float amount = finger == 0 ? Mathf.Lerp(Rest, 1, trigger) * IndexCurl
                    : finger == 1 ? Mathf.Lerp(Rest, 1, grip) * ThumbCurl : Mathf.Lerp(Rest, 1, grip) * GripCurl;
                joint.localRotation = rest * Quaternion.AngleAxis(amount, axis);
            }
        }
    }
}
