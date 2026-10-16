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

        [Serializable] private sealed class RepairsFile { public CoplanarOverlaps coplanar_overlaps; }
        [Serializable] private sealed class CoplanarOverlaps { public List<TrimEntry> trims; }
        [Serializable] private sealed class TrimEntry { public string @object; public List<string> under; public List<string> under_either_side; }

        private static Dictionary<string, MeshFilter> ImportedModel()
            => AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter.sharedMesh != null).GroupBy(filter => filter.name).ToDictionary(group => group.Key, group => group.First());

        private static List<(Vector3 a, Vector3 b, Vector3 c)> Faces(MeshFilter filter)
        {
            var mesh = filter.sharedMesh;
            var model = mesh.vertices.Select(vertex => filter.transform.localToWorldMatrix.MultiplyPoint3x4(vertex)).ToArray();
            var faces = new List<(Vector3, Vector3, Vector3)>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                    faces.Add((model[triangles[index]], model[triangles[index + 1]], model[triangles[index + 2]]));
            }
            return faces;
        }

        /// <summary>
        /// Area, in m², where faces of one set lie within 4 mm of the plane of faces of another and overlap them: turned the
        /// same way, or either way. With one set alone, each face against the earlier ones.
        /// </summary>
        private static double CoplanarOverlap(List<(Vector3 a, Vector3 b, Vector3 c)> first, List<(Vector3 a, Vector3 b, Vector3 c)> second, bool eitherSide)
        {
            bool self = ReferenceEquals(first, second);
            double total = 0;
            for (int i = 0; i < first.Count; i++)
            {
                var (a, b, c) = first[i];
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f) continue;
                var normal = cross.normalized;
                var x = (b - a).normalized;
                var y = Vector3.Cross(normal, x);
                Vector2 Flat(Vector3 point) => new Vector2(Vector3.Dot(point - a, x), Vector3.Dot(point - a, y));
                for (int j = 0; j < (self ? i : second.Count); j++)
                {
                    var other = second[j];
                    var otherCross = Vector3.Cross(other.b - other.a, other.c - other.a);
                    if (otherCross.sqrMagnitude < 1e-12f) continue;
                    float facing = Vector3.Dot(normal, otherCross.normalized);
                    if (Mathf.Abs(facing) < .9995f || !eitherSide && facing < 0) continue;
                    if (Mathf.Abs(Vector3.Dot(other.a - a, normal)) > .004f || Mathf.Abs(Vector3.Dot(other.b - a, normal)) > .004f
                        || Mathf.Abs(Vector3.Dot(other.c - a, normal)) > .004f) continue;
                    total += Intersection(new List<Vector2> { Flat(a), Flat(b), Flat(c) }, new List<Vector2> { Flat(other.a), Flat(other.b), Flat(other.c) });
                }
            }
            return total;
        }

        private static double Intersection(List<Vector2> subject, List<Vector2> clip)
        {
            double Signed(List<Vector2> polygon)
            {
                double sum = 0;
                for (int k = 0; k < polygon.Count; k++)
                    sum += (double)polygon[k].x * polygon[(k + 1) % polygon.Count].y - (double)polygon[(k + 1) % polygon.Count].x * polygon[k].y;
                return sum / 2;
            }
            if (Signed(clip) < 0) clip.Reverse();
            for (int edge = 0; edge < 3 && subject.Count > 2; edge++)
            {
                Vector2 p = clip[edge], q = clip[(edge + 1) % 3];
                float Side(Vector2 point) => (q.x - p.x) * (point.y - p.y) - (q.y - p.y) * (point.x - p.x);
                var input = subject;
                subject = new List<Vector2>();
                for (int k = 0; k < input.Count; k++)
                {
                    Vector2 current = input[k], previous = input[(k + input.Count - 1) % input.Count];
                    float now = Side(current), before = Side(previous);
                    if (now >= 0 != before >= 0) subject.Add(previous + (current - previous) * (before / (before - now)));
                    if (now >= 0) subject.Add(current);
                }
            }
            return subject.Count < 3 ? 0 : Math.Abs(Signed(subject));
        }

        [Test]
        public void TheReportedFloorJoinsDrawOneFloorAfterImport()
        {
            var model = ImportedModel();
            // Entrance: earth path and threshold stone; welcome room to characters: passage and room floor, door lining.
            Assert.That(CoplanarOverlap(Faces(model["Sendero_Jajan"]), Faces(model["Umbral_Bienvenida_Sur"]), false), Is.LessThan(5e-4));
            Assert.That(CoplanarOverlap(Faces(model["Pasaje_Sur_Piso"]), Faces(model["Bienvenida_Piso"]), false), Is.LessThan(5e-4));
            Assert.That(CoplanarOverlap(Faces(model["Personajes_Muro_Sur_E"]), Faces(model["Personajes_Muro_Madera_S"]), false), Is.LessThan(5e-4),
                "The door from the welcome room shows its wood lining alone.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Musiyo/Museum/Prefabs/MuseumArchitecture.prefab");
            foreach (var name in new[] { "Sendero_Jajan", "Pasaje_Sur_Piso", "Umbral_Bienvenida_Sur" })
            {
                var mesh = model[name].sharedMesh;
                // The walking surface keeps its delivered box, and the colour mapping runs on across the cut.
                Assert.That((bool)Invoke("MuseumDeliveryAssets", "IsBox", mesh, true), Is.True, name + " still closes its box.");
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var uv = mesh.uv;
                var top = Enumerable.Range(0, mesh.vertexCount)
                    .Where(index => normals[index].y > .99f && Mathf.Abs(vertices[index].y - mesh.bounds.max.y) < 1e-4f).ToList();
                var points = top.Select(index => new Vector2(vertices[index].x, vertices[index].z)).ToList();
                int o = 0;
                int p = Enumerable.Range(0, top.Count).OrderByDescending(k => (points[k] - points[o]).sqrMagnitude).First();
                int q = Enumerable.Range(0, top.Count).OrderByDescending(k => Mathf.Abs((points[p] - points[o]).x * (points[k] - points[o]).y
                    - (points[p] - points[o]).y * (points[k] - points[o]).x)).First();
                Vector2 e1 = points[p] - points[o], e2 = points[q] - points[o];
                float det = e1.x * e2.y - e2.x * e1.y;
                for (int k = 0; k < top.Count; k++)
                {
                    var d = points[k] - points[o];
                    float s = (d.x * e2.y - e2.x * d.y) / det, t = (e1.x * d.y - d.x * e1.y) / det;
                    var expected = uv[top[o]] + (uv[top[p]] - uv[top[o]]) * s + (uv[top[q]] - uv[top[o]]) * t;
                    Assert.That((uv[top[k]] - expected).magnitude, Is.LessThan(1e-3f), name + " keeps one texture mapping on its top.");
                }
                var item = prefab.GetComponentsInChildren<Transform>(true).First(transform => transform.name == name);
                var box = item.GetComponent<BoxCollider>();
                Assert.That(box, Is.Not.Null, name + " keeps its box collider.");
                Assert.That((box.size - mesh.bounds.size).magnitude, Is.LessThan(1e-4f), name + " keeps the walking surface it was delivered with.");
            }
        }

        [Test]
        public void EveryListedTrimLeavesNoCoplanarOverlap()
        {
            var repairs = JsonUtility.FromJson<RepairsFile>(File.ReadAllText("Assets/_Musiyo/Museum/Delivery/mesh_repairs.json"));
            Assert.That(repairs.coplanar_overlaps.trims, Has.Count.GreaterThanOrEqualTo(30));
            var model = ImportedModel();
            foreach (var trim in repairs.coplanar_overlaps.trims)
            {
                var faces = Faces(model[trim.@object]);
                foreach (var (names, eitherSide) in new[] { (trim.under, false), (trim.under_either_side, true) })
                    foreach (var name in names ?? new List<string>())
                    {
                        double area = CoplanarOverlap(faces, name == trim.@object ? faces : Faces(model[name]), eitherSide);
                        Assert.That(area, Is.LessThan(5e-4), trim.@object + " still overlaps " + name + " by " + area + " m².");
                    }
            }
        }

        [Test]
        public void FloorBandsMeetOnceAtEachCorner()
        {
            var model = ImportedModel();
            foreach (var name in new[] { "Corredor_Cenefa_Piso", "Instrumentos_Cenefa_Piso" })
            {
                var faces = Faces(model[name]);
                Assert.That(faces, Has.Count.EqualTo(8), name + " keeps its eight triangles.");
                Assert.That(CoplanarOverlap(faces, faces, false), Is.LessThan(1e-5), name + " strips no longer cross.");
                var corners = faces.SelectMany(face => new[] { face.a, face.b, face.c }).ToList();
                var low = corners.Aggregate(Vector3.Min);
                var high = corners.Aggregate(Vector3.Max);
                foreach (var corner in new[] { new Vector2(low.x, low.z), new Vector2(low.x, high.z), new Vector2(high.x, low.z), new Vector2(high.x, high.z) })
                {
                    // Just inside the outer corner, where the crossing strips used to leave a gap.
                    var inside = corner + new Vector2(Mathf.Sign((low.x + high.x) / 2 - corner.x), Mathf.Sign((low.z + high.z) / 2 - corner.y)) * .02f;
                    bool covered = faces.Any(face =>
                    {
                        var points = new[] { face.a, face.b, face.c }.Select(point => new Vector2(point.x, point.z)).ToArray();
                        float Side(Vector2 from, Vector2 to) => (to.x - from.x) * (inside.y - from.y) - (to.y - from.y) * (inside.x - from.x);
                        float s1 = Side(points[0], points[1]), s2 = Side(points[1], points[2]), s3 = Side(points[2], points[0]);
                        return s1 >= 0 && s2 >= 0 && s3 >= 0 || s1 <= 0 && s2 <= 0 && s3 <= 0;
                    });
                    Assert.That(covered, Is.True, name + " fills its outer corner at " + corner);
                }
            }
        }
    }
}
