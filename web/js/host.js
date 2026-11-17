// Bridge between the page and the NoteSpin app (WebView2 host).
// window.NS reaches the app's notebooks (SQLite), settings and Windows dialogs.
(() => {
  'use strict';
  const wv = window.chrome && window.chrome.webview;
  if (!wv || !wv.hostObjects) return;
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

  // Results are JSON; a failure arrives as {"__error": message}
  const parse = r => {
    const v = r == null || r === '' ? null : JSON.parse(r);
    if (v && typeof v === 'object' && '__error' in v) throw new Error(v.__error);
    return v;
  };

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

    on(type, fn) { listeners.set(type, fn); },
  };
})();
