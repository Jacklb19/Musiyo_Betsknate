using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A living fire built in layers at its own position: a bed of glowing embers, animated flame tongues that tear into
    /// wisps, a brighter core, sparks carried up on the heat and thin smoke, lit by a steady fire light and a smaller one
    /// that dances above the flames. Used by the hearth of the entry set and by the museum's own hearth. Its particle
    /// materials are built from noise by the editor (MuseumFireAssets); without them it builds nothing.
    /// </summary>
    public sealed class MuseumFire : MonoBehaviour
    {
        public const string FlameMaterial = "EntryFireFlame", SmokeMaterial = "EntryFireSmoke", SparkMaterial = "EntryFireSpark";
        /// <summary>Delivered placeholder of the museum's hearth fire, replaced by a living fire at runtime.</summary>
        public const string MuseumPlaceholder = "Fogon_Fuego_Provisional";
        private Light steady;
        private Light dancing;
        private float seed;

        /// <summary>How bright the fire burns this moment, from 0 to 1.</summary>
        public float Level { get; private set; }

        /// <summary>True when the fire's particle materials exist in this build.</summary>
        public static bool Available => Resources.Load<Material>(FlameMaterial) != null;

        /// <summary>Builds a fire under <paramref name="parent"/> at a local spot on the floor; null when it cannot be built.</summary>
        public static MuseumFire Create(Transform parent, Vector3 spot, float range = 6)
        {
            if (!Available) return null;
            var holder = new GameObject("LivingFire");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = spot;
            var fire = holder.AddComponent<MuseumFire>();
            fire.Build(range);
            return fire;
        }

        /// <summary>
        /// Replaces the delivered placeholder flame of the museum's hearth with a living fire standing on the same spot.
        /// Nothing happens when the museum has no such placeholder or the fire's materials are missing.
        /// </summary>
        public static MuseumFire ReplaceMuseumPlaceholder()
        {
            if (!Available) return null;
            var placeholder = FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(item => item.name == MuseumPlaceholder);
            if (placeholder == null) return null;
            var bounds = placeholder.bounds;
            placeholder.enabled = false;
            var holder = new GameObject("LivingFire_Hearth");
            holder.transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var fire = holder.AddComponent<MuseumFire>();
            fire.Build(5);
            return fire;
        }

        private void Build(float range)
        {
            seed = Random.value * 100;
            var embers = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            embers.name = "EmberBed";
            var solid = embers.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(solid); else DestroyImmediate(solid);
            embers.transform.SetParent(transform, false);
            embers.transform.localPosition = Vector3.up * .03f;
            embers.transform.localScale = new Vector3(.5f, .07f, .5f);
            Paint(embers.GetComponent<MeshRenderer>(), "M_Fuego", new Color(1f, .35f, .08f));
            steady = Light("FireLight", new Color(1f, .58f, .28f), range, 1.6f, Vector3.up * .75f);
            dancing = Light("FireFlicker", new Color(1f, .76f, .42f), range * .5f, .8f, Vector3.up * .6f);
            Tongues("Flames", .17f, new ParticleSystem.MinMaxCurve(.75f, 1.15f), 34, new Color(1f, .58f, .2f), .7f, 1.1f);
            Tongues("FlameCore", .08f, new ParticleSystem.MinMaxCurve(.45f, .7f), 24, new Color(1f, .86f, .5f), .5f, .8f);
            Sparks();
            Smoke();
        }

        private void Update()
        {
            if (steady == null) return;
            float time = Time.unscaledTime + seed;
            float flicker = .6f * Mathf.PerlinNoise(time * 3.1f, .3f) + .25f * Mathf.PerlinNoise(time * 11f, 4.1f);
            steady.intensity = 1.3f + flicker;
            Level = Mathf.Clamp01(flicker / .85f);
            // The smaller light dances above the flames, so the light seems to move as fire does.
            dancing.intensity = .5f + .7f * Mathf.PerlinNoise(time * 7.3f, 9.2f);
            dancing.transform.localPosition = new Vector3((Mathf.PerlinNoise(time * 2.3f, 1) - .5f) * .25f,
                .55f + Mathf.PerlinNoise(time * 3.7f, 2) * .25f, (Mathf.PerlinNoise(time * 2.9f, 3) - .5f) * .25f);
        }

        private Light Light(string name, Color color, float range, float intensity, Vector3 offset)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = offset;
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        private ParticleSystem Particles(string name, Vector3 offset, string material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            item.transform.localPosition = offset;
            var system = item.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = item.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>(material);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        /// <summary>Animated flame tongues from the flipbook, rising, swelling and dying away.</summary>
        private void Tongues(string name, float radius, ParticleSystem.MinMaxCurve size, int rate, Color body, float shortest, float longest)
        {
            var system = Particles(name, Vector3.up * .12f, FlameMaterial);
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(shortest, longest);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.18f, .4f);
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(-.18f, .18f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = rate * 2;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            // The flame grows up from where it is born, instead of being centred on it and half sunk in the embers.
            renderer.pivot = new Vector3(0, .45f, 0);
            var emission = system.emission;
            emission.rateOverTime = rate;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7;
            shape.radius = radius;
            shape.rotation = new Vector3(-90, 0, 0);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, .95f, .75f), 0), new GradientColorKey(body, .35f),
                    new GradientColorKey(new Color(.7f, .16f, .05f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.95f, .15f), new GradientAlphaKey(.7f, .6f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .55f), new Keyframe(.35f, 1), new Keyframe(1, .35f)));
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.cycleCount = 1;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .12f;
            noise.frequency = 1.2f;
            noise.scrollSpeed = .6f;
            system.Play();
        }

        /// <summary>Sparks carried up by the heat, wandering and fading as they cool.</summary>
        private void Sparks()
        {
            var system = Particles("Sparks", Vector3.up * .2f, SparkMaterial);
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .028f);
            main.startColor = new Color(1f, .7f, .3f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 40;
            main.gravityModifier = -.05f;
            var emission = system.emission;
            emission.rateOverTime = 7;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 2, 5, 0, 1.7f) });
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14;
            shape.radius = .12f;
            shape.rotation = new Vector3(-90, 0, 0);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, .85f, .5f), 0), new GradientColorKey(new Color(1f, .35f, .08f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .6f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .45f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = .7f;
            system.Play();
        }

        /// <summary>Thin smoke above the flames, spreading and thinning as it rises.</summary>
        private void Smoke()
        {
            var system = Particles("Smoke", Vector3.up * .75f, SmokeMaterial);
            var main = system.main;
            main.loop = true;
            main.prewarm = true;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .45f);
            main.startSize = new ParticleSystem.MinMaxCurve(.35f, .6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(.16f, .14f, .13f, .32f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 30;
            var emission = system.emission;
            emission.rateOverTime = 5;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10;
            shape.radius = .12f;
            shape.rotation = new Vector3(-90, 0, 0);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(.5f, .6f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var growth = system.sizeOverLifetime;
            growth.enabled = true;
            growth.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(1, 2.6f)));
            var spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-.4f, .4f);
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = .2f;
            noise.frequency = .5f;
            noise.scrollSpeed = .3f;
            system.Play();
        }

        /// <summary>Uses a material of the delivered museum when the scene has it, and a plain one otherwise.</summary>
        private static void Paint(MeshRenderer renderer, string museumMaterial, Color fallback)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var found = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SelectMany(candidate => candidate.sharedMaterials).FirstOrDefault(material => material != null && material.name == museumMaterial);
            if (found != null) { renderer.sharedMaterial = found; return; }
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", fallback);
            renderer.SetPropertyBlock(tint);
        }
    }
}
