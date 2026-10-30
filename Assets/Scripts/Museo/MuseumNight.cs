using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The night of the visit, read from <c>Resources/MuseumNight.json</c>: a starry sky with a faint cold moon where the
    /// sun shone, night fog and ambient, the lanterns lit and a warmer ambient inside the roofed rooms. Nothing is baked;
    /// the day's own values (<see cref="MuseumAtmosphere"/>) are what the museum returns to.
    /// </summary>
    [Preserve]
    public sealed class MuseumNightAtmosphere
    {
        public const string ResourceName = "MuseumNight";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("fog_color")] public string FogColor;
        [JsonProperty("fog_start")] public float FogStart;
        [JsonProperty("fog_end")] public float FogEnd;
        /// <summary>The museum's sun takes this colour and strength at night: the moon's light.</summary>
        [JsonProperty("moon_color")] public string MoonColor;
        [JsonProperty("moon_intensity")] public float MoonIntensity;
        /// <summary>Width of the moon's disc drawn in the night sky, in degrees.</summary>
        [JsonProperty("moon_size_degrees")] public float MoonSize = 2;
        /// <summary>Where the moon stands: degrees from north (+z) towards east (+x), and above the horizon.</summary>
        [JsonProperty("moon_azimuth_degrees")] public float MoonAzimuth = 270;
        [JsonProperty("moon_elevation_degrees")] public float MoonElevation = 24;
        [JsonProperty("moon_disc_color")] public string MoonDiscColor = "#E8EDF7";
        [JsonProperty("moon_halo_color")] public string MoonHaloColor = "#25314F";
        /// <summary>How far the moon's faint halo reaches, in moon radii.</summary>
        [JsonProperty("moon_halo_size")] public float MoonHaloSize = 10;
        /// <summary>Still water where the moon lays a soft streak of light at night, by its centre on the ground.</summary>
        [JsonProperty("moon_glints")] public MoonGlint[] MoonGlints = Array.Empty<MoonGlint>();
        [JsonProperty("moon_glint_color")] public string MoonGlintColor = "#B8C8E8";
        [JsonProperty("moon_glint_alpha")] public float MoonGlintAlpha = .3f;
        [JsonProperty("moon_glint_length")] public float MoonGlintLength = 4;
        [JsonProperty("moon_glint_width")] public float MoonGlintWidth = 1.2f;

        [Preserve]
        public sealed class MoonGlint
        {
            [JsonProperty("x")] public float X;
            [JsonProperty("z")] public float Z;
        }
        [JsonProperty("sky_exposure")] public float SkyExposure = 1;
        /// <summary>Share of the day's sky reflection left on glossy surfaces at night.</summary>
        [JsonProperty("reflection_intensity")] public float ReflectionIntensity = .25f;
        [JsonProperty("ambient_sky")] public string AmbientSky;
        [JsonProperty("ambient_equator")] public string AmbientEquator;
        [JsonProperty("ambient_ground")] public string AmbientGround;
        /// <summary>Rooms under a roof, where the ambient warms as if lit inside while the visitor stands in them.</summary>
        [JsonProperty("indoor_rooms")] public string[] IndoorRooms = Array.Empty<string>();
        [JsonProperty("indoor_ambient_sky")] public string IndoorAmbientSky;
        [JsonProperty("indoor_ambient_equator")] public string IndoorAmbientEquator;
        [JsonProperty("indoor_ambient_ground")] public string IndoorAmbientGround;
        [JsonProperty("indoor_blend_seconds")] public float IndoorBlendSeconds = 1.5f;
        /// <summary>Materials of the museum's lamps: their glow grows at night and a pool of light lies under each.</summary>
        [JsonProperty("lamp_materials")] public string[] LampMaterials = Array.Empty<string>();
        [JsonProperty("lamp_emission")] public float LampEmission = 2;
        [JsonProperty("lamp_pool_color")] public string LampPoolColor;
        [JsonProperty("lamp_pool_alpha")] public float LampPoolAlpha = .3f;
        [JsonProperty("lamp_pool_size")] public float LampPoolSize = 3;
        /// <summary>A softer glow on a wall standing this close to a lamp, so a room "gives off light" around it.</summary>
        [JsonProperty("lamp_wall_alpha")] public float LampWallAlpha = .2f;
        [JsonProperty("lamp_wall_reach")] public float LampWallReach = 1;
        [JsonProperty("lamp_wall_size")] public float LampWallSize = 2;
        /// <summary>The sky the lagoon reflects and the opening the clear water shows above it, at night.</summary>
        [JsonProperty("water_sky_tint")] public string WaterSkyTint;
        [JsonProperty("water_opening_color")] public string WaterOpeningColor;
        /// <summary>Seconds over which the museum turns to night or day when the visitor changes the setting.</summary>
        [JsonProperty("switch_seconds")] public float SwitchSeconds = 1.2f;
        /// <summary>Proposal, off until the owner approves it: night falls by itself in the overlook at the end of the visit.</summary>
        [JsonProperty("auto_night_at_overlook")] public bool AutoNightAtOverlook;
        [JsonProperty("overlook_room")] public string OverlookRoom = "room.overlook";
        [JsonProperty("auto_night_seconds")] public float AutoNightSeconds = 8;

        private static MuseumNightAtmosphere current;
        public static MuseumNightAtmosphere Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing museum night.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumNightAtmosphere Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumNightAtmosphere>(json);
            if (result == null || result.SchemaVersion != 1) throw new InvalidOperationException("Invalid museum night.");
            foreach (var color in new[] { result.FogColor, result.MoonColor, result.AmbientSky, result.AmbientEquator, result.AmbientGround,
                result.IndoorAmbientSky, result.IndoorAmbientEquator, result.IndoorAmbientGround, result.LampPoolColor,
                result.WaterSkyTint, result.WaterOpeningColor, result.MoonDiscColor, result.MoonHaloColor, result.MoonGlintColor })
                MuseumExperienceConfiguration.ColorValue(color);
            bool Between(float value, float low, float high) => MuseumExperienceConfiguration.Finite(value) && value >= low && value <= high;
            if (!Between(result.FogStart, 0, 1000) || !Between(result.FogEnd, 0, 1000) || result.FogEnd <= result.FogStart
                || !Between(result.MoonIntensity, 0, 2) || !Between(result.MoonSize, .2f, 10) || !Between(result.SkyExposure, .05f, 4)
                || !Between(result.MoonAzimuth, 0, 360) || !Between(result.MoonElevation, 5, 80) || !Between(result.MoonHaloSize, 1, 40)
                || !Between(result.MoonGlintAlpha, 0, 1) || !Between(result.MoonGlintLength, 0, 20) || !Between(result.MoonGlintWidth, 0, 10)
                || !Between(result.ReflectionIntensity, 0, 1) || !Between(result.IndoorBlendSeconds, 0, 10)
                || !Between(result.LampEmission, 0, 8) || !Between(result.LampPoolAlpha, 0, 1) || !Between(result.LampPoolSize, 0, 10)
                || !Between(result.LampWallAlpha, 0, 1) || !Between(result.LampWallReach, 0, 5) || !Between(result.LampWallSize, 0, 10)
                || !Between(result.SwitchSeconds, 0, 10) || !Between(result.AutoNightSeconds, 0, 60)
                || result.IndoorRooms == null || result.LampMaterials == null || result.IndoorRooms.Any(string.IsNullOrWhiteSpace)
                || result.LampMaterials.Any(string.IsNullOrWhiteSpace) || result.MoonGlints == null
                || result.MoonGlints.Any(glint => glint == null || !Between(glint.X, -1000, 1000) || !Between(glint.Z, -1000, 1000)))
                throw new InvalidOperationException("Invalid museum night.");
            return result;
        }

        /// <summary>The direction the moon is seen in, from its azimuth and elevation.</summary>
        public Vector3 MoonDirection()
        {
            float azimuth = MoonAzimuth * Mathf.Deg2Rad, elevation = MoonElevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Cos(azimuth) * Mathf.Cos(elevation));
        }

        /// <summary>The night's ambient, from the open air (0) to inside a roofed room (1).</summary>
        public (Color sky, Color equator, Color ground) Ambient(float indoor)
        {
            Color Blend(string outside, string inside) => MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(outside),
                MuseumExperienceConfiguration.ColorValue(inside), Mathf.Clamp01(indoor));
            return (Blend(AmbientSky, IndoorAmbientSky), Blend(AmbientEquator, IndoorAmbientEquator), Blend(AmbientGround, IndoorAmbientGround));
        }

        public bool Indoor(string roomKey) => !string.IsNullOrEmpty(roomKey) && IndoorRooms.Contains(roomKey);

        /// <summary>A blend that ends exactly on its values: whole day at 0, whole night at 1.</summary>
        public static Color Mix(Color from, Color to, float share) => share <= 0 ? from : share >= 1 ? to : Color.Lerp(from, to, share);
        public static float Mix(float from, float to, float share) => share <= 0 ? from : share >= 1 ? to : Mathf.Lerp(from, to, share);
    }

    /// <summary>
    /// Turns the museum to night and back, live (owner, 28/10/2026: «un modo noche para cambiar entorno a noche en el museo
    /// y poder ver las estrellas»; «las lámparas deberían tener luz y adentro hacer luz»; 30/10: «que la luna se pueda ver
    /// también, y que sea muy bonita»). It adds no real-time light: the hearth's fire is the night's main light, the
    /// lanterns glow brighter with a soft pool of light under each and on a wall beside them, the ambient warms inside the
    /// roofed rooms, and the museum's sun turns into the moon's light from where the moon stands in the sky. While a scenic
    /// set stands in for the museum (the title fire, the credits) it waits, since that set keeps and restores the museum's
    /// own lighting.
    /// </summary>
    public sealed class MuseumNight : MonoBehaviour
    {
        /// <summary>The night sky's material in Resources, and the name of the visit's copy of it.</summary>
        public const string SkyMaterialName = "MuseumNightSky";
        public const string PoolsName = "NightLampPools";
        /// <summary>Texels across the moon's face: about four for every headset pixel the disc covers.</summary>
        public const int MoonFaceSize = 256;
        private static Material sky;
        private static Texture2D moonFace;
        private MuseumNightAtmosphere night;
        private MuseumAtmosphere day;
        private MuseumWayfinding rooms;
        private MuseumVisitFlow flow;
        private float level;
        private float target;
        private float speed = float.PositiveInfinity;
        private float indoor;
        private bool indoorKnown;
        private float dayReflection = 1;
        private bool dayKept;
        private bool automatic;
        private bool asked;
        /// <summary>The museum's sun while it shines as the moon, and where it shone from by day.</summary>
        private Light moon;
        private Quaternion dayRotation;
        private readonly List<(Renderer renderer, int index, Color emission)> lamps = new();
        private readonly List<(Renderer renderer, bool lagoon)> waters = new();
        private readonly Dictionary<(Renderer, int), MaterialPropertyBlock> keptBlocks = new();
        private MeshRenderer pools;
        private bool found;

        /// <summary>True when the visitor chose the night, or the night fell by itself at the overlook.</summary>
        public bool Night => target > .5f;
        /// <summary>How far the museum stands into the night this moment, from 0 (the day as delivered) to 1.</summary>
        public float Level => level;
        /// <summary>How far the night's ambient has warmed for a roofed room, from 0 to 1.</summary>
        public float Indoor => indoor;
        public IReadOnlyList<(Renderer renderer, int index, Color emission)> Lamps { get { Find(); return lamps; } }
        /// <summary>
        /// The pools of light under the lamps, their glow on a wall beside them and the moon's streaks on still water, one
        /// mesh drawn at once; null when the museum has neither lamps nor such water.
        /// </summary>
        public MeshRenderer Pools { get { Find(); return pools; } }
        /// <summary>How many walls catch a lamp's glow and how many waters the moon's light, in <see cref="Pools"/>.</summary>
        public int WallGlows { get; private set; }
        public int MoonGlints { get; private set; }
        /// <summary>The overlook's nightfall at the end of the visit; off until the owner approves it.</summary>
        public bool AutoNightAtOverlook { get; set; }

        public void Configure(MuseumWayfinding wayfinding, MuseumVisitFlow visit = null, MuseumNightAtmosphere settings = null)
        {
            rooms = wayfinding;
            flow = visit;
            night = settings ?? MuseumNightAtmosphere.Current;
            AutoNightAtOverlook = night.AutoNightAtOverlook;
        }

        /// <summary>
        /// Asks for night or day. With no seconds it changes at once when the museum is in view; otherwise over the night's
        /// own switch time. Out of view (a scenic set stands in front) it changes at once as soon as the museum returns.
        /// </summary>
        public void Set(bool value, float seconds = -1)
        {
            Connect();
            automatic = false;
            // The visitor's saved choice, read as the museum opens, is there from the first frame.
            if (!asked) { asked = true; seconds = 0; }
            Turn(value ? 1 : 0, seconds < 0 ? night.SwitchSeconds : seconds);
        }

        private void Turn(float to, float seconds)
        {
            if (Mathf.Approximately(target, to) && (seconds > 0 || Mathf.Approximately(level, to))) return;
            target = to;
            bool showing = Showing();
            speed = seconds > 0 && showing ? 1 / seconds : float.PositiveInfinity;
            if (float.IsPositiveInfinity(speed) && showing) Advance(0);
        }

        private void Connect()
        {
            night ??= MuseumNightAtmosphere.Current;
            day ??= MuseumAtmosphere.Current;
        }

        /// <summary>True after night fell by itself at the overlook rather than by the visitor's choice.</summary>
        public bool Automatic => automatic;

        private void Update()
        {
            if (night == null) return;
            // The proposal of the overlook: night falls slowly once the visitor reaches it, without touching the setting.
            bool overlook = AutoNightAtOverlook && target < .5f && rooms != null && rooms.CurrentRoomKey == night.OverlookRoom
                && (flow == null || flow.Phase == VisitPhase.Exploring);
            bool indoorMoves = level > 0 && !Mathf.Approximately(indoor, night.Indoor(rooms != null ? rooms.CurrentRoomKey : null) ? 1 : 0);
            // Scenic sets are looked for only when something is to change, not on every frame.
            if (!overlook && !indoorMoves && Mathf.Approximately(level, target) || !Showing()) return;
            if (overlook)
            {
                automatic = true;
                target = 1;
                speed = night.AutoNightSeconds > 0 ? 1 / night.AutoNightSeconds : float.PositiveInfinity;
            }
            Advance(Time.unscaledDeltaTime);
        }

        /// <summary>One frame of <see cref="Update"/>, for tests: nothing changes while a scenic set is shown.</summary>
        public void Step(float seconds)
        {
            if (Showing()) Advance(seconds);
        }

        /// <summary>Moves towards the asked light and follows the room the visitor stands in.</summary>
        private void Advance(float seconds)
        {
            Connect();
            bool changed = false;
            // Night falling on a visitor already in a room finds the room as it is, without a second blend.
            bool fromDay = level <= 0;
            if (!Mathf.Approximately(level, target))
            {
                level = float.IsPositiveInfinity(speed) ? target : Mathf.MoveTowards(level, target, seconds * speed);
                changed = true;
            }
            float wanted = night.Indoor(rooms != null ? rooms.CurrentRoomKey : null) ? 1 : 0;
            if (!indoorKnown || fromDay || level <= 0) { indoor = wanted; indoorKnown = true; }
            else if (!Mathf.Approximately(indoor, wanted))
            {
                indoor = night.IndoorBlendSeconds > 0 ? Mathf.MoveTowards(indoor, wanted, seconds / night.IndoorBlendSeconds) : wanted;
                changed = true;
            }
            if (changed || float.IsPositiveInfinity(speed) && seconds == 0) Apply();
        }

        /// <summary>
        /// The museum's own lighting is in view: no scenic set stands in front of it. A set keeps the museum's lighting
        /// while it is shown and restores it afterwards, so the night waits for it.
        /// </summary>
        private static bool Showing()
        {
            foreach (var set in FindObjectsByType<MuseumEntryBackdrop>(FindObjectsSortMode.None))
                if (set.Shown) return false;
            return true;
        }

        private void Apply()
        {
            Find();
            if (level <= 0)
            {
                // The day exactly as delivered. The light that was the moon gets its daylight first, so the day finds it
                // again as its brightest directional light.
                if (moon != null)
                {
                    moon.color = MuseumExperienceConfiguration.ColorValue(day.SunColor);
                    moon.intensity = day.SunIntensity;
                    moon.transform.rotation = dayRotation;
                    moon = null;
                }
                day.Apply();
                if (dayKept) RenderSettings.reflectionIntensity = dayReflection;
                Restore();
                if (pools != null) pools.enabled = false;
                return;
            }
            if (!dayKept) { dayReflection = RenderSettings.reflectionIntensity; dayKept = true; }
            float t = Mathf.SmoothStep(0, 1, level);
            var fog = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.FogColor), MuseumExperienceConfiguration.ColorValue(night.FogColor), t);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = MuseumNightAtmosphere.Mix(day.FogStart, night.FogStart, t);
            RenderSettings.fogEndDistance = MuseumNightAtmosphere.Mix(day.FogEnd, night.FogEnd, t);
            var (skyColor, equator, ground) = night.Ambient(indoor);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.AmbientSky), skyColor, t);
            RenderSettings.ambientEquatorColor = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.AmbientEquator), equator, t);
            RenderSettings.ambientGroundColor = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.AmbientGround), ground, t);
            RenderSettings.reflectionIntensity = MuseumNightAtmosphere.Mix(dayReflection, night.ReflectionIntensity, t);
            var sun = moon != null ? moon : RenderSettings.sun;
            if (sun != null)
            {
                if (moon == null) dayRotation = sun.transform.rotation;
                moon = sun;
                sun.color = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.SunColor), MuseumExperienceConfiguration.ColorValue(night.MoonColor), t);
                sun.intensity = MuseumNightAtmosphere.Mix(day.SunIntensity, night.MoonIntensity, t);
                // The light swings round to come from the moon, so its shadows and the moon in the sky agree.
                var fromMoon = Quaternion.LookRotation(-night.MoonDirection(), Vector3.up);
                sun.transform.rotation = t >= 1 ? fromMoon : Quaternion.Slerp(dayRotation, fromMoon, t);
            }
            // The day's sky darkens away, then the night's rises: two panoramas never mix in one pixel.
            bool headset = Application.platform == RuntimePlatform.Android;
            var nightSky = level >= .5f ? Sky(night) : null;
            if (nightSky != null)
            {
                nightSky.SetColor("_HorizonColor", fog);
                nightSky.SetColor("_GroundColor", fog * .6f);
                nightSky.SetFloat("_Exposure", night.SkyExposure * Mathf.SmoothStep(0, 1, (level - .5f) * 2));
                RenderSettings.skybox = nightSky;
            }
            else
            {
                var daySky = day.Sky(headset);
                // Only the visit's own copy of the day sky is dimmed; the project's material is never written.
                if (daySky != null && (daySky.hideFlags & HideFlags.DontSave) != 0)
                {
                    daySky.SetColor("_HorizonColor", fog);
                    daySky.SetFloat("_Exposure", (headset ? day.SkyExposureHeadset : day.SkyExposure) * (1 - Mathf.SmoothStep(0, 1, level * 2)));
                    RenderSettings.skybox = daySky;
                }
            }
            LightLamps(t);
            DarkenWater(t, nightSky);
        }

        private void LightLamps(float t)
        {
            foreach (var (renderer, index, emission) in lamps)
            {
                if (renderer == null) continue;
                var block = Block(renderer, index);
                block.SetColor("_EmissionColor", emission * MuseumNightAtmosphere.Mix(1, night.LampEmission, t));
                renderer.SetPropertyBlock(block, index);
            }
            if (pools == null) return;
            pools.enabled = true;
            // Each glow carries its own colour and strength in its vertices; the night only fades them in together.
            var pool = new MaterialPropertyBlock();
            pool.SetColor("_BaseColor", new Color(1, 1, 1, t));
            pools.SetPropertyBlock(pool);
        }

        private void DarkenWater(float t, Material nightSky)
        {
            foreach (var (renderer, lagoon) in waters)
            {
                if (renderer == null) continue;
                var block = Block(renderer, -1);
                var material = renderer.sharedMaterial;
                if (lagoon)
                {
                    var tint = material.HasProperty("_SkyTint") ? material.GetColor("_SkyTint") : Color.white;
                    block.SetColor("_SkyTint", MuseumNightAtmosphere.Mix(tint, MuseumExperienceConfiguration.ColorValue(night.WaterSkyTint), t));
                    if (nightSky != null && nightSky.GetTexture("_MainTex") != null) block.SetTexture("_SkyTex", nightSky.GetTexture("_MainTex"));
                }
                else
                {
                    var opening = material.HasProperty("_OpeningColor") ? material.GetColor("_OpeningColor") : Color.white;
                    block.SetColor("_OpeningColor", MuseumNightAtmosphere.Mix(opening, MuseumExperienceConfiguration.ColorValue(night.WaterOpeningColor), t));
                }
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>A copy of what the renderer carried before the night, kept once so the day gets it back unchanged.</summary>
        private MaterialPropertyBlock Block(Renderer renderer, int index)
        {
            var key = (renderer, index);
            if (!keptBlocks.ContainsKey(key))
            {
                var kept = new MaterialPropertyBlock();
                bool has = renderer.HasPropertyBlock();
                if (has) { if (index < 0) renderer.GetPropertyBlock(kept); else renderer.GetPropertyBlock(kept, index); }
                keptBlocks[key] = has ? kept : null;
            }
            var block = new MaterialPropertyBlock();
            var original = keptBlocks[key];
            if (original != null) { if (index < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, index); }
            return block;
        }

        private void Restore()
        {
            foreach (var ((renderer, index), block) in keptBlocks)
            {
                if (renderer == null) continue;
                if (index < 0) renderer.SetPropertyBlock(block);
                else renderer.SetPropertyBlock(block, index);
            }
            keptBlocks.Clear();
        }

        /// <summary>
        /// The museum's lamps by their material, and its water by its shader. A pool of light lies on the ground under
        /// each lamp, a softer one on a wall standing close beside it, and the moon lays a streak on the still waters it
        /// names; all in one mesh, so the night costs one more draw for every lamp of the museum together.
        /// </summary>
        private void Find()
        {
            if (found) return;
            Connect();
            found = true;
            var names = new HashSet<string>(night.LampMaterials, StringComparer.Ordinal);
            var glows = new List<Glow>();
            var pooled = new List<Vector3>();
            var poolColour = MuseumExperienceConfiguration.ColorValue(night.LampPoolColor);
            float half = night.LampPoolSize * .5f;
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer) continue;
                var materials = renderer.sharedMaterials;
                bool lamp = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (material == null) continue;
                    if (names.Contains(BaseName(material.name)))
                    {
                        var emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                        lamps.Add((renderer, index, emission));
                        lamp = true;
                    }
                    else if (index == 0 && material.shader != null && (material.shader.name == "Musiyo/Lagoon Water"
                        || material.shader.name == "Musiyo/World/Clear Water"))
                        waters.Add((renderer, material.shader.name == "Musiyo/Lagoon Water"));
                }
                if (!lamp || night.LampPoolSize <= 0) continue;
                // A lantern's door and body are both glass: one pool for the lantern, not one for each pane.
                var foot = Ground(renderer.bounds) + Vector3.up * .03f;
                if (pooled.Any(spot => new Vector2(spot.x - foot.x, spot.z - foot.z).sqrMagnitude < .16f)) continue;
                pooled.Add(foot);
                glows.Add(new Glow(foot, Vector3.right * half, Vector3.forward * half, Alpha(poolColour, night.LampPoolAlpha)));
                if (night.LampWallAlpha > 0 && night.LampWallSize > 0 && Wall(renderer.bounds.center, night.LampWallReach, out var wall, out var facing))
                {
                    // Across the wall and up it, facing the lamp: the same winding as a pool facing up.
                    float wallHalf = night.LampWallSize * .5f;
                    glows.Add(new Glow(wall + facing * .02f, Vector3.Cross(facing, Vector3.up) * wallHalf, Vector3.up * wallHalf,
                        Alpha(poolColour, night.LampWallAlpha)));
                    WallGlows++;
                }
            }
            // The moon on still water: a soft streak lying towards where the moon stands, just above the water.
            var towards = Vector3.ProjectOnPlane(night.MoonDirection(), Vector3.up).normalized;
            foreach (var glint in night.MoonGlints)
            {
                if (night.MoonGlintAlpha <= 0 || !Physics.Raycast(new Vector3(glint.X, 400, glint.Z), Vector3.down, out var water, 800, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                glows.Add(new Glow(water.point + Vector3.up * .08f, Vector3.Cross(Vector3.up, towards) * night.MoonGlintWidth * .5f,
                    towards * night.MoonGlintLength * .5f, Alpha(MuseumExperienceConfiguration.ColorValue(night.MoonGlintColor), night.MoonGlintAlpha)));
                MoonGlints++;
            }
            if (glows.Count > 0) pools = BuildPools(glows);
        }

        /// <summary>One soft glow of the night: its centre, half its width and half its length, and its colour.</summary>
        private readonly struct Glow
        {
            public readonly Vector3 Centre, Across, Along;
            public readonly Color Colour;
            public Glow(Vector3 centre, Vector3 across, Vector3 along, Color colour) { Centre = centre; Across = across; Along = along; Colour = colour; }
        }

        private static Color Alpha(Color colour, float alpha) { colour.a = alpha; return colour; }

        /// <summary>
        /// The nearest wall within reach of a lamp, at the lamp's height: an upright surface of something at least as tall
        /// as a room's wall, so a post, a bench or a tree trunk does not count.
        /// </summary>
        public static bool Wall(Vector3 lamp, float reach, out Vector3 point, out Vector3 facing)
        {
            point = facing = default;
            float nearest = float.PositiveInfinity;
            for (int index = 0; index < 8; index++)
            {
                var direction = Quaternion.Euler(0, index * 45, 0) * Vector3.forward;
                if (!Physics.Raycast(lamp, direction, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.normal.y) > .3f || hit.collider.bounds.size.y < 1.8f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                point = hit.point;
                facing = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            }
            return nearest < float.PositiveInfinity;
        }

        public static string BaseName(string material) => material.EndsWith(" (Instance)") ? material.Substring(0, material.Length - 11) : material;

        /// <summary>
        /// The ground under a lamp: the highest surface facing up that lies well below the lamp, so the post the lamp
        /// stands on does not count; the foot of the lamp's box when nothing is found.
        /// </summary>
        public static Vector3 Ground(Bounds lamp)
        {
            var spot = new Vector3(lamp.center.x, lamp.min.y, lamp.center.z);
            float best = float.NegativeInfinity;
            foreach (var hit in Physics.RaycastAll(lamp.center, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .7f && hit.point.y < lamp.min.y - .5f && hit.point.y > best) { best = hit.point.y; spot.y = best; }
            return spot;
        }

        private MeshRenderer BuildPools(List<Glow> glows)
        {
            var holder = new GameObject(PoolsName, typeof(MeshFilter), typeof(MeshRenderer));
            holder.transform.SetParent(transform, false);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.transform.localScale = Vector3.one;
            var mesh = new Mesh { name = PoolsName };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            foreach (var glow in glows)
            {
                int first = vertices.Count;
                vertices.Add(glow.Centre - glow.Across - glow.Along); vertices.Add(glow.Centre + glow.Across - glow.Along);
                vertices.Add(glow.Centre - glow.Across + glow.Along); vertices.Add(glow.Centre + glow.Across + glow.Along);
                var normal = Vector3.Cross(glow.Along, glow.Across).normalized;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                for (int corner = 0; corner < 4; corner++) { normals.Add(normal); colours.Add(glow.Colour); }
                triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            // The particle shader of the fire's glow multiplies by the vertex colour: each glow's own colour and strength.
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            holder.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = Resources.Load<Material>(MuseumEntryBackdrop.GlowMaterial);
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>
        /// The night sky of the museum: the stars of the title and the credits, shared with them, and the moon drawn by the
        /// sky shader where the night places it, with its face, limb and halo. Where that shader cannot run, the stars alone;
        /// null when the build lacks the night sky altogether.
        /// </summary>
        public static Material Sky(MuseumNightAtmosphere night)
        {
            if (sky != null) return sky;
            var stars = MuseumEntryBackdrop.NightSky();
            if (stars == null) return null;
            var authored = Resources.Load<Material>(SkyMaterialName);
            if (authored == null || !authored.shader.isSupported)
            {
                sky = new Material(stars) { name = SkyMaterialName, hideFlags = HideFlags.DontSave };
                sky.SetFloat("_Rotation", 0);
                return sky;
            }
            sky = new Material(authored) { name = SkyMaterialName, hideFlags = HideFlags.DontSave };
            sky.SetTexture("_MainTex", stars.GetTexture("_MainTex"));
            sky.SetFloat("_HorizonHeight", stars.GetFloat("_HorizonHeight"));
            var direction = night.MoonDirection();
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            sky.SetVector("_MoonDirection", direction);
            sky.SetVector("_MoonRight", right);
            sky.SetVector("_MoonUp", Vector3.Cross(direction, right));
            sky.SetFloat("_MoonRadius", night.MoonSize * .5f * Mathf.Deg2Rad);
            sky.SetColor("_MoonColor", MuseumExperienceConfiguration.ColorValue(night.MoonDiscColor));
            sky.SetColor("_HaloColor", MuseumExperienceConfiguration.ColorValue(night.MoonHaloColor));
            sky.SetFloat("_HaloSize", night.MoonHaloSize);
            sky.SetTexture("_MoonTex", MoonFace());
            return sky;
        }

        /// <summary>
        /// The moon's face, the same every time: a pale ground with soft darker seas, a few small bright craters and a fine
        /// grain, brightest in the middle of its range so the sky shader's limb and colour do the rest. Mipmapped, so the
        /// disc stays smooth however small it is drawn.
        /// </summary>
        public static Texture2D MoonFace()
        {
            if (moonFace != null) return moonFace;
            moonFace = new Texture2D(MoonFaceSize, MoonFaceSize, TextureFormat.R8, true) { name = "MoonFace", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 1 };
            moonFace.SetPixelData(MoonFacePixels(), 0);
            // Uploaded with its smaller copies and released from memory: only the graphics card keeps it.
            moonFace.Apply(true, true);
            return moonFace;
        }

        /// <summary>The face's brightness, one byte per texel, row by row.</summary>
        public static byte[] MoonFacePixels()
        {
            const int size = MoonFaceSize;
            var random = new System.Random(20261030);
            var craters = Enumerable.Range(0, 9).Select(_ => (x: (float)random.NextDouble() * 1.5f - .75f, y: (float)random.NextDouble() * 1.5f - .75f,
                radius: .04f + (float)random.NextDouble() * .07f)).ToArray();
            var texels = new byte[size * size];
            for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                {
                    float x = (column + .5f) / size * 2 - 1, y = (row + .5f) / size * 2 - 1;
                    // Seas: two scales of soft noise, kept to broad patches.
                    float broad = Mathf.PerlinNoise(x * 1.4f + 11.3f, y * 1.4f + 4.7f) * .65f + Mathf.PerlinNoise(x * 3.1f + 2.1f, y * 3.1f + 8.8f) * .35f;
                    float sea = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .66f, broad));
                    float value = .92f - .26f * sea + (Mathf.PerlinNoise(x * 17 + 5.5f, y * 17 + 1.3f) - .5f) * .07f;
                    foreach (var crater in craters)
                    {
                        float distance = Mathf.Sqrt((x - crater.x) * (x - crater.x) + (y - crater.y) * (y - crater.y)) / crater.radius;
                        // A bright floor and a faint darker rim, both soft.
                        if (distance < 1) value += .06f * (1 - distance * distance);
                        else if (distance < 1.4f) value -= .03f * (1 - (distance - 1) / .4f);
                    }
                    texels[row * size + column] = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255);
                }
            return texels;
        }

        private void OnDestroy()
        {
            if (pools != null) Destroy(pools.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
