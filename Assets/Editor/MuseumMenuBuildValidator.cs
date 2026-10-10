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
            if (!included) throw new BuildFailedException("MuseumMenuRoot.prefab was not included in the player data.");
            Debug.Log("Museum menu prefab inclusion verified in the player build report.");
        }
    }
}
