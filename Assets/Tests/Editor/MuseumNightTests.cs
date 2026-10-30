using System;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The night of the museum (owner, 28/10/2026): stars and a faint moon instead of the sun, night fog and ambient, the
    /// lanterns lit with light under them, warmer rooms; the day comes back exactly as delivered, and the night waits
    /// while the title's or the credits' set stands in front of the museum.
    /// </summary>
    public sealed class MuseumNightTests
    {
        private GameObject sunObject, lampObject, groundObject, holder;
        private Light sun;
        private Material lampMaterial;
        private bool fog;
        private FogMode fogMode;
        private Color fogColor, ambientSky, ambientEquator, ambientGround;
        private float fogStart, fogEnd, reflection;
        private AmbientMode ambientMode;
        private Material skybox;
        private Light previousSun;

        [SetUp]
        public void Keep()
        {
            fog = RenderSettings.fog; fogMode = RenderSettings.fogMode; fogColor = RenderSettings.fogColor;
            fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance;
            ambientMode = RenderSettings.ambientMode; ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor; ambientGround = RenderSettings.ambientGroundColor;
            reflection = RenderSettings.reflectionIntensity; skybox = RenderSettings.skybox; previousSun = RenderSettings.sun;
        }

        [TearDown]
        public void Restore()
        {
            foreach (var item in new[] { holder, lampObject, groundObject, sunObject }) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            if (lampMaterial != null) UnityEngine.Object.DestroyImmediate(lampMaterial);
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator; RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.reflectionIntensity = reflection; RenderSettings.skybox = skybox; RenderSettings.sun = previousSun;
        }

        /// <summary>A sun, the ground, and one lantern standing over it with the museum's lantern glass.</summary>
        private MuseumNight Museum()
        {
            sunObject = new GameObject("NightTestSun");
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 100;
            sunObject.transform.rotation = Quaternion.Euler(40, 30, 0);
            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "NightTestGround";
            groundObject.transform.position = new Vector3(0, -.5f, 0);
            groundObject.transform.localScale = new Vector3(20, 1, 20);
            lampObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lampObject.name = "NightTestLantern";
            UnityEngine.Object.DestroyImmediate(lampObject.GetComponent<Collider>());
            lampObject.transform.position = new Vector3(2, 1.5f, 3);
            lampObject.transform.localScale = Vector3.one * .3f;
            lampMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PathLantern_Glass" };
            lampMaterial.SetColor("_EmissionColor", new Color(1.5f, .8f, .3f));
            lampObject.GetComponent<MeshRenderer>().sharedMaterial = lampMaterial;
            Physics.SyncTransforms();
            MuseumAtmosphere.Current.Apply();
            holder = new GameObject("NightTestMuseum");
            var night = holder.AddComponent<MuseumNight>();
            night.Configure(null);
            return night;
        }

        [Test]
        public void TheShippedNightIsValidAndAnInvalidOneIsRejected()
        {
            var night = MuseumNightAtmosphere.Current;
            Assert.That(night.AutoNightAtOverlook, Is.False, "The overlook's nightfall is a proposal, off until the owner approves it.");
            Assert.That(night.LampMaterials, Does.Contain("PathLantern_Glass"));
            Assert.That(night.Indoor("room.hearth"), Is.True);
            Assert.That(night.Indoor("room.arrival"), Is.False, "The arrival is open air.");
            Assert.That(night.Indoor(null), Is.False);
            Assert.Throws<InvalidOperationException>(() => MuseumNightAtmosphere.Parse("{\"schema_version\":2}"));
            Assert.Throws<InvalidOperationException>(() => MuseumNightAtmosphere.Parse(
                Resources.Load<TextAsset>(MuseumNightAtmosphere.ResourceName).text.Replace("\"fog_end\": 150", "\"fog_end\": 5")));
            var (sky, equator, ground) = night.Ambient(1);
            Assert.That(sky, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(night.IndoorAmbientSky)));
            Assert.That(night.Ambient(0).sky, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(night.AmbientSky)));
            Assert.That(sky.grayscale, Is.GreaterThan(night.Ambient(0).sky.grayscale * .9f), "Inside a room the night is not darker than outside.");
        }

        [Test]
        public void NightFallsAndTheDayReturnsExactlyAsDelivered()
        {
            var night = Museum();
            var settings = MuseumNightAtmosphere.Current;
            var day = (fog: RenderSettings.fogColor, start: RenderSettings.fogStartDistance, end: RenderSettings.fogEndDistance,
                sky: RenderSettings.ambientSkyColor, equator: RenderSettings.ambientEquatorColor, ground: RenderSettings.ambientGroundColor,
                box: RenderSettings.skybox, color: sun.color, intensity: sun.intensity, reflection: RenderSettings.reflectionIntensity);
            var renderer = lampObject.GetComponent<MeshRenderer>();

            night.Set(true);
            Assert.That(night.Level, Is.EqualTo(1), "The saved choice is there from the first frame.");
            Assert.That(RenderSettings.fogColor, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(settings.FogColor)));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(settings.FogEnd));
            Assert.That(sun.color, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(settings.MoonColor)), "The sun becomes the moon.");
            Assert.That(sun.intensity, Is.EqualTo(settings.MoonIntensity));
            Assert.That(RenderSettings.skybox, Is.Not.Null.And.Property("name").EqualTo(MuseumNight.SkyMaterialName));
            Assert.That(RenderSettings.skybox.GetTexture("_MainTex"), Is.Not.Null, "Stars and the moon.");
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(settings.ReflectionIntensity));
            Assert.That(night.Lamps.Count, Is.EqualTo(1), "The lantern is found by its glass.");
            Assert.That(renderer.HasPropertyBlock(), Is.True);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 0);
            Assert.That(block.GetColor("_EmissionColor").r, Is.EqualTo(1.5f * settings.LampEmission).Within(.001f), "The lantern glows brighter.");
            Assert.That(night.Pools, Is.Not.Null);
            Assert.That(night.Pools.enabled, Is.True);
            var pool = night.Pools.bounds;
            Assert.That(pool.center.x, Is.EqualTo(2).Within(.01f));
            Assert.That(pool.center.z, Is.EqualTo(3).Within(.01f));
            Assert.That(pool.center.y, Is.EqualTo(.03f).Within(.01f), "The pool lies on the ground under the lantern.");
            Assert.That(pool.size.x, Is.EqualTo(settings.LampPoolSize).Within(.01f));

            night.Set(false, 0);
            Assert.That(night.Level, Is.Zero);
            Assert.That(RenderSettings.fogColor, Is.EqualTo(day.fog));
            Assert.That(RenderSettings.fogStartDistance, Is.EqualTo(day.start));
            Assert.That(RenderSettings.fogEndDistance, Is.EqualTo(day.end));
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(day.sky));
            Assert.That(RenderSettings.ambientEquatorColor, Is.EqualTo(day.equator));
            Assert.That(RenderSettings.ambientGroundColor, Is.EqualTo(day.ground));
            Assert.That(RenderSettings.skybox, Is.SameAs(day.box));
            Assert.That(RenderSettings.skybox.GetFloat("_Exposure"), Is.EqualTo(MuseumAtmosphere.Current.SkyExposure));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(day.reflection));
            Assert.That(sun.color, Is.EqualTo(day.color));
            Assert.That(sun.intensity, Is.EqualTo(day.intensity));
            Assert.That(renderer.HasPropertyBlock(), Is.False, "The lantern gets its own material back.");
            Assert.That(night.Pools.enabled, Is.False);
        }

        [Test]
        public void TheVisitorsSwitchFadesThroughDarknessWithoutMixingSkies()
        {
            var night = Museum();
            night.Set(false, 0);
            var daySky = RenderSettings.skybox;
            night.Set(true, 1);
            night.Step(.3f);
            Assert.That(night.Level, Is.EqualTo(.3f).Within(.001f));
            Assert.That(RenderSettings.skybox, Is.SameAs(daySky), "The day's sky first darkens away.");
            Assert.That(RenderSettings.skybox.GetFloat("_Exposure"), Is.LessThan(MuseumAtmosphere.Current.SkyExposure));
            night.Step(.4f);
            Assert.That(RenderSettings.skybox.name, Is.EqualTo(MuseumNight.SkyMaterialName), "Then the night's rises.");
            night.Step(1);
            Assert.That(night.Level, Is.EqualTo(1));
            Assert.That(RenderSettings.skybox.GetFloat("_Exposure"), Is.EqualTo(MuseumNightAtmosphere.Current.SkyExposure).Within(.001f));
            night.Set(false, 0);
            Assert.That(RenderSettings.skybox.GetFloat("_Exposure"), Is.EqualTo(MuseumAtmosphere.Current.SkyExposure), "The day's sky is whole again.");
        }

        [Test]
        public void TheNightWaitsWhileTheTitleOrTheCreditsSetIsShown()
        {
            var night = Museum();
            night.Set(false, 0);
            var set = holder.AddComponent<MuseumEntryBackdrop>();
            set.Configure(EntryBackdropKind.Dark);
            var camera = new GameObject("NightTestCamera").AddComponent<Camera>();
            try
            {
                set.Show(true, camera);
                var setFog = RenderSettings.fogColor;
                night.Set(true, 1);
                night.Step(.5f);
                Assert.That(RenderSettings.fogColor, Is.EqualTo(setFog), "The set's own darkness is not touched.");
                set.Show(false, camera);
                night.Step(.1f);
                Assert.That(night.Level, Is.EqualTo(1), "Out of view, the change is made at once when the museum returns.");
                Assert.That(RenderSettings.fogColor, Is.EqualTo(MuseumExperienceConfiguration.ColorValue(MuseumNightAtmosphere.Current.FogColor)));
            }
            finally { UnityEngine.Object.DestroyImmediate(camera.gameObject); }
        }

        [Test]
        public void TheMoonShinesWhereTheSunStoodAndTheStarsStayElsewhere()
        {
            int width = MuseumEntryBackdrop.NightSkyWidth, height = MuseumEntryBackdrop.NightSkyHeight;
            Assert.That(MuseumNight.PanoramaPixel(Vector3.forward, width, height).x, Is.EqualTo(width / 2f).Within(.5f));
            Assert.That(MuseumNight.PanoramaPixel(Vector3.right, width, height).x, Is.EqualTo(width * .75f).Within(.5f));
            Assert.That(MuseumNight.PanoramaPixel(Vector3.up, width, height).y, Is.EqualTo(height).Within(.5f));
            var stars = MuseumEntryBackdrop.StarPixels();
            var withMoon = MuseumEntryBackdrop.StarPixels();
            var direction = Quaternion.Euler(-35, 120, 0) * Vector3.forward;
            MuseumNight.DrawMoon(withMoon, direction, 2.4f);
            var centre = MuseumNight.PanoramaPixel(direction, width, height);
            var face = withMoon[(int)centre.y * width + (int)centre.x];
            Assert.That(face.b, Is.GreaterThan(180), "A pale disc where the moon stands.");
            Assert.That(face.b, Is.GreaterThanOrEqualTo(face.r), "Cold, not warm.");
            int changed = 0;
            for (int index = 0; index < stars.Length; index++) if (!stars[index].Equals(withMoon[index])) changed++;
            Assert.That(changed, Is.InRange(50, width * height / 100), "Only the moon and its halo are drawn.");
            Assert.That(withMoon.Take(width * height / 2).Max(pixel => pixel.g), Is.LessThan(12), "Nothing below the horizon.");
        }

        [Test]
        public void TheSkyIsAChoiceSavedWithThePreferences()
        {
            var settings = MuseumExperienceConfiguration.Current;
            var earlier = MuseumVisitorPreferences.Load(settings, "{\"schema_version\":4,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":true}");
            Assert.That(earlier.Night, Is.False, "Day, as delivered, until the visitor chooses.");
            earlier.SetNight(true);
            var loaded = MuseumVisitorPreferences.Load(settings, earlier.Serialize());
            Assert.That(loaded.Night, Is.True);
            loaded.Reset();
            Assert.That(loaded.Night, Is.False);
            var odd = MuseumVisitorPreferences.Load(settings, "{\"schema_version\":4,\"sensitivity\":0.3,\"volume\":0.5,\"subtitles\":true,\"sky\":\"dusk\"}");
            Assert.That(odd.Sky, Is.EqualTo(MuseumVisitorPreferences.DaySky));
        }

        [Test]
        public void TheSettingsOfferTheSkyUnderItsOwnHeading()
        {
            bool hadStored = PlayerPrefs.HasKey(MuseumVisitorPreferences.StorageKey);
            string stored = PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "");
            MuseumInput.Replace(MuseumInput.CreateDefault());
            var root = new GameObject("NightMenuRuntime");
            var canvas = new GameObject("NightMenuCanvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("NightMenuEvents", typeof(EventSystem), typeof(InputSystemUIInputModule));
            MuseumMenuHud menu = null;
            try
            {
                var loader = root.AddComponent<TourLoader>();
                var interaction = root.AddComponent<MuseumInteraction>();
                var flow = root.AddComponent<MuseumVisitFlow>();
                var preferences = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current, null);
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events.GetComponent<EventSystem>(), null);
                EventSystem.current = events.GetComponent<EventSystem>();
                menu = canvas.AddComponent<MuseumMenuHud>();
                menu.Configure(flow, interaction, loader, preferences);
                Assert.That(menu.SkyValue.text, Is.EqualTo(MuseumInterfaceText.Get("sky_day")));
                Assert.That(menu.SkyButton.GetComponentInChildren<TMPro.TMP_Text>(true).text, Is.EqualTo(MuseumInterfaceText.Get("sky_option")));
                menu.SkyButton.onClick.Invoke();
                Assert.That(preferences.Night, Is.True);
                Assert.That(menu.SkyValue.text, Is.EqualTo(MuseumInterfaceText.Get("sky_night")));
                menu.SkyButton.onClick.Invoke();
                Assert.That(preferences.Night, Is.False);
                var reset = menu.View.Settings.Reset.transform;
                Assert.That(menu.SkyButton.transform.GetSiblingIndex(), Is.EqualTo(reset.GetSiblingIndex() - 1), "Last before the reset.");
                var heading = reset.parent.GetChild(menu.SkyButton.transform.GetSiblingIndex() - 1);
                Assert.That(heading.name, Is.EqualTo("Heading"));
                Assert.That(heading.GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(MuseumInterfaceText.Get("heading_sky")));
                Assert.That(menu.SkyButton.gameObject.activeSelf, Is.True, "Offered on a screen.");
                MuseumInput.Current.SetSpatial(true);
                menu.SetSpatial(true);
                Assert.That(menu.SkyButton.gameObject.activeSelf, Is.True, "And on a headset.");
            }
            finally
            {
                if (menu != null) typeof(MuseumMenuHud).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(menu, null);
                UnityEngine.Object.DestroyImmediate(canvas);
                UnityEngine.Object.DestroyImmediate(root);
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(events.GetComponent<EventSystem>(), null);
                UnityEngine.Object.DestroyImmediate(events);
                MuseumInput.Replace(null);
                if (hadStored) PlayerPrefs.SetString(MuseumVisitorPreferences.StorageKey, stored);
                else PlayerPrefs.DeleteKey(MuseumVisitorPreferences.StorageKey);
                PlayerPrefs.Save();
            }
        }
    }
}
