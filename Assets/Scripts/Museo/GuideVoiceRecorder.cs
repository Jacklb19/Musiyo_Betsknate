using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MusiyoBetsknate.Museum
{
    public enum GuideRecorderStart { Started, PermissionPending, Denied, Unavailable }

    /// <summary>Records one spoken question. Tests replace the microphone with their own samples.</summary>
    public interface IGuideVoiceRecorder : IDisposable
    {
        /// <summary>This device can record here; a browser cannot (its page records instead).</summary>
        bool Available { get; }
        bool Recording { get; }
        /// <summary>Seconds recorded so far.</summary>
        float Elapsed { get; }
        /// <summary>The microphone permission was asked for and the visitor has not answered yet.</summary>
        bool AwaitingPermission { get; }
        /// <summary>Starts recording for at most <paramref name="maxSeconds"/>, asking for the microphone when needed.</summary>
        GuideRecorderStart Begin(int maxSeconds);
        /// <summary>Stops and returns the recording as WAV PCM16 mono at 16 kHz, or null when it is shorter than a tenth of a second.</summary>
        byte[] End();
        /// <summary>Stops and drops the recording; nothing of it remains.</summary>
        void Discard();
    }

    /// <summary>
    /// The device microphone through Unity's Microphone (Quest and Windows). On Android the RECORD_AUDIO permission is
    /// asked for the first time it is used. The browser build has no Unity microphone, so voice is not offered there.
    /// Samples stay in memory until the recording is sent or dropped; nothing is written or logged.
    /// </summary>
    public sealed class MicrophoneVoiceRecorder : IGuideVoiceRecorder
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public bool Available => false;
        public bool Recording => false;
        public float Elapsed => 0;
        public bool AwaitingPermission => false;
        public GuideRecorderStart Begin(int maxSeconds) => GuideRecorderStart.Unavailable;
        public byte[] End() => null;
        public void Discard() { }
        public void Dispose() { }
#else
        private const float MinimumSeconds = .1f;
        private AudioClip clip;
        private int rate;
        private float startedAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool permissionAsked, permissionAnswered;
#endif

        public bool Available => Microphone.devices.Length > 0;
        public bool Recording => clip != null && Microphone.IsRecording(null);
        public float Elapsed => clip != null ? Mathf.Min(Time.realtimeSinceStartup - startedAt, clip.length) : 0;
        public bool AwaitingPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return permissionAsked && !permissionAnswered && !Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
                return false;
#endif
            }
        }

        public GuideRecorderStart Begin(int maxSeconds)
        {
            Discard();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                if (permissionAsked && permissionAnswered) return GuideRecorderStart.Denied;
                if (!permissionAsked)
                {
                    permissionAsked = true;
                    var callbacks = new PermissionCallbacks();
                    callbacks.PermissionGranted += _ => permissionAnswered = true;
                    callbacks.PermissionDenied += _ => permissionAnswered = true;
                    Permission.RequestUserPermission(Permission.Microphone, callbacks);
                }
                return GuideRecorderStart.PermissionPending;
            }
#endif
            if (!Available) return GuideRecorderStart.Unavailable;
            Microphone.GetDeviceCaps(null, out int lowest, out int highest);
            // Any rate is fine when the device says nothing; otherwise the closest to 16 kHz it can do.
            rate = lowest == 0 && highest == 0 ? WavAudio.RecordingRate : Mathf.Clamp(WavAudio.RecordingRate, lowest, highest);
            clip = Microphone.Start(null, false, Mathf.Clamp(maxSeconds, 1, 30), rate);
            if (clip == null) return GuideRecorderStart.Unavailable;
            startedAt = Time.realtimeSinceStartup;
            return GuideRecorderStart.Started;
        }

        public byte[] End()
        {
            if (clip == null) return null;
            bool stillRecording = Microphone.IsRecording(null);
            int frames = stillRecording ? Microphone.GetPosition(null) : clip.samples;
            Microphone.End(null);
            byte[] wav = null;
            if (frames >= rate * MinimumSeconds)
            {
                var data = new float[frames * clip.channels];
                clip.GetData(data, 0);
                var mono = data;
                if (clip.channels > 1)
                {
                    mono = new float[frames];
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;
                        for (int channel = 0; channel < clip.channels; channel++) sum += data[frame * clip.channels + channel];
                        mono[frame] = sum / clip.channels;
                    }
                }
                wav = WavAudio.EncodePcm16Mono(mono, rate);
                Array.Clear(data, 0, data.Length);
            }
            Drop();
            return wav;
        }

        public void Discard()
        {
            if (clip == null) return;
            Microphone.End(null);
            Drop();
        }

        private void Drop()
        {
            if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = null;
        }

        public void Dispose() => Discard();
#endif
    }
}
