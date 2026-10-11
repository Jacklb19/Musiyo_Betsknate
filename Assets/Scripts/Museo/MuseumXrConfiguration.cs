using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    [Preserve]
    public sealed class MuseumXrConfiguration
    {
        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("menu_distance")] public float MenuDistance;
        [JsonProperty("menu_width")] public float MenuWidth;
        [JsonProperty("ray_length")] public float RayLength;
        [JsonProperty("walk_speed")] public float WalkSpeed;
        [JsonProperty("snap_turn_degrees")] public float SnapTurnDegrees;
        [JsonProperty("device_height")] public float DeviceHeight;
        public static MuseumXrConfiguration Load() => Parse(Resources.Load<TextAsset>("MuseumXrConfiguration").text);
        public static MuseumXrConfiguration Parse(string json)
        {
            var value = JsonConvert.DeserializeObject<MuseumXrConfiguration>(json);
            if (value == null || value.SchemaVersion != 1 || !Range(value.MenuDistance, 1, 3)
                || !Range(value.MenuWidth, .8f, 2) || !Range(value.RayLength, 2, 10)
                || !Range(value.WalkSpeed, .2f, 2) || !Range(value.SnapTurnDegrees, 15, 45)
                || !Range(value.DeviceHeight, 1, 2)) throw new InvalidOperationException("Invalid museum XR configuration.");
            return value;
        }
        private static bool Range(float value, float min, float max)
            => MuseumExperienceConfiguration.Finite(value) && value >= min && value <= max;
    }
}
