// Drives a running NoteSpin through the Chrome DevTools Protocol, for testing.
// Start NoteSpin with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333 (see run-test.ps1).
// Needs Node 22 or later (built-in WebSocket and fetch).

import {writeFileSync} from 'node:fs';

export async function connect(port = 9333, timeoutMs = 20000) {
  const t0 = Date.now();
  let target;
  while (!target) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
      target = list.find(t => t.type === 'page' && t.url.startsWith('https://notespin.local/'));
    } catch { /* not up yet */ }
    if (!target) {
      if (Date.now() - t0 > timeoutMs) throw new Error('NoteSpin page not found on port ' + port);
      await new Promise(r => setTimeout(r, 300));
    }
  }
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  let id = 0;
  const pending = new Map();
  const logs = [];
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const {resolve, reject} = pending.get(m.id);
      pending.delete(m.id);
      m.error ? reject(new Error(m.error.message)) : resolve(m.result);
    } else if (m.method === 'Runtime.consoleAPICalled') {
      logs.push(`[console.${m.params.type}] ` + m.params.args.map(a => a.value ?? a.description ?? '').join(' '));
    } else if (m.method === 'Runtime.exceptionThrown') {
      const d = m.params.exceptionDetails;
      logs.push('[exception] ' + (d.exception?.description || d.text) + ` @${d.url || ''}:${d.lineNumber}`);
    } else if (m.method === 'Log.entryAdded') {
      logs.push(`[log.${m.params.entry.level}] ${m.params.entry.text} ${m.params.entry.url || ''}`);
    }
  };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const i = ++id;
    pending.set(i, {resolve, reject});
    ws.send(JSON.stringify({id: i, method, params}));
  });
  await send('Runtime.enable');
  await send('Log.enable');
  await send('Page.enable');

  const api = {
    send, logs,
    async eval(expr) {
      const r = await send('Runtime.evaluate', {expression: expr, awaitPromise: true, returnByValue: true});
      if (r.exceptionDetails) throw new Error('eval: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
      return r.result.value;
    },
    async waitFor(expr, timeoutMs = 10000) {
      const t = Date.now();
      for (;;) {
        try { if (await api.eval(expr)) return; } catch { /* page loading */ }
        if (Date.now() - t > timeoutMs) throw new Error('timed out waiting for ' + expr);
        await sleep(150);
      }
    },
    async shot(path) {
      const r = await send('Page.captureScreenshot', {format: 'png'});
      writeFileSync(path, Buffer.from(r.data, 'base64'));
      return path;
    },
    async click(x, y) {
      for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased'])
        await send('Input.dispatchMouseEvent', {type, x, y, button: 'left', clickCount: 1});
    },
    async drag(x0, y0, x1, y1, steps = 12, ms = 160) {
      await send('Input.dispatchMouseEvent', {type: 'mouseMoved', x: x0, y: y0});
      await send('Input.dispatchMouseEvent', {type: 'mousePressed', x: x0, y: y0, button: 'left', clickCount: 1});
      for (let i = 1; i <= steps; i++) {
        await send('Input.dispatchMouseEvent', {type: 'mouseMoved', x: x0 + (x1 - x0) * i / steps, y: y0 + (y1 - y0) * i / steps, button: 'left', buttons: 1});
        await sleep(ms / steps);
      }
      await send('Input.dispatchMouseEvent', {type: 'mouseReleased', x: x1, y: y1, button: 'left', clickCount: 1});
    },
    async wheel(x, y, deltaY) { await send('Input.dispatchMouseEvent', {type: 'mouseWheel', x, y, deltaX: 0, deltaY}); },
    async key(key, {code, ctrl = false, shift = false, text} = {}) {
      const modifiers = (ctrl ? 2 : 0) | (shift ? 8 : 0);
      const vk = {Enter: 13, Escape: 27, Tab: 9, ArrowDown: 40, ArrowUp: 38, ArrowLeft: 37, ArrowRight: 39, Backspace: 8, ' ': 32}[key] ?? key.toUpperCase().charCodeAt(0);
      await send('Input.dispatchKeyEvent', {type: 'rawKeyDown', key, code: code || key, windowsVirtualKeyCode: vk, modifiers});
      if (text) await send('Input.dispatchKeyEvent', {type: 'char', key, text, modifiers});
      await send('Input.dispatchKeyEvent', {type: 'keyUp', key, code: code || key, windowsVirtualKeyCode: vk, modifiers});
    },
    async type(text) { await send('Input.insertText', {text}); },
    close() { ws.close(); },
  };
  return api;
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

/** Presses a key the way a keyboard does (keydown and the typed character together, so a page can cancel it). */
export async function press(c, key, {text, ctrl = false, shift = false, code} = {}) {
  const modifiers = (ctrl ? 2 : 0) | (shift ? 8 : 0);
  const vk = {Enter: 13, Escape: 27, Tab: 9, ArrowDown: 40, ArrowUp: 38, ArrowLeft: 37, ArrowRight: 39, Backspace: 8, ' ': 32, End: 35, Home: 36}[key] ?? key.toUpperCase().charCodeAt(0);
  const t = text ?? (key === 'Enter' ? '\r' : key.length === 1 ? key : undefined);
  await c.send('Input.dispatchKeyEvent', {type: 'keyDown', key, code: code || key, windowsVirtualKeyCode: vk, modifiers, ...(t !== undefined ? {text: t, unmodifiedText: t} : {})});
  await c.send('Input.dispatchKeyEvent', {type: 'keyUp', key, code: code || key, windowsVirtualKeyCode: vk, modifiers});
}
