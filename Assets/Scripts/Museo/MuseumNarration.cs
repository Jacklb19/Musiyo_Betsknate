using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    public enum NarrationState { Idle, Loading, Playing, Paused, Finished, Unavailable }

    [RequireComponent(typeof(MuseumInteraction), typeof(TourLoader))]
    [RequireComponent(typeof(MuseumPointPresence))]
    public sealed class MuseumNarration : MonoBehaviour
    {
        private MuseumInteraction interaction;
        private TourLoader loader;
        private AudioSource source;
        private AudioClip decodingClip;
        private Coroutine loading;
        private Coroutine subtitlesLoading;
        private UnityWebRequest request;
        private UnityWebRequest subtitlesRequest;
        private System.Collections.Generic.IReadOnlyList<SubtitleCue> cues = Array.Empty<SubtitleCue>();
        private DateTimeOffset expiration;
        private bool pausedByVisit;
        private bool pausedByDistance;
        private MuseumPointPresence presence;
        public NarrationState State { get; private set; }
        public string Status { get; private set; } = "";
        public string Transcription { get; private set; } = "";
        public string Subtitle { get; private set; } = "";
        public string Attribution { get; private set; } = "";
        public bool Muted => source != null && source.mute;
        public float Volume => source != null ? source.volume : .8f;
        public bool CanPlay => source != null && source.clip != null;
        public float PlaybackTime => CanPlay ? source.time : 0;
        public float Duration => CanPlay ? source.clip.length : 0;
        public AudioSource ContextSource => source;
        public event Action Changed;
        public event Action SubtitleChanged;
        public bool PausedByDistance => pausedByDistance;

        private void Awake()
        {
            interaction = GetComponent<MuseumInteraction>();
            loader = GetComponent<TourLoader>();
            presence = GetComponent<MuseumPointPresence>();
            var audioObject = new GameObject("ContextNarration");
            audioObject.transform.SetParent(transform, false);
            source = audioObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1;
            source.maxDistance = 8;
            source.volume = .8f;
        }
        private void OnEnable()
        {
            interaction.ElementChanged += Select;
            interaction.Changed += OnInteractionChanged;
            presence.Changed += OnPresenceChanged;
            Select(interaction.SelectedElement);
        }
        private void OnDisable()
        {
            interaction.ElementChanged -= Select;
            interaction.Changed -= OnInteractionChanged;
            presence.Changed -= OnPresenceChanged;
            Clear();
        }
        private void OnDestroy() { if (source != null) Destroy(source.gameObject); }

        private void Select(ElementSummaryContractV1 element)
        {
            Clear();
            if (element == null || !element.has_narration) return;
            loading = StartCoroutine(Load(element.slug));
        }
        public void Retry() => Select(interaction.SelectedElement);
        public void SetVolume(float volume)
        { source.volume = Mathf.Clamp01(volume); Changed?.Invoke(); }
        public void ToggleMute() { source.mute = !source.mute; Changed?.Invoke(); }
        public void TogglePause()
        {
            if (!CanPlay || interaction.State == InteractionState.Paused) return;
            pausedByDistance = false;
            if (State == NarrationState.Playing) { source.Pause(); SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused")); }
            else if (State == NarrationState.Paused) { source.UnPause(); SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing")); }
            else Repeat();
        }
        public void Repeat()
        {
            if (!CanPlay || interaction.State == InteractionState.Paused) return;
            pausedByDistance = false;
            source.Stop();
            source.Play();
            SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
            UpdateSubtitle();
        }
        private void OnInteractionChanged()
        {
            if (interaction.State == InteractionState.Paused && State == NarrationState.Playing)
            { pausedByVisit = true; source.Pause(); SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused")); }
            else if (interaction.State != InteractionState.Paused && pausedByVisit && CanPlay)
            { pausedByVisit = false; source.UnPause(); SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing")); }
        }
        private void Update()
        {
            if (!CanPlay) return;
            if (DateTimeOffset.UtcNow >= expiration)
            {
                Clear();
                SetState(NarrationState.Unavailable, MuseumInterfaceText.Get("narration_expired"));
                return;
            }
            if (State == NarrationState.Playing && !source.isPlaying)
                SetState(NarrationState.Finished, MuseumInterfaceText.Get("narration_finished"));
            UpdateSubtitle();
        }
        /// <summary>Walking away from the point pauses its narration; coming back resumes it where it stopped.</summary>
        private void OnPresenceChanged(bool away)
        {
            if (away && !pausedByDistance && State == NarrationState.Playing)
            {
                pausedByDistance = true;
                source.Pause();
                SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused_away"));
            }
            else if (!away && pausedByDistance)
            {
                pausedByDistance = false;
                if (State != NarrationState.Paused) return;
                source.UnPause();
                SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
            }
        }
        private void UpdateSubtitle()
        {
            string text = cues.Count == 0 ? Transcription : State == NarrationState.Finished ? "" : WebVttReader.At(cues, PlaybackTime);
            if (text == Subtitle) return;
            Subtitle = text;
            SubtitleChanged?.Invoke();
        }

        private IEnumerator Load(string slug)
        {
            SetState(NarrationState.Loading, MuseumInterfaceText.Get("narration_loading"));
            request = UnityWebRequest.Get(loader.ApiBase + "/elements/" + Uri.EscapeDataString(slug));
            request.timeout = 15;
            yield return request.SendWebRequest();
            string json = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
            DisposeRequest();
            if (!ElementContractV1.TryParse(json, out var element) || element.slug != slug)
            { Unavailable(); yield break; }
            var narration = NarrationResourcePolicy.Select(element);
            if (narration == null) { Unavailable(); yield break; }
            Transcription = narration.transcription;
            Attribution = string.Join("\n", new[] {
                string.IsNullOrEmpty(narration.credit) ? null : MuseumInterfaceText.Format("narration_credit", narration.credit),
                string.IsNullOrEmpty(narration.provenance) ? null : MuseumInterfaceText.Format("narration_provenance", narration.provenance) }.Where(value => !string.IsNullOrEmpty(value)));
            UpdateSubtitle();
            Changed?.Invoke();
            request = AccessRequest(narration.id);
            yield return request.SendWebRequest();
            json = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
            DisposeRequest();
            if (!NarrationResourcePolicy.TryAccess(loader.ApiBase, json, "audio/mpeg", NarrationResourcePolicy.ByteLimit, out var access, out var url))
            { Unavailable(); yield break; }
            expiration = DateTimeOffset.Parse(access.expires_at, CultureInfo.InvariantCulture);
            request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG);
            request.timeout = 60;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (request.downloadedBytes > NarrationResourcePolicy.ByteLimit)
                { DisposeRequest(); Unavailable(); yield break; }
                yield return null;
            }
            AudioClip clip = null;
            if (request.result == UnityWebRequest.Result.Success && request.downloadedBytes <= NarrationResourcePolicy.ByteLimit)
            {
                try { clip = DownloadHandlerAudioClip.GetContent(request); }
                catch (Exception) { }
            }
            decodingClip = clip;
            float decodeDeadline = Time.realtimeSinceStartup + 20;
            while (clip != null && clip.loadState != AudioDataLoadState.Loaded
                && clip.loadState != AudioDataLoadState.Failed && Time.realtimeSinceStartup < decodeDeadline)
                yield return null;
            DisposeRequest();
            decodingClip = null;
            loading = null;
            if (clip == null || clip.loadState != AudioDataLoadState.Loaded || clip.length <= 0
                || clip.length > 180 || DateTimeOffset.UtcNow >= expiration)
            {
                if (clip != null) Destroy(clip);
                Unavailable();
                yield break;
            }
            source.transform.position = interaction.ActivePoint.Anchor.LookTarget.position;
            source.clip = clip;
            source.Play();
            SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
            OnInteractionChanged();
            if (presence.Away) OnPresenceChanged(true);
            var subtitles = element.resources.FirstOrDefault(resource => resource.id == narration.subtitles_resource_id
                && resource.kind == "subtitles" && resource.mime == "text/vtt");
            if (subtitles != null) subtitlesLoading = StartCoroutine(LoadSubtitles(subtitles.id));
        }

        private IEnumerator LoadSubtitles(string id)
        {
            subtitlesRequest = AccessRequest(id);
            yield return subtitlesRequest.SendWebRequest();
            var json = subtitlesRequest.result == UnityWebRequest.Result.Success ? subtitlesRequest.downloadHandler.text : null;
            DisposeSubtitlesRequest();
            if (!NarrationResourcePolicy.TryAccess(loader.ApiBase, json, "text/vtt", 300000, out _, out var url)) yield break;
            subtitlesRequest = UnityWebRequest.Get(url);
            subtitlesRequest.timeout = 15;
            var operation = subtitlesRequest.SendWebRequest();
            while (!operation.isDone)
            {
                if (subtitlesRequest.downloadedBytes > 300000) { DisposeSubtitlesRequest(); yield break; }
                yield return null;
            }
            if (subtitlesRequest.result == UnityWebRequest.Result.Success && subtitlesRequest.downloadedBytes <= 300000)
                cues = WebVttReader.Parse(subtitlesRequest.downloadHandler.text);
            DisposeSubtitlesRequest();
            subtitlesLoading = null;
            UpdateSubtitle();
        }
        private UnityWebRequest AccessRequest(string id)
            => new UnityWebRequest(loader.ApiBase + "/resources/" + Uri.EscapeDataString(id) + "/access?schema_version=1", "POST")
            { downloadHandler = new DownloadHandlerBuffer(), timeout = 15 };
        private void Unavailable()
        { loading = null; SetState(NarrationState.Unavailable, MuseumInterfaceText.Get("narration_unavailable")); }
        private void DisposeRequest()
        { request?.Abort(); request?.Dispose(); request = null; }
        private void DisposeSubtitlesRequest()
        { subtitlesRequest?.Abort(); subtitlesRequest?.Dispose(); subtitlesRequest = null; }
        public void Clear()
        {
            if (loading != null) StopCoroutine(loading);
            if (subtitlesLoading != null) StopCoroutine(subtitlesLoading);
            loading = subtitlesLoading = null;
            DisposeRequest();
            DisposeSubtitlesRequest();
            if (decodingClip != null) Destroy(decodingClip);
            decodingClip = null;
            if (source != null)
            {
                source.Stop();
                var clip = source.clip;
                source.clip = null;
                if (clip != null) Destroy(clip);
            }
            cues = Array.Empty<SubtitleCue>();
            pausedByVisit = false;
            pausedByDistance = false;
            Transcription = Attribution = "";
            UpdateSubtitle();
            SetState(NarrationState.Idle, "");
        }
        private void SetState(NarrationState state, string status)
        { State = state; Status = status; Changed?.Invoke(); }
    }
}
