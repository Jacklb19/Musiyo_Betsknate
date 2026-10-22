using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>How one guide looks: a model named in Resources, or the neutral figure in the given tones.</summary>
    [Preserve]
    public sealed class GuideFigureDefinition
    {
        /// <summary>
        /// Resources path of a model that replaces the neutral figure, empty for the neutral one. The model stands on its
        /// origin, faces +Z and is scaled to <see cref="Height"/>; nothing of it is read from the museum model.
        /// </summary>
        [JsonProperty("model")] public string Model = "";
        [JsonProperty("height_m")] public float Height = 1.62f;
        [JsonProperty("garment_color")] public string GarmentColor = "#5E4B3C";
        [JsonProperty("trim_color")] public string TrimColor = "#3E3129";
        [JsonProperty("head_color")] public string HeadColor = "#C9B29A";
    }

    /// <summary>
    /// The figures of the two guides, read from <c>Resources/MuseumGuideFigures.json</c>: <c>companion</c>, who walks with
    /// the visitor, and <c>interviewer</c>, who waits by the hearth. Changing a figure is a change of this file only.
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
                if (definition == null || !MuseumExperienceConfiguration.Finite(definition.Height) || definition.Height < 1 || definition.Height > 2.2f)
                    throw new InvalidOperationException("Invalid guide figure height.");
                MuseumExperienceConfiguration.ColorValue(definition.GarmentColor);
                MuseumExperienceConfiguration.ColorValue(definition.TrimColor);
                MuseumExperienceConfiguration.ColorValue(definition.HeadColor);
                definition.Model ??= "";
            }
            return result;
        }
    }

    public enum GuideGesture { Idle, Listen, Think, Speak, Wave }

    /// <summary>
    /// A guide's figure, built at run time so that it never depends on how a build combines the museum's meshes (on the
    /// headset, static batching turns a museum piece's mesh into the whole room). The neutral figure is a smooth, faceless
    /// standing form in plain tones: it represents nobody and carries no mask or costume. It breathes, turns its head
    /// toward someone, tilts it while listening, nods while thinking or speaking, and can raise a hand to invite.
    /// </summary>
    public sealed class GuideFigure : MonoBehaviour
    {
        public const string MaterialResource = "Guides/GuideFigure";
        private const int Sides = 20;
        private static readonly Dictionary<string, Mesh> Meshes = new();
        private readonly List<Material> materials = new();
        private Transform head, leftArm, rightArm, body;
        private Quaternion headRest, leftRest, rightRest;
        private float breath;
        private float wave;

        public float Height { get; private set; }
        public Renderer[] Renderers { get; private set; } = new Renderer[0];
        /// <summary>The figure is a replacement model rather than the neutral one.</summary>
        public bool FromModel { get; private set; }
        public Transform Head => head;
        public GuideGesture Gesture { get; set; }
        /// <summary>Where the head turns to, within a comfortable angle; null keeps it ahead.</summary>
        public Vector3? LookAt { get; set; }

        /// <summary>Builds the figure of a role under the parent, feet at its origin, facing its forward.</summary>
        public static GuideFigure Build(Transform parent, string role, float scale = 1)
            => Build(parent, GuideFigureSettings.Current.For(role), scale);

        public static GuideFigure Build(Transform parent, GuideFigureDefinition definition, float scale = 1)
        {
            var root = new GameObject("Figure");
            root.transform.SetParent(parent, false);
            var figure = root.AddComponent<GuideFigure>();
            float height = definition.Height * Mathf.Max(.1f, scale);
            if (!string.IsNullOrEmpty(definition.Model) && figure.BuildModel(definition.Model, height)) return figure;
            figure.BuildNeutral(definition, height);
            return figure;
        }

        private bool BuildModel(string path, float height)
        {
            var source = Resources.Load<GameObject>(path);
            if (source == null)
            {
                Debug.LogWarning("[GuideFigure] model " + path + " is missing; the neutral figure is used.");
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

        private void BuildNeutral(GuideFigureDefinition definition, float height)
        {
            float unit = height / 1.62f;
            var authored = Resources.Load<Material>(MaterialResource);
            Material Tone(string name, string color)
            {
                var material = authored != null ? new Material(authored) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.name = name;
                var value = MuseumExperienceConfiguration.ColorValue(color);
                material.SetColor("_BaseColor", value);
                material.color = value;
                materials.Add(material);
                return material;
            }
            var garment = Tone("GuideGarment", definition.GarmentColor);
            var trim = Tone("GuideTrim", definition.TrimColor);
            var skin = Tone("GuideHead", definition.HeadColor);
            var parts = new List<Renderer>();
            body = Part("Body", transform, BodyMesh(), new[] { garment, trim }, Vector3.zero, unit, parts);
            head = Part("Head", transform, HeadMesh(), new[] { skin }, new Vector3(0, 1.39f, .01f) * unit, unit, parts);
            leftArm = Part("LeftArm", transform, ArmMesh(), new[] { garment, skin }, new Vector3(-.215f, 1.31f, 0) * unit, unit, parts);
            rightArm = Part("RightArm", transform, ArmMesh(), new[] { garment, skin }, new Vector3(.215f, 1.31f, 0) * unit, unit, parts);
            leftArm.localRotation = leftRest = Quaternion.Euler(0, 0, -6);
            rightArm.localRotation = rightRest = Quaternion.Euler(0, 0, 6);
            headRest = Quaternion.identity;
            Renderers = parts.ToArray();
            Height = height;
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Material[] tones, Vector3 position, float unit, List<Renderer> parts)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
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
            if (body != null) body.localScale = new Vector3(1, 1 + Mathf.Sin(breath * 1.6f) * .006f, 1) * (Height / 1.62f);
            if (head == null) return;
            var wanted = headRest;
            if (LookAt.HasValue)
            {
                var local = transform.InverseTransformPoint(LookAt.Value) - head.localPosition;
                float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -55, 55);
                float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20, 20);
                wanted = Quaternion.Euler(pitch, yaw, 0);
            }
            switch (Gesture)
            {
                case GuideGesture.Listen: wanted *= Quaternion.Euler(4, 0, 9); break;
                case GuideGesture.Think: wanted *= Quaternion.Euler(-6 + Mathf.Sin(breath * 1.2f) * 2, Mathf.Sin(breath * .9f) * 8, -4); break;
                case GuideGesture.Speak: wanted *= Quaternion.Euler(Mathf.Sin(breath * 5.5f) * 3, 0, 0); break;
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, wanted, 1 - Mathf.Exp(-delta * 6));
            if (rightArm == null) return;
            wave = Mathf.MoveTowards(wave, Gesture == GuideGesture.Wave ? 1 : 0, delta * 2.5f);
            // An open hand raised beside the head, swaying gently: the common gesture of inviting someone over.
            var raised = Quaternion.Euler(0, 0, 150 + Mathf.Sin(breath * 5) * 14);
            rightArm.localRotation = Quaternion.Slerp(rightRest, raised, Smooth(wave));
            var speaking = Gesture == GuideGesture.Speak ? Quaternion.Euler(-18 + Mathf.Sin(breath * 2.2f) * 6, 0, -10) : leftRest;
            leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, speaking, 1 - Mathf.Exp(-delta * 3));
        }

        private static float Smooth(float value) => value * value * (3 - 2 * value);

        private void OnDestroy()
        {
            foreach (var material in materials)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            materials.Clear();
        }

        /// <summary>
        /// A long garment from hem to shoulders on an oval section (submesh 0), with a darker hem band (submesh 1), and the
        /// neck. Radii are metres for a figure of 1.62 m; the parts are scaled for other heights.
        /// </summary>
        public static Mesh BodyMesh() => Cached("GuideBody", () =>
        {
            var hem = new[] { (0f, .235f), (.02f, .25f), (.16f, .24f) };
            var garment = new[] { (.16f, .24f), (.5f, .205f), (.9f, .165f), (1.05f, .175f), (1.2f, .2f), (1.29f, .205f),
                (1.34f, .17f), (1.37f, .1f), (1.39f, .055f), (1.48f, .05f), (1.5f, 0f) };
            return Lathe(new[] { garment, hem }, .74f, Vector3.zero, true);
        });

        /// <summary>An oval head without a face, a little deeper than wide, standing on the neck.</summary>
        public static Mesh HeadMesh() => Cached("GuideHead", () =>
        {
            var profile = new List<(float, float)>();
            for (int step = 0; step <= 12; step++)
            {
                float angle = Mathf.PI * step / 12;
                profile.Add((.115f - Mathf.Cos(angle) * .115f, Mathf.Sin(angle) * .095f));
            }
            return Lathe(new[] { profile.ToArray() }, 1.12f, Vector3.zero, false);
        });

        /// <summary>A sleeve hanging from the shoulder (submesh 0) and the hand at its end (submesh 1), pivoting at the shoulder.</summary>
        public static Mesh ArmMesh() => Cached("GuideArm", () =>
        {
            var sleeve = new[] { (0f, 0f), (.01f, .05f), (.3f, .052f), (.5f, .058f), (.53f, .045f) };
            var hand = new List<(float, float)> { (.53f, .045f) };
            for (int step = 1; step <= 6; step++)
            {
                float angle = Mathf.PI * .5f * step / 6;
                hand.Add((.53f + Mathf.Sin(angle) * .085f, Mathf.Cos(angle) * .042f));
            }
            // The profile runs downward from the shoulder.
            return Lathe(new[] { sleeve, hand.ToArray() }, .9f, Vector3.zero, false, true);
        });

        private static Mesh Cached(string name, Func<Mesh> make)
        {
            if (Meshes.TryGetValue(name, out var mesh) && mesh != null) return mesh;
            mesh = make();
            mesh.name = name;
            return Meshes[name] = mesh;
        }

        /// <summary>
        /// Revolves height–radius profiles around the vertical, one submesh per profile, with an oval section whose depth is
        /// <paramref name="depth"/> times its width. A downward profile hangs below its origin.
        /// </summary>
        public static Mesh Lathe((float height, float radius)[][] profiles, float depth, Vector3 offset, bool closeBottom, bool downward = false)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var submeshes = new List<List<int>>();
            foreach (var profile in profiles)
            {
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
                if (closeBottom && profile[0].radius > 0 && profile[0].height <= .001f)
                {
                    int centre = vertices.Count;
                    vertices.Add(offset + Vector3.up * profile[0].height);
                    normals.Add(Vector3.down);
                    for (int side = 0; side < Sides; side++) { triangles.Add(centre); triangles.Add(start + side + 1); triangles.Add(start + side); }
                }
                submeshes.Add(triangles);
            }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
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
