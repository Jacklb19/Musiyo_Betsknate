using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    public static class BuildMuseumWindows
    {
        public const string Output = "Build/MuseumWindows/Musiyo.exe";

        [MenuItem("Musiyo/Build Museum Windows")]
        public static void Build()
        {
            using var service = MuseumServiceBuildSetting.Apply();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BuildMuseumBlockout.ScenePath },
                locationPathName = Output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Museum Windows build failed: " + report.summary.result);
            Debug.Log("Museum Windows build saved to " + Output + ". It has not been run or accepted on a desktop.");
        }
    }
}
