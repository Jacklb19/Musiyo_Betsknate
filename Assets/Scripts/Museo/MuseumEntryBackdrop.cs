using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Museum
{
    /// <summary>What the entry menu shows behind it. The museum itself stays the default until the owner chooses.</summary>
    public enum EntryBackdropKind { Museum, Dark, Hearth, Mask }

    /// <summary>
    /// A small scenic set behind the entry menu, built away from the museum with its own light: darkness, a hearth fire,
    /// or a published mask softly lit. While it is shown the museum's daylight and ambient are set aside and restored when
    /// the visit's view takes over. It carries no cultural content: a mask is only ever loaded through the API from a
    /// record that publishes a model for this platform and does not restrict its reuse.
    /// </summary>
    public sealed class MuseumEntryBackdrop : MonoBehaviour
    {
        public const string GlowMaterial = "EntryBackdropGlow";
        /// <summary>Far below the museum terrain, out of every view of the visit.</summary>
        public static readonly Vector3 Origin = new Vector3(0, -400, 0);
        /// <summary>Where the subject stands in the set, to the right of the menu column.</summary>
        public static readonly Vector3 Focus = new Vector3(1.15f, 0, 0);
        private static readonly Color Background = new Color(.008f, .009f, .011f);
        private Transform root;
        private Transform cameraAnchor;
        private Light fireLight;
        private MuseumFire fire;
        private Transform maskAnchor;
        private bool shown;
        private readonly List<(Light light, bool enabled)> daylight = new List<(Light, bool)>();
        private AmbientMode ambientMode;
        private Color ambientColor;
        private float ambientIntensity;
        private bool fog;
        private FogMode fogMode;
        private Color fogColor;
        private float fogDensity;
        private Material skybox;
        private CameraClearFlags clearFlags;
        private Color background;
        private float farClip;
        private Camera dressedCamera;
        private CancellationTokenSource maskLoad;
        private GltfImport maskImport;

        public EntryBackdropKind Kind { get; private set; }
        /// <summary>True when a set other than the museum is built and can be shown.</summary>
        public bool Active => Kind != EntryBackdropKind.Museum && root != null;
        public Transform CameraPose => cameraAnchor;
        /// <summary>The set itself, shown and hidden as a whole; the closing credits stand in it beside the fire.</summary>
        public Transform Root => root;
        public bool Shown => shown;
        /// <summary>How bright the fire burns this moment, from 0 to 1; the entry menu takes its warm light from it.</summary>
        public float FireLevel { get; private set; }
        /// <summary>Why the mask set shows no mask; empty once one is shown.</summary>
        public string MaskStatus { get; private set; } = "";
        public event Action Changed;

        public static EntryBackdropKind Parse(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "": case "museum": return EntryBackdropKind.Museum;
                case "dark": return EntryBackdropKind.Dark;
                case "hearth": return EntryBackdropKind.Hearth;
                case "mask": return EntryBackdropKind.Mask;
                default: throw new InvalidOperationException("Unknown entry backdrop: " + value);
            }
        }

        /// <summary>Builds the chosen set. The museum backdrop builds nothing and leaves the entry as it was.</summary>
        public void Configure(EntryBackdropKind kind, Func<string> apiBase = null, string maskElement = null)
        {
            Clear();
            Kind = kind;
            if (kind == EntryBackdropKind.Museum) return;
            root = new GameObject("EntryBackdrop_" + kind).transform;
            root.SetParent(transform, false);
            root.position = Origin;
            cameraAnchor = new GameObject("EntryBackdropCamera").transform;
            cameraAnchor.SetParent(root, false);
            var eye = new Vector3(-.35f, kind == EntryBackdropKind.Mask ? 1.35f : 1.25f, -3.6f);
            var target = Focus + Vector3.up * (kind == EntryBackdropKind.Mask ? 1.25f : .45f);
            cameraAnchor.localPosition = eye;
            cameraAnchor.localRotation = Quaternion.LookRotation(target - eye + new Vector3(-.85f, 0, 0), Vector3.up);
            if (kind != EntryBackdropKind.Mask) Ground();
            if (kind == EntryBackdropKind.Dark) BuildDark();
            else if (kind == EntryBackdropKind.Hearth) BuildHearth();
            else BuildMask(apiBase, maskElement);
            root.gameObject.SetActive(false);
        }

        /// <summary>Shows or hides the set for a camera, setting the museum's own light aside meanwhile.</summary>
        public void Show(bool value, Camera stage)
        {
            if (!Active) value = false;
            if (value == shown && (!value || stage == dressedCamera)) return;
            if (value)
            {
                if (!shown)
                {
                    daylight.Clear();
                    foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (light.type == LightType.Directional && !light.transform.IsChildOf(root))
                        {
                            daylight.Add((light, light.enabled));
                            light.enabled = false;
                        }
                    ambientMode = RenderSettings.ambientMode; ambientColor = RenderSettings.ambientLight;
                    ambientIntensity = RenderSettings.ambientIntensity; fog = RenderSettings.fog; skybox = RenderSettings.skybox;
                    fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor; fogDensity = RenderSettings.fogDensity;
                    RenderSettings.ambientMode = AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(.018f, .02f, .024f);
                    // Black fog dissolves the edge of the floor into the dark.
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = Background;
                    RenderSettings.fogDensity = .13f;
                    RenderSettings.skybox = null;
                    root.gameObject.SetActive(true);
                }
                Dress(stage);
            }
            else if (shown)
            {
                foreach (var (light, enabled) in daylight) if (light != null) light.enabled = enabled;
                daylight.Clear();
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambientColor;
                RenderSettings.ambientIntensity = ambientIntensity; RenderSettings.fog = fog; RenderSettings.skybox = skybox;
                RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor; RenderSettings.fogDensity = fogDensity;
                root.gameObject.SetActive(false);
                Undress();
            }
            shown = value;
        }

        private void Dress(Camera stage)
        {
            if (stage == dressedCamera) return;
            Undress();
            if (stage == null) return;
            dressedCamera = stage;
            clearFlags = stage.clearFlags; background = stage.backgroundColor; farClip = stage.farClipPlane;
            stage.clearFlags = CameraClearFlags.SolidColor;
            stage.backgroundColor = Background;
            stage.farClipPlane = 60;
        }

        private void Undress()
        {
            if (dressedCamera == null) return;
            dressedCamera.clearFlags = clearFlags; dressedCamera.backgroundColor = background; dressedCamera.farClipPlane = farClip;
            dressedCamera = null;
        }

        private void Update()
        {
            if (!shown) return;
            float time = Time.unscaledTime;
            if (fire != null) FireLevel = fire.Level;
            if (floorGlow != null)
            {
                var tint = FloorGlowTint;
                tint.a *= .7f + .3f * FireLevel;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", tint);
                floorGlow.SetPropertyBlock(block);
            }
            else if (fireLight != null)
            {
                float flicker = .6f * Mathf.PerlinNoise(time * 3.1f, .3f) + .25f * Mathf.PerlinNoise(time * 11f, 4.1f);
                fireLight.intensity = 1.3f + flicker;
                FireLevel = Mathf.Clamp01(flicker / .85f);
            }
            if (maskAnchor != null) maskAnchor.localRotation = Quaternion.Euler(0, 180 + Mathf.Sin(time * .25f) * 14, 0);
        }

        private void Ground()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ground.name = "Ground";
            Discard(ground.GetComponent<Collider>());
            ground.transform.SetParent(root, false);
            ground.transform.localPosition = Focus + Vector3.down * .05f;
            ground.transform.localScale = new Vector3(36, .05f, 36);
            Paint(ground, "M_Tierra_Apisonada", new Color(.22f, .17f, .13f));
        }

        private void BuildDark()
        {
            // A pool of warm light in the dark, with motes drifting through it.
            var pool = Spot("Pool", Focus + new Vector3(0, 4.2f, -.4f), Focus, new Color(1f, .78f, .52f), 3.2f, 34);
            pool.range = 8;
            Halo(Focus + new Vector3(.2f, 1.4f, 2.5f), 4.5f, new Color(.55f, .42f, .3f, .1f));
            Motes("Motes", Focus + Vector3.up * 1.2f, new Vector3(3.2f, 2.4f, 2.4f), 70, new Color(1f, .82f, .6f, .55f), .025f, .05f, .05f);
        }

        private void BuildHearth()
        {
            // Stones, ash and logs of its own: a copy of the museum's hearth would bring, on a headset, the museum's
            // combined mesh with it and stand as a strange shape floating by the fire.
            MuseumFirePit.Build(root, Focus);
            // The living fire: flames, embers, sparks, smoke and dancing light.
            fire = MuseumFire.Create(root, Focus);
            if (fire == null)
            {
                fireLight = new GameObject("FireLight").AddComponent<Light>();
                fireLight.transform.SetParent(root, false);
                fireLight.transform.localPosition = Focus + Vector3.up * .75f;
                fireLight.type = LightType.Point;
                fireLight.color = new Color(1f, .6f, .3f);
                fireLight.range = 6;
                fireLight.intensity = 1.6f;
                fireLight.shadows = LightShadows.None;
                Flames("Flames", .16f, new ParticleSystem.MinMaxCurve(.14f, .28f), 110, new Color(1f, .5f, .14f));
                Flames("FlameCore", .08f, new ParticleSystem.MinMaxCurve(.08f, .16f), 70, new Color(1f, .85f, .45f));
                Motes("Embers", Focus + Vector3.up * .9f, new Vector3(.5f, 1.2f, .5f), 40, new Color(1f, .55f, .2f, .9f), .012f, .03f, .45f);
            }
            // The fire's light lies on the earth around it, never on a card standing in the air.
            floorGlow = Floor("FloorGlow", Focus + Vector3.up * .006f, 3.2f, GlowMaterial, FloorGlowTint);
            // Low wooden stools around the fire, as the owner chose for the museum's hearth, standing on the earth.
            Stool("Props/Banco_Tripode", Focus + new Vector3(-1.05f, 0, .55f));
            Stool("Props/Banco_Curvo", Focus + new Vector3(1.0f, 0, .7f));
            Stool("Props/Banco_Tripode", Focus + new Vector3(.25f, 0, 1.25f));
        }

        /// <summary>
        /// A stool turned to face the fire, its feet on the earth whatever the model's origin, with a soft shadow under it;
        /// the set simply goes without it when the model is missing.
        /// </summary>
        private void Stool(string resource, Vector3 spot)
        {
            var model = Resources.Load<GameObject>(resource);
            if (model == null) return;
            var stool = Instantiate(model, root, false);
            stool.name = model.name;
            var toFire = Focus - spot;
            toFire.y = 0;
            var turn = Quaternion.LookRotation(toFire.normalized, Vector3.up);
            if (model.name == "Banco_Curvo") turn *= Quaternion.Euler(0, 90, 0);
            stool.transform.localPosition = spot;
            stool.transform.localRotation = turn;
            foreach (var collider in stool.GetComponentsInChildren<Collider>()) Discard(collider);
            var renderers = stool.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float floor = root.TransformPoint(spot).y;
            stool.transform.position += Vector3.up * (floor - bounds.min.y);
            float reach = Mathf.Max(bounds.extents.x, bounds.extents.z);
            var shadow = Floor("StoolShadow", spot + Vector3.up * .004f, reach * 2.6f, MuseumFire.SmokeMaterial, StoolShadowTint);
            shadow.sharedMaterial = ShadowMaterial(shadow.sharedMaterial);
        }

        private static Material shadowMaterial;
        /// <summary>The soft dark patch is drawn after the fire's light on the earth, so the light does not wash it away.</summary>
        private static Material ShadowMaterial(Material source)
        {
            if (source == null) return null;
            if (shadowMaterial == null)
            {
                shadowMaterial = new Material(source) { name = "EntryStoolShadow" };
                shadowMaterial.renderQueue = Mathf.Max(source.renderQueue, (int)RenderQueue.Transparent) + 20;
                // A plain soft disc: darkest under the seat, fading to nothing at its edge.
                const int size = 64;
                var blob = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "EntryStoolShadow", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float distance = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / (size / 2f);
                        blob.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(1, 0, Mathf.Clamp01(distance))));
                    }
                blob.Apply();
                shadowMaterial.SetTexture("_BaseMap", blob);
            }
            return shadowMaterial;
        }

        private static readonly Color FloorGlowTint = new Color(1f, .5f, .2f, .34f);
        private static readonly Color StoolShadowTint = new Color(0, 0, 0, .7f);
        private MeshRenderer floorGlow;

        /// <summary>A soft round patch lying flat on the earth: the fire's light, or the shadow under a stool.</summary>
        private MeshRenderer Floor(string name, Vector3 position, float size, string material, Color tint)
            => MuseumFirePit.Patch(root, name, position, size, Resources.Load<Material>(material), tint);

        private void BuildMask(Func<string> apiBase, string element)
        {
            maskAnchor = new GameObject("MaskAnchor").transform;
            maskAnchor.SetParent(root, false);
            maskAnchor.localPosition = Focus + Vector3.up * 1.25f;
            // Soft key from above left and a cool rim from behind; no fill, so the face emerges from the dark.
            Spot("Key", maskAnchor.localPosition + new Vector3(-1.4f, 1.6f, -1.6f), maskAnchor.localPosition, new Color(1f, .9f, .78f), 4.5f, 30);
            Spot("Rim", maskAnchor.localPosition + new Vector3(1.2f, .8f, 1.5f), maskAnchor.localPosition, new Color(.62f, .74f, 1f), 3.2f, 28);
            Halo(maskAnchor.localPosition + new Vector3(.1f, 0, 1.2f), 2.6f, new Color(.85f, .62f, .38f, .16f));
            Motes("Motes", maskAnchor.localPosition, new Vector3(2.4f, 1.8f, 1.6f), 40, new Color(1f, .85f, .65f, .45f), .015f, .035f, .04f);
            MaskStatus = "loading";
            if (apiBase == null || string.IsNullOrWhiteSpace(element)) { MaskStatus = "no_element"; return; }
            maskLoad = new CancellationTokenSource();
            LoadMask(apiBase, element, maskLoad.Token);
        }

        /// <summary>The published model of the record, through the same checks as an examined model; nothing is kept locally.</summary>
        private async void LoadMask(Func<string> apiBase, string element, CancellationToken cancel)
        {
            string profile = Application.platform == RuntimePlatform.Android ? "quest" : "web";
            try
            {
                string api = apiBase();
                ElementContractV1 record;
                using (var request = UnityWebRequest.Get(api + "/elements/" + Uri.EscapeDataString(element)))
                {
                    await Send(request, 15, cancel);
                    if (!ElementContractV1.TryParse(request.downloadHandler.text, out record)) { Finish("incompatible"); return; }
                }
                if (!MayDecorate(record)) { Finish("restricted"); return; }
                if (!ModelResourcePolicy.TrySelect(record, profile, out _, out var id)) { Finish("no_model"); return; }
                ResourceAccessContractV1 access;
                Uri url;
                using (var request = new UnityWebRequest(api + "/resources/" + Uri.EscapeDataString(id) + "/access?schema_version=1", "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    await Send(request, 15, cancel);
                    if (!ModelResourcePolicy.TryAccess(api, request.downloadHandler.text, profile, DateTimeOffset.UtcNow, out access, out url))
                    { Finish("no_access"); return; }
                }
                byte[] data;
                using (var request = UnityWebRequest.Get(url))
                {
                    await Send(request, 60, cancel);
                    data = request.downloadHandler.data;
                }
                if (!ModelResourcePolicy.ValidateGlb(data, profile, access.sha256)) { Finish("invalid_model"); return; }
                maskImport = new GltfImport();
                if (!await maskImport.Load(data, url, new ImportSettings { GenerateMipMaps = true }, cancel)
                    || cancel.IsCancellationRequested || maskAnchor == null)
                { Finish("invalid_model"); return; }
                var model = new GameObject("Mask").transform;
                model.SetParent(maskAnchor, false);
                var instantiator = new GameObjectInstantiator(maskImport, model, settings: new InstantiationSettings { Mask = ComponentType.Mesh });
                if (!await maskImport.InstantiateMainSceneAsync(instantiator, cancel) || !ModelExaminationPose.Normalize(model, .45f))
                { Finish("invalid_model"); return; }
                Finish("");
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Finish("unavailable"); }
        }

        /// <summary>A record that restricts reuse is never turned into decoration.</summary>
        public static bool MayDecorate(ElementContractV1 record)
            => record != null && (record.restrictions == null || record.restrictions.All(restriction => restriction.kind != "no_reuse"));

        private void Finish(string status)
        {
            MaskStatus = status;
            Changed?.Invoke();
        }

        private static async Task Send(UnityWebRequest request, int timeout, CancellationToken cancel)
        {
            request.timeout = timeout;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancel.IsCancellationRequested) { request.Abort(); throw new OperationCanceledException(); }
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException(request.error);
        }

        private Light Spot(string name, Vector3 from, Vector3 to, Color color, float intensity, float angle)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.transform.localPosition = from;
            light.transform.localRotation = Quaternion.LookRotation(to - from, Vector3.up);
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * .35f;
            light.range = 6;
            light.shadows = LightShadows.None;
            return light;
        }

        private ParticleSystem Particles(string name, Vector3 position, string materialName = GlowMaterial)
        {
            var item = new GameObject(name);
            item.transform.SetParent(root, false);
            item.transform.localPosition = position;
            var system = item.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var material = Resources.Load<Material>(materialName);
            var renderer = item.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        /// <summary>A soft glow card behind a subject, turned to the camera of the set.</summary>
        private void Halo(Vector3 position, float size, Color tint)
        {
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "Halo";
            Discard(halo.GetComponent<Collider>());
            halo.transform.SetParent(root, false);
            halo.transform.localPosition = position;
            halo.transform.localRotation = Quaternion.LookRotation(position - cameraAnchor.localPosition, Vector3.up);
            halo.transform.localScale = Vector3.one * size;
            var renderer = halo.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.sharedMaterial = Resources.Load<Material>(GlowMaterial);
            var tintBlock = new MaterialPropertyBlock();
            tintBlock.SetColor("_BaseColor", tint);
            renderer.SetPropertyBlock(tintBlock);
        }

        private void Flames(string name, float radius, ParticleSystem.MinMaxCurve size, int rate, Color body)
        {
            var system = Particles(name, Focus + Vector3.up * .1f);
            var main = system.main;
            main.duration = 1;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.35f, .7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.55f, 1.05f);
            main.startSize = size;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = rate;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            // Tongues of flame stretch along their rise.
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = .18f;
            renderer.lengthScale = 1.6f;
            main.useUnscaledTime = true;
            main.prewarm = true;
            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6;
            shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, .9f, .6f), 0), new GradientColorKey(body, .4f),
                    new GradientColorKey(new Color(.5f, .1f, .04f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .1f), new GradientAlphaKey(.6f, .55f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .7f), new Keyframe(.3f, 1), new Keyframe(1, .15f)));
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .25f;
            noise.frequency = 1.6f;
            noise.scrollSpeed = .8f;
            system.Play();
        }

        private void Motes(string name, Vector3 centre, Vector3 extent, int count, Color tint, float minimum, float maximum, float rise)
        {
            var system = Particles(name, centre);
            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4, 8);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(minimum, maximum);
            main.startColor = tint;
            main.maxParticles = count;
            main.useUnscaledTime = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = system.emission;
            emission.rateOverTime = count / 6f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = extent;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-.03f, .03f);
            velocity.y = new ParticleSystem.MinMaxCurve(rise * .5f, rise);
            velocity.z = new ParticleSystem.MinMaxCurve(-.03f, .03f);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .3f), new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .08f;
            noise.frequency = .4f;
            system.Play();
        }

        /// <summary>Uses a material of the delivered museum when the scene has it, and a plain one otherwise.</summary>
        private static void Paint(GameObject item, string museumMaterial, Color fallback)
        {
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var found = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SelectMany(candidate => candidate.sharedMaterials).FirstOrDefault(material => material != null && material.name == museumMaterial);
            if (found != null) { renderer.sharedMaterial = found; return; }
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", fallback);
            renderer.SetPropertyBlock(tint);
        }

        private void Clear()
        {
            Show(false, null);
            maskLoad?.Cancel();
            maskLoad?.Dispose();
            maskLoad = null;
            maskImport?.Dispose();
            maskImport = null;
            if (root != null) Discard(root.gameObject);
            root = null; cameraAnchor = null; fireLight = null; fire = null; maskAnchor = null; floorGlow = null;
            Kind = EntryBackdropKind.Museum;
            MaskStatus = "";
        }

        private static void Discard(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDestroy() => Clear();
    }
}
