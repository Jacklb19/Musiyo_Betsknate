using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The world built around and inside the museum when the scene is processed (MuseumWorldDressing).</summary>
    public sealed class MuseumWorldDressingTests
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";

        private static Type Dressing => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("MusiyoBetsknate.Editor.MuseumWorldDressing")).First(type => type != null);

        private static object Call(string method, params object[] arguments)
        {
            var target = Dressing.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .First(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Length);
            try { return target.Invoke(null, arguments); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        /// <summary>Opens the museum scene, builds the world into it and hands both to the check; the scene is never saved.</summary>
        private static void WithWorld(Action<Scene, GameObject> check, bool headset = true)
        {
            var scene = EditorSceneManager.OpenScene(MuseumScene, OpenSceneMode.Additive);
            try { check(scene, (GameObject)Call("Apply", scene, headset)); }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static List<Transform> Everything(Scene scene)
            => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToList();

        private static int Triangles(GameObject root)
            => root.GetComponentsInChildren<MeshFilter>().Where(filter => filter.GetComponent<MeshRenderer>() != null)
                .Sum(filter => Enumerable.Range(0, filter.sharedMesh.subMeshCount).Sum(index => (int)filter.sharedMesh.GetIndexCount(index) / 3));

        [Test]
        public void TheWorldReplacesTheDeliveredStandInsAndTheLooseArrivalPlants()
        {
            WithWorld((scene, root) =>
            {
                var all = Everything(scene);
                Assert.That(all.Any(item => item.GetComponent<MuseumCompanion>() != null) && all.Any(item => item.GetComponent<MuseumMusic>() != null), Is.True,
                    "The music and the companion stay.");
                foreach (var name in (string[])Dressing.GetField("Replaced").GetValue(null))
                    Assert.That(all.Single(item => item.name == name).GetComponent<Renderer>().enabled, Is.False, name);
                foreach (var part in new[] { "ValleyGround", "ValleyRidges", "ForestCards", "ForestUnderstorey", "Meadow", "TreeOfLifeCrown", "PathGrass", "WalkLimit", "Surroundings" })
                    Assert.That(root.transform.Find(part), Is.Not.Null, part);
                var dressing = all.First(item => item.name == "MuseumSetDressing");
                Assert.That(dressing.Cast<Transform>().Any(plant => (plant.name == "PlanterLeaves" || plant.name == "FlowerClump") && plant.position.z < -26), Is.False,
                    "The ferns and flowers loose on the arrival lawn are gone.");
                Assert.That(dressing.Cast<Transform>().Count(plant => plant.name == "PlanterLeaves"), Is.GreaterThan(0), "The courtyard planters keep theirs.");
                Assert.That(dressing.Cast<Transform>().Count(plant => plant.name == "MaizePlant"), Is.EqualTo(100));
                var lagoon = all.Single(item => item.name == "Bienvenida_Agua_Laguna").GetComponent<Renderer>();
                Assert.That(lagoon.sharedMaterial.shader.name, Is.EqualTo("Musiyo/World/Clear Water"));
                Assert.That(lagoon.sharedMaterial.GetTexture("_BedTex"), Is.Not.Null);
            });
        }

        [Test]
        public void TheWorldIsTheSameEachTimeItIsBuilt()
        {
            int first = 0;
            Vector3 corner = default;
            WithWorld((scene, root) => { first = Triangles(root); corner = root.transform.Find("ForestCards").GetComponent<MeshFilter>().sharedMesh.vertices[123]; });
            WithWorld((scene, root) =>
            {
                Assert.That(Triangles(root), Is.EqualTo(first));
                Assert.That(root.transform.Find("ForestCards").GetComponent<MeshFilter>().sharedMesh.vertices[123], Is.EqualTo(corner));
            });
        }

        [Test]
        public void TheWorldStaysWithinTheHeadsetBudget()
        {
            WithWorld((scene, root) =>
            {
                int triangles = Triangles(root);
                int draws = root.GetComponentsInChildren<MeshRenderer>().Sum(renderer => renderer.sharedMaterials.Length);
                TestContext.WriteLine("World: " + triangles + " triangles, " + draws + " drawn submeshes.");
                foreach (var part in root.GetComponentsInChildren<MeshFilter>().Where(filter => filter.GetComponent<MeshRenderer>() != null)
                    .GroupBy(filter => filter.transform.parent == root.transform ? filter.name : filter.transform.parent.name + "/" + filter.name)
                    .Select(group => (group.Key, group.Sum(filter => (int)filter.sharedMesh.GetIndexCount(0) / 3))).OrderByDescending(entry => entry.Item2))
                    Debug.Log("World part " + part.Key + ": " + part.Item2);
                // Upper bounds of everything at once; one view draws less. Measured 2026-10-18 at about 132 000 triangles.
                Assert.That(triangles, Is.LessThan(170000), "Triangles of the whole world.");
                Assert.That(root.transform.Find("ForestCards").GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
                Assert.That(root.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.transform.parent == root.transform || renderer.transform.parent.name == "Surroundings")
                    .Sum(renderer => renderer.sharedMaterials.Length), Is.LessThan(25), "The large parts are a handful of draws.");
            });
        }

        [Test]
        public void TheGroundIsFlatWhereTheVisitorWalksAndTheEdgeKeepsThemOutOfTheForest()
        {
            foreach (var spot in new[] { new Vector2(0, -58), new Vector2(0, -28), new Vector2(-25, 0), new Vector2(30, -20) })
                Assert.That((float)Call("GroundHeight", spot.x, spot.y), Is.EqualTo(-.02f).Within(.001f), spot.ToString());
            Assert.That((float)Call("GroundHeight", 0f, -200f), Is.GreaterThan(10), "The valley rises far away.");
            Assert.That((float)Call("ClearingDistance", 0f, -20f), Is.LessThan(0), "Inside the welcome room.");
            Assert.That((float)Call("ClearingDistance", 60f, 0f), Is.GreaterThan(30));
            WithWorld((scene, root) =>
            {
                Assert.That(root.transform.Find("ValleyGround").GetComponent<MeshCollider>(), Is.Not.Null, "The visitor stands on the ground they see.");
                Physics.SyncTransforms();
                var origin = new Vector3(0, 1.2f, -20);
                for (int step = 0; step < 16; step++)
                {
                    var direction = Quaternion.Euler(0, step * 22.5f, 0) * Vector3.forward;
                    var hits = Physics.RaycastAll(origin, direction, 120);
                    Assert.That(hits.Any(hit => hit.collider.name == "WalkLimit"), Is.True, "The edge stands across " + direction);
                    float limit = hits.Where(hit => hit.collider.name == "WalkLimit").Min(hit => hit.distance);
                    var point = origin + direction * limit;
                    Assert.That((float)Call("ClearingDistance", point.x, point.z), Is.InRange(16f, 20f), "The edge is where the forest thickens.");
                }
            });
        }

        /// <summary>
        /// Owner, 22/10: the corridor bench before the doorway to the instruments room was in the way. No seat stands near a
        /// doorway or passage, no stool stands against a bench, and every seat left can still be sat on.
        /// </summary>
        [Test]
        public void NoSeatStandsInADoorwayOrAgainstABench()
        {
            WithWorld((scene, root) =>
            {
                Physics.SyncTransforms();
                var all = Everything(scene);
                var doorways = all.Where(item => (item.name.StartsWith("Umbral_") || item.name.StartsWith("Pasaje_")) && item.GetComponent<Renderer>() != null)
                    .Select(item => item.GetComponent<Renderer>().bounds).ToList();
                Assert.That(doorways, Has.Count.GreaterThanOrEqualTo(4));
                float Gap(Bounds a, Bounds b)
                {
                    float x = Mathf.Max(0, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
                    float z = Mathf.Max(0, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
                    return Mathf.Sqrt(x * x + z * z);
                }
                var benches = root.GetComponentsInChildren<BoxCollider>().Where(collider => collider.name == "Bench").Select(collider => collider.bounds).ToList();
                foreach (var bench in benches)
                    Assert.That(doorways.Min(doorway => Gap(doorway, bench)), Is.GreaterThanOrEqualTo(.95f), "Bench at " + bench.center + " leaves the doorway free.");
                Assert.That(benches.Any(bench => Vector3.Distance(new Vector3(bench.center.x, 0, bench.center.z), new Vector3(12.3f, 0, -5)) < .5f), Is.False,
                    "The corridor bench before the doorway to the instruments room is gone.");
                var stools = all.Where(item => item.parent != null && item.parent.name == "MuseumStools").ToList();
                Assert.That(stools, Is.Not.Empty);
                foreach (var stool in stools.Where(item => item.gameObject.activeInHierarchy))
                {
                    var bounds = stool.GetComponentInChildren<Renderer>().bounds;
                    Assert.That(benches.All(bench => Gap(bench, bounds) >= .35f), Is.True, stool.name + " leaves room before the benches.");
                }
                Assert.That(stools.Any(item => item.name == "Welcome_Banco_Tripode_0" && item.gameObject.activeInHierarchy), Is.False,
                    "The stool before the welcome room's bench is taken away.");
                var seats = MuseumSeating.FindSeats();
                Assert.That(seats.Count(seat => seat.Name == "Bench"), Is.EqualTo(benches.Count), "Every bench left can be sat on.");
                Assert.That(seats.Count(seat => seat.Name.Contains("_Banco_")), Is.GreaterThanOrEqualTo(stools.Count(item => item.gameObject.activeInHierarchy)));
            });
        }

        [Test]
        public void BenchesAndPotsStandFreeAndBlockTheWay()
        {
            WithWorld((scene, root) =>
            {
                Physics.SyncTransforms();
                var benches = root.GetComponentsInChildren<BoxCollider>().Where(collider => collider.name == "Bench").ToList();
                Assert.That(benches, Has.Count.InRange(10, 13));
                var pots = root.GetComponentsInChildren<Transform>().Where(item => item.name == "FloorPot" || item.name == "DoorPot").ToList();
                Assert.That(pots, Has.Count.GreaterThanOrEqualTo(4));
                Assert.That(pots.All(pot => pot.GetComponent<CapsuleCollider>() != null), Is.True, "Standing pots block the way.");
                Assert.That(root.GetComponentsInChildren<Transform>().Where(item => item.name == "HangingPot").All(pot => pot.GetComponent<Collider>() == null), Is.True);
                foreach (var bench in benches)
                {
                    var middle = bench.transform.TransformPoint(bench.center);
                    var others = Physics.OverlapBox(middle, bench.size * .45f, bench.transform.rotation)
                        .Where(found => found != bench && found.name != "Terreno_Valle_Provisional" && found.name != "ValleyGround" && !found.name.Contains("Piso"));
                    Assert.That(others.Select(found => found.name), Is.Empty, "Bench at " + middle);
                }
            });
        }
    }
}
