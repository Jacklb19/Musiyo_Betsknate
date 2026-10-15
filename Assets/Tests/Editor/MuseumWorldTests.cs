using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MusiyoBetsknate.Museum;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumWorldTests
    {
        private const string ThirdParty = "Assets/_Musiyo/Museum/ThirdParty/";
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string ModelPath = "Assets/_Musiyo/Museum/Models/Museum_v3.fbx";

        private static object Invoke(string type, string method, params object[] arguments)
        {
            var found = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor." + type))
                .First(candidate => candidate != null);
            var target = found.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length);
            try { return target.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void EveryThirdPartyFileMatchesItsRecordedHashAndCarriesAnAcceptedLicence()
        {
            string registry = File.ReadAllText(ThirdParty + "third_party_assets.json");
            var licences = Regex.Matches(registry, @"""license"":\s*""([^""]+)""").Cast<Match>().Select(match => match.Groups[1].Value).ToList();
            Assert.That(licences, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(licences, Is.All.EqualTo("CC0 1.0"));
            // Repository files are listed by their path under ThirdParty; the sources outside the repository have no folder.
            var files = Regex.Matches(registry, @"""([A-Za-z0-9_]+/[^""]+)"":\s*""([0-9a-f]{64})""").Cast<Match>().ToList();
            Assert.That(files, Has.Count.GreaterThanOrEqualTo(15));
            using var sha = SHA256.Create();
            foreach (var file in files)
            {
                string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(ThirdParty + file.Groups[1].Value))).Replace("-", "").ToLowerInvariant();
                Assert.That(hash, Is.EqualTo(file.Groups[2].Value), file.Groups[1].Value);
            }
        }

        private static MuseumCompanionSettings Settings(string remarks = "[]", string welcome = "Hola.")
            => MuseumCompanionSettings.Parse("{\"schema_version\":1,\"follow_distance_m\":2,\"personal_space_m\":1,\"hide_within_m\":.6,"
                + "\"side_offset_m\":.8,\"walk_speed\":1.5,\"catch_up_speed\":3,\"catch_up_beyond_m\":4,\"rejoin_beyond_m\":9,"
                + "\"turn_degrees_per_second\":160,\"figure_scale\":1.3,\"home_room\":\"room.hearth\",\"welcome_text\":\"" + welcome + "\","
                + "\"welcome_delay_seconds\":2,\"remark_seconds\":5,\"dwell_seconds\":3,\"gap_seconds\":10,\"remarks\":" + remarks + "}");

        private const string TwoRemarks = "[{\"id\":\"a\",\"zone\":\"room.arrival\",\"text\":\"Uno.\",\"approved\":true},"
            + "{\"id\":\"b\",\"zone\":\"room.arrival\",\"text\":\"Pendiente.\",\"approved\":false}]";

        private static CompanionMoment At(double time, string zone = "room.arrival", bool busy = false, bool muted = false, bool exploring = true)
            => new CompanionMoment(time, zone, exploring, busy, muted);

        [Test]
        public void TheWelcomeComesOnceAfterTheVisitStartsAndWaitsForAQuietMoment()
        {
            var remarks = new MuseumCompanionRemarks(Settings());
            Assert.That(remarks.Tick(At(0, exploring: false)), Is.Null, "Nothing is said in the menu.");
            Assert.That(remarks.Tick(At(1)), Is.Null);
            Assert.That(remarks.Tick(At(2.5, busy: true)), Is.Null, "A narration or an open screen comes first.");
            Assert.That(remarks.Tick(At(3))?.Id, Is.EqualTo(MuseumCompanionRemarks.WelcomeId));
            Assert.That(remarks.Tick(At(4))?.Id, Is.EqualTo(MuseumCompanionRemarks.WelcomeId), "It stays while it is read.");
            Assert.That(remarks.Tick(At(9)), Is.Null);
            for (double time = 10; time < 200; time += 5) Assert.That(remarks.Tick(At(time))?.Id, Is.Not.EqualTo(MuseumCompanionRemarks.WelcomeId));
        }

        [Test]
        public void OnlyApprovedRemarksAreMadeOnceAfterStayingAndNeverOnReentry()
        {
            var remarks = new MuseumCompanionRemarks(Settings(TwoRemarks, ""));
            Assert.That(remarks.Tick(At(0)), Is.Null);
            Assert.That(remarks.Tick(At(2)), Is.Null, "Passing through says nothing.");
            Assert.That(remarks.Tick(At(3.5))?.Id, Is.EqualTo("a"));
            Assert.That(remarks.Tick(At(4, zone: "room.welcome")), Is.Not.Null, "A remark already begun is finished.");
            Assert.That(remarks.Tick(At(30)), Is.Null);
            Assert.That(remarks.Tick(At(40)), Is.Null, "Coming back to the area does not repeat it.");
            Assert.That(remarks.Tick(At(100)), Is.Null, "The unapproved remark is never made.");
            Assert.That(remarks.Said, Is.EquivalentTo(new[] { "a" }));
        }

        [Test]
        public void QuietAndPauseSilenceTheCompanionAndLaterRemarksKeepTheirGap()
        {
            var remarks = new MuseumCompanionRemarks(Settings(TwoRemarks, ""));
            remarks.Add(new[] { new CompanionRemark { Id = "c", Zone = "room.arrival", Text = "Dos.", Approved = true } });
            Assert.That(remarks.Tick(At(0, muted: true)), Is.Null);
            Assert.That(remarks.Tick(At(10, muted: true)), Is.Null, "Asked for quiet, it stays quiet.");
            Assert.That(remarks.Tick(At(11))?.Id, Is.EqualTo("a"));
            Assert.That(remarks.Tick(At(12, exploring: false)), Is.Null, "A pause or the menu hides it.");
            Assert.That(remarks.Tick(At(13)), Is.Null, "The interrupted remark is not repeated and the gap applies.");
            Assert.That(remarks.Tick(At(20)), Is.Null);
            Assert.That(remarks.Tick(At(26))?.Id, Is.EqualTo("c"));
        }

        [Test]
        public void TheShippedCompanionSaysNothingButANeutralWelcomeAndWearsNoMask()
        {
            var settings = MuseumCompanionSettings.Current;
            Assert.That(settings.Remarks, Is.Empty, "Remarks are content and come approved from the content service.");
            Assert.That(settings.MaskElement, Is.Empty, "A mask is worn only when the content service allows its reuse.");
            Assert.That(settings.WelcomeText, Is.Not.Empty);
            Assert.That(settings.FollowDistance, Is.GreaterThan(settings.PersonalSpace));
        }

        [TestCase("{\"schema_version\":2}")]
        [TestCase("{\"schema_version\":1,\"follow_distance_m\":1,\"personal_space_m\":2,\"hide_within_m\":.6,\"walk_speed\":1,\"catch_up_speed\":2,"
            + "\"catch_up_beyond_m\":4,\"rejoin_beyond_m\":9,\"turn_degrees_per_second\":1,\"figure_scale\":1,\"remark_seconds\":1,\"dwell_seconds\":1}")]
        public void InvalidCompanionSettingsAreRejected(string json)
            => Assert.Throws<InvalidOperationException>(() => MuseumCompanionSettings.Parse(json));

        [Test]
        public void TheTrailKeepsTheWalkedWayAndForgetsItAfterAJump()
        {
            var trail = new CompanionTrail(.3f);
            for (int step = 0; step <= 20; step++) trail.Record(new Vector3(0, 0, step * .1f));
            Assert.That(trail.Count, Is.EqualTo(7), "A point every 30 cm.");
            Assert.That(trail.LengthFrom(new Vector3(0, 0, -1), 0, new Vector3(0, 0, 2.5f)), Is.EqualTo(3.5f).Within(.01f));
            int reachable = trail.FarthestReachable(Vector3.zero, (from, to) => to.z < 1.5f);
            Assert.That(trail[reachable].z, Is.LessThan(1.5f).And.GreaterThan(1.1f), "The newest point with a clear way.");
            Assert.That(trail.Record(new Vector3(10, 0, 10)), Is.False, "A jump is reported.");
            Assert.That(trail.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheCompanionDoesNotStandBetweenTheVisitorAndTheExhibit()
        {
            var visitor = Vector3.zero;
            var exhibit = new Vector3(0, 1.2f, 4);
            Assert.That(MuseumCompanion.Blocks(visitor, new Vector3(.2f, 0, 2), exhibit), Is.True);
            Assert.That(MuseumCompanion.Blocks(visitor, new Vector3(2, 0, 2), exhibit), Is.False);
            Assert.That(MuseumCompanion.Blocks(visitor, new Vector3(0, 0, -2), exhibit), Is.False, "Behind the visitor is fine.");
            Assert.That(MuseumCompanion.Blocks(visitor, new Vector3(0, 0, 5), exhibit), Is.False, "Beyond the exhibit is fine.");
        }

        [Test]
        public void MusicFollowsTheVisitorVolumeAndStepsBackForVoices()
        {
            var settings = MuseumMusicSettings.Current;
            Assert.That(MuseumMusic.TargetVolume(0, settings.Level, 1, false, 1), Is.EqualTo(0), "Volume at zero is silence.");
            Assert.That(MuseumMusic.TargetVolume(1, settings.Level, 1, true, 1), Is.EqualTo(0), "Muted music is silent.");
            float full = MuseumMusic.TargetVolume(.8f, settings.Level, 1, false, 1);
            float ducked = MuseumMusic.TargetVolume(.8f, settings.Level, 1, false, settings.VoiceDuck);
            Assert.That(full, Is.GreaterThan(0).And.LessThanOrEqualTo(.8f * settings.Level + 1e-5f));
            Assert.That(ducked, Is.LessThan(full * .5f), "Narration and the guide stay clearly above the music.");
            Assert.Throws<InvalidOperationException>(() => MuseumMusicSettings.Parse("{\"schema_version\":1,\"level\":2}"));
        }

        [Test]
        public void TheMusicTrackStreamsAndIsLongEnoughToLoopWithItsCrossfade()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ThirdParty + "OpenGameArt/FirstLightParticles/first_light_particles.ogg");
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.GreaterThan(MuseumMusicSettings.Current.LoopCrossfadeSeconds * 4));
            var importer = (AudioImporter)AssetImporter.GetAtPath(ThirdParty + "OpenGameArt/FirstLightParticles/first_light_particles.ogg");
            Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
        }

        [Test]
        public void TheSceneCarriesMusicAndCompanionWithoutTouchingTheVisitBootstrap()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var music = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MuseumMusic>(true)).ToList();
                var companions = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MuseumCompanion>(true)).ToList();
                Assert.That(music, Has.Count.EqualTo(1));
                Assert.That(music[0].Track, Is.Not.Null);
                Assert.That(companions, Has.Count.EqualTo(1));
                Assert.That(companions[0].GetComponent<MuseumCompanionMask>(), Is.Not.Null);
                Assert.That(companions[0].GetComponentsInChildren<Collider>(true), Is.Empty, "The companion never blocks the visitor.");
                var placeholder = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                    .Where(renderer => renderer.name == MuseumCompanion.BodyPiece || renderer.name == MuseumCompanion.HeadPiece).ToList();
                Assert.That(placeholder, Has.Count.EqualTo(2), "The figure is taken from the museum model's guide placeholder.");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void TheCorridorRaftersReachTheInnerBeamAfterImport()
        {
            var meshes = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.name == "Personajes_Estructura_Madera_Anillo").ToList();
            Assert.That(meshes, Has.Count.EqualTo(1));
            var filter = meshes[0];
            var world = filter.sharedMesh.vertices.Select(vertex => filter.transform.localToWorldMatrix.MultiplyPoint3x4(vertex)).ToList();
            // Corners of rafters (above 3.3 m, within the beam span) at the delivered and at the repaired end.
            int Count(float endZ) => world.Count(point => point.y > 3.3f && Mathf.Abs(point.x) <= 10.1f && Mathf.Abs(Mathf.Abs(point.z) - endZ) < .03f);
            Assert.That(Count(9.99f), Is.EqualTo(0), "No north or south rafter stops short of the beam.");
            Assert.That(Count(8f), Is.GreaterThanOrEqualTo(22 * 4), "22 rafters end over the beam.");
            Assert.That(world.Where(point => point.y > 3.3f && Mathf.Abs(point.x) <= 10.1f && Mathf.Abs(Mathf.Abs(point.z) - 8f) < .03f)
                .All(point => point.y > 3.25f && point.y < 3.6f), "They rest on the beam under the thatch.");
        }

        [Test]
        public void TheSkyAndTheLagoonUseTheirOwnShadersOnEveryProfile()
        {
            var sky = Resources.Load<Material>(MuseumAtmosphere.SkyMaterial);
            Assert.That(sky.shader.name, Is.EqualTo("Musiyo/Sky Panorama"));
            Assert.That(sky.GetTexture("_MainTex"), Is.Not.Null);
            Assert.That(Resources.Load<Material>(MuseumAtmosphere.FallbackSkyMaterial)?.shader.name, Is.EqualTo("Skybox/Procedural"));
            var panorama = (TextureImporter)AssetImporter.GetAtPath(ThirdParty + "PolyHaven/Kloppenheim03PureSky/museum_sky_kloppenheim_03_4k.jpg");
            Assert.That(panorama.mipmapEnabled, Is.False);
            Assert.That(panorama.GetPlatformTextureSettings("Android").maxTextureSize, Is.LessThanOrEqualTo(2048));
            foreach (var profile in new[] { "Web", "Quest", "Windows" })
            {
                var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Musiyo/Museum/Materials/" + profile + "/M_Agua_Laguna.mat");
                Assert.That(water.shader.name, Is.EqualTo("Musiyo/Lagoon Water"), profile);
                Assert.That(water.GetTexture("_SkyTex"), Is.Not.Null, profile);
                Assert.That(water.renderQueue, Is.LessThan((int)RenderQueue.AlphaTest), "The lagoon stays opaque: " + profile);
            }
            Assert.That(MuseumAtmosphere.Current.Sky(true), Is.Not.Null);
        }

        private static readonly string[][] HeadsetVariants =
        {
            new string[0], new[] { "STEREO_MULTIVIEW_ON" }, new[] { "STEREO_INSTANCING_ON" }, new[] { "STEREO_MULTIVIEW_ON", "FOG_LINEAR" },
        };

        [TestCase("Musiyo/Sky Panorama")]
        [TestCase("Musiyo/Lagoon Water")]
        [TestCase("Musiyo/Foliage")]
        public void TheWorldShadersCompileForTheHeadsetInStereo(string name)
        {
            var shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, name);
            var data = ShaderUtil.GetShaderData(shader);
            var subshader = data.GetSubshader(0);
            for (int pass = 0; pass < subshader.PassCount; pass++)
                foreach (var platform in new[] { ShaderCompilerPlatform.Vulkan, ShaderCompilerPlatform.GLES3x })
                    foreach (var keywords in HeadsetVariants)
                        foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            var result = subshader.GetPass(pass).CompileVariant(stage, keywords, platform, BuildTarget.Android);
                            Assert.That(result.Success, Is.True, name + " pass " + pass + " " + platform + " " + stage + " ["
                                + string.Join(" ", keywords) + "]: " + string.Join("; ", result.Messages.Select(message => message.message)));
                        }
        }

        [Test]
        public void ThePlantsStandOnTheirSpotsAndTheStandInsAreSwitchedOff()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToList();
                foreach (var group in new[] { "Jajan_Plantas_Medicinales", "Patio_Plantas_Medicinales", "Jajan_Flores_rojo", "Patio_Flores_amarillo" })
                    Assert.That(renderers.Single(renderer => renderer.name == group).enabled, Is.False, group);
                var root = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                    .First(item => item.name == "MuseumSetDressing");
                var plants = root.Cast<Transform>().ToList();
                Assert.That(plants.Count(item => item.name == "PlanterLeaves"), Is.InRange(30, 60), "One per stand-in plant; close leaves form one plant.");
                Assert.That(plants.Count(item => item.name == "FlowerClump"), Is.InRange(90, 120));
                Assert.That(plants.Count(item => item.name == "MaizePlant"), Is.EqualTo(100));
                foreach (var plant in plants.Where(item => item.name != "PathLantern"))
                {
                    var bounds = plant.GetComponentInChildren<Renderer>().bounds;
                    Assert.That(bounds.min.y, Is.InRange(-.1f, .7f), plant.name + " stands on the ground or in a planter.");
                    Assert.That(plant.GetComponentsInChildren<Collider>(), Is.Empty, plant.name + " does not block the way.");
                    Assert.That(GameObjectUtility.GetStaticEditorFlags(plant.gameObject) & StaticEditorFlags.BatchingStatic, Is.EqualTo((StaticEditorFlags)0));
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void PlantsAreCombinedPerAreaWithTheirOwnHeightAndTone()
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                int made = (int)Invoke("MuseumFoliageBatch", "Combine", scene, true);
                var root = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                    .First(item => item.name == "MuseumSetDressing");
                var combined = root.GetComponentsInChildren<MeshFilter>().Where(filter => filter.name.StartsWith("Foliage_")).ToList();
                Assert.That(made, Is.EqualTo(combined.Count).And.InRange(4, 16), "A handful of draws for every plant.");
                Assert.That(root.Cast<Transform>().Any(item => item.name == "MaizePlant" || item.name == "PlanterLeaves"), Is.False);
                // Each material covers part of its plants (lower leaves, ears...); together they span foot to top.
                var all = combined.SelectMany(filter => filter.sharedMesh.colors32).ToList();
                Assert.That(all.Max(color => color.r), Is.GreaterThan(240), "The combined plants reach their tops.");
                Assert.That(all.Min(color => color.r), Is.LessThan(15), "The combined plants start at their feet.");
                Assert.That(all.Select(color => color.g).Distinct().Count(), Is.GreaterThan(20), "Each plant keeps a tone of its own.");
                foreach (var filter in combined)
                {
                    var colors = filter.sharedMesh.colors32;
                    Assert.That(colors.Length, Is.EqualTo(filter.sharedMesh.vertexCount));
                    Assert.That(colors.Max(color => color.r), Is.GreaterThan(colors.Min(color => color.r)), filter.name + " varies with height.");
                    Assert.That(filter.GetComponent<MeshRenderer>().sharedMaterial.shader.name, Is.EqualTo("Musiyo/Foliage"));
                    if (filter.name.Contains("PlanterLeaves") || filter.name.Contains("FlowerClump"))
                        Assert.That(filter.GetComponent<MeshRenderer>().sharedMaterial.name, Does.EndWith("_Headset"), "The headset leaves out normal maps.");
                }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
