using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Owner, headset test of 23/10: the automatic tour must take the visitor along the paths, never into the benches,
    /// pots or pieces of decoration standing beside them. Synthetic tour, no published content.
    /// </summary>
    public sealed class MuseumAutoTourDecorPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown() { MuseumInput.Replace(null); base.TearDown(); }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator TheTourWalksToEveryPointWithoutWalkingIntoTheDecoration()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var wayfinding = flow.GetComponent<MuseumWayfinding>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            RoomContractV1 Room(string key, int order) => new RoomContractV1 { key = key, name = key, order = order, points = new PointContractV1[0] };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[] { Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2), Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5) },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;
            Physics.SyncTransforms();

            // The solid things the world adds beside the paths: benches, pots and the pieces of decoration.
            bool Obstacle(Collider collider) => collider.name == "Bench" || collider.name == "DecorSolid" || collider.name == "FloorPot" || collider.name == "DoorPot";
            var start = runtime.FindRoom("room.arrival").transform.position;
            var crossings = new List<string>();
            int routes = 0;
            foreach (var anchor in Object.FindObjectsByType<PointAnchor>(FindObjectsSortMode.None))
            {
                var goal = anchor.transform.position - anchor.transform.forward * Mathf.Min(anchor.ActivationRadius * .6f, 1.4f);
                if (!wayfinding.TryPathAlongPaths(start, goal, out var corners)) continue;
                routes++;
                for (int index = 1; index < corners.Length; index++)
                {
                    var from = corners[index - 1]; var to = corners[index];
                    for (float along = 0; along <= 1; along += .25f / Mathf.Max(.25f, Vector3.Distance(from, to)))
                    {
                        var spot = Vector3.Lerp(from, to, along);
                        foreach (var hit in Physics.OverlapCapsule(spot + Vector3.up * .3f, spot + Vector3.up * 1.4f, .2f, ~0, QueryTriggerInteraction.Ignore).Where(Obstacle))
                            crossings.Add(anchor.Key + " → " + hit.name + " at " + hit.transform.position);
                    }
                }
            }
            Assert.That(routes, Is.GreaterThan(5), "Routes to the points of every room.");
            Assert.That(crossings.Distinct(), Is.Empty, "The routes keep clear of what stands beside the paths.");
        }
#endif
    }
}
