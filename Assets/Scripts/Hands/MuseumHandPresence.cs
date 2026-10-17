using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Shows the visitor's hands on the headset. With the controllers put down each hand follows the visitor's own
    /// fingers (hand tracking); while a controller is held, a hand model wraps around it and its fingers close with the
    /// trigger and the grip. Only Android and the Editor compile this assembly, so the Web and Windows builds carry no
    /// hand tracking. Development builds report its state to the log every few seconds.
    /// </summary>
    public sealed class MuseumHandPresence : MonoBehaviour
    {
        private MuseumXrHost host;
        private XRHandSubsystem subsystem;
        private readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        private MuseumControllerHand leftHeld, rightHeld;
        private float nextReport;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Application.isMobilePlatform && !Application.isEditor) return;
            var holder = new GameObject("MuseumHandPresence");
            DontDestroyOnLoad(holder);
            holder.AddComponent<MuseumHandPresence>();
        }

        private void Update()
        {
            if (host == null)
            {
                host = FindAnyObjectByType<MuseumXrHost>();
                if (host == null || !host.Ready) { host = null; return; }
                var assets = Resources.Load<MuseumHandAssets>(MuseumHandAssets.ResourceName);
                if (assets == null) { Debug.LogWarning("MuseumHands: no hand assets; hands stay hidden."); enabled = false; return; }
                if (assets.LeftTracked != null) Instantiate(assets.LeftTracked, host.TrackingOrigin, false).name = "LeftHandTracked";
                if (assets.RightTracked != null) Instantiate(assets.RightTracked, host.TrackingOrigin, false).name = "RightHandTracked";
                leftHeld = MuseumControllerHand.Create(assets.LeftTracked, false, host.TrackingOrigin);
                rightHeld = MuseumControllerHand.Create(assets.RightTracked, true, host.TrackingOrigin);
            }
            if (subsystem == null || !subsystem.running)
            {
                SubsystemManager.GetSubsystems(subsystems);
                subsystem = subsystems.Count > 0 ? subsystems[0] : null;
            }
            var input = MuseumInput.Current;
            bool tracking = subsystem != null && subsystem.running;
            bool leftHand = tracking && subsystem.leftHand.isTracked, rightHand = tracking && subsystem.rightHand.isTracked;
            bool leftController = !leftHand && input.LeftTracked.IsPressed(), rightController = !rightHand && input.RightTracked.IsPressed();
            Hold(leftHeld, leftController, input.LeftPosition.ReadValue<Vector3>(), input.LeftRotation.ReadValue<Quaternion>(), XRNode.LeftHand);
            Hold(rightHeld, rightController, input.RightPosition.ReadValue<Vector3>(), input.RightRotation.ReadValue<Quaternion>(), XRNode.RightHand);
            if (Debug.isDebugBuild && Time.unscaledTime >= nextReport)
            {
                nextReport = Time.unscaledTime + 5;
                Debug.Log($"MuseumHands tracking={tracking} handLeft={leftHand} handRight={rightHand} "
                    + $"controllerLeft={leftController} controllerRight={rightController} frame={Time.unscaledDeltaTime * 1000:F1}ms");
            }
        }

        private static void Hold(MuseumControllerHand hand, bool shown, Vector3 position, Quaternion rotation, XRNode node)
        {
            if (hand == null) return;
            if (hand.gameObject.activeSelf != shown) hand.gameObject.SetActive(shown);
            if (!shown) return;
            hand.Place(position, rotation);
            var device = InputDevices.GetDeviceAtXRNode(node);
            float trigger = 0, grip = 0;
            if (device.isValid)
            {
                device.TryGetFeatureValue(CommonUsages.trigger, out trigger);
                device.TryGetFeatureValue(CommonUsages.grip, out grip);
            }
            hand.Pose(trigger, grip);
        }
    }
}
