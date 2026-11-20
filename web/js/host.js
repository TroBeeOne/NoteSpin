// Bridge between the page and the NoteSpin app (WebView2 on Windows, WebKit2GTK on Linux).
// window.NS reaches the app's notebooks (SQLite), settings and native dialogs.
(() => {
  'use strict';

  // Results are JSON; a failure arrives as {"__error": message}
  const parse = r => {
    const v = r == null || r === '' ? null : JSON.parse(r);
    if (v && typeof v === 'object' && '__error' in v) throw new Error(v.__error);
    return v;
  };

  /* ---------- Windows: Edge WebView2 hostObjects ---------- */
  const wv = window.chrome && window.chrome.webview;
  if (wv && wv.hostObjects) {
    const api = wv.hostObjects.ns;        // async: each call returns a Promise
    const sync = wv.hostObjects.sync.ns;  // sync: for settings needed while the page starts up

    // Dialogs answer with a web message {id, path, error}; the app also sends {type, ...} messages
    const waiting = new Map();
    const listeners = new Map();
    let seq = 0;
    wv.addEventListener('message', e => {
      const m = e.data;
      if (!m) return;
      if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); return; }
      if (m.type && listeners.has(m.type)) listeners.get(m.type)(m);
    });
    const request = send => new Promise((resolve, reject) => {
      const id = 'q' + (++seq);
      waiting.set(id, resolve);
      try { send(id); } catch (e) { waiting.delete(id); reject(e); }
    });

    window.NS = {
      async call(name, ...args) { return parse(await api[name](...args)); },
      callSync(name, ...args) { return parse(sync[name](...args)); },

      // settings, stored as strings
      prefGet(key) { try { return this.callSync('PrefGet', key); } catch (e) { console.warn('prefGet', e); return null; } },
      prefSet(key, value) { try { this.callSync('PrefSet', key, String(value)); } catch (e) { console.warn('prefSet', e); } },

      // Windows dialogs: resolve to a path, or null when cancelled
      exportDialog() {
        return request(id => api.NbExportDialog(id)).then(m => { if (m.error) throw new Error(m.error); return m.path || null; });
      },
      importDialog() { return request(id => api.NbImportDialog(id)).then(m => m.path || null); },
      folderDialog() { return request(id => api.NbFolderDialog(id)).then(m => m.path || null); },

      // the real path of a File that was dropped on the page (null for files that don't come from disk)
      filePath(file) {
        if (!wv.postMessageWithAdditionalObjects || !(file instanceof File)) return Promise.resolve(null);
        return request(id => wv.postMessageWithAdditionalObjects({type: 'filePath', id}, [file])).then(m => m.path || null);
      },

      // closes the note in note window win (saving it); resolves once it's closed (or after 10 s, whatever happened)
      closeNoteWindow(win) {
        return Promise.race([request(id => api.NoteWinClose(win, id)), new Promise(r => setTimeout(r, 10000))]);
      },

      on(type, fn) { listeners.set(type, fn); },
    };
    return;
  }

  /* ---------- Linux: WebKit2GTK script message handler ---------- */
  const wk = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.ns;
  if (!wk) return;

  const prefs = Object.assign(Object.create(null), window.__nsInitPrefs || {});
  let noteWinState = Array.isArray(window.__nsNoteWinState) ? window.__nsNoteWinState : [];

  const pendingCalls = new Map();
  const waiting = new Map();
  const listeners = new Map();
  let seq = 0;

  window.__nsReceive = m => {
    if (!m) return;
    if (m.__callId && pendingCalls.has(m.__callId)) {
      const p = pendingCalls.get(m.__callId);
      pendingCalls.delete(m.__callId);
      try { p.resolve(parse(m.raw)); } catch (err) { p.reject(err); }
      return;
    }
    if (m.type === '__noteWinState') {
      noteWinState = Array.isArray(m.state) ? m.state : [];
      return;
    }
    if (m.id && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); return; }
    if (m.type && listeners.has(m.type)) listeners.get(m.type)(m);
  };

  const invoke = (method, args, expectReply = true) => {
    if (!expectReply) {
      wk.postMessage(JSON.stringify({method, args}));
      return;
    }
    return new Promise((resolve, reject) => {
      const callId = 'c' + (++seq);
      pendingCalls.set(callId, {resolve, reject});
      try { wk.postMessage(JSON.stringify({callId, method, args})); }
      catch (e) { pendingCalls.delete(callId); reject(e); }
    });
  };

  const request = send => new Promise((resolve, reject) => {
    const id = 'q' + (++seq);
    waiting.set(id, resolve);
    try { send(id); } catch (e) { waiting.delete(id); reject(e); }
  });

  window.NS = {
    call(name, ...args) { return invoke(name, args, true); },
    callSync(name, ...args) {
      if (name === 'NoteWinState') return noteWinState;
      if (name === 'PrefGet') return this.prefGet(args[0]);
      if (name === 'PrefSet') { this.prefSet(args[0], args[1]); return true; }
      return null;
    },

    prefGet(key) { return Object.prototype.hasOwnProperty.call(prefs, key) ? prefs[key] : null; },
    prefSet(key, value) {
      prefs[key] = String(value);
      invoke('PrefSet', [key, String(value)], true).catch(e => console.warn('prefSet', e));
    },

    exportDialog() {
      return request(id => invoke('NbExportDialog', [id], false)).then(m => { if (m.error) throw new Error(m.error); return m.path || null; });
    },
    importDialog() { return request(id => invoke('NbImportDialog', [id], false)).then(m => m.path || null); },
    folderDialog() { return request(id => invoke('NbFolderDialog', [id], false)).then(m => m.path || null); },

    filePath(file) {
      if (!(file instanceof File)) return Promise.resolve(null);
      return invoke('DroppedFilePath', [file.name], true).catch(() => null);
    },

    closeNoteWindow(win) {
      return Promise.race([request(id => invoke('NoteWinClose', [win, id], false)), new Promise(r => setTimeout(r, 10000))]);
    },

    on(type, fn) { listeners.set(type, fn); },
  };
})();
