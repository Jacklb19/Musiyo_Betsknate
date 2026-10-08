using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Tests
{
    public sealed class MuseumSceneConfigurationTests
    {
        [Test]
        public void SavedMuseumHasWiredDesktopControlsAndAccessibleInterface()
        {
            const string path = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                TourRuntime runtime = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out TourRuntime found)) runtime = found;
                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime.GetComponentsInChildren<RoomAnchor>(true).Length, Is.EqualTo(6));
                Assert.That(runtime.GetComponentsInChildren<TourPoint>(true).Length, Is.EqualTo(16));
                Assert.That(runtime.GetComponent<MuseumInteraction>(), Is.Not.Null);
                Assert.That(runtime.GetComponent<DesktopPointInput>(), Is.Not.Null);
                var visitor = runtime.GetComponentInChildren<DesktopVisitorController>(true);
                Assert.That(visitor, Is.Not.Null);
                var body = visitor.GetComponent<CharacterController>();
                Assert.That(body.height, Is.EqualTo(1.75f).Within(.001f));
                var bodyCenter = visitor.transform.TransformPoint(body.center);
                float groundClearance = bodyCenter.y - body.height * visitor.transform.lossyScale.y / 2;
                Assert.That(groundClearance, Is.InRange(-.001f, body.stepOffset), "The capsule must start near the arrival floor without penetrating it.");
                Assert.That(visitor.GetComponentInChildren<Camera>().transform.position.y, Is.EqualTo(1.65f).Within(.001f));
                var input = new SerializedObject(visitor);
                Assert.That(input.FindProperty("inputActions").objectReferenceValue, Is.Not.Null);
                Assert.That(input.FindProperty("viewCamera").objectReferenceValue, Is.Not.Null);
                Assert.That(input.FindProperty("interaction").objectReferenceValue, Is.Not.Null);
                var hud = runtime.GetComponentInChildren<MuseumHud>(true);
                Assert.That(hud, Is.Not.Null);
                var serialized = new SerializedObject(hud);
                foreach (var field in new[] { "loader", "interaction", "pointInput", "statusText", "focusText", "panelText",
                    "panel", "elementList", "elementTemplate", "retryButton", "closeButton", "pauseButton", "detailButton", "sensitivitySlider", "visitor" })
                    Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
                var font = runtime.GetComponentInChildren<TMP_Text>(true).font;
                foreach (char character in "ëñšÿáéíóúü") Assert.That(font.HasCharacter(character, false, true), Is.True, character.ToString());
                var ui = runtime.GetComponentInChildren<InputSystemUIInputModule>(true);
                Assert.That(ui.actionsAsset, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(ui.actionsAsset), Does.StartWith("Assets/_Musiyo/UI/"));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
