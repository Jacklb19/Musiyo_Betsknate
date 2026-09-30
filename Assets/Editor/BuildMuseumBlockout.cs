using System;
using System.IO;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>Builds a separate technical scene without cultural content or private references.</summary>
    public static class BuildMuseumBlockout
    {
        public const string ScenePath = "Assets/_Musiyo/Scenes/10_Museum_Blockout.unity";
        private const string ArchitecturePath =
            "Assets/_Musiyo/Arte/Modelos/Boceto/Neutral_Blockout_Architecture.fbx";

        [MenuItem("Musiyo/Build Neutral Museum Blockout")]
        public static void Create()
        {
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                Debug.Log("The neutral blockout already exists; no scene was overwritten.");
                return;
            }

            var architecture = AssetDatabase.LoadAssetAtPath<GameObject>(ArchitecturePath);
            if (architecture == null)
                throw new InvalidOperationException("Missing neutral FBX: " + ArchitecturePath);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var previousScene = SceneManager.GetActiveScene();
            var useSingleScene = Application.isBatchMode && string.IsNullOrEmpty(previousScene.path);
            if (!useSingleScene && string.IsNullOrEmpty(previousScene.path))
                throw new InvalidOperationException("Save the current scene before building the blockout.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                useSingleScene ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("MuseumBlockout");
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(architecture);
                visual.name = "NeutralArchitecture";
                visual.transform.SetParent(root.transform, false);
                visual.transform.rotation = Quaternion.Euler(0, 180, 0);

                var arrival = CreateRoom(root.transform, "Arrival", new Vector3(0, 0, -43));
                var welcome = CreateRoom(root.transform, "WelcomeRoom", new Vector3(0, 0, -20));
                var characters = CreateRoom(root.transform, "CharacterRoom", Vector3.zero);
                var instruments = CreateRoom(root.transform, "InstrumentRoom", new Vector3(20.5f, 0, -4.5f));
                var hearth = CreateRoom(root.transform, "HearthRoom", new Vector3(0, 0, 18.5f));
                var overlook = CreateRoom(root.transform, "Overlook", new Vector3(-11.5f, 0, 18.5f));
                var passages = CreateRoom(root.transform, "Passages", Vector3.zero);

                var spawn = new GameObject("VisitorSpawn");
                spawn.transform.SetParent(arrival, false);
                spawn.transform.position = new Vector3(0, 0, -58);

                AddPoint(arrival, "zona.llegada.tutorial", 0, -40, 2.5f);
                AddPoint(welcome, "punto.bienvenida.mito", 0, -15.6f, 1.8f);
                AddPoint(welcome, "punto.bienvenida.significados", -4.2f, -20, 1.8f);
                AddPoint(characters, "punto.personajes.pedestal_1", -7, -6, 1.8f);
                AddPoint(characters, "punto.personajes.pedestal_2", -4.95f, -1.05f, 1.8f);
                AddPoint(characters, "punto.personajes.pedestal_3", 0, 1, 1.8f);
                AddPoint(characters, "punto.personajes.pedestal_4", 4.95f, -1.05f, 1.8f);
                AddPoint(characters, "punto.personajes.pedestal_5", 7, -6, 1.8f);
                AddPoint(instruments, "punto.instrumentos.pedestal_1", 18, -0.8f, 1.5f);
                AddPoint(instruments, "punto.instrumentos.pedestal_2", 23, -0.8f, 1.5f);
                AddPoint(instruments, "punto.instrumentos.pedestal_3", 24.9f, -4.5f, 1.5f);
                AddPoint(instruments, "punto.instrumentos.pedestal_4", 23, -8.2f, 1.5f);
                AddPoint(instruments, "punto.instrumentos.pedestal_5", 18, -8.2f, 1.5f);
                AddPoint(instruments, "punto.instrumentos.danza_ocho", 20.2f, -4.5f, 1.5f);
                AddPoint(hearth, "guia.principal", 1, 20.3f, 2.5f);
                AddPoint(overlook, "punto.mirador.creditos", -11.5f, 18.5f, 2);

                AddFloor(arrival, 0, -43, 2.5f, 34);
                AddFloor(welcome, 0, -20, 12.3f, 12.3f);
                AddFloor(passages, 0, -12.9f, 3.6f, 2.8f);
                AddFloor(characters, 0, 0, 27, 23);
                AddFloor(passages, 14.25f, -4.5f, 1.5f, 3.6f);
                AddFloor(instruments, 20.5f, -4.5f, 11, 11);
                AddFloor(passages, 0, 13, 3.6f, 3);
                AddFloor(hearth, 0, 18.5f, 8, 8);
                AddFloor(passages, -5, 18.5f, 2, 4);
                AddFloor(overlook, -11.5f, 18.5f, 11, 4);
                AddWalls(welcome, characters, instruments, hearth, overlook);

                var visitor = new GameObject("DesktopVisitor");
                visitor.transform.SetParent(root.transform, false);
                visitor.transform.position = spawn.transform.position + Vector3.up;
                var body = visitor.AddComponent<CharacterController>();
                body.height = 1.8f;
                body.radius = 0.3f;
                body.stepOffset = 0.25f;
                var cameraObject = new GameObject("VisitorCamera");
                cameraObject.transform.SetParent(visitor.transform, false);
                cameraObject.transform.localPosition = new Vector3(0, 0.65f, 0);
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                visitor.AddComponent<ControladorEscritorio>();

                var lightObject = new GameObject("NeutralDaylight");
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(55, -30, 0);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.58f);

                var errors = ValidateMuseumScene.Validate(scene);
                if (errors.Count != 0)
                    throw new InvalidOperationException(string.Join("\n", errors));
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("Neutral blockout created with 15 source anchors, one tutorial zone, and simple collisions.");
            }
            finally
            {
                if (!useSingleScene)
                {
                    if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Transform CreateRoom(Transform parent, string name, Vector3 center)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            return go.transform;
        }

        private static void AddPoint(Transform room, string key, float x, float z, float radius)
        {
            var go = new GameObject(key);
            go.transform.SetParent(room, false);
            go.transform.position = new Vector3(x, 0, z);
            var target = new GameObject("LookTarget");
            target.transform.SetParent(go.transform, false);
            target.transform.localPosition = Vector3.up * 1.6f;
            go.AddComponent<PointAnchor>().Configure(key, radius, target.transform);
        }

        private static void AddFloor(Transform room, float x, float z, float width, float length)
        {
            AddBox(room, "FloorCollider", new Vector3(x, -0.15f, z),
                new Vector3(width, 0.3f, length));
        }

        private static void AddBox(Transform room, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void AddWalls(Transform welcome, Transform characters,
            Transform instruments, Transform hearth, Transform overlook)
        {
            // Keep openings clear at the courtyard, instrument room, and hearth room.
            AddBox(characters, "WestWall", new Vector3(-13.5f, 1.5f, 0), new Vector3(0.3f, 3, 23));
            AddBox(characters, "SouthWestWall", new Vector3(-7.65f, 1.5f, -11.5f), new Vector3(11.7f, 3, 0.3f));
            AddBox(characters, "SouthEastWall", new Vector3(7.65f, 1.5f, -11.5f), new Vector3(11.7f, 3, 0.3f));
            AddBox(characters, "NorthWestWall", new Vector3(-7.65f, 1.5f, 11.5f), new Vector3(11.7f, 3, 0.3f));
            AddBox(characters, "NorthEastWall", new Vector3(7.65f, 1.5f, 11.5f), new Vector3(11.7f, 3, 0.3f));
            AddBox(characters, "EastSouthWall", new Vector3(13.5f, 1.5f, -8.9f), new Vector3(0.3f, 3, 5.2f));
            AddBox(characters, "EastNorthWall", new Vector3(13.5f, 1.5f, 4.4f), new Vector3(0.3f, 3, 14.2f));

            AddBox(instruments, "EastWall", new Vector3(26, 1.5f, -4.5f), new Vector3(0.3f, 3, 11));
            AddBox(instruments, "NorthWall", new Vector3(20.5f, 1.5f, 1), new Vector3(11, 3, 0.3f));
            AddBox(instruments, "SouthWall", new Vector3(20.5f, 1.5f, -10), new Vector3(11, 3, 0.3f));
            AddBox(instruments, "WestWallN", new Vector3(15, 1.5f, -0.85f), new Vector3(0.3f, 3, 3.7f));
            AddBox(instruments, "WestWallS", new Vector3(15, 1.5f, -8.15f), new Vector3(0.3f, 3, 3.7f));

            AddBox(hearth, "EastWall", new Vector3(4, 1.5f, 18.5f), new Vector3(0.3f, 3, 8));
            AddBox(hearth, "NorthWall", new Vector3(0, 1.5f, 22.5f), new Vector3(8, 3, 0.3f));
            AddBox(hearth, "SouthWestWall", new Vector3(-2.9f, 1.5f, 14.5f), new Vector3(2.2f, 3, 0.3f));
            AddBox(hearth, "SouthEastWall", new Vector3(2.9f, 1.5f, 14.5f), new Vector3(2.2f, 3, 0.3f));
            AddBox(hearth, "WestWallN", new Vector3(-4, 1.5f, 21.5f), new Vector3(0.3f, 3, 2));
            AddBox(hearth, "WestWallS", new Vector3(-4, 1.5f, 15.5f), new Vector3(0.3f, 3, 2));

            AddBox(overlook, "WestRailing", new Vector3(-17, 0.7f, 18.5f), new Vector3(0.2f, 1.4f, 4));
            AddBox(overlook, "NorthRailing", new Vector3(-11.5f, 0.7f, 20.5f), new Vector3(11, 1.4f, 0.2f));
            AddBox(overlook, "SouthRailing", new Vector3(-11.5f, 0.7f, 16.5f), new Vector3(11, 1.4f, 0.2f));

            const int segments = 24;
            for (var i = 0; i < segments; i++)
            {
                var angle = (i + 0.5f) * Mathf.PI * 2 / segments;
                var x = Mathf.Sin(angle) * 6.15f;
                var z = Mathf.Cos(angle) * 6.15f - 20;
                if (Mathf.Abs(x) < 1.8f && (z < -25.4f || z > -14.6f)) continue;
                var go = new GameObject("CircularWall");
                go.transform.SetParent(welcome, false);
                go.transform.position = new Vector3(x, 3, z);
                go.transform.rotation = Quaternion.Euler(0, angle * Mathf.Rad2Deg + 90, 0);
                go.AddComponent<BoxCollider>().size = new Vector3(0.3f, 6, 1.65f);
            }
        }
    }
}
