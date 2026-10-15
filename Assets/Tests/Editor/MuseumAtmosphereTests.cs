using System;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumAtmosphereTests
    {
        [Test]
        public void TheShippedAtmosphereLightsTheVisitAndKeepsHeadsetShadowsApart()
        {
            var atmosphere = MuseumAtmosphere.Current;
            Assert.That(MuseumAtmosphere.Shadows(atmosphere.SunShadowsHeadset), Is.EqualTo(LightShadows.None),
                "Headset shadows stay off until they are measured on the device.");
            var sun = new GameObject("AtmosphereTestSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            // The brightest directional light is the sun; the open test scene may hold its own.
            sun.intensity = 100;
            var fog = RenderSettings.fog;
            var mode = RenderSettings.ambientMode;
            var sky = RenderSettings.skybox;
            try
            {
                atmosphere.Apply();
                Assert.That(RenderSettings.fog, Is.True);
                Assert.That(RenderSettings.fogEndDistance, Is.GreaterThan(RenderSettings.fogStartDistance));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(sun.intensity, Is.EqualTo(atmosphere.SunIntensity));
                Assert.That(sun.shadows, Is.EqualTo(MuseumAtmosphere.Shadows(atmosphere.SunShadows)));
                Assert.That(RenderSettings.skybox, Is.Not.Null.And.Property("name").EqualTo(MuseumAtmosphere.SkyMaterial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sun.gameObject);
                RenderSettings.fog = fog;
                RenderSettings.ambientMode = mode;
                RenderSettings.skybox = sky;
            }
        }

        [TestCase("{\"schema_version\":2}")]
        [TestCase("{\"schema_version\":1,\"fog_color\":\"#FFFFFF\",\"fog_start\":50,\"fog_end\":10,\"sun_color\":\"#FFFFFF\",\"sun_intensity\":1,\"ambient_sky\":\"#FFFFFF\",\"ambient_equator\":\"#FFFFFF\",\"ambient_ground\":\"#FFFFFF\"}")]
        [TestCase("{\"schema_version\":1,\"fog_color\":\"#FFFFFF\",\"fog_start\":5,\"fog_end\":10,\"sun_color\":\"#FFFFFF\",\"sun_intensity\":1,\"sun_shadows\":\"blurry\",\"ambient_sky\":\"#FFFFFF\",\"ambient_equator\":\"#FFFFFF\",\"ambient_ground\":\"#FFFFFF\"}")]
        public void AnInvalidAtmosphereIsRejected(string json)
            => Assert.Throws<InvalidOperationException>(() => MuseumAtmosphere.Parse(json));
    }
}
