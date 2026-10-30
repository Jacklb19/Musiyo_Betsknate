using System;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Makes a narration shorter without raising its voice: WSOLA (waveform-similarity overlap-add) takes overlapping
    /// slices of the recording a little further apart than it lays them down, and picks each slice where it continues the
    /// previous one best, so pitch and timbre stay as recorded. Playing the sound faster instead (AudioSource.pitch) would
    /// raise the voice with it. It runs once per narration, off the main thread, and the result plays as an ordinary clip:
    /// nothing is added to each audio frame on the headset.
    /// </summary>
    public static class NarrationTimeStretch
    {
        /// <summary>Length of a slice, in seconds: some pitch periods of a voice, short enough not to smear its syllables.</summary>
        public const float SliceSeconds = .04f;
        /// <summary>How far, each way, a slice may move from its nominal place to fit the previous one.</summary>
        public const float SearchSeconds = .01f;

        /// <summary>
        /// Interleaved <paramref name="samples"/> of <paramref name="channels"/> channels heard <paramref name="rate"/> times
        /// as fast: about <c>length / rate</c> frames, where an output frame <c>k</c> says what the input says near frame
        /// <c>k * rate</c>. A rate of 1 returns a copy.
        /// </summary>
        public static float[] Stretch(float[] samples, int channels, int frequency, float rate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels < 1 || frequency < 1 || float.IsNaN(rate) || rate < .25f || rate > 4)
                throw new ArgumentOutOfRangeException(nameof(rate));
            int frames = samples.Length / channels;
            if (Math.Abs(rate - 1) < 1e-4f || frames == 0) return (float[])samples.Clone();
            int slice = Math.Max(16, (int)(frequency * SliceSeconds) & ~1);
            int hop = slice / 2;
            int search = Math.Max(1, (int)(frequency * SearchSeconds));
            int outputFrames = (int)Math.Ceiling(frames / rate);
            var output = new float[outputFrames * channels];
            var weights = new float[outputFrames];
            var window = new float[slice];
            // A periodic Hann window: slices half a slice apart add up to one.
            for (int i = 0; i < slice; i++) window[i] = .5f - .5f * (float)Math.Cos(2 * Math.PI * i / slice);
            // The fit is judged on every fourth frame of the first channel, then refined frame by frame near the best.
            const int coarse = 4;
            int previous = 0;
            for (int k = 0; (long)k * hop < outputFrames; k++)
            {
                int at = k * hop;
                int nominal = (int)Math.Round((double)at * rate);
                int position;
                if (k == 0) position = 0;
                else
                {
                    // The natural continuation of the slice just laid down, and where this slice may come from.
                    int follow = previous + hop;
                    int low = Math.Max(0, nominal - search), high = Math.Min(frames - 1, nominal + search);
                    if (follow >= frames || low > high) position = Math.Min(Math.Max(0, nominal), frames - 1);
                    else
                    {
                        position = Best(samples, channels, frames, follow, low, high, coarse, coarse, slice, nominal);
                        position = Best(samples, channels, frames, follow, Math.Max(low, position - coarse + 1),
                            Math.Min(high, position + coarse - 1), 1, 2, slice, position);
                    }
                }
                for (int i = 0; i < slice; i++)
                {
                    int target = at + i, from = position + i;
                    if (target >= outputFrames) break;
                    if (from >= frames) break;
                    float weight = window[i];
                    weights[target] += weight;
                    for (int channel = 0; channel < channels; channel++)
                        output[target * channels + channel] += samples[from * channels + channel] * weight;
                }
                previous = position;
            }
            for (int frame = 0; frame < outputFrames; frame++)
            {
                float weight = weights[frame];
                if (weight < 1e-3f) continue;
                for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] /= weight;
            }
            return output;
        }

        /// <summary>
        /// The start between <paramref name="low"/> and <paramref name="high"/>, taken every <paramref name="step"/> frames,
        /// whose slice looks most like the one starting at <paramref name="follow"/>, compared every <paramref name="stride"/>
        /// frames; <paramref name="fallback"/> when nothing compares.
        /// </summary>
        private static int Best(float[] samples, int channels, int frames, int follow, int low, int high, int step,
            int stride, int slice, int fallback)
        {
            int best = fallback;
            double bestScore = double.NegativeInfinity;
            int length = Math.Min(slice, frames - follow);
            for (int candidate = low; candidate <= high; candidate += step)
            {
                int span = Math.Min(length, frames - candidate);
                if (span <= 0) continue;
                double score = 0;
                for (int i = 0; i < span; i += stride)
                    score += samples[(candidate + i) * channels] * samples[(follow + i) * channels];
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best;
        }
    }
}
