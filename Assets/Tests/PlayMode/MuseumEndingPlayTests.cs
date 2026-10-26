using System;
using System.Collections;
using System.IO;
using System.Linq;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// The end of the visit in the saved museum: the overlook asks whether to end after a few seconds or at its far end,
    /// "Seguir recorriendo" lets the visit go on, and "Sí, finalizar" turns the museum dark, shows the fire of the title menu
    /// with the API's credits rolling beside it in their published order, and comes back to the title menu, where a new
    /// visit starts from the beginning. The question never comes before the credits are here.
    /// </summary>
    public sealed class MuseumEndingPlayTests : InputTestFixture
    {
        private const string MuseumScene = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string Folder = "TestResults/ending";
        private SyntheticMuseumService service;

        public override void Setup() { base.Setup(); MuseumInput.Replace(MuseumInput.CreateDefault()); }
        public override void TearDown()
        {
            MuseumInput.Replace(null);
            service?.Dispose();
            service = null;
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator UnloadMuseum()
        {
            var scene = SceneManager.GetSceneByPath(MuseumScene);
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static TourContractV1 Tour(TourRuntime runtime)
        {
            RoomContractV1 Room(string key, int order) => new RoomContractV1 { key = key, name = key, order = order, points = Array.Empty<PointContractV1>() };
            return new TourContractV1
            {
                schema_version = 1, tour = new TourMetadataContractV1 { key = runtime.TourKey, name = "Test" },
                rooms = new[]
                {
                    Room("room.arrival", 0), Room("room.welcome", 1), Room("room.characters", 2),
                    Room("room.instruments", 3), Room("room.hearth", 4), Room("room.overlook", 5),
                },
            };
        }

        private sealed class Visit
        {
            public MuseumVisitFlow Flow;
            public MuseumEnding Ending;
            public DesktopVisitorController Visitor;
            public CharacterController Body;
            public Vector3 Start;
        }

        private IEnumerator Open(Visit visit, string credits)
        {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            service = new SyntheticMuseumService { Credits = credits };
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MuseumScene,
                new LoadSceneParameters(LoadSceneMode.Additive));
            var flow = UnityEngine.Object.FindAnyObjectByType<MuseumVisitFlow>();
            var loader = flow.GetComponent<TourLoader>();
            var settings = new UnityEditor.SerializedObject(loader);
            settings.FindProperty("apiBase").stringValue = service.Base;
            settings.ApplyModifiedPropertiesWithoutUndo();
            flow.GetComponent<MuseumEntryView>().HeadsetActive = () => false;
            yield return null;
            for (float waited = 0; loader.State == TourLoadState.Loading && waited < 20; waited += Time.unscaledDeltaTime) yield return null;
            var runtime = flow.GetComponent<TourRuntime>();
            Assert.That(runtime.Apply(Tour(runtime), out var error), Is.True, error);
            MuseumAtmosphere.Current.Apply();
            visit.Flow = flow;
            visit.Ending = flow.GetComponent<MuseumEnding>();
            visit.Visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            visit.Body = visit.Visitor.GetComponent<CharacterController>();
            visit.Start = visit.Visitor.transform.position;
            Assert.That(visit.Ending, Is.Not.Null, "The visit is assembled with its ending.");
            flow.EnterImmediately();
            yield return null;
        }

        /// <summary>Stands the visitor on the floor at a point of the museum, looking along a direction.</summary>
        private static void Stand(Visit visit, Vector3 spot, Vector3 facing)
        {
            Assert.That(Physics.Raycast(spot + Vector3.up * 3, Vector3.down, out var floor, 6, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore), Is.True, "There is floor at " + spot);
            var centre = floor.point + Vector3.up * (visit.Body.height * .5f + .02f);
            visit.Visitor.PlaceAt(centre - visit.Body.center, Quaternion.LookRotation(facing, Vector3.up));
        }

        private static IEnumerator Until(Func<bool> condition, float seconds)
        {
            for (float waited = 0; !condition() && waited < seconds; waited += Time.unscaledDeltaTime) yield return null;
        }

        private static void Render(Camera camera, string name)
        {
            Directory.CreateDirectory(Folder);
            const int width = 1600, height = 1000;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            target.Release();
        }

        // In the saved museum the overlook runs west from the hearth; its far end is the railing at about x = -17.
        private static readonly Vector3 OverlookMiddle = new Vector3(-10.5f, 0, 18.5f);
        private static readonly Vector3 OverlookFarEnd = new Vector3(-16.3f, 0, 18.5f);
        private static readonly Vector3 Hearth = new Vector3(0, 0, 17);

        [UnityTest]
        public IEnumerator TheOverlookAsksAfterAFewSecondsOrAtItsFarEndAndCanBeDeclined()
        {
            var visit = new Visit();
            yield return Open(visit, SyntheticMuseumService.ExampleCredits);
            var ending = visit.Ending;
            var flow = visit.Flow;
            ending.DwellSeconds = 1.5f;

            // Standing in the overlook, free to look around, the question comes after the dwell.
            Stand(visit, OverlookMiddle, Vector3.left);
            yield return Until(() => ending.Client.Ready, 10);
            Assert.That(ending.Client.Ready, Is.True, "The credits are asked for on arrival.");
            Assert.That(ending.DistanceToEnd, Is.GreaterThan(ending.EdgeDistance + 2), "The middle of the overlook is far from its end.");
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Idle));
            yield return Until(() => ending.Stage == EndingStage.Asking, 5);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Asking));
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Asking));
            Assert.That(ending.Dwell, Is.GreaterThanOrEqualTo(1.5f));
            yield return new WaitForSecondsRealtime(.5f);
            // A small card stands in the world in front of the eyes, with the exact words and both choices.
            var camera = visit.Visitor.GetComponentInChildren<Camera>();
            var card = ending.Card;
            Assert.That(card.renderMode, Is.EqualTo(RenderMode.WorldSpace), "The question stands in the museum, not on the screen.");
            var toCard = card.transform.position - camera.transform.position;
            Assert.That(Vector3.Dot(camera.transform.forward, toCard.normalized), Is.GreaterThan(.9f));
            Assert.That(toCard.magnitude, Is.InRange(.6f, 2f));
            // On a screen it reads: wide in the view, but never past its edges (owner, Web test of 23/10).
            var corners = new Vector3[4];
            ((RectTransform)card.transform).GetWorldCorners(corners);
            float left = camera.WorldToViewportPoint(corners[0]).x, right = camera.WorldToViewportPoint(corners[2]).x;
            Assert.That(right - left, Is.InRange(.45f, .82f), "The card takes a good part of the view's width.");
            Assert.That(left, Is.GreaterThan(0), "It stays inside the view.");
            Assert.That(right, Is.LessThan(1), "It stays inside the view.");
            var words = card.GetComponentsInChildren<TMP_Text>().Select(text => text.text).ToArray();
            Assert.That(words, Has.Member("Has llegado al final del recorrido."));
            Assert.That(words, Has.Member("¿Quieres darlo por finalizado?"));
            Assert.That(words, Has.Member("Sí, finalizar"));
            Assert.That(words, Has.Member("Seguir recorriendo"));
            Render(camera, "1_pregunta_en_el_mirador");

            // "Seguir recorriendo": the visit goes on, and the question does not come back while the visitor stays.
            ending.KeepButton.onClick.Invoke();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Idle));
            Assert.That(card.gameObject.activeSelf, Is.False);
            yield return new WaitForSecondsRealtime(3);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Idle), "Declined, it waits until the visitor leaves and comes back.");
            Stand(visit, OverlookFarEnd, Vector3.left);
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Idle), "Not even at the far end.");

            // Leaving and coming back, the far end asks at once, without waiting for the dwell.
            Stand(visit, Hearth, Vector3.left);
            yield return new WaitForSecondsRealtime(.3f);
            ending.DwellSeconds = 100;
            Stand(visit, OverlookFarEnd, Vector3.left);
            yield return Until(() => ending.Stage == EndingStage.Asking, 2);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Asking), "Coming close to the end of the overlook asks at once.");
            Assert.That(ending.DistanceToEnd, Is.LessThanOrEqualTo(ending.EdgeDistance));
            Assert.That(ending.Dwell, Is.LessThan(2));

            // Escape answers "Seguir recorriendo" too, and does not open the pause menu.
            var keyboard = Keyboard.current;
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Exploring));
            Assert.That(flow.GetComponent<MuseumInteraction>().State, Is.Not.EqualTo(InteractionState.Paused));
        }

        [UnityTest]
        public IEnumerator WithoutCreditsTheQuestionWaitsAndTheyAreAskedForAgain()
        {
            var visit = new Visit();
            yield return Open(visit, null);
            var ending = visit.Ending;
            ending.DwellSeconds = .5f;
            ending.RetrySeconds = .5f;
            Stand(visit, OverlookMiddle, Vector3.left);
            yield return new WaitForSecondsRealtime(3);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Idle), "An empty roll is never offered.");
            Assert.That(visit.Flow.Phase, Is.EqualTo(VisitPhase.Exploring));
            Assert.That(service.CreditRequests, Is.GreaterThanOrEqualTo(2), "The credits are asked for again quietly.");
            service.Credits = SyntheticMuseumService.ExampleCredits;
            yield return Until(() => ending.Stage == EndingStage.Asking, 5);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Asking));
        }

        [UnityTest]
        public IEnumerator FinishingRollsTheCreditsByTheFireAndReturnsToTheTitleMenu()
        {
            var visit = new Visit();
            // The published credits can be given from a file to see the real roll; the shared example otherwise.
            var file = Environment.GetEnvironmentVariable("MUSIYO_CREDITS_FILE");
            yield return Open(visit, !string.IsNullOrEmpty(file) && File.Exists(file) ? File.ReadAllText(file) : SyntheticMuseumService.ExampleCredits);
            var ending = visit.Ending;
            var flow = visit.Flow;
            Stand(visit, OverlookMiddle, Vector3.left);
            yield return Until(() => ending.Client.Ready, 10);
            Assert.That(ending.Ask(), Is.True);
            yield return null;

            // "Sí, finalizar": the museum goes dark and quiet.
            ending.YesButton.onClick.Invoke();
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Ending));
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Closing));
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(ending.Darkness, Is.InRange(.2f, .95f), "The view darkens gradually.");
            Assert.That(ending.InSet, Is.False, "The museum stays until the view is dark.");
            yield return Until(() => ending.InSet, 3);
            Assert.That(ending.Darkness, Is.GreaterThan(.99f), "The fire appears only once the view is dark.");

            // Out of the dark, the fire of the title menu, seen from its own camera, and the credits beside it.
            yield return Until(() => ending.Roll != null, 3);
            var roll = ending.Roll;
            Assert.That(roll, Is.Not.Null);
            var stage = flow.Entry.StageCamera;
            Assert.That(stage != null && stage.enabled, Is.True, "The desktop sees the credits from the camera of the fire.");
            Assert.That(ending.Backdrop.Kind, Is.EqualTo(EntryBackdropKind.Hearth));
            Assert.That(ending.Backdrop.Shown, Is.True);
            Assert.That(Vector3.Distance(stage.transform.position, ending.Backdrop.CameraPose.position), Is.LessThan(.01f));
            var music = UnityEngine.Object.FindAnyObjectByType<MuseumMusic>();
            if (music != null) Assert.That(music.Fade, Is.LessThan(.05f), "The museum's music has faded out.");
            yield return Until(() => ending.Darkness <= 0, 4);
            Assert.That(ending.Darkness, Is.LessThanOrEqualTo(0));

            // The roll stands to the left of the fire, whole within the view, and opens on the museum's name.
            var fire = stage.WorldToViewportPoint(ending.Backdrop.Root.TransformPoint(MuseumEntryBackdrop.Focus));
            var column = stage.WorldToViewportPoint(roll.transform.position);
            Assert.That(column.x, Is.LessThan(.45f));
            Assert.That(fire.x, Is.GreaterThan(.55f));
            var corners = new Vector3[4];
            ((RectTransform)roll.transform).GetWorldCorners(corners);
            foreach (var corner in corners.Select(stage.WorldToViewportPoint))
            {
                Assert.That(corner.x, Is.InRange(0f, 1f), "The column is inside the view.");
                Assert.That(corner.y, Is.InRange(-.02f, 1.02f), "The column is inside the view.");
            }
            var credits = ending.Client.Credits;
            Assert.That(roll.Title.text, Is.EqualTo(credits.title));
            Assert.That(roll.SectionTitles.Select(text => text.text), Is.EqualTo(credits.sections.Select(section => section.title)),
                "Sections roll in the order the API publishes them.");
            Assert.That(roll.Progress, Is.EqualTo(0).Within(.001f), "The opening stands alone first.");
            Render(stage, "2_fogata_apertura");
            yield return new WaitForSecondsRealtime(MuseumCreditsRoll.OpeningSeconds + 1);
            Assert.That(roll.Progress, Is.GreaterThan(0), "Then the credits rise.");

            // A short press pauses and resumes the roll.
            float before = roll.Progress;
            var keyboard = Keyboard.current;
            Press(keyboard.enterKey);
            yield return null;
            Release(keyboard.enterKey);
            yield return null;
            Assert.That(roll.Paused, Is.True);
            float paused = roll.Progress;
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(roll.Progress, Is.EqualTo(paused), "Paused, nothing moves.");
            Press(keyboard.enterKey);
            yield return null;
            Release(keyboard.enterKey);
            yield return null;
            Assert.That(roll.Paused, Is.False);
            Assert.That(paused, Is.GreaterThanOrEqualTo(before));

            // Halfway, the sections and their lines are reading on the column.
            while (roll.Progress < .5f) roll.Advance(2);
            yield return null;
            Render(stage, "3_fogata_creditos_a_media_altura");
            // At the end the thanks stand still by the fire.
            roll.Advance(10000);
            yield return null;
            Assert.That(roll.Progress, Is.EqualTo(1).Within(.001f));
            Assert.That(roll.Thanks.text, Is.EqualTo("Gracias por tu visita."));
            var thanks = stage.WorldToViewportPoint(roll.Thanks.transform.position);
            Assert.That(thanks.y, Is.InRange(.3f, .7f), "The thanks rest in the middle of the column.");
            yield return new WaitForSecondsRealtime(1);
            Render(stage, "4_gracias_por_tu_visita");
            Assert.That(roll.Done, Is.False, "The thanks are held for a moment.");
            yield return Until(() => ending.Stage == EndingStage.Leaving, MuseumCreditsRoll.ThanksSeconds + 2);
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Leaving));

            // The title menu rises over the same fire; a new visit starts where the first one did.
            yield return Until(() => flow.Phase == VisitPhase.Menu, 4);
            Assert.That(flow.Phase, Is.EqualTo(VisitPhase.Menu));
            Assert.That(flow.AfterEnding, Is.True);
            Assert.That(flow.HasStarted, Is.False);
            Assert.That(ending.Roll, Is.Null);
            Assert.That(ending.Backdrop.Shown, Is.True, "The fire burns on behind the menu.");
            var back = visit.Visitor.transform.position - visit.Start;
            back.y = 0;
            Assert.That(back.magnitude, Is.LessThan(.05f), "Back where the visit began (gravity may settle it on the floor).");
            var menu = flow.Menu.View;
            Assert.That(menu.Main.gameObject.activeInHierarchy, Is.True);
            Assert.That(menu.Overlay.alpha, Is.LessThan(1), "The menu rises gently.");
            yield return new WaitForSecondsRealtime(MuseumMenuHud.AfterEndingFadeSeconds + .3f);
            Assert.That(menu.Overlay.alpha, Is.EqualTo(1));
            Render(stage, "5_menu_tras_los_creditos");
            if (music != null)
            {
                yield return new WaitForSecondsRealtime(2.5f);
                Assert.That(music.Fade, Is.GreaterThan(.9f), "The music comes back with the menu.");
            }
        }

        [UnityTest]
        public IEnumerator SkippingEndsTheCreditsAtOnce()
        {
            var visit = new Visit();
            yield return Open(visit, SyntheticMuseumService.ExampleCredits);
            var ending = visit.Ending;
            Stand(visit, OverlookMiddle, Vector3.left);
            yield return Until(() => ending.Client.Ready, 10);
            Assert.That(ending.Ask(), Is.True);
            ending.Finish();
            yield return Until(() => ending.Roll != null, 6);
            Assert.That(ending.Roll, Is.Not.Null);
            yield return new WaitForSecondsRealtime(.3f);
            var keyboard = Keyboard.current;
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assert.That(ending.Stage, Is.EqualTo(EndingStage.Leaving), "Escape skips to the end.");
            yield return Until(() => visit.Flow.Phase == VisitPhase.Menu, 4);
            Assert.That(visit.Flow.Phase, Is.EqualTo(VisitPhase.Menu));
            Assert.That(visit.Flow.GetComponent<MuseumInteraction>().State, Is.EqualTo(InteractionState.Paused).Or.EqualTo(InteractionState.Exploration));
        }
    }
}
