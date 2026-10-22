// The browser's microphone for spoken questions to the guides. Unity's Microphone class does not exist in a browser
// build, so the page records: getUserMedia, then mono samples resampled to 16 kHz and kept in memory only until Unity
// takes them as WAV PCM16 or drops them. Nothing is stored, sent or logged here; the stream is released after each take.
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

    release: function () {
      var voice = musiyoVoice;
      if (voice.processor) { try { voice.processor.disconnect(); } catch (e) {} voice.processor.onaudioprocess = null; }
      if (voice.source) { try { voice.source.disconnect(); } catch (e) {} }
      if (voice.stream) voice.stream.getTracks().forEach(function (track) { track.stop(); });
      if (voice.context) { try { voice.context.close(); } catch (e) {} }
      voice.processor = voice.source = voice.stream = voice.context = null;
    },

    encode: function (samples, inputRate) {
      // Linear resampling to 16 kHz is enough for speech recognition.
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
        var position = frame * ratio, index = Math.floor(position), fraction = position - index;
        var next = index + 1 < samples.length ? samples[index + 1] : samples[index];
        var value = Math.max(-1, Math.min(1, samples[index] + (next - samples[index]) * fraction));
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

  // Asks for the microphone; the page's own prompt answers asynchronously.
  MusiyoVoiceRequest: function () {
    var voice = musiyoVoice;
    if (voice.state === 1 || voice.state === 2 || voice.state === 3) return;
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) { voice.state = 5; return; }
    voice.state = 1;
    navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true } })
      .then(function (stream) {
        if (voice.state !== 1) { stream.getTracks().forEach(function (track) { track.stop(); }); return; }
        voice.stream = stream;
        voice.state = 2;
      })
      .catch(function (error) {
        voice.state = error && (error.name === 'NotAllowedError' || error.name === 'SecurityError') ? 4 : 5;
      });
  },

  // Starts a take of at most the given seconds; returns 1 when recording.
  MusiyoVoiceStart: function (maxSeconds) {
    var voice = musiyoVoice;
    if (voice.state !== 2 || !voice.stream) return 0;
    try {
      var Context = window.AudioContext || window.webkitAudioContext;
      voice.context = new Context();
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
      return 1;
    } catch (e) {
      voice.release();
      voice.state = 5;
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
    var total = 0;
    voice.chunks.forEach(function (chunk) { total += chunk.length; });
    var samples = new Float32Array(total), offset = 0;
    voice.chunks.forEach(function (chunk) { samples.set(chunk, offset); offset += chunk.length; });
    voice.chunks = [];
    voice.release();
    voice.state = 0;
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
