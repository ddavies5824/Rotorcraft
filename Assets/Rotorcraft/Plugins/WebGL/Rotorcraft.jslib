// Browser helpers for the web build: keep saves in IndexedDB, and Google sign-in with Google Identity Services.
mergeInto(LibraryManager.library, {
  // The aircraft's sound. Web builds never call OnAudioFilterRead, so the game renders blocks of sound itself and queues them here.
  // Browsers keep audio asleep until the page is touched or a key is pressed.
  RcAudioStart: function () {
    if (window['rcAudio']) return window['rcAudio']['ctx'].sampleRate;
    var Ctx = window.AudioContext || window.webkitAudioContext;
    if (!Ctx) return 0;
    var ctx = new Ctx();
    var out = ctx.createGain();
    out.connect(ctx.destination);
    var meter = ctx.createAnalyser();
    meter.fftSize = 2048;
    out.connect(meter);
    window['rcAudio'] = { 'ctx': ctx, 'out': out, 'meter': meter, 'next': 0 };
    var wake = function () { if (ctx.state !== 'running') { try { ctx.resume(); } catch (e) { } } };
    ['pointerdown', 'pointerup', 'touchend', 'mousedown', 'keydown'].forEach(function (e) { document.addEventListener(e, wake, true); });
    wake();
    return ctx.sampleRate;
  },

  // Seconds of sound still to play; a full second while audio is asleep, so that nothing piles up.
  RcAudioQueued: function () {
    var a = window['rcAudio'];
    if (!a || a['ctx'].state !== 'running') return 1;
    return Math.max(0, a['next'] - a['ctx'].currentTime);
  },

  RcAudioPush: function (samples, count) {
    var a = window['rcAudio'];
    if (!a || a['ctx'].state !== 'running') return;
    var ctx = a['ctx'];
    var buffer = ctx.createBuffer(1, count, ctx.sampleRate);
    buffer.getChannelData(0).set(HEAPF32.subarray(samples >> 2, (samples >> 2) + count));
    var source = ctx.createBufferSource();
    source.buffer = buffer;
    source.connect(a['out']);
    var at = Math.max(a['next'], ctx.currentTime + 0.02);
    source.start(at);
    a['next'] = at + count / ctx.sampleRate;
  },

  RcSyncFs: function () {
    try { FS.syncfs(false, function (err) { if (err) console.warn('Rotorcraft: saving to browser storage failed', err); }); } catch (e) { }
  },

  RcGoogleToken: function (clientIdPtr, scopesPtr, objPtr) {
    var clientId = UTF8ToString(clientIdPtr), scopes = UTF8ToString(scopesPtr), obj = UTF8ToString(objPtr);
    var send = function (o) { SendMessage(obj, 'OnWebToken', JSON.stringify(o)); };
    var ask = function () {
      try {
        var client = google.accounts.oauth2.initTokenClient({
          client_id: clientId, scope: scopes, prompt: '',
          callback: function (r) { send(r); },
          error_callback: function (e) { send({ error: (e && e.type) || 'popup_failed' }); }
        });
        client.requestAccessToken();
      } catch (e) { send({ error: String(e) }); }
    };
    // Browsers only allow the popup from a real click, and the game's button press has already finished by the
    // time this runs: open it on the next click or key press in the page.
    var arm = function () {
      send({ pending: true });
      var once = function () { document.removeEventListener('pointerup', once, true); document.removeEventListener('keyup', once, true); ask(); };
      document.addEventListener('pointerup', once, true);
      document.addEventListener('keyup', once, true);
    };
    if (window.google && google.accounts && google.accounts.oauth2) { arm(); return; }
    var s = document.createElement('script');
    s.src = 'https://accounts.google.com/gsi/client'; s.async = true;
    s.onload = arm;
    s.onerror = function () { send({ error: 'Could not load Google sign-in. Check your connection or ad blocker.' }); };
    document.head.appendChild(s);
  }
});
