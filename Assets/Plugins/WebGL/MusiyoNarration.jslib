// A record's narration heard faster in the browser. Unity's audio in a browser build plays a clip through Web Audio,
// whose playback rate raises the voice with the speed; the page's own audio element keeps the voice's pitch while it
// plays faster (preservesPitch, on by default in current browsers and set here anyway). The element plays the same
// temporary address the narration was downloaded from; nothing is stored. Unity keeps its clip and plays it again at the
// normal speed, so this element is only used at 1.5x.
//
// State: 0 none, 1 playing, 2 paused, 3 ended, 4 failed (it could not load, or the browser refused to play it).
var MusiyoNarration = {
  $musiyoNarration: {
    audio: null,
    state: 0,
    // Where to start once the element knows its length; a browser may ignore a seek made before that.
    pendingSeek: -1,

    preserve: function (audio) {
      if ('preservesPitch' in audio) audio.preservesPitch = true;
      if ('mozPreservesPitch' in audio) audio.mozPreservesPitch = true;
      if ('webkitPreservesPitch' in audio) audio.webkitPreservesPitch = true;
    },

    seek: function (seconds) {
      var narration = musiyoNarration, audio = narration.audio;
      if (!audio) return;
      if (audio.readyState >= 1) { try { audio.currentTime = seconds; } catch (e) {} narration.pendingSeek = -1; }
      else narration.pendingSeek = seconds;
    },

    close: function () {
      var narration = musiyoNarration, audio = narration.audio;
      if (audio) {
        audio.onended = audio.onerror = audio.onloadedmetadata = null;
        try { audio.pause(); } catch (e) {}
        audio.removeAttribute('src');
        try { audio.load(); } catch (e) {}
      }
      narration.audio = null;
      narration.state = 0;
      narration.pendingSeek = -1;
    }
  },

  // 1 when this browser can keep the voice's pitch at another speed.
  MusiyoNarrationAvailable: function () {
    if (typeof Audio === 'undefined') return 0;
    var probe = new Audio();
    return ('preservesPitch' in probe || 'mozPreservesPitch' in probe || 'webkitPreservesPitch' in probe) ? 1 : 0;
  },

  // Prepares an element for the narration at this address, paused at its start.
  MusiyoNarrationOpen: function (urlPointer) {
    var narration = musiyoNarration;
    narration.close();
    var audio = new Audio();
    audio.preload = 'auto';
    narration.preserve(audio);
    audio.onloadedmetadata = function () {
      if (narration.audio !== audio || narration.pendingSeek < 0) return;
      try { audio.currentTime = narration.pendingSeek; } catch (e) {}
      narration.pendingSeek = -1;
    };
    audio.onended = function () { if (narration.audio === audio) narration.state = 3; };
    audio.onerror = function () {
      if (narration.audio !== audio) return;
      narration.state = 4;
      console.log('[MusiyoNarration] the narration could not be played faster: ' + (audio.error ? audio.error.code : 'error'));
    };
    audio.src = UTF8ToString(urlPointer);
    narration.audio = audio;
    narration.state = 2;
  },

  // Plays from the given second at the given rate.
  MusiyoNarrationPlay: function (seconds, rate) {
    var narration = musiyoNarration, audio = narration.audio;
    if (!audio || narration.state === 4) return;
    narration.preserve(audio);
    audio.defaultPlaybackRate = rate;
    audio.playbackRate = rate;
    if (seconds >= 0) narration.seek(seconds);
    narration.state = 1;
    var started = audio.play();
    if (started && started.catch) started.catch(function (error) {
      if (narration.audio !== audio) return;
      narration.state = 4;
      console.log('[MusiyoNarration] the browser did not play the narration faster: ' + (error && error.name));
    });
  },

  // Pauses where it is; a later play with a negative second goes on from there.
  MusiyoNarrationPause: function () {
    var narration = musiyoNarration;
    if (!narration.audio) return;
    try { narration.audio.pause(); } catch (e) {}
    if (narration.state === 1) narration.state = 2;
  },

  // Moves to the given second without playing.
  MusiyoNarrationSeek: function (seconds) {
    musiyoNarration.seek(seconds);
  },

  MusiyoNarrationTime: function () {
    var narration = musiyoNarration;
    if (!narration.audio) return 0;
    return narration.pendingSeek >= 0 ? narration.pendingSeek : narration.audio.currentTime;
  },

  MusiyoNarrationState: function () {
    return musiyoNarration.state;
  },

  MusiyoNarrationVolume: function (volume, muted) {
    var audio = musiyoNarration.audio;
    if (!audio) return;
    audio.volume = Math.max(0, Math.min(1, volume));
    audio.muted = muted !== 0;
  },

  MusiyoNarrationClose: function () {
    musiyoNarration.close();
  }
};

autoAddDeps(MusiyoNarration, '$musiyoNarration');
mergeInto(LibraryManager.library, MusiyoNarration);
