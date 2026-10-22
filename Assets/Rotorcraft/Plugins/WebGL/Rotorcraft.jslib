// Browser helpers for the web build: keep saves in IndexedDB, and Google sign-in with Google Identity Services.
mergeInto(LibraryManager.library, {
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
