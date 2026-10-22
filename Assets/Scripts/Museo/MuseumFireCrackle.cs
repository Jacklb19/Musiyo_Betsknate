using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The crackle of the fire during the closing credits, made here rather than recorded: a soft breath of flame under
    /// small sharp crackles and an occasional pop, in a seamless loop. Nothing is downloaded and nothing is borrowed.
    /// </summary>
    public static class MuseumFireCrackle
    {
        public const int Rate = 22050;
        public const float Seconds = 9;
        private static AudioClip clip;

        public static AudioSource Create(Transform parent, Vector3 localPosition, bool spatial)
        {
            var holder = new GameObject("FireCrackle");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition + Vector3.up * .3f;
            var source = holder.AddComponent<AudioSource>();
            source.clip = clip != null ? clip : clip = Make(1508);
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0;
            source.priority = 180;
            // On a headset the visitor stands by the fire; a desktop hears it as the screen shows it.
            source.spatialBlend = spatial ? .7f : 0;
            source.minDistance = 1.5f;
            source.maxDistance = 14;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
            return source;
        }

        /// <summary>The same seed always gives the same fire.</summary>
        public static AudioClip Make(int seed)
        {
            int count = (int)(Rate * Seconds);
            var data = new float[count];
            var random = new System.Random(seed);
            float Noise() => (float)(random.NextDouble() * 2 - 1);
            // The breath of the flames: slow, dark noise.
            float brown = 0, low = 0;
            for (int index = 0; index < count; index++)
            {
                brown = Mathf.Clamp(brown * .997f + Noise() * .03f, -1, 1);
                low += (brown - low) * .05f;
                data[index] = low * .5f;
            }
            // The loop is the clip less its last third of a second, which is folded over its start: the end runs on into
            // the beginning without a click.
            int seam = Rate / 3;
            for (int index = 0; index < seam; index++)
            {
                float share = index / (float)seam;
                data[index] = data[index] * share + data[count - seam + index] * (1 - share);
            }
            // Crackles: short bright bursts at random moments, mostly small, now and then a louder pop.
            double time = 0;
            while (true)
            {
                time += -System.Math.Log(1 - random.NextDouble()) / 9.0;
                int at = (int)(time * Rate);
                bool pop = random.NextDouble() < .06;
                int length = (int)(Rate * (pop ? .03 + random.NextDouble() * .03 : .002 + random.NextDouble() * .012));
                if (at + length >= count - seam) break;
                float strength = pop ? .55f + (float)random.NextDouble() * .35f : .08f + Mathf.Pow((float)random.NextDouble(), 2.2f) * .6f;
                float previous = 0;
                for (int offset = 0; offset < length; offset++)
                {
                    float envelope = Mathf.Exp(-offset / (length * (pop ? .25f : .18f)));
                    float sample = Noise();
                    // A pop is rounder; a crackle keeps only its sharp edge.
                    float shaped = pop ? (previous = previous + (sample - previous) * .35f) : sample - previous * .6f;
                    if (!pop) previous = sample;
                    data[at + offset] += shaped * envelope * strength;
                }
            }
            var loop = new float[count - seam];
            System.Array.Copy(data, loop, loop.Length);
            float peak = 0;
            foreach (var sample in loop) peak = Mathf.Max(peak, Mathf.Abs(sample));
            if (peak > 0) for (int index = 0; index < loop.Length; index++) loop[index] *= .9f / peak;
            var made = AudioClip.Create("FireCrackle", loop.Length, 1, Rate, false);
            made.SetData(loop, 0);
            return made;
        }
    }
}
