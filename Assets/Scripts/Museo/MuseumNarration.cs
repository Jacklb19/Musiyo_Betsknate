using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    /// <summary>Ready: loaded and offered with its button, not started; it is how a record chosen with the arrows begins.</summary>
    public enum NarrationState { Idle, Loading, Playing, Paused, Finished, Unavailable, Ready }

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
        /// <summary>Visitor's volume for the narration; a fade scales it without changing it.</summary>
        private float volume = .8f;
        private Coroutine fading;
        private TourPoint narratedPoint;
        /// <summary>Seconds the narration of a record takes to fade out when the visitor moves to another record.</summary>
        public const float SwitchFadeSeconds = .6f;
        private MuseumPointPresence presence;
        /// <summary>The narration as downloaded, and the same narration stretched to be heard 1.5 times as fast.</summary>
        private AudioClip narrationClip, fastClip;
        private float speed = MuseumNarrationSpeed.Normal;
        private bool stretching, stretchFailed;
        /// <summary>Grows with every narration cleared, so a stretch finished for an earlier one is dropped.</summary>
        private int stretchGeneration;
        /// <summary>Temporary address of the narration, which the browser's audio element plays at 1.5x.</summary>
        private string narrationUrl;
        private readonly BrowserNarration browser = new BrowserNarration();
        private bool browserOpen, inBrowser, browserFailed;
        private AudioListener listener;
        public NarrationState State { get; private set; }
        public string Status { get; private set; } = "";
        public string Transcription { get; private set; } = "";
        public string Subtitle { get; private set; } = "";
        /// <summary>
        /// Credits shown with the narration: none. The narration's resource credit names the synthetic voice and its
        /// provenance holds working notes; both belong to the closing credits (owner, 22/10/2026).
        /// </summary>
        public string Attribution { get; private set; } = "";
        public bool Muted => source != null && source.mute;
        public float Volume => volume;
        /// <summary>True while the previous record's narration fades out before the next one is offered.</summary>
        public bool Fading => fading != null;
        public bool CanPlay => source != null && narrationClip != null;
        /// <summary>Seconds into the narration as recorded, at either speed, so subtitles and the reading keep time.</summary>
        public float PlaybackTime => !CanPlay ? 0 : inBrowser ? browser.Time
            : fastClip != null && source.clip == fastClip ? source.time * MuseumNarrationSpeed.Fast : source.time;
        public float Duration => CanPlay ? narrationClip.length : 0;
        /// <summary>Speed chosen for the narrations: 1 or 1.5.</summary>
        public float Speed => speed;
        /// <summary>True while the narration is heard at the chosen speed; 1.5x needs a moment to be prepared first.</summary>
        public bool SpeedApplied => speed <= MuseumNarrationSpeed.Normal || inBrowser || fastClip != null && source.clip == fastClip;
        /// <summary>False where faster narration cannot keep the voice's pitch, such as a browser without it.</summary>
        public bool SpeedAvailable => !browserFailed && !stretchFailed && (!InBrowserBuild || BrowserNarration.Available);
        private static bool InBrowserBuild => Application.platform == RuntimePlatform.WebGLPlayer;
        public AudioSource ContextSource => source;
        public event Action Changed;
        public event Action SubtitleChanged;
        public bool PausedByDistance => pausedByDistance;
        /// <summary>Timed fragments of the narration, from its published WebVTT subtitles; empty when it has none.</summary>
        public System.Collections.Generic.IReadOnlyList<SubtitleCue> Cues => cues;
        /// <summary>Record whose narration is loaded or playing; pieces of one point may share it.</summary>
        public string NarratedSlug => narratedSlug;
        /// <summary>Counts every start of playback from the beginning, including Repeat, so readers can follow from the top.</summary>
        public int Starts { get; private set; }

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

        private string narratedSlug;

        /// <summary>
        /// The narration an element is heard with: its own, or, among the several pieces of one point, the narration of
        /// the point's common record, which keeps playing while the visitor moves between the pieces.
        /// </summary>
        public static ElementSummaryContractV1 NarrationFor(TourPoint point, ElementSummaryContractV1 element)
        {
            if (element == null || element.has_narration) return element;
            var elements = point != null && point.HasContent ? point.Content.elements : null;
            if (elements == null || !element.has_3d_model || elements.Count(candidate => candidate.has_3d_model) < 2) return element;
            return elements.FirstOrDefault(candidate => !candidate.has_3d_model && candidate.has_narration) ?? element;
        }

        private void Select(ElementSummaryContractV1 element)
        {
            var target = NarrationFor(interaction.ActivePoint, element);
            if (target != null && target.has_narration && target.slug == narratedSlug && State != NarrationState.Idle
                && State != NarrationState.Unavailable) return;
            // Another record of the same point, chosen with the arrows: what was heard fades out, and the new record's
            // narration is offered with its button instead of starting by itself. Arriving at a point starts it.
            bool step = element != null && interaction.ActivePoint != null && interaction.ActivePoint == narratedPoint;
            bool audible = step && CanPlay && (State == NarrationState.Playing || State == NarrationState.Paused);
            if (fading != null) { StopCoroutine(fading); fading = null; source.volume = volume; }
            if (audible && State == NarrationState.Playing) { fading = StartCoroutine(FadeThenLoad(target, step)); return; }
            Clear();
            Begin(target, step);
        }

        private void Begin(ElementSummaryContractV1 target, bool offer)
        {
            narratedPoint = interaction.ActivePoint;
            if (target == null || !target.has_narration) return;
            narratedSlug = target.slug;
            loading = StartCoroutine(Load(target.slug, !offer));
        }

        private IEnumerator FadeThenLoad(ElementSummaryContractV1 target, bool offer)
        {
            for (float elapsed = 0; elapsed < SwitchFadeSeconds && Sounding; elapsed += Time.unscaledDeltaTime)
            {
                source.volume = volume * (1 - Mathf.SmoothStep(0, 1, elapsed / SwitchFadeSeconds));
                yield return null;
            }
            fading = null;
            Clear();
            source.volume = volume;
            Begin(target, offer);
        }

        public void Retry() => Select(interaction.SelectedElement);
        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            if (fading == null) source.volume = volume;
            Changed?.Invoke();
        }
        public void ToggleMute() { source.mute = !source.mute; Changed?.Invoke(); }

        /// <summary>
        /// Hears the narrations at <paramref name="value"/> times their speed, 1 or 1.5, with the voice's pitch kept. A
        /// narration under way goes on from the same words; until its faster version is ready it is heard as it was.
        /// </summary>
        public void SetSpeed(float value)
        {
            if (!MuseumNarrationSpeed.IsSpeed(value) || Mathf.Approximately(value, speed)) return;
            speed = value;
            PrepareFast();
            ApplySpeed();
            Changed?.Invoke();
        }

        public void TogglePause()
        {
            if (!CanPlay || interaction.State == InteractionState.Paused) return;
            pausedByDistance = false;
            if (State == NarrationState.Playing) { PausePlayback(); SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused")); }
            else if (State == NarrationState.Ready) Repeat();
            else if (State == NarrationState.Paused) { ResumePlayback(); SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing")); }
            else Repeat();
        }
        public void Repeat()
        {
            if (!CanPlay || interaction.State == InteractionState.Paused) return;
            pausedByDistance = false;
            PlayFromStart();
            SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
            UpdateSubtitle();
        }
        private void OnInteractionChanged()
        {
            if (interaction.State == InteractionState.Paused && State == NarrationState.Playing)
            { pausedByVisit = true; PausePlayback(); SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused")); }
            else if (interaction.State != InteractionState.Paused && pausedByVisit && CanPlay)
            { pausedByVisit = false; ResumePlayback(); SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing")); }
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
            if (inBrowser) FollowBrowser();
            if (State == NarrationState.Playing && !Sounding)
                SetState(NarrationState.Finished, MuseumInterfaceText.Get("narration_finished"));
            UpdateSubtitle();
        }

        /// <summary>True while the narration is being heard, from Unity's source or from the browser's element.</summary>
        private bool Sounding => inBrowser ? browser.State == BrowserNarration.PlayState.Playing : source.isPlaying;

        /// <summary>The clip heard in Unity at the chosen speed, once its faster version is ready.</summary>
        private AudioClip SpeedClip => speed > MuseumNarrationSpeed.Normal && fastClip != null ? fastClip : narrationClip;

        private void PlayFromStart()
        {
            source.Stop();
            Starts++;
            if (UseBrowser()) { inBrowser = true; browser.Play(0, MuseumNarrationSpeed.Fast); return; }
            if (inBrowser) { browser.Pause(); inBrowser = false; }
            if (source.clip != SpeedClip) source.clip = SpeedClip;
            source.Play();
        }
        private void PausePlayback() { if (inBrowser) browser.Pause(); else source.Pause(); }
        private void ResumePlayback() { if (inBrowser) browser.Play(-1, MuseumNarrationSpeed.Fast); else source.UnPause(); }

        /// <summary>In a browser build, 1.5x plays through the page's audio element, opened once for this narration.</summary>
        private bool UseBrowser()
        {
            if (!InBrowserBuild || speed <= MuseumNarrationSpeed.Normal || browserFailed) return false;
            if (browserOpen) return true;
            if (!BrowserNarration.Available || string.IsNullOrEmpty(narrationUrl)) return false;
            browser.Open(narrationUrl);
            return browserOpen = true;
        }

        /// <summary>Starts the stretch of the narration for 1.5x, off the main thread, when it is wanted and not yet made.</summary>
        private void PrepareFast()
        {
            if (InBrowserBuild || speed <= MuseumNarrationSpeed.Normal || narrationClip == null || fastClip != null || stretching) return;
            var clip = narrationClip;
            int channels = clip.channels, frequency = clip.frequency;
            var samples = new float[clip.samples * channels];
            // A clip whose samples cannot be read cannot be stretched: the control then hides for this narration.
            if (!clip.GetData(samples, 0)) { stretchFailed = true; Changed?.Invoke(); return; }
            stretching = true;
            var work = System.Threading.Tasks.Task.Run(() => NarrationTimeStretch.Stretch(samples, channels, frequency, MuseumNarrationSpeed.Fast));
            StartCoroutine(AwaitFast(work, stretchGeneration, channels, frequency));
        }

        private IEnumerator AwaitFast(System.Threading.Tasks.Task<float[]> work, int generation, int channels, int frequency)
        {
            while (!work.IsCompleted) yield return null;
            if (generation != stretchGeneration) yield break;
            stretching = false;
            if (narrationClip == null) yield break;
            if (work.IsFaulted || work.IsCanceled) { stretchFailed = true; Changed?.Invoke(); yield break; }
            var data = work.Result;
            fastClip = AudioClip.Create(narrationClip.name + " 1.5x", data.Length / channels, channels, frequency, false);
            fastClip.SetData(data, 0);
            ApplySpeed();
            Changed?.Invoke();
        }

        /// <summary>Moves what is heard to the chosen speed, from the same point of the narration and in the same state.</summary>
        private void ApplySpeed()
        {
            if (!CanPlay) return;
            bool playing = State == NarrationState.Playing, paused = State == NarrationState.Paused;
            float at = PlaybackTime;
            if (InBrowserBuild)
            {
                bool wanted = speed > MuseumNarrationSpeed.Normal && !browserFailed;
                if (wanted == inBrowser) return;
                if (wanted)
                {
                    if (!UseBrowser()) return;
                    source.Pause();
                    inBrowser = true;
                    if (playing) browser.Play(at, MuseumNarrationSpeed.Fast); else browser.Seek(at);
                }
                else
                {
                    browser.Pause();
                    inBrowser = false;
                    Place(narrationClip, at, playing, paused);
                }
                return;
            }
            var target = SpeedClip;
            if (source.clip == target) return;
            source.clip = target;
            Place(target, at, playing, paused);
        }

        /// <summary>Puts Unity's source at <paramref name="at"/> seconds of the recording, playing or paused there.</summary>
        private void Place(AudioClip clip, float at, bool playing, bool paused)
        {
            if (source.clip != clip) source.clip = clip;
            if (!playing && !paused) return;
            float rate = clip == fastClip ? MuseumNarrationSpeed.Fast : MuseumNarrationSpeed.Normal;
            source.Play();
            source.time = Mathf.Clamp(at / rate, 0, Mathf.Max(0, clip.length - .05f));
            if (paused) source.Pause();
        }

        /// <summary>
        /// Keeps the browser's element as loud as the narration's 3D source would be from where the visitor stands, and
        /// goes back to Unity's source at the normal speed if the browser cannot play it.
        /// </summary>
        private void FollowBrowser()
        {
            if (browser.State == BrowserNarration.PlayState.Failed)
            {
                float at = browser.Time;
                bool playing = State == NarrationState.Playing, paused = State == NarrationState.Paused;
                browser.Close();
                browserOpen = inBrowser = false;
                browserFailed = true;
                Place(narrationClip, at, playing, paused);
                Changed?.Invoke();
                return;
            }
            if (listener == null || !listener.isActiveAndEnabled) listener = FindAnyObjectByType<AudioListener>();
            float distance = listener != null ? Vector3.Distance(listener.transform.position, source.transform.position) : 0;
            browser.Volume(source.volume * BrowserNarration.Rolloff(distance, source.minDistance, source.maxDistance), source.mute);
        }
        /// <summary>Walking away from the point pauses its narration; coming back resumes it where it stopped.</summary>
        private void OnPresenceChanged(bool away)
        {
            if (away && !pausedByDistance && State == NarrationState.Playing)
            {
                pausedByDistance = true;
                PausePlayback();
                SetState(NarrationState.Paused, MuseumInterfaceText.Get("narration_paused_away"));
            }
            else if (!away && pausedByDistance)
            {
                pausedByDistance = false;
                if (State != NarrationState.Paused) return;
                ResumePlayback();
                SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
            }
        }
        private void UpdateSubtitle()
        {
            // A narration offered but not started has nothing being said yet.
            string text = State == NarrationState.Ready ? "" : cues.Count == 0 ? Transcription
                : State == NarrationState.Finished ? "" : WebVttReader.At(cues, PlaybackTime);
            if (text == Subtitle) return;
            Subtitle = text;
            SubtitleChanged?.Invoke();
        }

        private IEnumerator Load(string slug, bool autoplay = true)
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
            Attribution = "";
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
            source.clip = narrationClip = clip;
            narrationUrl = url;
            PrepareFast();
            if (autoplay)
            {
                PlayFromStart();
                SetState(NarrationState.Playing, MuseumInterfaceText.Get("narration_playing"));
                OnInteractionChanged();
                if (presence.Away) OnPresenceChanged(true);
            }
            else SetState(NarrationState.Ready, MuseumInterfaceText.Get("narration_ready"));
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
            narratedSlug = null;
            narratedPoint = null;
            if (fading != null) { StopCoroutine(fading); fading = null; }
            if (source != null) source.volume = volume;
            if (loading != null) StopCoroutine(loading);
            if (subtitlesLoading != null) StopCoroutine(subtitlesLoading);
            loading = subtitlesLoading = null;
            DisposeRequest();
            DisposeSubtitlesRequest();
            if (decodingClip != null) Destroy(decodingClip);
            decodingClip = null;
            stretchGeneration++;
            stretching = stretchFailed = false;
            if (browserOpen) browser.Close();
            browserOpen = inBrowser = browserFailed = false;
            narrationUrl = null;
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }
            if (narrationClip != null) Destroy(narrationClip);
            if (fastClip != null) Destroy(fastClip);
            narrationClip = fastClip = null;
            cues = Array.Empty<SubtitleCue>();
            pausedByVisit = false;
            pausedByDistance = false;
            Transcription = Attribution = "";
            UpdateSubtitle();
            SetState(NarrationState.Idle, "");
        }
        private void SetState(NarrationState state, string status)
        {
            // A narration and the guide's voice are never heard together (UD-8): a narration that starts silences the guide.
            var guideVoice = state == NarrationState.Playing ? GetComponent<MuseumGuideVoice>() : null;
            if (guideVoice != null && guideVoice.Speaking) guideVoice.StopSpeech();
            State = state; Status = status; Changed?.Invoke();
        }
    }
}
