using System.Collections.Generic;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum MuseumTurnMode { Continuous, Snap }

    /// <summary>Right stick turning. It turns the visitor's play area around the head; head tracking itself is never altered.</summary>
    public static class MuseumTurning
    {
        public const float MinimumSpeed = 20;
        public const float MaximumSpeed = 240;
        /// <summary>A snap turn fires past this deflection and rearms once the stick returns inside the rest zone.</summary>
        public const float SnapThreshold = .7f;
        public const float SnapRest = .25f;

        public static MuseumTurnMode? ParseMode(string value)
            => value == "continuous" ? MuseumTurnMode.Continuous : value == "snap" ? MuseumTurnMode.Snap : (MuseumTurnMode?)null;

        public static string Name(MuseumTurnMode mode) => mode == MuseumTurnMode.Snap ? "snap" : "continuous";

        /// <summary>
        /// Degrees to turn during one frame. Nothing inside the dead zone, and nothing while the stick points mostly up or
        /// down, which scrolls a floating screen. Past the dead zone the rate grows smoothly from zero to the chosen speed.
        /// </summary>
        public static float ContinuousDegrees(Vector2 stick, float deadzone, float speed, float elapsed)
        {
            if (!MuseumExperienceConfiguration.Finite(stick.x) || !MuseumExperienceConfiguration.Finite(stick.y)
                || !MuseumExperienceConfiguration.Finite(elapsed) || elapsed <= 0) return 0;
            float sideways = Mathf.Abs(stick.x);
            if (sideways <= deadzone || sideways < Mathf.Abs(stick.y)) return 0;
            float amount = Mathf.Clamp01((sideways - deadzone) / (1 - deadzone));
            return Mathf.Sign(stick.x) * amount * Mathf.Clamp(speed, MinimumSpeed, MaximumSpeed) * elapsed;
        }
    }

    /// <summary>One turn per push of the stick: the stick must come back to rest before the next one.</summary>
    public sealed class SnapTurnGate
    {
        private bool armed;

        public void Disarm() => armed = false;

        public float Step(float sideways, float degrees)
        {
            if (!MuseumExperienceConfiguration.Finite(sideways)) return 0;
            if (Mathf.Abs(sideways) < MuseumTurning.SnapRest) armed = true;
            if (!armed || Mathf.Abs(sideways) <= MuseumTurning.SnapThreshold) return 0;
            armed = false;
            return Mathf.Sign(sideways) * degrees;
        }
    }

    /// <summary>
    /// Decides when a menu that opens is placed in front of the headset: once tracking is valid and the head has held
    /// still for a moment, or after a timeout. It is placed once per request and then stays where it is.
    /// </summary>
    public sealed class MenuPlacementGate
    {
        private float? lastYaw;
        private float stable;
        private float waited;
        public bool Pending { get; private set; }

        public void Request()
        {
            Pending = true;
            lastYaw = null;
            stable = waited = 0;
        }

        public void Cancel() => Pending = false;

        /// <returns>True on the one frame the menu should be placed.</returns>
        public bool Step(bool tracked, float yaw, float elapsed, float settleSeconds, float settleTurnSpeed, float timeout)
        {
            if (!Pending) return false;
            // Untracked frames say nothing about where the visitor looks, so they neither settle nor time out.
            if (!tracked || !MuseumExperienceConfiguration.Finite(yaw) || !MuseumExperienceConfiguration.Finite(elapsed))
            { lastYaw = null; stable = 0; return false; }
            elapsed = Mathf.Max(0, elapsed);
            waited += elapsed;
            if (lastYaw.HasValue)
            {
                float speed = elapsed > 0 ? Mathf.Abs(Mathf.DeltaAngle(lastYaw.Value, yaw)) / elapsed : 0;
                stable = speed <= settleTurnSpeed ? stable + elapsed : 0;
            }
            lastYaw = yaw;
            if (stable < settleSeconds && waited < timeout) return false;
            Pending = false;
            return true;
        }
    }

    /// <summary>Whether the visitor stands outside the museum, read from the delivered ground under the feet and the room anchors.</summary>
    public static class MuseumOutdoors
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static bool Contains(Vector3 feet, IReadOnlyList<RoomAnchor> rooms, ICollection<string> outdoorRooms,
            ICollection<string> outdoorGround, Transform visitor)
        {
            if (rooms != null)
                foreach (var room in rooms)
                    if (room != null && room.Contains(feet) && !outdoorRooms.Contains(room.Key)) return false;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * .5f, Vector3.down, Hits, 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Transform ground = null;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                var hit = Hits[index];
                if (visitor != null && hit.transform.IsChildOf(visitor) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.transform;
            }
            for (var item = ground; item != null; item = item.parent)
                if (outdoorGround.Contains(item.name)) return true;
            return false;
        }
    }
}
