#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The two speeds a narration is heard at (owner, 28/10/2026: «Intentemos hacerlo a 1.5 nada mas, por dos creo que es
    /// demasiado, y si no queda bien, lo ocultamos»). Faster keeps the voice's pitch: on the headset and in the Editor the
    /// recording is stretched once (<see cref="NarrationTimeStretch"/>); in a browser the page's own audio element plays
    /// it (<see cref="BrowserNarration"/>).
    /// </summary>
    public static class MuseumNarrationSpeed
    {
        public const float Normal = 1, Fast = 1.5f;

        public static bool IsSpeed(float value) => Mathf.Approximately(value, Normal) || Mathf.Approximately(value, Fast);

        /// <summary>The other speed: the control toggles between the two.</summary>
        public static float Next(float value) => value > Normal ? Normal : Fast;

        /// <summary>Whether this build offers the control; each platform can hide it in the experience configuration.</summary>
        public static bool Offered(MuseumExperienceConfiguration settings)
            => Application.platform == RuntimePlatform.WebGLPlayer ? settings.NarrationSpeedWeb : settings.NarrationSpeedHeadset;
    }

    /// <summary>
    /// The page's audio element, through MusiyoNarration.jslib, for a narration heard faster in a browser build; nothing
    /// anywhere else.
    /// </summary>
    public sealed class BrowserNarration
    {
        public enum PlayState { None = 0, Playing = 1, Paused = 2, Ended = 3, Failed = 4 }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int MusiyoNarrationAvailable();
        [DllImport("__Internal")] private static extern void MusiyoNarrationOpen(string url);
        [DllImport("__Internal")] private static extern void MusiyoNarrationPlay(float seconds, float rate);
        [DllImport("__Internal")] private static extern void MusiyoNarrationPause();
        [DllImport("__Internal")] private static extern void MusiyoNarrationSeek(float seconds);
        [DllImport("__Internal")] private static extern float MusiyoNarrationTime();
        [DllImport("__Internal")] private static extern int MusiyoNarrationState();
        [DllImport("__Internal")] private static extern void MusiyoNarrationVolume(float volume, int muted);
        [DllImport("__Internal")] private static extern void MusiyoNarrationClose();
        private static int available = -1;
        public static bool Available => (available < 0 ? available = MusiyoNarrationAvailable() : available) == 1;
        public void Open(string url) => MusiyoNarrationOpen(url);
        /// <summary>Plays from <paramref name="seconds"/>, or from where it is when negative.</summary>
        public void Play(float seconds, float rate) => MusiyoNarrationPlay(seconds, rate);
        public void Pause() => MusiyoNarrationPause();
        public void Seek(float seconds) => MusiyoNarrationSeek(seconds);
        public float Time => MusiyoNarrationTime();
        public PlayState State => (PlayState)MusiyoNarrationState();
        public void Volume(float volume, bool muted) => MusiyoNarrationVolume(volume, muted ? 1 : 0);
        public void Close() => MusiyoNarrationClose();
#else
        public static bool Available => false;
        public void Open(string url) { }
        public void Play(float seconds, float rate) { }
        public void Pause() { }
        public void Seek(float seconds) { }
        public float Time => 0;
        public PlayState State => PlayState.None;
        public void Volume(float volume, bool muted) { }
        public void Close() { }
#endif

        /// <summary>
        /// The share of the volume heard at <paramref name="distance"/> metres, as the narration's own 3D source would give
        /// it with logarithmic rolloff: full up to <paramref name="nearest"/>, then falling as nearest / distance.
        /// </summary>
        public static float Rolloff(float distance, float nearest, float farthest)
        {
            if (!(nearest > 0)) return 1;
            float clamped = Mathf.Min(Mathf.Max(distance, nearest), Mathf.Max(nearest, farthest));
            return nearest / clamped;
        }
    }
}
