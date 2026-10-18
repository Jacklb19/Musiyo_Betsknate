using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Builds the museum's world (<see cref="MuseumWorldDressing"/>) into the museum scene when it enters Play mode or a
    /// build. It runs before the plants are combined (<see cref="MuseumFoliageBatch"/>, which then no longer finds the
    /// loose arrival plants) and before the surface profile swaps the delivered materials the world borrows.
    /// </summary>
    public sealed class MuseumWorldBuildStep : IProcessSceneWithReport
    {
        public int callbackOrder => 4;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (scene.path != MuseumSurfaceAudit.MuseumScene) return;
            MuseumWorldDressing.Apply(scene, report != null && report.summary.platform == BuildTarget.Android);
        }
    }
}
