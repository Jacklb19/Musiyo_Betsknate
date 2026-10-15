using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Sky, distance haze, daylight and ambient of the visit, read from <c>Resources/MuseumAtmosphere.json</c> and applied
    /// when the visit starts, so the saved scene stays as delivered. It changes how the museum is lit, never what it holds.
    /// </summary>
    [Preserve]
    public sealed class MuseumAtmosphere
    {
        public const string ResourceName = "MuseumAtmosphere";
        public const string SkyMaterial = "MuseumSky";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("fog_color")] public string FogColor;
        [JsonProperty("fog_start")] public float FogStart;
        [JsonProperty("fog_end")] public float FogEnd;
        [JsonProperty("sun_color")] public string SunColor;
        [JsonProperty("sun_intensity")] public float SunIntensity;
        /// <summary>Sun shadows on screens (none, hard or soft); the render pipeline asset of each platform sets their quality.</summary>
        [JsonProperty("sun_shadows")] public string SunShadows = "none";
        /// <summary>Sun shadows in a headset, kept apart until they are measured on the device.</summary>
        [JsonProperty("sun_shadows_headset")] public string SunShadowsHeadset = "none";
        [JsonProperty("shadow_strength")] public float ShadowStrength = 1;
        [JsonProperty("ambient_sky")] public string AmbientSky;
        [JsonProperty("ambient_equator")] public string AmbientEquator;
        [JsonProperty("ambient_ground")] public string AmbientGround;

        private static MuseumAtmosphere current;
        public static MuseumAtmosphere Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing museum atmosphere.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumAtmosphere Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumAtmosphere>(json);
            if (result == null || result.SchemaVersion != 1 || !MuseumExperienceConfiguration.Finite(result.FogStart)
                || !MuseumExperienceConfiguration.Finite(result.FogEnd) || result.FogStart < 0 || result.FogEnd <= result.FogStart
                || !MuseumExperienceConfiguration.Finite(result.SunIntensity) || result.SunIntensity < 0)
                throw new InvalidOperationException("Invalid museum atmosphere.");
            foreach (var color in new[] { result.FogColor, result.SunColor, result.AmbientSky, result.AmbientEquator, result.AmbientGround })
                MuseumExperienceConfiguration.ColorValue(color);
            Shadows(result.SunShadows); Shadows(result.SunShadowsHeadset);
            if (!MuseumExperienceConfiguration.Finite(result.ShadowStrength) || result.ShadowStrength < 0 || result.ShadowStrength > 1)
                throw new InvalidOperationException("Invalid museum shadow strength.");
            return result;
        }

        public static LightShadows Shadows(string value)
        {
            switch (value)
            {
                case "none": return LightShadows.None;
                case "hard": return LightShadows.Hard;
                case "soft": return LightShadows.Soft;
                default: throw new InvalidOperationException("Unknown shadow setting: " + value);
            }
        }

        /// <summary>Applies the atmosphere to the open scene; the sun is the scene's brightest directional light.</summary>
        public void Apply()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = MuseumExperienceConfiguration.ColorValue(FogColor);
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = MuseumExperienceConfiguration.ColorValue(AmbientSky);
            RenderSettings.ambientEquatorColor = MuseumExperienceConfiguration.ColorValue(AmbientEquator);
            RenderSettings.ambientGroundColor = MuseumExperienceConfiguration.ColorValue(AmbientGround);
            var sky = Resources.Load<Material>(SkyMaterial);
            if (sky != null) RenderSettings.skybox = sky;
            Light sun = null;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && (sun == null || light.intensity > sun.intensity)) sun = light;
            if (sun == null) return;
            sun.color = MuseumExperienceConfiguration.ColorValue(SunColor);
            sun.intensity = SunIntensity;
            sun.shadows = Shadows(Application.platform == RuntimePlatform.Android ? SunShadowsHeadset : SunShadows);
            sun.shadowStrength = ShadowStrength;
            RenderSettings.sun = sun;
        }
    }
}
