using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.TestTools.Utils;

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
            Assert.That(Vector3.Angle(-sun.transform.forward, settings.MoonDirection()), Is.LessThan(.01f), "Its light comes from where the moon stands.");
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
            Assert.That(Quaternion.Angle(sun.transform.rotation, Quaternion.Euler(40, 30, 0)), Is.LessThan(.01f), "The sun shines from where it did.");
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
        public void TheMoonStandsOverTheOverlooksValleyWhereTheEyeMeetsItWithoutLookingUpMuch()
        {
            var settings = MuseumNightAtmosphere.Current;
            var moon = settings.MoonDirection();
            Assert.That(moon.magnitude, Is.EqualTo(1).Within(.001f));
            Assert.That(moon.x, Is.LessThan(-.8f), "Over the valley the overlook looks out on, to the west.");
            Assert.That(Mathf.Asin(moon.y) * Mathf.Rad2Deg, Is.InRange(18f, 32f), "High enough over the forest, low enough for a level gaze.");
            Assert.That(settings.MoonSize, Is.InRange(2f, 4f), "Larger than the real half degree, still a moon in the sky.");
            var north = MuseumNightAtmosphere.Parse(Resources.Load<TextAsset>(MuseumNightAtmosphere.ResourceName).text
                .Replace("\"moon_azimuth_degrees\": 270", "\"moon_azimuth_degrees\": 0").Replace("\"moon_elevation_degrees\": 24", "\"moon_elevation_degrees\": 30"));
            Assert.That(Vector3.Angle(north.MoonDirection(), Quaternion.Euler(-30, 0, 0) * Vector3.forward), Is.LessThan(.01f));
            Assert.Throws<InvalidOperationException>(() => MuseumNightAtmosphere.Parse(Resources.Load<TextAsset>(MuseumNightAtmosphere.ResourceName).text
                .Replace("\"moon_elevation_degrees\": 24", "\"moon_elevation_degrees\": -10")), "Never under the horizon.");
        }

        [Test]
        public void TheSkyDrawsTheMoonFromItsDirectionOverTheTitlesOwnStars()
        {
            var settings = MuseumNightAtmosphere.Current;
            var sky = MuseumNight.Sky(settings);
            Assert.That(sky, Is.Not.Null);
            Assert.That(sky.shader.name, Is.EqualTo("Musiyo/Night Sky"));
            Assert.That(sky.GetTexture("_MainTex"), Is.SameAs(MuseumEntryBackdrop.NightSky().GetTexture("_MainTex")),
                "The stars of the title and the credits, already on the graphics card: no second panorama.");
            Assert.That((Vector3)sky.GetVector("_MoonDirection"), Is.EqualTo(settings.MoonDirection()).Using(Vector3EqualityComparer.Instance));
            Assert.That(Vector3.Dot(sky.GetVector("_MoonRight"), sky.GetVector("_MoonDirection")), Is.EqualTo(0).Within(.001f));
            Assert.That(Vector3.Dot(sky.GetVector("_MoonUp"), sky.GetVector("_MoonDirection")), Is.EqualTo(0).Within(.001f));
            Assert.That(sky.GetFloat("_MoonRadius"), Is.EqualTo(settings.MoonSize * .5f * Mathf.Deg2Rad).Within(1e-5f));
            var face = (Texture2D)sky.GetTexture("_MoonTex");
            Assert.That(face, Is.SameAs(MuseumNight.MoonFace()));
            Assert.That((face.width, face.height, face.format), Is.EqualTo((MuseumNight.MoonFaceSize, MuseumNight.MoonFaceSize, TextureFormat.R8)));
            Assert.That(face.mipmapCount, Is.GreaterThan(1), "Smooth however small the disc is drawn.");
            Assert.That(face.isReadable, Is.False, "Only the graphics card keeps it.");
        }

        [Test]
        public void TheMoonsFaceIsPaleWithSoftSeas()
        {
            var texels = MuseumNight.MoonFacePixels();
            Assert.That(texels.Length, Is.EqualTo(MuseumNight.MoonFaceSize * MuseumNight.MoonFaceSize));
            Assert.That(texels, Is.EqualTo(MuseumNight.MoonFacePixels()), "The same face every time.");
            var values = texels.Select(texel => texel / 255f).ToArray();
            Assert.That(values.Average(), Is.InRange(.7f, .92f), "Pale overall.");
            Assert.That(values.Min(), Is.GreaterThan(.45f), "Seas are soft, never black.");
            Assert.That(values.Max() - values.Min(), Is.GreaterThan(.15f), "But they are there.");
        }

        [Test]
        public void ALampBesideAWallLightsItAndTheMoonLaysAStreakOnStillWater()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "NightTestWall";
            wall.transform.position = new Vector3(2, 1.5f, 3.75f);
            wall.transform.localScale = new Vector3(4, 3, .2f);
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "NightTestPost";
            post.transform.position = new Vector3(2.7f, 1.5f, 3);
            post.transform.localScale = new Vector3(.1f, 1.2f, .1f);
            try
            {
                var settings = MuseumNightAtmosphere.Parse(Resources.Load<TextAsset>(MuseumNightAtmosphere.ResourceName).text
                    .Replace("{ \"x\": -27.5, \"z\": 19.0 }", "{ \"x\": -5, \"z\": -5 }"));
                var night = Museum();
                night.Configure(null, null, settings);
                Physics.SyncTransforms();
                night.Set(true);
                Assert.That(night.WallGlows, Is.EqualTo(1), "The wall .6 m behind the lantern; the post beside it is no wall.");
                Assert.That(night.MoonGlints, Is.EqualTo(1));
                var mesh = night.Pools.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(12), "Pool, wall and streak in one mesh: one draw.");
                var vertices = mesh.vertices;
                var colours = mesh.colors;
                var onWall = vertices.Skip(4).Take(4).ToArray();
                Assert.That(onWall.All(vertex => Mathf.Abs(vertex.z - 3.63f) < .01f), Is.True, "Upright on the wall's face, just in front of it.");
                Assert.That(onWall.Max(vertex => vertex.y) - onWall.Min(vertex => vertex.y), Is.EqualTo(settings.LampWallSize).Within(.01f));
                Assert.That(mesh.normals[4].z, Is.LessThan(-.99f), "Facing the lamp.");
                Assert.That(colours[4].a, Is.EqualTo(settings.LampWallAlpha).Within(.001f), "Softer than the pool on the floor.");
                Assert.That(colours[0].a, Is.EqualTo(settings.LampPoolAlpha).Within(.001f));
                var streak = vertices.Skip(8).Take(4).ToArray();
                var centre = streak.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / 4;
                Assert.That(centre.x, Is.EqualTo(-5).Within(.01f));
                Assert.That(centre.z, Is.EqualTo(-5).Within(.01f));
                Assert.That(centre.y, Is.EqualTo(.08f).Within(.01f), "Just above the water.");
                Assert.That(streak.Max(vertex => vertex.x) - streak.Min(vertex => vertex.x), Is.EqualTo(settings.MoonGlintLength).Within(.01f),
                    "Lying towards the moon in the west.");
                Assert.That(colours[8].b, Is.GreaterThan(colours[8].r), "Cold moonlight, not a lamp's.");
                var block = new MaterialPropertyBlock();
                night.Pools.GetPropertyBlock(block);
                Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.white), "The night only fades the glows in together.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(post);
            }
        }

        [Test]
        public void TheNightSkyCompilesForTheHeadsetInStereo()
        {
            var shader = Shader.Find("Musiyo/Night Sky");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
            foreach (var platform in new[] { ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.GLES3x })
                foreach (var keywords in new[] { new string[0], new[] { "STEREO_MULTIVIEW_ON" }, new[] { "STEREO_INSTANCING_ON" } })
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        Assert.That(pass.CompileVariant(stage, keywords, platform, BuildTarget.Android).Success, Is.True,
                            platform + " " + stage + " [" + string.Join(" ", keywords) + "]");
            Assert.That(Resources.Load<Material>(MuseumNight.SkyMaterialName).shader, Is.SameAs(shader), "Shipped with a material, so builds keep it.");
        }

        private static Type Dressing => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.MuseumWorldDressing")).First(type => type != null);

        [Test]
        public void TheRoomsThatHadNoLampGetThePathsLanternOnAPlainPostWithNothingInTheWay()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Musiyo/Scenes/10_Museum_Blockout.unity", OpenSceneMode.Additive);
            try
            {
                var apply = Dressing.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
                var world = (GameObject)apply.Invoke(null, new object[] { scene, true });
                var lamps = world.transform.Find("MuseumNightLamps");
                Assert.That(lamps, Is.Not.Null);
                var rooms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RoomAnchor>(true)).ToList();
                var points = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true)).ToList();
                var expected = new Dictionary<string, string>
                {
                    ["NightLamp_Welcome_Loom"] = "room.welcome", ["NightLamp_Instruments_Drum"] = "room.instruments",
                    ["NightLamp_Instruments_Shelf"] = "room.instruments", ["NightLamp_Overlook_Bench"] = "room.overlook",
                };
                Assert.That(lamps.Cast<Transform>().Select(lamp => lamp.name), Is.EquivalentTo(expected.Keys), "All four stand: none crossed anything solid.");
                foreach (Transform lamp in lamps)
                {
                    var glass = lamp.GetComponentsInChildren<Renderer>().Where(renderer => renderer.sharedMaterials.Any(material => material != null
                        && material.name == "PathLantern_Glass")).ToList();
                    Assert.That(glass, Is.Not.Empty, lamp.name + ": the path's lantern, found by its glass at night.");
                    var at = glass[0].bounds.center;
                    Assert.That(rooms.Where(room => room.Contains(at)).Select(room => room.Key), Does.Contain(expected[lamp.name]), lamp.name);
                    Assert.That(lamp.GetComponentsInChildren<Collider>(true), Is.Empty, lamp.name + ": nothing to walk into.");
                    Assert.That(lamp.GetComponentsInChildren<Light>(true), Is.Empty, lamp.name + ": no real-time light.");
                    Assert.That(points.Min(point => Vector2.Distance(new Vector2(point.transform.position.x, point.transform.position.z), new Vector2(at.x, at.z))),
                        Is.GreaterThanOrEqualTo(1.2f), lamp.name + ": clear of the points of the visit.");
                    Assert.That(lamp.GetComponentsInChildren<MeshRenderer>().SelectMany(renderer => renderer.sharedMaterials).Select(material => material.name).Distinct(),
                        Is.SubsetOf(new[] { "PathLantern_Glass", "PathLantern_Wood" }), lamp.name + ": the lantern's own wood and glass, nothing else.");
                    float height = at.y - lamp.position.y;
                    Assert.That(height, lamp.name.Contains("Instruments") ? Is.InRange(.6f, 1f) : Is.InRange(1.1f, 1.6f), lamp.name + ": low by the drums, standing elsewhere.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
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
