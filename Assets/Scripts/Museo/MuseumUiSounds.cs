using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    public enum MuseumUiSound { Focus, Confirm, Back, Step, Open, Close }

    /// <summary>
    /// The quiet sounds of the interface: a soft tick when the focus moves, a warm two-note tap on a choice, a lower one
    /// on the way back, a small click on a slider step and a breath when a menu opens or closes. Like the fire's crackle,
    /// they are made here rather than recorded, so nothing is downloaded and nothing is borrowed. They follow the
    /// visitor's general volume and are never loud.
    /// </summary>
    public sealed class MuseumUiSounds : MonoBehaviour
    {
        public const int Rate = 22050;
        /// <summary>Share of the general volume the interface sounds take.</summary>
        public const float Level = .32f;
        private static readonly AudioClip[] clips = new AudioClip[6];
        private static MuseumUiSounds current;
        private AudioSource source;
        private float volume = .8f;
        private int lastFrame = -1;
        private MuseumUiSound lastKind;

        /// <summary>The one interface voice of the application; made the first time it is asked for.</summary>
        public static MuseumUiSounds Current
        {
            get
            {
                if (current != null) return current;
                var holder = new GameObject("MuseumUiSounds");
                if (Application.isPlaying) DontDestroyOnLoad(holder);
                current = holder.AddComponent<MuseumUiSounds>();
                return current;
            }
        }

        /// <summary>Count of sounds played, for tests and for a mute switch that only silences the interface.</summary>
        public int Played { get; private set; }
        public bool Muted { get; set; }

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0;
            source.priority = 32;
            source.bypassReverbZones = true;
            source.bypassEffects = true;
        }

        private void OnDestroy() { if (current == this) current = null; }

        /// <summary>The visitor's general volume, applied as the ceiling of every interface sound.</summary>
        public void SetVolume(float value) { if (MuseumExperienceConfiguration.Finite(value)) volume = Mathf.Clamp01(value); }

        public void Play(MuseumUiSound kind)
        {
            Played++;
            if (Muted || source == null || volume <= 0) return;
            // Two sounds in one frame (a click that also moves the focus) are heard as one.
            if (lastFrame == Time.frameCount && (lastKind == MuseumUiSound.Confirm || lastKind == MuseumUiSound.Back)) return;
            lastFrame = Time.frameCount;
            lastKind = kind;
            source.PlayOneShot(Clip(kind), volume * Level);
        }

        public static AudioClip Clip(MuseumUiSound kind)
        {
            int index = (int)kind;
            if (clips[index] == null) clips[index] = Make(kind);
            return clips[index];
        }

        /// <summary>Samples of one sound, from -1 to 1; the same every time.</summary>
        public static float[] Samples(MuseumUiSound kind)
        {
            switch (kind)
            {
                case MuseumUiSound.Focus: return Tone(.07f, new[] { (1480f, .5f) }, .004f, .018f, .35f);
                case MuseumUiSound.Confirm: return Tone(.16f, new[] { (660f, .55f), (988f, .4f) }, .005f, .06f, .55f, 1.0f);
                case MuseumUiSound.Back: return Tone(.15f, new[] { (784f, .5f), (523f, .45f) }, .005f, .055f, .5f, 1.0f);
                case MuseumUiSound.Step: return Tone(.045f, new[] { (2093f, .4f) }, .002f, .012f, .3f);
                case MuseumUiSound.Open: return Tone(.26f, new[] { (262f, .5f), (392f, .3f) }, .06f, .12f, .4f);
                default: return Tone(.22f, new[] { (392f, .4f), (262f, .4f) }, .03f, .1f, .35f);
            }
        }

        private static AudioClip Make(MuseumUiSound kind)
        {
            var data = Samples(kind);
            var clip = AudioClip.Create("Ui" + kind, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// A short, round sound: sine partials under an attack and a decay, with a light glide between the first two
        /// partials when <paramref name="glide"/> is given, so a confirmation rises and a step back falls.
        /// </summary>
        private static float[] Tone(float seconds, (float hertz, float gain)[] partials, float attack, float decay, float peak, float glide = 0)
        {
            int count = Mathf.Max(1, (int)(Rate * seconds));
            var data = new float[count];
            for (int index = 0; index < count; index++)
            {
                float t = index / (float)Rate;
                float envelope = Mathf.Clamp01(t / attack) * Mathf.Exp(-Mathf.Max(0, t - attack) / decay);
                float share = glide > 0 && partials.Length > 1 ? Mathf.Clamp01(t / (seconds * .5f)) : 0;
                float sample = 0;
                for (int p = 0; p < partials.Length; p++)
                {
                    float gain = partials[p].gain;
                    // With a glide the second note takes over from the first across the sound.
                    if (glide > 0 && partials.Length > 1) gain *= p == 0 ? 1 - share : share;
                    sample += Mathf.Sin(2 * Mathf.PI * partials[p].hertz * t) * gain;
                }
                data[index] = Mathf.Clamp(sample * envelope * peak, -1, 1);
            }
            // The last samples fade so the end never clicks.
            int tail = Mathf.Min(count, Rate / 200);
            for (int index = 0; index < tail; index++) data[count - 1 - index] *= index / (float)tail;
            return data;
        }
    }
}
