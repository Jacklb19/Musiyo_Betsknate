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
                result.WaterSkyTint, result.WaterOpeningColor })
                MuseumExperienceConfiguration.ColorValue(color);
            bool Between(float value, float low, float high) => MuseumExperienceConfiguration.Finite(value) && value >= low && value <= high;
            if (!Between(result.FogStart, 0, 1000) || !Between(result.FogEnd, 0, 1000) || result.FogEnd <= result.FogStart
                || !Between(result.MoonIntensity, 0, 2) || !Between(result.MoonSize, 0, 10) || !Between(result.SkyExposure, .05f, 4)
                || !Between(result.ReflectionIntensity, 0, 1) || !Between(result.IndoorBlendSeconds, 0, 10)
                || !Between(result.LampEmission, 0, 8) || !Between(result.LampPoolAlpha, 0, 1) || !Between(result.LampPoolSize, 0, 10)
                || !Between(result.SwitchSeconds, 0, 10) || !Between(result.AutoNightSeconds, 0, 60)
                || result.IndoorRooms == null || result.LampMaterials == null || result.IndoorRooms.Any(string.IsNullOrWhiteSpace)
                || result.LampMaterials.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Invalid museum night.");
            return result;
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
    /// y poder ver las estrellas»; «las lámparas deberían tener luz y adentro hacer luz»). It adds no real-time light:
    /// the hearth's fire is the night's main light, the lanterns glow brighter with a soft pool of light under each, and the
    /// ambient warms inside the roofed rooms. While a scenic set stands in for the museum (the title fire, the credits) it
    /// waits, since that set keeps and restores the museum's own lighting.
    /// </summary>
    public sealed class MuseumNight : MonoBehaviour
    {
        public const string SkyMaterialName = "MuseumNightSky";
        public const string PoolsName = "NightLampPools";
        private static Material sky;
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
        /// <summary>The museum's sun while it shines as the moon.</summary>
        private Light moon;
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
        /// <summary>The pools of light under the lamps, one mesh drawn at once; null when the museum has no lamps.</summary>
        public MeshRenderer Pools { get { Find(); return pools; } }
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
                moon = sun;
                sun.color = MuseumNightAtmosphere.Mix(MuseumExperienceConfiguration.ColorValue(day.SunColor), MuseumExperienceConfiguration.ColorValue(night.MoonColor), t);
                sun.intensity = MuseumNightAtmosphere.Mix(day.SunIntensity, night.MoonIntensity, t);
            }
            // The day's sky darkens away, then the night's rises: two panoramas never mix in one pixel.
            bool headset = Application.platform == RuntimePlatform.Android;
            var nightSky = level >= .5f ? Sky(sun != null ? -sun.transform.forward : default, night.MoonSize) : null;
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
            var tint = MuseumExperienceConfiguration.ColorValue(night.LampPoolColor);
            tint.a = night.LampPoolAlpha * t;
            var pool = new MaterialPropertyBlock();
            pool.SetColor("_BaseColor", tint);
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
        /// each lamp, all in one mesh, so the night costs one more draw for every lamp of the museum together.
        /// </summary>
        private void Find()
        {
            if (found) return;
            Connect();
            found = true;
            var names = new HashSet<string>(night.LampMaterials, StringComparer.Ordinal);
            var spots = new List<Vector3>();
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
                if (lamp) spots.Add(Ground(renderer.bounds));
            }
            if (spots.Count > 0 && night.LampPoolSize > 0) pools = BuildPools(spots);
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

        private MeshRenderer BuildPools(List<Vector3> spots)
        {
            var holder = new GameObject(PoolsName, typeof(MeshFilter), typeof(MeshRenderer));
            holder.transform.SetParent(transform, false);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.transform.localScale = Vector3.one;
            var mesh = new Mesh { name = PoolsName };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float half = night.LampPoolSize * .5f;
            foreach (var spot in spots)
            {
                int first = vertices.Count;
                var at = spot + Vector3.up * .03f;
                vertices.Add(at + new Vector3(-half, 0, -half)); vertices.Add(at + new Vector3(half, 0, -half));
                vertices.Add(at + new Vector3(-half, 0, half)); vertices.Add(at + new Vector3(half, 0, half));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
            mesh.SetUVs(0, uvs);
            // The particle shader of the fire's glow multiplies by the vertex colour.
            mesh.SetColors(Enumerable.Repeat(Color.white, vertices.Count).ToList());
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
        /// The night sky of the museum: the stars of the title and the credits with a faint cold moon where the sun
        /// shone, drawn once into a panorama of its own the first time night falls; null when the build lacks the sky.
        /// </summary>
        public static Material Sky(Vector3 moon, float moonSize)
        {
            if (sky != null) return sky;
            var shared = Resources.Load<Material>(MuseumEntryBackdrop.NightSkyMaterial);
            if (shared == null) return null;
            sky = new Material(shared) { name = SkyMaterialName, hideFlags = HideFlags.DontSave };
            var texture = new Texture2D(MuseumEntryBackdrop.NightSkyWidth, MuseumEntryBackdrop.NightSkyHeight, TextureFormat.RGB24, false)
            { name = "NightStarsMoon", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = MuseumEntryBackdrop.StarPixels();
            DrawMoon(pixels, moon, moonSize);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sky.SetTexture("_MainTex", texture);
            sky.SetFloat("_Rotation", 0);
            return sky;
        }

        /// <summary>Where the panorama shows a direction, with no rotation: the column and row of the sky shader's mapping.</summary>
        public static Vector2 PanoramaPixel(Vector3 direction, int width, int height)
        {
            direction.Normalize();
            float u = Mathf.Atan2(direction.x, direction.z) / (2 * Mathf.PI) + .5f;
            float v = Mathf.Asin(Mathf.Clamp(direction.y, -1, 1)) / Mathf.PI + .5f;
            return new Vector2(u * width, v * height);
        }

        /// <summary>
        /// A pale, cold disc with a few soft darker seas and a faint halo around it. A direction under the horizon or none
        /// at all puts the moon low in the south-west, where the day's sun stands in the delivered scene.
        /// </summary>
        public static void DrawMoon(Color32[] pixels, Vector3 direction, float sizeDegrees)
        {
            int width = MuseumEntryBackdrop.NightSkyWidth, height = MuseumEntryBackdrop.NightSkyHeight;
            if (direction.sqrMagnitude < .01f || direction.normalized.y < .1f)
                direction = Quaternion.Euler(-30, 210, 0) * Vector3.forward;
            direction.Normalize();
            float radius = Mathf.Max(.1f, sizeDegrees * .5f) * Mathf.Deg2Rad;
            var centre = PanoramaPixel(direction, width, height);
            float cosine = Mathf.Max(.2f, Mathf.Sqrt(1 - direction.y * direction.y));
            int reachRows = Mathf.CeilToInt(radius * 4 / Mathf.PI * height) + 2;
            int reachColumns = Mathf.CeilToInt(radius * 4 / (2 * Mathf.PI) * width / cosine) + 2;
            var tangent = Vector3.Cross(Vector3.up, direction).normalized;
            var bitangent = Vector3.Cross(direction, tangent);
            for (int row = (int)centre.y - reachRows; row <= (int)centre.y + reachRows; row++)
            {
                if (row < 0 || row >= height) continue;
                float latitude = ((row + .5f) / height - .5f) * Mathf.PI;
                for (int step = -reachColumns; step <= reachColumns; step++)
                {
                    int column = ((int)centre.x + step + width) % width;
                    float longitude = ((column + .5f) / width - .5f) * 2 * Mathf.PI;
                    var at = new Vector3(Mathf.Sin(longitude) * Mathf.Cos(latitude), Mathf.Sin(latitude), Mathf.Cos(longitude) * Mathf.Cos(latitude));
                    float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(at, direction), -1, 1));
                    var under = (Color)pixels[row * width + column];
                    Color colour;
                    if (angle < radius)
                    {
                        // Seas: soft darker patches across the face, the same every time.
                        float x = Vector3.Dot(at - direction, tangent) / radius, y = Vector3.Dot(at - direction, bitangent) / radius;
                        float sea = Mathf.PerlinNoise(x * 1.7f + 3.1f, y * 1.7f + 7.4f);
                        float edge = Mathf.SmoothStep(1, .85f, angle / radius);
                        var face = new Color(.86f, .9f, .96f) * (.78f + .22f * sea);
                        colour = Color.Lerp(under, face, Mathf.Clamp01(edge + (1 - angle / radius) * .5f));
                    }
                    else
                    {
                        float glow = Mathf.Clamp01(1 - (angle - radius) / (radius * 3));
                        colour = under + new Color(.16f, .19f, .26f) * glow * glow;
                    }
                    pixels[row * width + column] = new Color(Mathf.Clamp01(colour.r), Mathf.Clamp01(colour.g), Mathf.Clamp01(colour.b));
                }
            }
        }

        private void OnDestroy()
        {
            if (pools != null) Destroy(pools.GetComponent<MeshFilter>().sharedMesh);
        }
    }
}
