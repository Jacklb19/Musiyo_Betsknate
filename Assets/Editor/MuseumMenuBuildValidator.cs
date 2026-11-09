using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public sealed class MuseumMenuBuildValidator : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            var view = Resources.Load<MuseumMenuView>(MuseumMenuView.ResourceName);
            if (view == null) throw new BuildFailedException("Missing Resources/MuseumMenuRoot.prefab. Restore the authored museum menu before building.");
            view.ValidateReferences();
            foreach (var child in view.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                    throw new BuildFailedException("Missing script in museum menu: " + child.name);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            bool included = false;
            foreach (var packed in report.packedAssets)
                foreach (var asset in packed.contents)
                    if (asset.sourceAssetPath.EndsWith("/Resources/MuseumMenuRoot.prefab", System.StringComparison.Ordinal))
                        included = true;
            // Android's GUID-named serialized files can omit PackedAssets source attribution.
            // Verify the exact generated resource in the final APK instead of relaxing the guard.
            if (!included && report.summary.platform == UnityEditor.BuildTarget.Android)
            {
                var guid = UnityEditor.AssetDatabase.AssetPathToGUID("Assets/_Musiyo/UI/Resources/MuseumMenuRoot.prefab");
                foreach (var file in report.GetFiles())
                    if (file.path.Replace('\\', '/').EndsWith("/assets/bin/Data/" + guid, System.StringComparison.OrdinalIgnoreCase)
                        && AndroidResourceMatches(report.summary.outputPath, file.path, guid))
                        included = true;
            }
            if (!included) throw new BuildFailedException("MuseumMenuRoot.prefab was not included in the player data.");
            Debug.Log("Museum menu prefab inclusion verified in the player build report.");
        }

        public static bool AndroidResourceMatches(string apk, string generated, string guid)
        {
            if (string.IsNullOrEmpty(guid) || !File.Exists(apk) || !File.Exists(generated)) return false;
            try
            {
                using var bundle = ZipFile.OpenRead(apk);
                var entry = bundle.GetEntry("assets/bin/Data/" + guid);
                if (entry == null || entry.Length == 0 || entry.Length != new FileInfo(generated).Length) return false;
                using var packaged = entry.Open();
                using var original = File.OpenRead(generated);
                using var hash = SHA256.Create();
                var expected = hash.ComputeHash(original);
                var actual = hash.ComputeHash(packaged);
                for (int index = 0; index < expected.Length; index++)
                    if (expected[index] != actual[index]) return false;
                return true;
            }
            catch (InvalidDataException) { return false; }
        }
    }
}
