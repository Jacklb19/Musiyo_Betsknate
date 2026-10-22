using System;
using System.Collections;
using System.Reflection;
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
    /// The automatic tour keeps to the paths in the saved museum: from the arrival garden's maize it first goes back to the
    /// path and follows it (owner, 22/10/2026). Synthetic tour, no published content.
    /// </summary>
    public sealed class MuseumAutoTourPathPlayTests : InputTestFixture
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
        public IEnumerator FromTheMaizeTheTourGoesBackToThePathAndFollowsIt()
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            // Nothing answers here: the tour below is applied directly.
            settings.FindProperty("apiBase").stringValue = "http://127.0.0.1:9/api/v1";
            settings.ApplyModifiedPropertiesWithoutUndo();
            yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            var tour = flow.GetComponent<MuseumAutoTour>();
            var wayfinding = flow.GetComponent<MuseumWayfinding>();
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            RoomContractV1 Room(string key, int order, params PointContractV1[] points)
                => new RoomContractV1 { key = key, name = key, order = order, points = points };
            Assert.That(runtime.Apply(new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1, new PointContractV1
                    {
                        key = "punto.bienvenida.mito", name = "Mito", order = 0, activation = new[] { "proximity", "keyboard" },
                        elements = new[] { new ElementSummaryContractV1 { slug = "synthetic-myth", title = "Sintético" } }
                    }),
                    Room("room.characters", 2), Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            }, out var error), Is.True, error);
            flow.EnterImmediately();
            yield return null;

            // The arrival garden: its path runs north along x = 0, with maize on either side.
            var path = runtime.FindRoom("room.arrival").transform.position;
            var start = path + new Vector3(4.5f, 0, -2);
            Assert.That(wayfinding.AreaAt(path), Is.EqualTo(MuseumWayfinding.PathArea), "The garden's path is a path.");
            Assert.That(wayfinding.AreaAt(start), Is.EqualTo(MuseumWayfinding.OpenGroundArea), "The maize beside it is open ground.");
            visitor.PlaceAt(start + Vector3.up * .05f, Quaternion.identity);
            yield return null;
            Assert.That(tour.Begin(), Is.True);
            var corners = (Vector3[])typeof(MuseumAutoTour).GetField("corners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tour);
            tour.Stop();
            Assert.That(corners.Length, Is.GreaterThanOrEqualTo(2));
            // Its first turn is on the path, beside where it started, rather than a diagonal across the maize.
            var turn = corners[1];
            Assert.That(Mathf.Abs(turn.x - path.x), Is.LessThan(1.6f), "The tour first goes back to the path.");
            Assert.That(Mathf.Abs(turn.z - start.z), Is.LessThan(3), "By the shortest way to it.");
            int offPath = 0;
            for (int index = 1; index < corners.Length; index++)
            {
                var middle = (corners[index - 1] + corners[index]) * .5f;
                if (index > 1 && wayfinding.AreaAt(middle) == MuseumWayfinding.OpenGroundArea) offPath++;
            }
            Assert.That(offPath, Is.EqualTo(0), "Once on the path it never crosses open ground again.");
        }
#endif
    }
}
