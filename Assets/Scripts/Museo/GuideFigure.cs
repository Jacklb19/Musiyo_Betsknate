using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>How one guide looks: a model named in Resources, or the woven figure in the given tones.</summary>
    [Preserve]
    public sealed class GuideFigureDefinition
    {
        /// <summary>
        /// Resources path of a model that replaces the woven figure, empty for the woven one. The model stands on its
        /// origin, faces +Z and is scaled to <see cref="Height"/>; nothing of it is read from the museum model.
        /// </summary>
        [JsonProperty("model")] public string Model = "";
        [JsonProperty("height_m")] public float Height = 1.6f;
        /// <summary>Seated on a bench (the hearth guide) instead of standing and walking (the companion).</summary>
        [JsonProperty("seated")] public bool Seated;
        /// <summary>The sayo's wool: white turning to yellow (PES, p. 29). Never blue or red with white, the colours of authority.</summary>
        [JsonProperty("sayo_color")] public string SayoColor = "#EDE2C3";
        [JsonProperty("stripe_color")] public string StripeColor = "#1F1B17";
        /// <summary>Thin stripes around the sayo; each is this share of its band.</summary>
        [JsonProperty("stripe_count")] public int StripeCount = 18;
        [JsonProperty("stripe_width")] public float StripeWidth = .14f;
        [JsonProperty("tunic_color")] public string TunicColor = "#2B2622";
        [JsonProperty("belt_color")] public string BeltColor = "#F1ECE0";
        /// <summary>A smooth head the colour of wood or wool, without features.</summary>
        [JsonProperty("head_color")] public string HeadColor = "#9A7048";
    }

    /// <summary>
    /// The figures of the two guides, read from <c>Resources/MuseumGuideFigures.json</c>: <c>companion</c>, who walks with
    /// the visitor, and <c>interviewer</c>, who waits seated by the hearth. Changing a figure is a change of this file only.
    /// </summary>
    [Preserve]
    public sealed class GuideFigureSettings
    {
        public const string ResourceName = "MuseumGuideFigures";
        public const string Companion = "companion";
        public const string Interviewer = "interviewer";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("figures")] public Dictionary<string, GuideFigureDefinition> Figures = new();

        private static GuideFigureSettings current;
        public static GuideFigureSettings Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing guide figure settings.");
                return current = Parse(resource.text);
            }
        }

        public GuideFigureDefinition For(string role)
            => role != null && Figures.TryGetValue(role, out var definition) ? definition : new GuideFigureDefinition();

        public static GuideFigureSettings Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<GuideFigureSettings>(json);
            if (result == null || result.SchemaVersion != 1 || result.Figures == null) throw new InvalidOperationException("Invalid guide figure settings.");
            foreach (var definition in result.Figures.Values)
            {
                if (definition == null || !MuseumExperienceConfiguration.Finite(definition.Height) || definition.Height < 1 || definition.Height > 2.2f
                    || definition.StripeCount < 0 || definition.StripeCount > 64 || !MuseumExperienceConfiguration.Finite(definition.StripeWidth)
                    || definition.StripeWidth < 0 || definition.StripeWidth > .5f)
                    throw new InvalidOperationException("Invalid guide figure.");
                foreach (var color in new[] { definition.SayoColor, definition.StripeColor, definition.TunicColor, definition.BeltColor, definition.HeadColor })
                    MuseumExperienceConfiguration.ColorValue(color);
                definition.Model ??= "";
            }
            return result;
        }
    }

    /// <summary>What the figure's head does; the woven figure has no gestures of the hands (D5).</summary>
    public enum GuideGesture { Idle, Listen, Think, Speak }

    /// <summary>
    /// A guide's figure, built at run time so that it never depends on how a build combines the museum's meshes (on the
    /// headset, static batching turns a museum piece's mesh into the whole room). The woven figure (owner's decision D5,
    /// 22/10/2026, pending review with the community) is a stylised human form without a face: a long wool sayo of thin
    /// stripes over a plain dark tunic, with a white belt, a garment shared by Kamëntsá and Inga and worn by anyone; the
    /// head is a smooth shape the colour of wood. It is opaque, warm and stands or sits on the ground; it never glows,
    /// floats or fades. It breathes, walks with a step, and turns, tilts or nods its head; it has no gestures of the hands.
    /// One mesh with a striped texture serves both guides.
    /// </summary>
    public sealed class GuideFigure : MonoBehaviour
    {
        public const string MaterialResource = "Guides/GuideFigure";
        /// <summary>Height of the hips above the floor when seated, in metres for a figure of 1.6 m.</summary>
        public const float SeatHeight = .45f;
        private const float Standard = 1.6f, Hip = .84f;
        private const int Sides = 20;
        private static readonly Dictionary<string, Mesh> Meshes = new();
        private readonly List<UnityEngine.Object> owned = new();
        private Transform head, body, leftLeg, rightLeg;
        private Quaternion headRest;
        private float unit = 1;
        private float breath;
        private float step;

        public float Height { get; private set; }
        public Renderer[] Renderers { get; private set; } = new Renderer[0];
        /// <summary>The figure is a replacement model rather than the woven one.</summary>
        public bool FromModel { get; private set; }
        public bool Seated { get; private set; }
        public Transform Head => head;
        public GuideGesture Gesture { get; set; }
        /// <summary>How fast the figure walks, from 0 (standing) to 1 (an ordinary pace): its legs step with it.</summary>
        public float Stride { get; set; }
        /// <summary>Where the head turns to, within a comfortable angle; null keeps it ahead.</summary>
        public Vector3? LookAt { get; set; }

        /// <summary>Builds the figure of a role under the parent, feet (or seat) at its origin, facing its forward.</summary>
        public static GuideFigure Build(Transform parent, string role, float scale = 1)
            => Build(parent, GuideFigureSettings.Current.For(role), scale);

        public static GuideFigure Build(Transform parent, GuideFigureDefinition definition, float scale = 1)
        {
            var root = new GameObject("Figure");
            root.transform.SetParent(parent, false);
            var figure = root.AddComponent<GuideFigure>();
            float height = definition.Height * Mathf.Max(.1f, scale);
            figure.Seated = definition.Seated;
            if (!string.IsNullOrEmpty(definition.Model) && figure.BuildModel(definition.Model, height)) return figure;
            figure.BuildWoven(definition, height);
            return figure;
        }

        private bool BuildModel(string path, float height)
        {
            var source = Resources.Load<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning("[GuideFigure] model " + path + " is missing; the woven figure is used.");
                return false;
            }
            var model = Instantiate(source, transform, false);
            model.name = "Model";
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Destroy(collider);
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Destroy(model); return false; }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.y < 1e-3f) { Destroy(model); return false; }
            float factor = height / bounds.size.y;
            model.transform.localScale *= factor;
            var foot = transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            model.transform.localPosition -= foot * factor;
            Renderers = renderers;
            Height = height;
            FromModel = true;
            head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            head.localPosition = Vector3.up * height * .93f;
            headRest = Quaternion.identity;
            return true;
        }

        private void BuildWoven(GuideFigureDefinition definition, float height)
        {
            unit = height / Standard;
            var authored = Resources.Load<Material>(MaterialResource);
            Material Tone(string name, string color, Texture2D texture = null, int repeat = 1)
            {
                var material = authored != null ? new Material(authored) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = name;
                var value = MuseumExperienceConfiguration.ColorValue(color);
                material.SetColor("_BaseColor", value);
                material.color = value;
                if (texture != null)
                {
                    material.SetTexture("_BaseMap", texture);
                    material.SetTextureScale("_BaseMap", new Vector2(repeat, 1));
                    material.SetColor("_BaseColor", Color.white);
                    material.color = Color.white;
                }
                owned.Add(material);
                return material;
            }
            var stripes = Stripes(definition);
            var sayo = Tone("GuideSayo", definition.SayoColor, stripes, Mathf.Max(1, definition.StripeCount));
            var tunic = Tone("GuideTunic", definition.TunicColor);
            var belt = Tone("GuideBelt", definition.BeltColor);
            var wood = Tone("GuideHead", definition.HeadColor);
            var parts = new List<Renderer>();
            // Seated, the body rests on the bench: the hips sit at the seat and the legs bend at the knee.
            float drop = Seated ? Hip - SeatHeight : 0;
            body = Part("Body", Seated ? SeatedBodyMesh() : BodyMesh(), new[] { tunic, belt, sayo }, new Vector3(0, -drop, 0), parts);
            head = Part("Head", HeadMesh(), new[] { wood }, new Vector3(0, 1.37f - drop, .01f), parts);
            if (Seated)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    var thigh = Part(side < 0 ? "LeftThigh" : "RightThigh", LegMesh(), new[] { tunic }, new Vector3(.09f * side, SeatHeight, 0), parts);
                    thigh.localRotation = Quaternion.Euler(-90, 0, 0);
                    thigh.localScale = new Vector3(1.1f, .5f, 1.1f) * unit;
                    var shin = Part(side < 0 ? "LeftShin" : "RightShin", LegMesh(), new[] { tunic }, new Vector3(.09f * side, SeatHeight, .4f), parts);
                    shin.localScale = new Vector3(1, SeatHeight / Hip, 1) * unit;
                }
            }
            else
            {
                leftLeg = Part("LeftLeg", LegMesh(), new[] { tunic }, new Vector3(-.085f, Hip, 0), parts);
                rightLeg = Part("RightLeg", LegMesh(), new[] { tunic }, new Vector3(.085f, Hip, 0), parts);
            }
            headRest = Quaternion.identity;
            Renderers = parts.ToArray();
            Height = Seated ? (1.37f + .23f - drop) * unit : height;
        }

        /// <summary>The sayo's stripes: one band of wool with a thin dark stripe, repeated around the garment.</summary>
        private Texture2D Stripes(GuideFigureDefinition definition)
        {
            const int width = 64;
            var texture = new Texture2D(width, 2, TextureFormat.RGBA32, false) { name = "GuideSayoStripes", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var wool = MuseumExperienceConfiguration.ColorValue(definition.SayoColor);
            var stripe = MuseumExperienceConfiguration.ColorValue(definition.StripeColor);
            int dark = Mathf.RoundToInt(width * definition.StripeWidth);
            var pixels = new Color[width * 2];
            for (int x = 0; x < width; x++) pixels[x] = pixels[x + width] = x < dark ? stripe : wool;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            owned.Add(texture);
            return texture;
        }

        private Transform Part(string name, Mesh mesh, Material[] tones, Vector3 position, List<Renderer> parts)
        {
            var part = new GameObject(name);
            part.transform.SetParent(transform, false);
            part.transform.localPosition = position * unit;
            part.transform.localScale = Vector3.one * unit;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = tones;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            parts.Add(renderer);
            return part.transform;
        }

        private void Update()
        {
            float delta = Time.deltaTime;
            breath += delta;
            if (body != null) body.localScale = new Vector3(1, 1 + Mathf.Sin(breath * 1.6f) * .005f, 1) * unit;
            float stride = Seated ? 0 : Mathf.Clamp01(Stride);
            step += delta * (4 + 3 * stride) * (stride > .02f ? 1 : 0);
            float swing = Mathf.Sin(step) * 20 * stride;
            if (leftLeg != null) leftLeg.localRotation = Quaternion.Euler(swing, 0, 0);
            if (rightLeg != null) rightLeg.localRotation = Quaternion.Euler(-swing, 0, 0);
            if (head == null) return;
            var wanted = headRest;
            if (LookAt.HasValue)
            {
                var local = transform.InverseTransformPoint(LookAt.Value) - head.localPosition;
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -60, 60);
                float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20, 20);
                wanted = Quaternion.Euler(pitch, yaw, 0);
            }
            switch (Gesture)
            {
                case GuideGesture.Listen: wanted *= Quaternion.Euler(4, 0, 8); break;
                case GuideGesture.Think: wanted *= Quaternion.Euler(-5 + Mathf.Sin(breath * 1.2f) * 2, Mathf.Sin(breath * .9f) * 7, -3); break;
                case GuideGesture.Speak: wanted *= Quaternion.Euler(Mathf.Sin(breath * 5.5f) * 3, 0, 0); break;
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, wanted, 1 - Mathf.Exp(-delta * 5));
        }

        private void OnDestroy()
        {
            foreach (var item in owned)
                if (item != null) { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
            owned.Clear();
        }

        // The garment's profiles: height above the floor and radius, in metres for a figure of 1.6 m standing.
        private static readonly (float, float)[] Tunic = { (.12f, .16f), (.5f, .175f), (.84f, .19f), (.98f, .18f), (1.05f, .18f), (1.2f, .19f),
            (1.28f, .19f), (1.33f, .15f), (1.36f, .09f), (1.38f, .05f), (1.46f, .046f), (1.48f, 0f) };
        private static readonly (float, float)[] Belt = { (.95f, .188f), (.96f, .196f), (1.03f, .196f), (1.04f, .186f) };
        // The sayo hangs from the shoulders to below the knees, open at the sides where the tunic and the belt show.
        private static readonly (float, float)[] Sayo = { (.42f, .2f), (.44f, .225f), (.8f, .205f), (1.0f, .182f), (1.18f, .2f), (1.28f, .215f),
            (1.33f, .19f), (1.37f, .12f), (1.39f, .07f), (1.41f, .05f) };

        /// <summary>The standing garment: the tunic (submesh 0), the belt (submesh 1) and the striped sayo (submesh 2), in one mesh.</summary>
        public static Mesh BodyMesh() => Cached("GuideBody", () => Lathe(new[] { Tunic, Belt, Sayo }, new[] { .78f, .78f, 1.35f }, Vector3.zero, 0));

        /// <summary>
        /// The seated garment: the same parts from the hips up, with the sayo's hem resting at the seat; the tunic covers the
        /// legs, built apart.
        /// </summary>
        public static Mesh SeatedBodyMesh() => Cached("GuideSeatedBody", () =>
        {
            (float, float)[] From(IEnumerable<(float, float)> profile, float lowest, float hem)
                => new[] { (lowest, hem) }.Concat(profile.Where(ring => ring.Item1 > lowest + .01f)).ToArray();
            return Lathe(new[] { From(Tunic, Hip, .2f), Belt, From(Sayo, Hip + .02f, .24f) }, new[] { .78f, .78f, 1.35f }, Vector3.zero, 0);
        });

        /// <summary>An oval head without features, a little deeper than wide, standing on the neck.</summary>
        public static Mesh HeadMesh() => Cached("GuideHead", () =>
        {
            var profile = new List<(float, float)>();
            for (int index = 0; index <= 12; index++)
            {
                float angle = Mathf.PI * index / 12;
                profile.Add((.115f - Mathf.Cos(angle) * .115f, Mathf.Sin(angle) * .093f));
            }
            return Lathe(new[] { profile.ToArray() }, new[] { 1.12f }, Vector3.zero);
        });

        /// <summary>One leg hanging from the hip, where it pivots, down to the sole.</summary>
        public static Mesh LegMesh() => Cached("GuideLeg", () =>
        {
            var leg = new[] { (0f, 0f), (.03f, .08f), (.4f, .066f), (.78f, .066f), (.83f, .07f), (.84f, 0f) };
            return Lathe(new[] { leg }, new[] { .9f }, Vector3.zero, -1, true);
        });

        private static Mesh Cached(string name, Func<Mesh> make)
        {
            if (Meshes.TryGetValue(name, out var mesh) && mesh != null) return mesh;
            mesh = make();
            mesh.name = name;
            return Meshes[name] = mesh;
        }

        /// <summary>
        /// Revolves height–radius profiles around the vertical, one submesh per profile, each with an oval section whose
        /// depth is its <paramref name="depths"/> times its width. The profile numbered <paramref name="capped"/> is closed at
        /// its lowest ring. Texture coordinates run once around (u) and up the height (v). A downward profile hangs below
        /// its origin.
        /// </summary>
        public static Mesh Lathe((float height, float radius)[][] profiles, float[] depths, Vector3 offset, int capped = -1, bool downward = false)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<List<int>>();
            for (int number = 0; number < profiles.Length; number++)
            {
                var profile = profiles[number];
                float depth = depths[Mathf.Min(number, depths.Length - 1)];
                var triangles = new List<int>();
                int start = vertices.Count;
                for (int ring = 0; ring < profile.Length; ring++)
                {
                    var (height, radius) = profile[ring];
                    // The slope of the profile at this ring gives the normal's vertical part.
                    var before = profile[Mathf.Max(0, ring - 1)];
                    var after = profile[Mathf.Min(profile.Length - 1, ring + 1)];
                    float rise = after.height - before.height, spread = after.radius - before.radius;
                    for (int side = 0; side <= Sides; side++)
                    {
                        float angle = 2 * Mathf.PI * side / Sides;
                        float x = Mathf.Sin(angle), z = Mathf.Cos(angle);
                        float y = downward ? -height : height;
                        vertices.Add(offset + new Vector3(x * radius, y, z * radius * depth));
                        var outward = new Vector3(x, 0, z / depth).normalized;
                        var normal = (outward * Mathf.Abs(rise) + Vector3.up * (downward ? spread : -spread) * Mathf.Sign(rise == 0 ? 1 : rise)).normalized;
                        normals.Add(normal.sqrMagnitude > 0 ? normal : outward);
                        uvs.Add(new Vector2((float)side / Sides, height));
                    }
                }
                for (int ring = 0; ring < profile.Length - 1; ring++)
                    for (int side = 0; side < Sides; side++)
                    {
                        int a = start + ring * (Sides + 1) + side, b = a + 1, c = a + Sides + 1, d = c + 1;
                        // Unity draws clockwise faces; a hanging profile runs the other way round.
                        if (downward) { triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(b); triangles.Add(c); triangles.Add(d); }
                        else { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(d); triangles.Add(c); }
                    }
                if (number == capped && profile[0].radius > 0)
                {
                    int centre = vertices.Count;
                    vertices.Add(offset + Vector3.up * (downward ? -profile[0].height : profile[0].height));
                    normals.Add(Vector3.down);
                    uvs.Add(new Vector2(.5f, profile[0].height));
                    for (int side = 0; side < Sides; side++) { triangles.Add(centre); triangles.Add(start + side + 1); triangles.Add(start + side); }
                }
                submeshes.Add(triangles);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int index = 0; index < submeshes.Count; index++) mesh.SetTriangles(submeshes[index], index);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Bounds of every renderer of the figure, in world space.</summary>
        public Bounds WorldBounds()
        {
            var shown = Renderers.Where(renderer => renderer != null).ToArray();
            if (shown.Length == 0) return new Bounds(transform.position, Vector3.zero);
            var bounds = shown[0].bounds;
            foreach (var renderer in shown) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
