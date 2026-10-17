using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    [Preserve]
    public sealed class MuseumXrConfiguration
    {
        public const int Version = 2;
        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("menu_distance")] public float MenuDistance;
        [JsonProperty("menu_width")] public float MenuWidth;
        /// <summary>Seconds the head must hold still before a menu that opens is placed in front of it.</summary>
        [JsonProperty("menu_settle_seconds")] public float MenuSettleSeconds;
        /// <summary>Head turning slower than this, in degrees per second, counts as holding still.</summary>
        [JsonProperty("menu_settle_turn_speed")] public float MenuSettleTurnSpeed;
        /// <summary>A head that never holds still still gets its menu after this many tracked seconds.</summary>
        [JsonProperty("menu_placement_timeout")] public float MenuPlacementTimeout;
        [JsonProperty("ray_length")] public float RayLength;
        [JsonProperty("walk_speed")] public float WalkSpeed;
        /// <summary>Speed outside the museum while running is switched on. Inside the museum the visitor always walks.</summary>
        [JsonProperty("run_speed")] public float RunSpeed;
        /// <summary>How fast walking and running blend into each other, in metres per second squared.</summary>
        [JsonProperty("run_acceleration")] public float RunAcceleration;
        /// <summary>Ground objects of the delivered museum that lie outdoors.</summary>
        [JsonProperty("outdoor_ground_objects")] public string[] OutdoorGroundObjects;
        /// <summary>Room anchors that are open air, such as the arrival path; every other room is indoors.</summary>
        [JsonProperty("outdoor_rooms")] public string[] OutdoorRooms;
        /// <summary>"continuous" or "snap": how the right stick turns when the visitor has not chosen.</summary>
        [JsonProperty("default_turn_mode")] public string DefaultTurnMode;
        [JsonProperty("snap_turn_degrees")] public float SnapTurnDegrees;
        /// <summary>Speeds, in degrees per second, the visitor can choose for continuous turning.</summary>
        [JsonProperty("continuous_turn_speeds")] public float[] ContinuousTurnSpeeds;
        [JsonProperty("default_continuous_turn_speed")] public float DefaultContinuousTurnSpeed;
        /// <summary>Stick deflection, from 0 to 1, below which continuous turning does nothing.</summary>
        [JsonProperty("turn_deadzone")] public float TurnDeadzone;
        /// <summary>Extra distance around a published piece within which the hand can take it directly.</summary>
        [JsonProperty("grab_reach")] public float GrabReach;
        /// <summary>A piece carried further than this from its pedestal goes back to it.</summary>
        [JsonProperty("grab_max_distance")] public float GrabMaxDistance;
        /// <summary>A piece let go closer than this to its exhibition pose goes back onto its pedestal.</summary>
        [JsonProperty("grab_snap_distance")] public float GrabSnapDistance;
        /// <summary>A piece left floating goes back to its pedestal once the visitor's head is further than this from it.</summary>
        [JsonProperty("grab_rest_return_distance")] public float GrabRestReturnDistance;
        /// <summary>Degrees per second the right stick turns a held piece at full deflection.</summary>
        [JsonProperty("grab_stick_turn_speed")] public float GrabStickTurnSpeed;
        [JsonProperty("device_height")] public float DeviceHeight;
        private static MuseumXrConfiguration current;

        public MuseumTurnMode DefaultTurn => MuseumTurning.ParseMode(DefaultTurnMode) ?? MuseumTurnMode.Continuous;

        /// <summary>Shipped settings, parsed once.</summary>
        public static MuseumXrConfiguration Current => current ??= Load();
        public static MuseumXrConfiguration Load() => Parse(Resources.Load<TextAsset>("MuseumXrConfiguration").text);
        public static MuseumXrConfiguration Parse(string json)
        {
            var value = JsonConvert.DeserializeObject<MuseumXrConfiguration>(json);
            if (value == null || value.SchemaVersion != Version || !Range(value.MenuDistance, 1, 3)
                || !Range(value.MenuWidth, .8f, 2) || !Range(value.RayLength, 2, 10)
                || !Range(value.MenuSettleSeconds, 0, 1) || !Range(value.MenuSettleTurnSpeed, 5, 360)
                || !Range(value.MenuPlacementTimeout, Mathf.Max(.1f, value.MenuSettleSeconds), 5)
                || !Range(value.WalkSpeed, .2f, 2) || !Range(value.RunSpeed, value.WalkSpeed, 4)
                || !Range(value.RunAcceleration, .5f, 10)
                || value.OutdoorGroundObjects == null || value.OutdoorGroundObjects.Any(string.IsNullOrWhiteSpace)
                || value.OutdoorRooms == null || value.OutdoorRooms.Any(string.IsNullOrWhiteSpace)
                || MuseumTurning.ParseMode(value.DefaultTurnMode) == null
                || !Range(value.SnapTurnDegrees, 15, 90)
                || value.ContinuousTurnSpeeds == null || value.ContinuousTurnSpeeds.Length == 0
                || value.ContinuousTurnSpeeds.Any(speed => !Range(speed, MuseumTurning.MinimumSpeed, MuseumTurning.MaximumSpeed))
                || !value.ContinuousTurnSpeeds.Contains(value.DefaultContinuousTurnSpeed)
                || !Range(value.TurnDeadzone, .05f, .6f)
                || !Range(value.GrabReach, 0, .5f) || !Range(value.GrabMaxDistance, .5f, 4)
                || !Range(value.GrabSnapDistance, .05f, .5f) || !Range(value.GrabRestReturnDistance, 1, 6)
                || !Range(value.GrabStickTurnSpeed, 30, 360)
                || !Range(value.DeviceHeight, 1, 2)) throw new InvalidOperationException("Invalid museum XR configuration.");
            return value;
        }

        /// <summary>The visitor's saved speed when it is one of the offered speeds, otherwise the default one.</summary>
        public float TurnSpeed(float saved) => ContinuousTurnSpeeds.Contains(saved) ? saved : DefaultContinuousTurnSpeed;

        /// <summary>The next offered speed after the current one, wrapping around.</summary>
        public float NextTurnSpeed(float saved)
        {
            int index = Array.IndexOf(ContinuousTurnSpeeds, TurnSpeed(saved));
            return ContinuousTurnSpeeds[(index + 1) % ContinuousTurnSpeeds.Length];
        }

        private static bool Range(float value, float min, float max)
            => MuseumExperienceConfiguration.Finite(value) && value >= min && value <= max;
    }
}
