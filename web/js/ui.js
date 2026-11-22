// Small UI pieces: dialogs, toasts and pop-up menus.

export const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'}[c]));

/**
 * The letter of a shortcut key (Ctrl+B → 'b'), lower case, or ''. With a Hebrew, Arabic, Greek… keyboard layout
 * the key gives that script's letter, so it's the key's place on a US keyboard instead (as the browser's own
 * shortcuts do).
 */
export function letterKey(e) {
  if (/^[a-z]$/i.test(e.key)) return e.key.toLowerCase();
  return /^Key[A-Z]$/.test(e.code || '') ? e.code.slice(3).toLowerCase() : '';
}

let openDialogs = 0;
export const dialogOpen = () => openDialogs > 0;
const cancellers = new Set();

/** Closes every open dialog as if Esc was pressed (the note window, closing while one is up). */
export function cancelDialogs() {
  for (const c of [...cancellers]) c();
}

/**
 * A modal dialog. Resolves with the value of the button pressed (cancelValue for Esc).
 * submit(value, box) may check the dialog first: return false to keep it open, or a value to resolve with.
 */
export function dialog({title, html = '', node = null, buttons = [{label: 'OK', value: true, primary: true}], cancelValue = null, init, submit, wide = false}) {
  return new Promise(resolve => {
    const before = document.activeElement;
    const back = document.createElement('div');
    back.className = 'dlg-back';
    const box = document.createElement('div');
    box.className = 'dlg' + (wide ? ' dlg-wide' : '');
    box.setAttribute('role', 'dialog');
    box.setAttribute('aria-modal', 'true');
    box.innerHTML = `<h2>${esc(title)}</h2><div class="dlg-body">${html}</div><div class="dlg-buttons"></div>`;
    if (node) box.querySelector('.dlg-body').append(node);
    const row = box.querySelector('.dlg-buttons');
    let busy = false, done = false;
    const finish = async v => {
      if (busy || done) return;
      if (submit && v !== cancelValue) {
        busy = true;
        let r;
        try { r = await submit(v, box); } finally { busy = false; }
        if (r === false || done) return;
        if (r !== undefined) v = r;
      }
      done = true;
      cancellers.delete(cancel);
      back.remove();
      openDialogs--;
      document.removeEventListener('keydown', onKey, true);
      if (before && before.focus && document.contains(before)) before.focus();
      resolve(v);
    };
    for (const b of buttons) {
      const el = document.createElement('button');
      el.type = 'button';
      el.className = 'btn' + (b.primary ? ' primary' : '') + (b.danger ? ' danger' : '') + (b.left ? ' left' : '');
      el.textContent = b.label;
      el.addEventListener('click', () => finish(b.value));
      row.append(el);
    }
    const onKey = e => {
      if (!document.contains(box) || back.nextElementSibling) return;   // only the top dialog
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); finish(cancelValue); return; }
      if (e.key === 'Enter' && !e.shiftKey && e.target.tagName !== 'TEXTAREA' && e.target.tagName !== 'BUTTON') {
        const p = buttons.find(b => b.primary);
        if (p) { e.preventDefault(); e.stopPropagation(); finish(p.value); }
        return;
      }
      if (e.key === 'Tab') {
        const f = [...box.querySelectorAll('button, input, select, textarea, [tabindex]:not([tabindex="-1"])')].filter(x => !x.disabled && x.offsetParent);
        if (!f.length) return;
        const i = f.indexOf(document.activeElement);
        if (e.shiftKey && i <= 0) { e.preventDefault(); f[f.length - 1].focus(); }
        else if (!e.shiftKey && i === f.length - 1) { e.preventDefault(); f[0].focus(); }
      }
    };
    const cancel = () => { busy = false; finish(cancelValue); };
    cancellers.add(cancel);
    document.addEventListener('keydown', onKey, true);
    back.append(box);
    document.getElementById('dialogs').append(back);
    openDialogs++;
    init?.(box, finish);
    const first = box.querySelector('input, textarea, select') || row.querySelector('.primary') || row.querySelector('button');
    first?.focus();
    if (first?.select) first.select();
  });
}

export function confirmDlg({title, message, html, ok = 'OK', danger = false}) {
  return dialog({
    title, html: html ?? `<p>${esc(message)}</p>`, cancelValue: false,
    buttons: [{label: 'Cancel', value: false}, {label: ok, value: true, primary: true, danger}],
  });
}

export function alertDlg(title, message) {
  return dialog({title, html: `<p>${esc(message)}</p>`, buttons: [{label: 'OK', value: true, primary: true}], cancelValue: true});
}

/** Asks for one line of text; resolves with it trimmed, or null. */
export function promptDlg({title, label = '', value = '', placeholder = '', ok = 'OK', hint = ''}) {
  return dialog({
    title,
    html: `<label class="field"><span>${esc(label)}</span><input type="text" maxlength="80" value="${esc(value)}" placeholder="${esc(placeholder)}" spellcheck="false"></label>${hint ? `<p class="hint">${esc(hint)}</p>` : ''}`,
    buttons: [{label: 'Cancel', value: null}, {label: ok, value: 'ok', primary: true}],
    submit: (v, box) => {
      const t = box.querySelector('input').value.trim();
      if (!t) { box.querySelector('input').focus(); return false; }
      return t;
    },
  });
}

/* ---------- toasts ---------- */

export function toast(message, {action = null, timeout = 4200, kind = ''} = {}) {
  const host = document.getElementById('toasts');
  const el = document.createElement('div');
  el.className = 'toast ' + kind;
  el.innerHTML = `<span>${esc(message)}</span>`;
  let timer;
  const close = () => { clearTimeout(timer); el.classList.add('out'); setTimeout(() => el.remove(), 200); };
  if (action) {
    const b = document.createElement('button');
    b.textContent = action.label;
    b.addEventListener('click', () => { close(); action.fn(); });
    el.append(b);
  }
  const x = document.createElement('button');
  x.className = 'x';
  x.setAttribute('aria-label', 'Dismiss');
  x.textContent = '×';
  x.addEventListener('click', close);
  el.append(x);
  host.append(el);
  timer = setTimeout(close, timeout);
  el.addEventListener('mouseenter', () => clearTimeout(timer));
  el.addEventListener('mouseleave', () => { timer = setTimeout(close, 2000); });
  return close;
}

/* ---------- pop-up menus ---------- */

let closeMenu = null;

/**
 * items: {label, hint, checked, disabled, danger, onClick} or '-' for a divider.
 * The menu drops down from anchor, or (at: {x, y}) opens at that point, inside bounds (a rectangle; else the
 * window); anchor then only gets the focus back when it closes.
 */
export function menu(anchor, items, {align = 'left', at = null, bounds = null} = {}) {
  closeMenu?.();
  const el = document.createElement('div');
  el.className = 'menu';
  el.setAttribute('role', 'menu');
  for (const it of items) {
    if (it === '-') { el.append(Object.assign(document.createElement('div'), {className: 'menu-sep'})); continue; }
    if (it.heading) { el.append(Object.assign(document.createElement('div'), {className: 'menu-head', textContent: it.heading})); continue; }
    const b = document.createElement('button');
    b.type = 'button';
    b.setAttribute('role', 'menuitem');
    b.className = 'menu-item' + (it.checked ? ' checked' : '') + (it.danger ? ' danger' : '');
    b.disabled = !!it.disabled;
    b.innerHTML = `<span class="tick">${it.checked ? '✓' : ''}</span><span class="lbl">${esc(it.label)}</span>${it.hint ? `<span class="mhint">${esc(it.hint)}</span>` : ''}`;
    b.addEventListener('click', () => { close(); it.onClick?.(); });
    el.append(b);
  }
  document.body.append(el);
  const mw = el.offsetWidth, mh = el.offsetHeight;
  let x, y;
  if (at) {
    const b = bounds || {left: 0, top: 0, right: innerWidth, bottom: innerHeight};
    // 8 pixels from the edges, less where it's tight (the hidden-frame window's box can be just wider than a menu)
    const px = Math.min(8, Math.max(0, (b.right - b.left - mw) / 2)), py = Math.min(8, Math.max(0, (b.bottom - b.top - mh) / 2));
    x = at.x + 2;
    y = at.y + 2;
    if (x + mw > b.right - px) x = at.x - mw - 2;     // no room to the right: to the left of the point
    if (y + mh > b.bottom - py) y = b.bottom - py - mh;
    x = Math.max(b.left + px, Math.min(x, b.right - mw - px));
    y = Math.max(b.top + py, y);
  } else {
    const r = anchor.getBoundingClientRect();
    x = align === 'right' ? r.right - mw : r.left;
    y = r.bottom + 6;
    if (y + mh > innerHeight - 8) y = Math.max(8, r.top - mh - 6);
    x = Math.max(8, Math.min(x, innerWidth - mw - 8));
  }
  el.style.left = x + 'px';
  el.style.top = y + 'px';
  const items$ = [...el.querySelectorAll('.menu-item:not(:disabled)')];
  items$[0]?.focus();

  // a press outside closes it (one on the anchor too, when it opened at a point: that's somewhere else on it)
  const onDown = e => { if (!el.contains(e.target) && (at || (e.target !== anchor && !anchor.contains(e.target)))) close(); };
  const onKey = e => {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); anchor.focus(); return; }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      e.stopPropagation();
      const i = items$.indexOf(document.activeElement);
      const n = e.key === 'ArrowDown' ? (i + 1) % items$.length : (i - 1 + items$.length) % items$.length;
      items$[n]?.focus();
    }
  };
  function close() {
    el.remove();
    document.removeEventListener('pointerdown', onDown, true);
    document.removeEventListener('keydown', onKey, true);
    if (closeMenu === close) closeMenu = null;
  }
  setTimeout(() => document.addEventListener('pointerdown', onDown, true));
  document.addEventListener('keydown', onKey, true);
  closeMenu = close;
  return close;
}

export const menuOpen = () => closeMenu != null;
