using System.Linq;
using MusiyoBetsknate.Museum;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Adds the world systems of the visit to the museum scene as their own object: background music and the companion
    /// guide (with its mask mount). They find the visit's components at runtime, so the HUD bootstrap is not changed.
    /// </summary>
    public static class MuseumWorldSetup
    {
        public const string RootName = "MuseumWorld";
        public const string MusicPath = "Assets/_Musiyo/Museum/ThirdParty/OpenGameArt/FirstLightParticles/first_light_particles.ogg";
        public const string FontPath = "Assets/_Musiyo/UI/MuseumSans.asset";

        [MenuItem("Musiyo/Museum World/Configure Music And Companion")]
        public static void Run()
        {
            ConfigureMusic();
            var scene = EditorSceneManager.OpenScene(MuseumSurfaceAudit.MuseumScene, OpenSceneMode.Single);
            var existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);
            if (existing != null) Object.DestroyImmediate(existing);
            var world = new GameObject(RootName);
            var music = world.AddComponent<MuseumMusic>();
            music.Configure(AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath));
            var companionObject = new GameObject("Companion");
            companionObject.transform.SetParent(world.transform, false);
            var companion = companionObject.AddComponent<MuseumCompanion>();
            companion.Configure(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath));
            companionObject.AddComponent<MuseumCompanionMask>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Museum world systems configured: music " + (music.Track != null ? music.Track.name : "missing") + ", companion.");
        }

        /// <summary>The track streams from storage on screens and the headset, so it never sits whole in memory.</summary>
        public static void ConfigureMusic()
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(MusicPath);
            importer.forceToMono = false;
            importer.loadInBackground = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .6f;
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;
            var headset = settings;
            headset.quality = .45f;
            importer.SetOverrideSampleSettings("Android", headset);
            importer.SaveAndReimport();
        }
    }
}
