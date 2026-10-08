using System;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    public static class ConfigureMuseumDesktop
    {
        [MenuItem("Musiyo/Configure Museum Desktop Rig")]
        public static void Configure()
        {
            var scene = SceneManager.GetActiveScene();
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path))
                scene = EditorSceneManager.OpenScene(BuildMuseumBlockout.ScenePath);
            if (scene.path != BuildMuseumBlockout.ScenePath || scene.isDirty)
                throw new InvalidOperationException("The saved museum scene must be active and clean.");
            var visitor = UnityEngine.Object.FindAnyObjectByType<DesktopVisitorController>();
            if (visitor == null) throw new InvalidOperationException("Museum desktop controls are missing.");
            var camera = visitor.GetComponentInChildren<Camera>();
            var body = visitor.GetComponent<CharacterController>();
            Transform spawn = null;
            foreach (var child in visitor.transform.root.GetComponentsInChildren<Transform>())
                if (child.name == "VisitorSpawn") spawn = child;
            if (camera == null || body == null || spawn == null) throw new InvalidOperationException("Museum rig or spawn is incomplete.");
            Undo.RecordObjects(new UnityEngine.Object[] { body, camera, camera.transform, visitor.transform }, "Configure desktop visitor height");
            body.height = 1.75f;
            body.radius = .3f;
            body.center = Vector3.up * (body.height * .5f);
            body.stepOffset = .3f;
            body.slopeLimit = 30;
            visitor.transform.position = spawn.position;
            camera.transform.localPosition = Vector3.up * 1.65f;
            camera.fieldOfView = 70;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateMuseumScene.ValidateCurrentScene();
        }
    }
}
