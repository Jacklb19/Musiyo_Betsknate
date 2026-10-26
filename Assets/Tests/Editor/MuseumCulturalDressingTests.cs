using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The decoration approved from the research (PART A, owner 23/10/2026), built with the world when the scene is
    /// processed (MuseumCulturalDressing): light, out of the way, and true to the sensitivities its sources ask for.
    /// </summary>
    public sealed class MuseumCulturalDressingTests
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private static readonly string[] Rooms = { "Arrival", "Welcome", "Characters", "Instruments", "Hearth", "Overlook" };

        private static Type EditorType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor." + name)).First(type => type != null);

        private static object Call(Type type, string method, params object[] arguments)
        {
            var target = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .First(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length);
            try { return target.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        private static float Constant(string name) => (float)EditorType("MuseumCulturalDressing").GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        /// <summary>Opens the museum scene, builds the world into it and hands both to the check; the scene is never saved.</summary>
        private static void WithWorld(Action<Scene, Transform> check)
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try
            {
                var world = (GameObject)Call(EditorType("MuseumWorldDressing"), "Apply", scene, true);
                Physics.SyncTransforms();
                check(scene, world.transform.Find("MuseumDecor"));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static List<T> All<T>(Scene scene) where T : Component
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToList();

        private static Vector2 Flat(Vector3 point) => new Vector2(point.x, point.z);

        [Test]
        public void EveryRoomHasItsDecorationInAFewLightDraws()
        {
            WithWorld((scene, decor) =>
            {
                Assert.That(decor, Is.Not.Null, "The decoration is built with the world.");
                foreach (var room in Rooms)
                {
                    var part = decor.Find("Decor_" + room);
                    Assert.That(part, Is.Not.Null, room);
                    var renderers = part.GetComponentsInChildren<MeshRenderer>();
                    Assert.That(renderers.Length, Is.InRange(1, 2), room + ": its pieces and its leaves, one draw each.");
                    Assert.That(renderers.All(renderer => renderer.shadowCastingMode == ShadowCastingMode.Off), Is.True, room);
                    Assert.That(renderers.All(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith("MuseumDecor")), Is.True, room);
                }
                int triangles = decor.GetComponentsInChildren<MeshFilter>().Sum(filter => (int)filter.sharedMesh.GetIndexCount(0) / 3);
                TestContext.WriteLine("Decoration: " + triangles + " triangles.");
                // Measured 23/10/2026 at about 22 000.
                Assert.That(triangles, Is.LessThan(30000));
            });
        }

        [Test]
        public void NothingOfTheDecorationStandsInADoorwayOrOnAPoint()
        {
            WithWorld((scene, decor) =>
            {
                var solids = decor.GetComponentsInChildren<BoxCollider>();
                Assert.That(solids, Is.Not.Empty);
                var doorways = All<Renderer>(scene).Where(item => item.name.StartsWith("Umbral_") || item.name.StartsWith("Pasaje_")).Select(item => item.bounds.center).ToList();
                var points = All<PointAnchor>(scene).Select(anchor => anchor.transform.position).ToList();
                Assert.That(doorways, Is.Not.Empty);
                Assert.That(points, Is.Not.Empty);
                foreach (var solid in solids)
                {
                    foreach (var doorway in doorways)
                        Assert.That(Vector2.Distance(Flat(solid.ClosestPoint(doorway)), Flat(doorway)), Is.GreaterThan(1f), solid.transform.parent.name + " near a doorway at " + doorway);
                    foreach (var point in points)
                        Assert.That(Vector2.Distance(Flat(solid.ClosestPoint(point)), Flat(point)), Is.GreaterThan(.6f), solid.transform.parent.name + " on the point at " + point);
                }
            });
        }

        /// <summary>Owner, Web test of 23/10: the banners stood in the way beside the castle; they stand aside now.</summary>
        [Test]
        public void TheBannersStandAsideFromTheCastle()
        {
            WithWorld((scene, decor) =>
            {
                var castle = All<Transform>(scene).Single(item => item.name == "CastleLookTarget").position;
                var rack = (Vector3)EditorType("MuseumCulturalDressing").GetField("BannerRack", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Assert.That(Vector2.Distance(Flat(rack), Flat(castle)), Is.GreaterThan(5), "Well away from the castle.");
                var rackSolid = decor.Find("Decor_Characters").GetComponentsInChildren<BoxCollider>()
                    .Single(item => Vector2.Distance(Flat(item.transform.position), Flat(rack)) < .1f);
                Assert.That(rackSolid.size.x, Is.InRange(1.4f, 1.8f), "The rack keeps its size.");
            });
        }

        /// <summary>No meal is set before a seat (research, general rule): a low table never stands in front of a bench.</summary>
        [Test]
        public void NoTableStandsBeforeASeat()
        {
            WithWorld((scene, decor) =>
            {
                var benches = All<BoxCollider>(scene).Where(item => item.name == "Bench").ToList();
                Assert.That(benches, Is.Not.Empty);
                foreach (var solid in decor.GetComponentsInChildren<BoxCollider>().Where(item => item.name != "Bench"))
                {
                    var middle = solid.transform.TransformPoint(solid.center);
                    foreach (var bench in benches)
                    {
                        var local = bench.transform.InverseTransformPoint(middle) - bench.center;
                        bool before = Mathf.Abs(local.x) < bench.size.x * .5f && local.z > 0 && local.z < bench.size.z * .5f + 1f;
                        Assert.That(before, Is.False, solid.transform.parent.name + " piece at " + middle + " stands before the bench at " + bench.transform.position);
                    }
                }
            });
        }

        /// <summary>The jajañ takes over the maize beside the path: those plants are switched off, never left inside the beds.</summary>
        [Test]
        public void TheBedsReplaceTheMaizeBesideThePath()
        {
            WithWorld((scene, decor) =>
            {
                var maize = All<Transform>(scene).Where(item => item.name == "MaizePlant").ToList();
                float near = Constant("BedsNear"), far = Constant("BedsFar"), west = Constant("BedsWest");
                var inBeds = maize.Where(item => item.position.x > west && item.position.z < near + .4f && item.position.z > far - .4f).ToList();
                Assert.That(inBeds, Is.Not.Empty, "The beds take the place of some maize.");
                Assert.That(inBeds.All(item => !item.gameObject.activeSelf), Is.True, "Switched off, so the field's combined mesh leaves them out.");
                Assert.That(maize.Count(item => item.gameObject.activeSelf), Is.GreaterThan(maize.Count / 2), "The rest of the field stays.");
            });
        }

        /// <summary>A6.1: the overlook's bench is a seat like any other, turned to the valley, and seated eyes clear the parapet.</summary>
        [Test]
        public void TheOverlookBenchIsASeatWithAViewOfThePlain()
        {
            WithWorld((scene, decor) =>
            {
                var bench = decor.Find("Decor_Overlook").GetComponentsInChildren<BoxCollider>().Single(item => item.name == "Bench");
                Assert.That(Vector3.Angle(bench.transform.forward, Vector3.left), Is.LessThan(1), "Facing the valley, to the west.");
                var top = bench.transform.TransformPoint(bench.center + Vector3.up * bench.size.y * .5f);
                Assert.That(top.y, Is.InRange(.42f, .58f), "The height of a seat over the deck, which lies a few centimetres up.");
                var eyes = top + Vector3.up * MuseumSeating.SeatedEyes;
                var plain = new Vector3(-27.5f, .3f, top.z);
                var blocked = Physics.RaycastAll(eyes, plain - eyes, Vector3.Distance(eyes, plain))
                    .Where(hit => hit.collider.name != "WalkLimit" && hit.collider.name != "ValleyGround" && !hit.collider.name.StartsWith("Terreno")).ToList();
                Assert.That(blocked.Select(hit => hit.collider.name), Is.Empty, "From the seat the lagoon on the plain is in view.");
            });
        }

        [Test]
        public void TheDecorationCanBeLeftOutForPicturesBeforeAndAfter()
        {
            var before = Environment.GetEnvironmentVariable("MUSIYO_DECOR");
            Environment.SetEnvironmentVariable("MUSIYO_DECOR", "off");
            try { WithWorld((scene, decor) => Assert.That(decor, Is.Null)); }
            finally { Environment.SetEnvironmentVariable("MUSIYO_DECOR", before); }
        }
    }
}
