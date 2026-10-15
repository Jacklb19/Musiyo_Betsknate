using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Lets glTFast work on a model only for a few milliseconds of each frame. glTFast's own default assumes 30 frames
    /// per second when no target is set, which allows about 17 ms per frame: more than a whole 72 Hz headset frame.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class ModelLoadBudget : MonoBehaviour, IDeferAgent
    {
        private float frameStart;
        public float BudgetSeconds { get; set; } = .004f;

        private void Update() => frameStart = Time.realtimeSinceStartup;

        public bool ShouldDefer() => ShouldDefer(0);
        public bool ShouldDefer(float duration) => Time.realtimeSinceStartup - frameStart + duration > BudgetSeconds;

        public async Task BreakPoint()
        {
            if (ShouldDefer()) await Task.Yield();
        }

        public async Task BreakPoint(float duration)
        {
            if (ShouldDefer(duration)) await Task.Yield();
        }
    }
}
