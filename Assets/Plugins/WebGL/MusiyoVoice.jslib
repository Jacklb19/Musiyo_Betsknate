// The browser's microphone for spoken questions to the guides. Unity's Microphone class does not exist in a browser
// build, so the page records: getUserMedia, then mono samples resampled to 16 kHz and kept in memory only until Unity
// takes them as WAV PCM16 or drops them. Nothing is stored or sent here, and only the level of a take is logged (its
// peak and loudness, never its sound); the stream is released after each take.
//
// Unity calls these from its frame loop, never from the visitor's click or key itself, and a browser keeps an audio
// context started outside a gesture suspended until it is allowed to play: a suspended context records nothing, or
// silence, which speech recognition turns into a stock phrase (the owner heard «Gracias por ver el video», 25/10/2026).
// So the context is made together with the stream, as the web page's own recorder does, the take waits until it really
// runs, and the next click or key on the page resumes it if the browser had held it back.
var MusiyoVoice = {
  $musiyoVoice: {
    // 0 idle, 1 waiting for permission, 2 ready, 3 recording, 4 denied, 5 unavailable
    state: 0,
    stream: null,
    context: null,
    source: null,
    processor: null,
    chunks: [],
    rate: 16000,
    limit: 0,
    taken: null,
    listening: false,

    // The page's next gesture lets a held-back context run; the listener stays for later takes.
    resumeOnGesture: function () {
      var voice = musiyoVoice;
      if (voice.listening) return;
      voice.listening = true;
      var resume = function () {
        if (voice.context && voice.context.state === 'suspended' && voice.context.resume) voice.context.resume();
      };
      ['pointerdown', 'keydown', 'touchstart'].forEach(function (name) { document.addEventListener(name, resume, true); });
    },

    describe: function () {
      var voice = musiyoVoice;
      var track = voice.stream ? voice.stream.getAudioTracks()[0] : null;
      return 'context ' + (voice.context ? voice.context.state + ' at ' + voice.context.sampleRate + ' Hz' : 'none')
        + ', microphone ' + (track ? track.readyState + (track.muted ? ' (muted)' : '') + (track.enabled ? '' : ' (disabled)') : 'none');
    },

    release: function () {
      var voice = musiyoVoice;
      if (voice.processor) { try { voice.processor.disconnect(); } catch (e) {} voice.processor.onaudioprocess = null; }
      if (voice.source) { try { voice.source.disconnect(); } catch (e) {} }
      if (voice.stream) voice.stream.getTracks().forEach(function (track) { track.stop(); });
      if (voice.context) { try { voice.context.close(); } catch (e) {} }
      voice.processor = voice.source = voice.stream = voice.context = null;
    },

    encode: function (samples, inputRate) {
      // Each 16 kHz sample is the average of its window of input samples: a simple low-pass before decimation.
      var ratio = inputRate / musiyoVoice.rate;
      var frames = Math.floor(samples.length / ratio);
      var buffer = new ArrayBuffer(44 + frames * 2);
      var view = new DataView(buffer);
      function text(offset, value) { for (var i = 0; i < value.length; i++) view.setUint8(offset + i, value.charCodeAt(i)); }
      text(0, 'RIFF'); view.setUint32(4, 36 + frames * 2, true); text(8, 'WAVE');
      text(12, 'fmt '); view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
      view.setUint32(24, musiyoVoice.rate, true); view.setUint32(28, musiyoVoice.rate * 2, true);
      view.setUint16(32, 2, true); view.setUint16(34, 16, true);
      text(36, 'data'); view.setUint32(40, frames * 2, true);
      for (var frame = 0; frame < frames; frame++) {
        var start = Math.floor(frame * ratio), end = Math.max(start + 1, Math.min(samples.length, Math.floor((frame + 1) * ratio)));
        var sum = 0;
        for (var j = start; j < end; j++) sum += samples[j];
        var value = Math.max(-1, Math.min(1, sum / (end - start)));
        view.setInt16(44 + frame * 2, value < 0 ? value * 0x8000 : value * 0x7FFF, true);
      }
      return new Uint8Array(buffer);
    }
  },

  MusiyoVoiceAvailable: function () {
    return (navigator.mediaDevices && navigator.mediaDevices.getUserMedia && (window.AudioContext || window.webkitAudioContext)) ? 1 : 0;
  },

  MusiyoVoiceState: function () {
    return musiyoVoice.state;
  },

  // Asks for the microphone and makes the audio context with it; ready once the context runs.
  MusiyoVoiceRequest: function () {
    var voice = musiyoVoice;
    if (voice.state === 1 || voice.state === 2 || voice.state === 3) return;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { voice.state = 5; return; }
    voice.state = 1;
    voice.resumeOnGesture();
    navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } })
      .then(function (stream) {
        if (voice.state !== 1) { stream.getTracks().forEach(function (track) { track.stop(); }); return null; }
        voice.stream = stream;
        var Context = window.AudioContext || window.webkitAudioContext;
        voice.context = new Context();
        if (voice.context.state === 'running' || !voice.context.resume) return null;
        // Waits a moment for the browser to let it run; past that the take starts and the next gesture resumes it.
        return Promise.race([voice.context.resume(), new Promise(function (done) { setTimeout(done, 1500); })]);
      })
      .then(function () {
        if (voice.state !== 1 || !voice.stream) return;
        voice.state = 2;
        console.log('[MusiyoVoice] microphone ready: ' + voice.describe());
      })
      .catch(function (error) {
        voice.release();
        voice.state = error && (error.name === 'NotAllowedError' || error.name === 'SecurityError') ? 4 : 5;
        console.log('[MusiyoVoice] microphone unavailable: ' + (error && error.name));
      });
  },

  // Starts a take of at most the given seconds; returns 1 when recording.
  MusiyoVoiceStart: function (maxSeconds) {
    var voice = musiyoVoice;
    if (voice.state !== 2 || !voice.stream || !voice.context) return 0;
    try {
      if (voice.context.state === 'suspended' && voice.context.resume) voice.context.resume();
      voice.source = voice.context.createMediaStreamSource(voice.stream);
      voice.processor = voice.context.createScriptProcessor(4096, 1, 1);
      voice.chunks = [];
      voice.limit = Math.max(1, Math.min(30, maxSeconds)) * voice.context.sampleRate;
      var gathered = 0;
      voice.processor.onaudioprocess = function (event) {
        if (voice.state !== 3) return;
        var input = event.inputBuffer.getChannelData(0);
        var room = voice.limit - gathered;
        if (room <= 0) return;
        var copy = new Float32Array(input.subarray(0, Math.min(room, input.length)));
        voice.chunks.push(copy);
        gathered += copy.length;
        if (gathered >= voice.limit) voice.state = 2;
      };
      voice.source.connect(voice.processor);
      voice.processor.connect(voice.context.destination);
      voice.state = 3;
      console.log('[MusiyoVoice] recording: ' + voice.describe());
      return 1;
    } catch (e) {
      voice.release();
      voice.state = 5;
      console.log('[MusiyoVoice] could not record: ' + (e && e.name));
      return 0;
    }
  },

  MusiyoVoiceRecording: function () {
    return musiyoVoice.state === 3 ? 1 : 0;
  },

  // Ends the take and prepares the WAV; returns its size in bytes, 0 when nothing was recorded.
  MusiyoVoiceStop: function () {
    var voice = musiyoVoice;
    var rate = voice.context ? voice.context.sampleRate : 48000;
    var described = voice.describe();
    var total = 0;
    voice.chunks.forEach(function (chunk) { total += chunk.length; });
    var samples = new Float32Array(total), offset = 0;
    voice.chunks.forEach(function (chunk) { samples.set(chunk, offset); offset += chunk.length; });
    voice.chunks = [];
    voice.release();
    voice.state = 0;
    var peak = 0, energy = 0;
    for (var i = 0; i < total; i++) { var sample = Math.abs(samples[i]); if (sample > peak) peak = sample; energy += sample * sample; }
    console.log('[MusiyoVoice] take ' + (total / rate).toFixed(2) + ' s, peak ' + peak.toFixed(4) + ', rms '
      + (total > 0 ? Math.sqrt(energy / total) : 0).toFixed(4) + '; ' + described);
    voice.taken = total > rate * 0.1 ? voice.encode(samples, rate) : null;
    samples.fill(0);
    return voice.taken ? voice.taken.length : 0;
  },

  // Copies the prepared WAV into Unity's buffer and forgets it.
  MusiyoVoiceTake: function (pointer, length) {
    var voice = musiyoVoice;
    if (!voice.taken || length < voice.taken.length) return 0;
    HEAPU8.set(voice.taken, pointer);
    var size = voice.taken.length;
    voice.taken.fill(0);
    voice.taken = null;
    return size;
  },

  MusiyoVoiceDiscard: function () {
    var voice = musiyoVoice;
    voice.chunks = [];
    if (voice.taken) voice.taken.fill(0);
    voice.taken = null;
    voice.release();
    if (voice.state !== 4 && voice.state !== 5) voice.state = 0;
  }
};

autoAddDeps(MusiyoVoice, '$musiyoVoice');
mergeInto(LibraryManager.library, MusiyoVoice);
