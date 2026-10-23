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
        /// <summary>This device can record here: a headset, a computer with a microphone, a browser that allows it.</summary>
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
    /// asked for the first time it is used. The browser build has no Unity microphone; there the page records
    /// (<see cref="WebVoiceRecorder"/>). Samples stay in memory until the recording is sent or dropped; nothing is written
    /// or logged.
    /// </summary>
    public sealed class MicrophoneVoiceRecorder : IGuideVoiceRecorder
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        private readonly WebVoiceRecorder page = new WebVoiceRecorder();
        public bool Available => page.Available;
        public bool Recording => page.Recording;
        public float Elapsed => page.Elapsed;
        public bool AwaitingPermission => page.AwaitingPermission;
        public GuideRecorderStart Begin(int maxSeconds) => page.Begin(maxSeconds);
        public byte[] End() => page.End();
        public void Discard() => page.Discard();
        public void Dispose() => page.Dispose();
#else
        private const float MinimumSeconds = .1f;
        private AudioClip clip;
        private int rate;
        private float startedAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool permissionAsked, permissionAnswered;
#endif

        /// <summary>
        /// A headset always has a microphone, and Android may list none until the permission is granted, which is only
        /// asked when the visitor first talks; elsewhere a listed device is needed.
        /// </summary>
        public bool Available => Application.platform == RuntimePlatform.Android || Microphone.devices.Length > 0;
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
            if (Microphone.devices.Length == 0) return GuideRecorderStart.Unavailable;
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

    /// <summary>
    /// The browser's microphone, through the page (<c>Plugins/WebGL/MusiyoVoice.jslib</c>). The browser asks the visitor
    /// for the microphone the first time; the stream is released after every take, so the browser's recording mark goes
    /// out as soon as the visitor stops talking. Outside a browser build nothing is available.
    /// </summary>
    public sealed class WebVoiceRecorder : IGuideVoiceRecorder
    {
        private const int Idle = 0, Waiting = 1, Ready = 2, Taking = 3, Refused = 4, Missing = 5;
        private float startedAt;
        private int limit;
        private bool asked;
        private bool recording;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceAvailable();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceState();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void MusiyoVoiceRequest();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceStart(int maxSeconds);
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceRecording();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceStop();
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern int MusiyoVoiceTake(byte[] buffer, int length);
        [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void MusiyoVoiceDiscard();
#else
        private static int MusiyoVoiceAvailable() => 0;
        private static int MusiyoVoiceState() => Missing;
        private static void MusiyoVoiceRequest() { }
        private static int MusiyoVoiceStart(int maxSeconds) => 0;
        private static int MusiyoVoiceRecording() => 0;
        private static int MusiyoVoiceStop() => 0;
        private static int MusiyoVoiceTake(byte[] buffer, int length) => 0;
        private static void MusiyoVoiceDiscard() { }
#endif

        public bool Available => MusiyoVoiceAvailable() == 1;
        public bool Recording => recording && MusiyoVoiceRecording() == 1;
        public float Elapsed => recording ? Mathf.Min(Time.realtimeSinceStartup - startedAt, limit) : 0;
        public bool AwaitingPermission => asked && MusiyoVoiceState() == Waiting;

        public GuideRecorderStart Begin(int maxSeconds)
        {
            // A granted stream waits for this call; only a take in progress is dropped.
            if (recording) Discard();
            switch (MusiyoVoiceState())
            {
                case Refused: return GuideRecorderStart.Denied;
                case Missing: return GuideRecorderStart.Unavailable;
                case Ready:
                    limit = Mathf.Clamp(maxSeconds, 1, 30);
                    if (MusiyoVoiceStart(limit) != 1) return GuideRecorderStart.Unavailable;
                    asked = false;
                    recording = true;
                    startedAt = Time.realtimeSinceStartup;
                    return GuideRecorderStart.Started;
                default:
                    asked = true;
                    MusiyoVoiceRequest();
                    return MusiyoVoiceState() == Refused ? GuideRecorderStart.Denied : GuideRecorderStart.PermissionPending;
            }
        }

        public byte[] End()
        {
            if (!recording) return null;
            recording = false;
            int size = MusiyoVoiceStop();
            if (size <= 0) return null;
            var wav = new byte[size];
            return MusiyoVoiceTake(wav, size) == size ? wav : null;
        }

        public void Discard()
        {
            recording = false;
            MusiyoVoiceDiscard();
        }

        public void Dispose() => Discard();
    }
}
