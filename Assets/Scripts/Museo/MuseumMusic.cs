using System;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Mix of the background music, read from <c>Resources/MuseumMusic.json</c>.</summary>
    [Preserve]
    public sealed class MuseumMusicSettings
    {
        public const string ResourceName = "MuseumMusic";

        [JsonProperty("schema_version")] public int SchemaVersion;
        /// <summary>Music level relative to the visitor's chosen volume, on screens and in a headset.</summary>
        [JsonProperty("level")] public float Level;
        [JsonProperty("level_headset")] public float LevelHeadset;
        /// <summary>Share of the music level kept while a narration or the guide speaks.</summary>
        [JsonProperty("voice_duck")] public float VoiceDuck;
        [JsonProperty("duck_seconds")] public float DuckSeconds;
        [JsonProperty("fade_in_seconds")] public float FadeInSeconds;
        /// <summary>Overlap between the end of the track and its next start, so the loop has no seam.</summary>
        [JsonProperty("loop_crossfade_seconds")] public float LoopCrossfadeSeconds;

        private static MuseumMusicSettings current;
        public static MuseumMusicSettings Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing museum music settings.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumMusicSettings Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumMusicSettings>(json);
            if (result == null || result.SchemaVersion != 1 || !Unit(result.Level) || !Unit(result.LevelHeadset) || !Unit(result.VoiceDuck)
                || !Positive(result.DuckSeconds) || !Positive(result.FadeInSeconds) || !Positive(result.LoopCrossfadeSeconds))
                throw new InvalidOperationException("Invalid museum music settings.");
            return result;
        }

        private static bool Unit(float value) => MuseumExperienceConfiguration.Finite(value) && value >= 0 && value <= 1;
        private static bool Positive(float value) => MuseumExperienceConfiguration.Finite(value) && value > 0 && value <= 60;
    }

    /// <summary>
    /// Quiet background music for the visit: one neutral, licensed track (see ThirdParty/third_party_assets.json) looped
    /// with a crossfade, never cultural audio, which only the API serves. It follows the visitor's volume preference, goes
    /// silent at zero, and steps back while a narration or the guide speaks so voices stay clear.
    /// </summary>
    public sealed class MuseumMusic : MonoBehaviour
    {
        [SerializeField] private AudioClip track;
        private readonly AudioSource[] sources = new AudioSource[2];
        private int playing;
        private float started;
        private float duck = 1;
        private float musicLevel = 1;
        private MuseumNarration narration;
        private MuseumCompanion companion;
        private MuseumMusicSettings settings;
        private float fallbackVolume = -1;

        public AudioClip Track => track;
        /// <summary>The visitor's own music level (0 to 1) on top of the general volume, for a settings control.</summary>
        public float MusicLevel => musicLevel;
        /// <summary>Silences the music only; narrations and the guide are unaffected.</summary>
        public bool Muted { get; set; }
        public bool VoiceActive { get; private set; }
        public float CurrentVolume => sources[playing] != null ? sources[playing].volume : 0;
        public AudioSource Playing => sources[playing];

        public void Configure(AudioClip clip) => track = clip;
        public void SetMusicLevel(float value) { if (MuseumExperienceConfiguration.Finite(value)) musicLevel = Mathf.Clamp01(value); }

        /// <summary>The volume the music aims at: the visitor's volume times the music level, ducked while a voice speaks.</summary>
        public static float TargetVolume(float visitorVolume, float level, float musicLevel, bool muted, float duck)
            => muted ? 0 : Mathf.Clamp01(visitorVolume) * Mathf.Clamp01(level) * Mathf.Clamp01(musicLevel) * Mathf.Clamp01(duck);

        private void Awake()
        {
            settings = MuseumMusicSettings.Current;
            musicLevel = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current,
                PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "")).MusicLevel;
            for (int index = 0; index < sources.Length; index++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0;
                source.priority = 200;
                source.bypassReverbZones = true;
                source.volume = 0;
                sources[index] = source;
            }
        }

        private void Start()
        {
            if (track == null) return;
            sources[playing].clip = track;
            sources[playing].Play();
            started = Time.unscaledTime;
        }

        private void Update()
        {
            if (track == null) return;
            VoiceActive = SpeakingVoice();
            float wanted = VoiceActive ? settings.VoiceDuck : 1;
            duck = Mathf.MoveTowards(duck, wanted, Time.unscaledDeltaTime / settings.DuckSeconds);
            float level = Application.platform == RuntimePlatform.Android ? settings.LevelHeadset : settings.Level;
            float target = TargetVolume(VisitorVolume(), level, musicLevel, Muted, duck)
                * Mathf.Clamp01((Time.unscaledTime - started) / settings.FadeInSeconds);
            var current = sources[playing];
            var next = sources[1 - playing];
            float remaining = track.length - current.time;
            if (current.isPlaying && remaining <= settings.LoopCrossfadeSeconds && !next.isPlaying)
            {
                next.clip = track;
                next.time = 0;
                next.Play();
            }
            if (next.isPlaying && current.isPlaying)
            {
                // Equal-power crossfade over the overlap.
                float share = Mathf.Clamp01(1 - remaining / settings.LoopCrossfadeSeconds);
                current.volume = target * Mathf.Cos(share * Mathf.PI / 2);
                next.volume = target * Mathf.Sin(share * Mathf.PI / 2);
            }
            else if (!current.isPlaying && next.isPlaying)
            {
                playing = 1 - playing;
                sources[playing].volume = target;
            }
            else if (!current.isPlaying && !next.isPlaying && current.clip != null && current.time == 0)
            {
                // A paused or interrupted application resumes the track from its start.
                current.Play();
            }
            else current.volume = target;
        }

        private bool SpeakingVoice()
        {
            if (narration == null) narration = FindAnyObjectByType<MuseumNarration>();
            if (companion == null) companion = FindAnyObjectByType<MuseumCompanion>();
            return (narration != null && narration.State == NarrationState.Playing && !narration.Muted && narration.Volume > 0)
                || (companion != null && companion.Speaking);
        }

        /// <summary>The visitor's general volume: the one the narration applies, or the saved preference before it exists.</summary>
        private float VisitorVolume()
        {
            if (narration != null) return narration.Volume;
            if (fallbackVolume < 0)
                fallbackVolume = MuseumVisitorPreferences.Load(MuseumExperienceConfiguration.Current,
                    PlayerPrefs.GetString(MuseumVisitorPreferences.StorageKey, "")).Volume;
            return fallbackVolume;
        }
    }
}
