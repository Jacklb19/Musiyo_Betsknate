using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Writes to the device log, every few seconds of the visit, how smoothly the museum draws: frames per second on
    /// average and in the slowest one percent of frames, with the room the visitor is in and the resident pieces standing
    /// ready. It is how the headset's frame rate with the masks and the Castle present from the start is measured
    /// (adb logcat -s Unity | findstr MuseumFrames). Nothing is shown to the visitor.
    /// </summary>
    [RequireComponent(typeof(MuseumInteraction))]
    public sealed class MuseumFrameLog : MonoBehaviour
    {
        public const string LogTag = "[MuseumFrames]";
        /// <summary>Seconds of frames summed in each line.</summary>
        public const float WindowSeconds = 10;
        private readonly List<float> frames = new List<float>(1024);
        private float elapsed;
        private Transform visitor;
        private MuseumModelPresenter presenter;
        private TourRuntime runtime;

        public void Configure(Transform visitorTransform) => visitor = visitorTransform;

        private void Awake()
        {
            presenter = GetComponent<MuseumModelPresenter>();
            runtime = GetComponent<TourRuntime>();
        }

        private void Update()
        {
            float frame = Time.unscaledDeltaTime;
            if (frame <= 0 || frame > 1) return;
            frames.Add(frame);
            elapsed += frame;
            if (elapsed < WindowSeconds) return;
            Debug.Log(Summary(frames, Room(), presenter != null ? presenter.ResidentCount : 0));
            frames.Clear();
            elapsed = 0;
        }

        private string Room()
        {
            if (visitor == null || runtime == null) return "-";
            return runtime.RoomAt(visitor.position)?.key ?? "-";
        }

        /// <summary>One log line for a window of frame times, in seconds.</summary>
        public static string Summary(List<float> frameSeconds, string room, int residents)
        {
            if (frameSeconds == null || frameSeconds.Count == 0) return LogTag + " no frames";
            float total = 0;
            foreach (var frame in frameSeconds) total += frame;
            var sorted = new List<float>(frameSeconds);
            sorted.Sort();
            int slowest = Mathf.Max(1, sorted.Count / 100);
            float slow = 0;
            for (int index = sorted.Count - slowest; index < sorted.Count; index++) slow += sorted[index];
            return string.Format(CultureInfo.InvariantCulture, "{0} room={1} residents={2} fps={3:F1} low1%={4:F1} worst_ms={5:F1} frames={6}",
                LogTag, room, residents, frameSeconds.Count / total, slowest / slow, sorted[sorted.Count - 1] * 1000, frameSeconds.Count);
        }
    }
}
