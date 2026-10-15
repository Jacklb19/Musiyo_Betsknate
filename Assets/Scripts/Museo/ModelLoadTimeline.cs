using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Measured stages of one model load and the longest frame seen while it ran, so a stall on a device can be located
    /// from its log instead of guessed. Times are wall-clock milliseconds on this device.
    /// </summary>
    public sealed class ModelLoadTimeline
    {
        public const string LogTag = "[MuseumModelLoad]";
        private readonly List<(string stage, double milliseconds)> stages = new List<(string, double)>();
        private readonly Stopwatch total = Stopwatch.StartNew();
        private Stopwatch current;
        private string currentStage;
        public string Slug { get; }
        public string Profile { get; }
        public long Bytes { get; set; }
        public bool FromCache { get; set; }
        public int Textures { get; set; }
        public long Triangles { get; set; }
        /// <summary>Longest frame, in milliseconds, between the request and shortly after the piece appeared.</summary>
        public float WorstFrameMilliseconds { get; private set; }
        public long MemoryBefore { get; set; }
        public long MemoryAfter { get; set; }
        public string Outcome { get; set; } = "pending";
        public IReadOnlyList<(string stage, double milliseconds)> Stages => stages;
        public double TotalMilliseconds => total.Elapsed.TotalMilliseconds;

        public ModelLoadTimeline(string slug, string profile) { Slug = slug; Profile = profile; }

        public void Begin(string stage)
        {
            End();
            currentStage = stage;
            current = Stopwatch.StartNew();
        }

        public void End()
        {
            if (current == null) return;
            stages.Add((currentStage, current.Elapsed.TotalMilliseconds));
            current = null;
        }

        public double Of(string stage)
        {
            double sum = 0;
            foreach (var (name, milliseconds) in stages) if (name == stage) sum += milliseconds;
            return sum;
        }

        public void ObserveFrame(float seconds)
        {
            if (MuseumExperienceConfiguration.Finite(seconds)) WorstFrameMilliseconds = Mathf.Max(WorstFrameMilliseconds, seconds * 1000);
        }

        /// <summary>One line per load, with a fixed tag, for the device log.</summary>
        public override string ToString()
        {
            var line = new StringBuilder(LogTag).Append(' ').Append(Slug).Append(" profile=").Append(Profile)
                .Append(" outcome=").Append(Outcome).Append(" bytes=").Append(Bytes).Append(" cached=").Append(FromCache ? "yes" : "no")
                .Append(" textures=").Append(Textures).Append(" triangles=").Append(Triangles);
            foreach (var (stage, milliseconds) in stages)
                line.Append(' ').Append(stage).Append('=').Append(milliseconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("ms");
            line.Append(" total=").Append(TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" worst_frame=").Append(WorstFrameMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)).Append("ms");
            line.Append(" memory_delta=").Append(MemoryBefore > 0 && MemoryAfter > 0
                ? ((MemoryAfter - MemoryBefore) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "MB" : "n/a");
            return line.ToString();
        }
    }

    /// <summary>
    /// The last few verified model files, kept in memory so switching between pieces does not download them again. An entry
    /// is only reused after a fresh access grant names the same file hash, so revocation and expiry still apply.
    /// </summary>
    public sealed class ModelByteCache
    {
        private readonly LinkedList<(string key, string sha256, byte[] data)> entries = new LinkedList<(string, string, byte[])>();
        public int MaximumEntries { get; }
        public long MaximumBytes { get; }
        public int Count => entries.Count;
        public long Bytes { get; private set; }

        public ModelByteCache(int maximumEntries, long maximumBytes)
        { MaximumEntries = Mathf.Max(0, maximumEntries); MaximumBytes = maximumBytes < 0 ? 0 : maximumBytes; }

        public bool TryGet(string key, string sha256, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sha256)) return false;
            for (var node = entries.First; node != null; node = node.Next)
            {
                if (node.Value.key != key) continue;
                if (!string.Equals(node.Value.sha256, sha256, System.StringComparison.OrdinalIgnoreCase))
                { Remove(node); return false; }
                entries.Remove(node);
                entries.AddFirst(node);
                data = node.Value.data;
                return true;
            }
            return false;
        }

        public void Put(string key, string sha256, byte[] data)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(sha256) || data == null
                || MaximumEntries == 0 || data.LongLength > MaximumBytes) return;
            for (var node = entries.First; node != null; node = node.Next)
                if (node.Value.key == key) { Remove(node); break; }
            entries.AddFirst((key, sha256, data));
            Bytes += data.LongLength;
            while (entries.Count > MaximumEntries || Bytes > MaximumBytes) Remove(entries.Last);
        }

        public void Clear() { entries.Clear(); Bytes = 0; }

        private void Remove(LinkedListNode<(string key, string sha256, byte[] data)> node)
        {
            Bytes -= node.Value.data.LongLength;
            entries.Remove(node);
        }
    }
}
