// The note editor: a sticky note in front of the cards, or filling the note window (windowed). One font,
// and only bulleted, numbered and checkbox lists. It saves as you type; a locked note is encrypted before
// every save and locks again (its password forgotten) when it closes. A note closed with nothing in it is deleted.
// A note can move between the two windows (exportState, then openState in the other one); only one is open at a time.

import {parse, parseHtml, analyze, toHtml, textToBlocks, bandColor, pickColor, GENERAL_ID} from './notes.js';
import {LockSession, WrongPassword} from './crypto.js';
import {dialog, confirmDlg, toast, esc} from './ui.js';

const SAVE_DELAY = 650;
const $ = id => document.getElementById(id);

const MARKUP = `<div id="editorLayer" class="ed-layer" hidden>
  <div class="ed-card" role="dialog" aria-label="Note">
    <header class="ed-band">
      <div class="ed-cat">
        <svg><use href="#i-tag"/></svg>
        <input id="edCat" type="text" maxlength="60" placeholder="General" autocomplete="off" spellcheck="false" aria-label="Category (blank for General)">
        <div id="edCatList" class="ed-catlist" hidden></div>
      </div>
      <span id="edStatus" class="ed-status"></span>
      <button id="edLock" class="ed-btn" title="Lock with a password" aria-label="Lock"><svg><use href="#i-lock"/></svg></button>
      <button id="edDelete" class="ed-btn" title="Delete note" aria-label="Delete note"><svg><use href="#i-trash"/></svg></button>
      <button id="edPopOut" class="ed-btn" title="Open in its own window (or drag the note out of NoteSpin by this strip)" aria-label="Open in its own window"><svg><use href="#i-popout"/></svg></button>
      <button id="edPin" class="ed-btn" title="Keep on top of other windows" aria-label="Keep on top" aria-pressed="false"><svg><use href="#i-pin"/></svg></button>
      <button id="edPopIn" class="ed-btn" title="Put it back in the NoteSpin window (or drag it onto the cards)" aria-label="Put back in NoteSpin"><svg><use href="#i-popin"/></svg></button>
      <button id="edClose" class="ed-btn" title="Close (Esc)" aria-label="Close"><svg><use href="#i-x"/></svg></button>
    </header>
    <div class="ed-tools">
      <button data-cmd="ul" title="Bulleted list (type - and a space)" aria-label="Bulleted list"><svg><use href="#i-ul"/></svg></button>
      <button data-cmd="ol" title="Numbered list (type 1. and a space)" aria-label="Numbered list"><svg><use href="#i-ol"/></svg></button>
      <button data-cmd="check" title="Checklist (type [] and a space; Ctrl+Enter ticks)" aria-label="Checklist"><svg><use href="#i-check"/></svg></button>
      <span class="ed-sep"></span>
      <button data-cmd="outdent" title="Move a list item out a level (Shift+Tab)" aria-label="Move list item out"><svg><use href="#i-outdent"/></svg></button>
      <button data-cmd="indent" title="Move a list item in, under the one above (Tab)" aria-label="Move list item in"><svg><use href="#i-indent"/></svg></button>
      <span id="edLockInfo" class="ed-lockinfo" title="Locked: the first line and the category stay readable" hidden><svg><use href="#i-lock"/></svg><span>Locked: the first line and the category stay readable</span></span>
    </div>
    <div class="ed-scroll">
      <div id="edBody" class="ed-body" contenteditable="true" spellcheck="true" aria-label="Note text" aria-multiline="true"></div>
    </div>
    <footer class="ed-foot"><span id="edMeta"></span></footer>
  </div>
</div>`;

const fmtDate = ms => {
  if (!ms) return '';
  const d = new Date(ms), now = new Date();
  const time = d.toLocaleTimeString(undefined, {hour: 'numeric', minute: '2-digit'});
  if (d.toDateString() === now.toDateString()) return 'today ' + time;
  const y = new Date(now); y.setDate(now.getDate() - 1);
  if (d.toDateString() === y.toDateString()) return 'yesterday ' + time;
  return d.toLocaleDateString(undefined, {day: 'numeric', month: 'short', year: d.getFullYear() === now.getFullYear() ? undefined : 'numeric'});
};

/* ---------- moving list items in and out ---------- */

const isList = n => n?.nodeType === 1 && (n.tagName === 'UL' || n.tagName === 'OL');
const sameKind = (a, b) => a.tagName === b.tagName && a.classList.contains('check') === b.classList.contains('check');

// a new, empty list of the same kind (bullets, numbers or checkboxes) as `like`
function listLike(like) {
  const l = document.createElement(like.tagName);
  if (like.classList.contains('check')) l.className = 'check';
  return l;
}

/** Repairs nesting the browser's own commands can leave: a list straight inside a list, an item straight inside an item. */
function fixNesting(root) {
  for (const l of [...root.querySelectorAll('ul > ul, ul > ol, ol > ul, ol > ol')]) {
    let prev = l.previousElementSibling;
    if (!prev || prev.tagName !== 'LI') { prev = document.createElement('li'); l.before(prev); }
    prev.append(l);
  }
  for (const li of [...root.querySelectorAll('li > li')]) li.parentElement.after(li);
}

/** Puts an item under the one above it, in a sub-list of the same kind. Returns the item, or null when it can't move. */
function indentItem(li) {
  const prev = li.previousElementSibling;
  if (!prev || prev.tagName !== 'LI') return null;   // the first item has nothing to go under
  const list = li.parentElement;
  let sub = prev.lastElementChild;
  if (!isList(sub) || !sameKind(sub, list)) { sub = listLike(list); prev.append(sub); }
  sub.append(li);
  return li;
}

/**
 * Moves an item up a level; the items after it in its list come along as its own sub-items.
 * A top-level item becomes an ordinary line (its sub-lists stay lists). Returns where the item went.
 */
function outdentItem(li) {
  const list = li.parentElement;
  const parentLi = list.parentElement?.tagName === 'LI' ? list.parentElement : null;
  const after = [];
  for (let n = li.nextSibling; n; n = n.nextSibling) after.push(n);
  const hasAfter = after.some(n => n.nodeType === 1);
  if (parentLi) {
    if (hasAfter) {
      let sub = [...li.children].find(c => isList(c) && sameKind(c, list));
      if (!sub) { sub = listLike(list); li.append(sub); }
      for (const n of after) sub.append(n);
    }
    parentLi.after(li);
    if (!list.querySelector('li')) list.remove();
    return li;
  }
  const p = document.createElement('p');
  const subs = [];
  for (const n of [...li.childNodes]) { if (isList(n)) subs.push(n); else p.append(n); }
  if (!p.textContent && !p.querySelector('br')) p.append(document.createElement('br'));
  let rest = null;
  if (hasAfter) { rest = listLike(list); for (const n of after) rest.append(n); }
  list.after(p);
  let last = p;
  for (const s of subs) { last.after(s); last = s; }
  if (rest) last.after(rest);
  li.remove();
  if (!list.querySelector('li')) list.remove();
  return p;
}

/* ---------- password dialogs ---------- */

const pwField = (label, id) => `<label class="field"><span>${label}</span>
  <span class="pw"><input type="password" id="${id}" autocomplete="off" spellcheck="false"></span></label>`;

/** Asks for a new password, twice. Resolves with it, or null. */
export function askNewPassword(title) {
  return dialog({
    title: 'Lock this note',
    html: `<p class="lead">“${esc(title || 'Untitled')}”</p>
      ${pwField('Password', 'pw1')}${pwField('Type it again', 'pw2')}
      <label class="check"><input type="checkbox" id="pwShow"> Show password</label>
      <p class="hint">The first line (the title) and the category stay readable. Everything else is encrypted.
      <strong>There's no way to get the note back if you forget the password.</strong></p>
      <p class="err" id="pwErr" hidden></p>`,
    buttons: [{label: 'Cancel', value: null}, {label: 'Lock note', value: 'ok', primary: true}],
    init: box => {
      box.querySelector('#pwShow').addEventListener('change', e =>
        box.querySelectorAll('input[type=password], input.shown').forEach(i => { i.type = e.target.checked ? 'text' : 'password'; i.classList.toggle('shown', e.target.checked); }));
    },
    submit: (v, box) => {
      const a = box.querySelector('#pw1').value, b = box.querySelector('#pw2').value, err = box.querySelector('#pwErr');
      const fail = msg => { err.textContent = msg; err.hidden = false; return false; };
      if (!a) { box.querySelector('#pw1').focus(); return fail('Type a password.'); }
      if (a !== b) { box.querySelector('#pw2').select(); return fail('The two passwords are different.'); }
      return a;
    },
  });
}

/**
 * Asks for a locked note's password and opens it with open(password), which throws WrongPassword
 * when it doesn't fit. Resolves with what open returned, 'delete' when the person chose to delete the
 * note instead (a forgotten password shouldn't leave it stuck forever), or null when cancelled.
 */
export function askPassword(title, open) {
  return dialog({
    title: 'Locked note',
    html: `<p class="lead">“${esc(title || 'Untitled')}”</p>${pwField('Password', 'pw1')}
      <label class="check"><input type="checkbox" id="pwShow"> Show password</label>
      <p class="err" id="pwErr" hidden></p>`,
    buttons: [
      {label: 'Delete note…', value: 'delete', danger: true, left: true},
      {label: 'Cancel', value: null},
      {label: 'Unlock', value: 'ok', primary: true},
    ],
    init: box => box.querySelector('#pwShow').addEventListener('change', e => { box.querySelector('#pw1').type = e.target.checked ? 'text' : 'password'; }),
    submit: async (v, box) => {
      if (v === 'delete') return 'delete';
      const input = box.querySelector('#pw1'), err = box.querySelector('#pwErr');
      const btn = box.querySelector('.btn.primary');
      btn.disabled = true;
      btn.textContent = 'Unlocking…';
      try { return await open(input.value); }
      catch (e) {
        err.textContent = e instanceof WrongPassword ? 'That password doesn\'t open this note.' : e.message;
        err.hidden = false;
        box.classList.remove('shake');
        void box.offsetWidth;
        box.classList.add('shake');
        input.select();
        return false;
      } finally {
        btn.disabled = false;
        btn.textContent = 'Unlock';
      }
    },
  });
}

/* ---------- the editor ---------- */

export class Editor {
  /**
   * app: what the editor needs from the page (categories, the wheel, what to do when a note closes).
   * windowed: it's the note window's editor (no wheel behind it; the top strip moves the window).
   */
  constructor(app, {windowed = false} = {}) {
    this.app = app;
    this.windowed = windowed;
    if (!$('editorLayer')) $('dialogs').insertAdjacentHTML('beforebegin', MARKUP);
    $('edPopOut').hidden = windowed;
    $('edPin').hidden = $('edPopIn').hidden = !windowed;
    this.layer = $('editorLayer');
    this.card = this.layer.querySelector('.ed-card');
    this.body = $('edBody');
    this.catInput = $('edCat');
    this.catList = $('edCatList');
    this.status = $('edStatus');
    this.meta = $('edMeta');
    this.note = null;
    this.session = null;
    this.dirty = false;
    this.saving = null;
    this.timer = 0;
    this.catPick = -1;
    document.execCommand('defaultParagraphSeparator', false, 'p');
    this.wire();
  }

  get isOpen() { return !this.layer.hidden; }

  wire() {
    $('edClose').addEventListener('click', () => this.close());
    $('edDelete').addEventListener('click', () => this.deleteNote());
    $('edLock').addEventListener('click', () => this.toggleLock());
    $('edPopOut').addEventListener('click', () => this.app.popOut?.());
    $('edPin').addEventListener('click', () => this.app.togglePin?.());
    $('edPopIn').addEventListener('click', () => this.app.popIn?.());
    this.card.querySelector('.ed-band').addEventListener('pointerdown', e => this.onBandDown(e));
    this.layer.querySelectorAll('.ed-tools [data-cmd]').forEach(b => {
      b.addEventListener('mousedown', e => e.preventDefault());   // keep the caret in the note
      b.addEventListener('click', () => this.command(b.dataset.cmd));
    });
    // a click on the dimmed background closes the note (but not a text selection that ends there)
    let downOnLayer = false;
    this.layer.addEventListener('mousedown', e => { downOnLayer = e.target === this.layer; if (downOnLayer) e.preventDefault(); });
    this.layer.addEventListener('click', e => { if (e.target === this.layer && downOnLayer) this.close(); downOnLayer = false; });

    const body = this.body;
    body.addEventListener('input', e => this.onInput(e));
    body.addEventListener('keydown', e => this.onKey(e));
    body.addEventListener('paste', e => this.onPaste(e));
    body.addEventListener('drop', e => this.onDrop(e));
    body.addEventListener('dragover', e => { if ([...(e.dataTransfer?.types || [])].includes('Files')) { e.preventDefault(); e.dataTransfer.dropEffect = 'none'; } });
    body.addEventListener('mousedown', e => this.onCheckboxClick(e));
    document.addEventListener('selectionchange', () => { if (this.isOpen) this.updateTools(); });
    this.layer.addEventListener('keydown', e => {
      if (e.key === 'Escape' && !this.catList.hidden) return;
      if (e.key === 'Escape') { e.preventDefault(); this.close(); }
      if (e.key === 's' && e.ctrlKey) { e.preventDefault(); this.save(); }
    });

    // category: type a name or pick one; blank is General
    const ci = this.catInput;
    ci.addEventListener('focus', () => this.showCats());
    ci.addEventListener('input', () => { this.showCats(); this.paintCategory(); });
    ci.addEventListener('blur', () => setTimeout(() => { this.hideCats(); this.commitCategory(); }, 120));
    ci.addEventListener('keydown', e => {
      const opts = [...this.catList.querySelectorAll('.opt')];
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        if (this.catList.hidden) this.showCats();
        this.catPick = opts.length ? (this.catPick + (e.key === 'ArrowDown' ? 1 : -1) + opts.length) % opts.length : -1;
        opts.forEach((o, i) => o.classList.toggle('on', i === this.catPick));
      } else if (e.key === 'Enter') {
        e.preventDefault();
        if (this.catPick >= 0 && opts[this.catPick]) ci.value = opts[this.catPick].dataset.name;
        this.hideCats();
        this.commitCategory();
        this.focusEnd();
      } else if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        if (!this.catList.hidden) this.hideCats(); else this.close();
      }
    });
    this.catList.addEventListener('mousedown', e => {
      const o = e.target.closest('.opt');
      if (!o) return;
      e.preventDefault();
      ci.value = o.dataset.name;
      this.hideCats();
      this.commitCategory();
    });
  }

  /* ---------- open / close ---------- */

  /**
   * note: a stored note (from NoteGet) or null for a new one.
   * opts: html (the note's text, decrypted if it was locked), session (when unlocked), categoryId (for a
   * new note), from (the card's place on screen, to grow out of), highlight (search terms).
   */
  open(note, {html = '', session = null, categoryId = GENERAL_ID, from = null, highlight = null, original = null} = {}) {
    this.note = note ? {...note} : {id: '', cat: categoryId, locked: false, created: 0, updated: 0, title: ''};
    this.session = session;
    this.original = original;   // the note as stored, to put back if it's emptied and deleted
    this.dirty = false;
    clearTimeout(this.timer);
    const cat = this.app.category(this.note.cat);
    this.catInput.value = cat && cat.id !== GENERAL_ID ? cat.name : '';
    this.newColor = null;
    const clean = toHtml(parseHtml(html || ''));
    this.body.innerHTML = clean || '<p><br></p>';
    this.paintCategory();
    this.setStatus('');
    this.showMeta();
    this.updateLockUi();
    this.updateEmpty();
    this.layer.hidden = false;
    this.card.style.transform = '';
    if (!this.windowed) {
      this.app.spinner.enabled = false;
      this.animate(from, true);
    }
    this.from = from;
    if (highlight?.length) this.highlight(highlight);
    this.focusEnd(!!highlight?.length);
  }

  /** Saves and closes the note (deleting it if it's empty). opts go along to app.editorClosed. */
  async close(opts = {}) {
    if (!this.isOpen || this.closing) return;
    this.closing = true;
    try {
      await this.save();
      const doc = analyze(parse(this.body));
      let removed = null;
      if (doc.empty && this.note.id) {
        try {
          await NS.call('NoteDelete', this.note.id);
          removed = this.original;
        } catch (e) { toast('Couldn\'t delete the empty note: ' + e.message, {kind: 'error'}); }
      }
      const id = doc.empty ? null : this.note.id || null;
      if (!this.windowed) await this.animate(this.app.spinner.frontRect() && id ? this.from : null, false);
      this.hide();
      this.app.editorClosed(id, {...opts, emptied: removed});
    } finally {
      this.closing = false;
    }
  }

  hide() {
    this.layer.hidden = true;
    this.body.innerHTML = '';
    this.session = null;   // locked notes lock again
    this.card.style.transform = '';
    this.card.classList.remove('tearing');
    CSS.highlights?.delete('ns-search');
    if (!this.windowed) this.app.spinner.enabled = true;
  }

  /* ---------- moving a note to the other window ---------- */

  /**
   * Everything the other window's editor needs to carry on with this note: the note, its text, the category
   * typed, the key while it's unlocked, and whether a change is still to be saved. Saves first, so nothing
   * typed is lost on the way and a new note has its id before it moves.
   */
  async exportState() {
    await this.save();
    clearTimeout(this.timer);
    const doc = analyze(parse(this.body));
    const n = this.note;
    return {
      note: n.id ? {id: n.id, cat: n.cat, title: n.title, locked: !!this.session, created: n.created, updated: n.updated} : null,
      categoryId: n.cat,
      catName: this.catInput.value.trim(),
      newColor: this.newColor,
      html: doc.html,
      key: this.session ? await this.session.export() : null,
      original: this.original || null,
      dirty: this.dirty,
    };
  }

  /** Closes the editor without saving or deleting anything: the note has moved to the other window. */
  detach() {
    clearTimeout(this.timer);
    this.dirty = false;
    this.hide();
  }

  /** Opens a note brought over from the other window (what exportState gave there). */
  async openState(st, {from = null} = {}) {
    const session = st.key ? await LockSession.import(st.key) : null;
    this.open(st.note, {html: st.html, session, categoryId: st.categoryId || GENERAL_ID, original: st.original, from, highlight: st.highlight});
    let dirty = !!st.dirty;
    if (typeof st.catName === 'string' && st.catName !== this.catInput.value) { this.catInput.value = st.catName; dirty = true; }
    if (st.newColor) this.newColor = st.newColor;
    this.paintCategory();
    if (dirty) this.markDirty();
  }

  /* ---------- the top strip: drags the note out of the main window, or moves the note window ---------- */

  onBandDown(e) {
    if (e.button !== 0 || e.target.closest('input, button, .ed-catlist')) return;
    if (this.windowed) {
      e.preventDefault();
      this.app.dragWindow?.(e.pointerType);
      return;
    }
    if (!this.app.popOut) return;
    e.preventDefault();
    const band = e.currentTarget, card = this.card;
    const r = card.getBoundingClientRect();
    const g = {id: e.pointerId, x0: e.clientX, y0: e.clientY, gx: e.clientX - r.left, gy: e.clientY - r.top, moved: false, out: null};
    band.setPointerCapture(e.pointerId);
    const move = ev => {
      if (ev.pointerId !== g.id || g.out) return;
      const dx = ev.clientX - g.x0, dy = ev.clientY - g.y0;
      if (!g.moved) {
        if (Math.hypot(dx, dy) < 6) return;
        g.moved = true;
        card.classList.add('tearing');
      }
      card.style.transform = `translate(${dx}px, ${dy}px)`;
      // past the edge of the window: the note gets a window of its own, which carries on with the drag
      if (ev.clientX <= 0 || ev.clientY <= 0 || ev.clientX >= innerWidth - 1 || ev.clientY >= innerHeight - 1)
        g.out = this.app.popOut({drag: true, grabX: g.gx, grabY: g.gy, pointerType: ev.pointerType});
    };
    const end = ev => {
      if (ev.pointerId !== g.id) return;
      band.removeEventListener('pointermove', move);
      band.removeEventListener('pointerup', end);
      band.removeEventListener('pointercancel', end);
      // the note window follows a mouse until its button comes up; for a finger or pen it's told when the drag ends
      if (g.out) { if (ev.pointerType !== 'mouse') g.out.then(() => NS.call('NoteWinFollowEnd')).catch(() => {}); return; }
      card.classList.remove('tearing');
      if (g.moved) {
        card.animate([{transform: card.style.transform}, {transform: 'none'}], {duration: 180, easing: 'cubic-bezier(.2,.8,.2,1)'});
        card.style.transform = '';
      }
    };
    band.addEventListener('pointermove', move);
    band.addEventListener('pointerup', end);
    band.addEventListener('pointercancel', end);
  }

  animate(rect, opening) {
    const card = this.card;
    const ease = 'cubic-bezier(.2,.8,.2,1)';
    const fadeLayer = this.layer.animate(
      [{backgroundColor: 'rgba(12,14,18,0)'}, {backgroundColor: 'rgba(12,14,18,.5)'}],
      {duration: 280, easing: ease, direction: opening ? 'normal' : 'reverse', fill: 'forwards'});
    let frames = [{opacity: 0, transform: 'translateY(14px) scale(.97)'}, {opacity: 1, transform: 'none'}];
    if (rect && rect.width > 20) {
      const f = card.getBoundingClientRect();
      const dx = rect.left + rect.width / 2 - (f.left + f.width / 2), dy = rect.top + rect.height / 2 - (f.top + f.height / 2);
      frames = [{opacity: 0.35, transform: `translate(${dx}px, ${dy}px) scale(${rect.width / f.width}, ${rect.height / f.height})`}, {opacity: 1, transform: 'none'}];
    }
    const a = card.animate(frames, {duration: opening ? 320 : 240, easing: ease, direction: opening ? 'normal' : 'reverse', fill: 'forwards'});
    return a.finished.then(() => { a.cancel(); fadeLayer.cancel(); }).catch(() => {});
  }

  focusEnd(top = false) {
    this.body.focus();
    const sel = getSelection(), r = document.createRange();
    r.selectNodeContents(this.body);
    r.collapse(top);
    sel.removeAllRanges();
    sel.addRange(r);
    if (!top) this.body.lastElementChild?.scrollIntoView({block: 'nearest'});
  }

  /* ---------- saving ---------- */

  markDirty() {
    this.dirty = true;
    this.setStatus('Editing…');
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.save(), SAVE_DELAY);
  }

  /** Saves now if anything changed (waits for a save already under way). */
  async save() {
    clearTimeout(this.timer);
    while (this.saving) await this.saving;
    if (!this.dirty || !this.note) return;
    this.dirty = false;
    this.saving = this.write().finally(() => { this.saving = null; });
    await this.saving;
  }

  /** For the app closing: starts any waiting save; true while one is being written. */
  busy() {
    if (this.isOpen && this.dirty && !this.saving) this.save();
    return !!this.saving || (this.isOpen && this.dirty);
  }

  async write() {
    const doc = analyze(parse(this.body));
    if (doc.empty && !this.note.id) { this.setStatus(''); return; }   // nothing typed yet: no note to keep
    const name = this.catInput.value.trim();
    const existing = name ? this.app.categoryByName(name) : this.app.category(GENERAL_ID);
    if (!existing && !this.newColor) this.newColor = pickColor(this.app.categories());
    const locked = !!this.session;
    this.setStatus(locked ? 'Encrypting…' : 'Saving…');
    try {
      const payload = {
        id: this.note.id,
        title: doc.title,
        body: locked ? await this.session.encrypt(doc.html) : doc.html,
        preview: locked ? '' : JSON.stringify(doc.lines),
        search: locked ? doc.title.toLocaleLowerCase() : doc.search,
        locked,
        category: name,
        categoryColor: existing ? existing.color : this.newColor,
      };
      const r = await NS.call('NoteSave', JSON.stringify(payload));
      Object.assign(this.note, {id: r.id, cat: r.cat, created: r.created, updated: r.updated, title: doc.title, locked});
      if (r.newCategory) await this.app.reloadCategories();
      this.app.noteChanged(this.note);
      this.setStatus(this.dirty ? 'Editing…' : 'Saved');
      this.showMeta();
    } catch (e) {
      this.dirty = true;
      this.setStatus('Not saved');
      toast('Couldn\'t save the note: ' + e.message, {kind: 'error', timeout: 7000});
    }
  }

  setStatus(s) {
    this.status.textContent = s;
    this.status.dataset.state = s === 'Saved' ? 'ok' : s === 'Not saved' ? 'error' : '';
  }

  showMeta() {
    const n = this.note;
    this.meta.textContent = n.created ? `Created ${fmtDate(n.created)}${n.updated && n.updated !== n.created ? ' · Edited ' + fmtDate(n.updated) : ''}` : 'New note';
  }

  /* ---------- category ---------- */

  paintCategory() {
    const name = this.catInput.value.trim();
    const c = name ? this.app.categoryByName(name) : this.app.category(GENERAL_ID);
    if (!c && !this.newColor) this.newColor = pickColor(this.app.categories());
    const color = c ? c.color : this.newColor;
    this.card.style.setProperty('--paper', color);
    this.card.style.setProperty('--band', bandColor(color));
    this.app.painted?.(color, bandColor(color));
  }

  commitCategory() {
    const name = this.catInput.value.trim();
    const cur = this.app.category(this.note?.cat);
    const same = cur && (cur.id === GENERAL_ID ? name === '' || name.toLowerCase() === 'general' : cur.name.toLowerCase() === name.toLowerCase());
    if (name.toLowerCase() === 'general') this.catInput.value = '';
    this.paintCategory();
    if (!same && this.note) this.markDirty();
  }

  showCats() {
    const q = this.catInput.value.trim().toLowerCase();
    const cats = this.app.categories();
    const list = cats.filter(c => !q || c.name.toLowerCase().includes(q));
    const exact = cats.some(c => c.name.toLowerCase() === q);
    let html = list.map(c => `<div class="opt" data-name="${esc(c.id === GENERAL_ID ? '' : c.name)}"><span class="dot" style="background:${c.color}"></span>${esc(c.name)}</div>`).join('');
    if (q && !exact) {
      const color = this.newColor || pickColor(cats);
      html = `<div class="opt new" data-name="${esc(this.catInput.value.trim())}"><span class="dot" style="background:${color}"></span>New category “${esc(this.catInput.value.trim())}”</div>` + html;
    }
    this.catList.innerHTML = html || '<div class="none">No categories</div>';
    this.catList.hidden = false;
    this.catPick = -1;
  }

  hideCats() { this.catList.hidden = true; this.catPick = -1; }

  /* ---------- lock ---------- */

  updateLockUi() {
    const locked = !!this.session;
    $('edLock').classList.toggle('on', locked);
    $('edLock').title = locked ? 'Locked: click to remove the lock' : 'Lock with a password';
    $('edLockInfo').hidden = !locked;
    this.card.classList.toggle('locked', locked);
  }

  /** The note window's "keep on top" button. */
  setPinned(on) {
    const b = $('edPin');
    b.classList.toggle('on', on);
    b.setAttribute('aria-pressed', String(on));
    b.title = on ? 'Kept on top of other windows (click to stop)' : 'Keep on top of other windows';
  }

  async toggleLock() {
    if (!this.session) {
      const doc = analyze(parse(this.body));
      if (doc.empty) { toast('Write something first, then lock it.'); return; }
      const pw = await askNewPassword(doc.title);
      if (!pw) { this.body.focus(); return; }
      this.setStatus('Locking…');
      this.session = await LockSession.create(pw);
      this.updateLockUi();
      this.dirty = true;
      await this.save();
      toast('Locked. It locks again each time you close it.');
    } else {
      const ok = await confirmDlg({
        title: 'Remove the lock?',
        message: 'The note will be saved without a password, so anyone using this notebook can read it.',
        ok: 'Remove lock',
      });
      if (!ok) { this.body.focus(); return; }
      this.session = null;
      this.updateLockUi();
      this.dirty = true;
      await this.save();
    }
    this.body.focus();
  }

  async deleteNote() {
    const doc = analyze(parse(this.body));
    if (!this.note.id && doc.empty) { this.close(); return; }
    const ok = await confirmDlg({title: 'Delete this note?', message: `“${doc.title || 'Untitled'}” will be deleted.`, ok: 'Delete', danger: true});
    if (!ok) { this.body.focus(); return; }
    clearTimeout(this.timer);
    while (this.saving) await this.saving;
    this.dirty = false;
    try {
      if (this.note.id) await NS.call('NoteDelete', this.note.id);
    } catch (e) {
      toast('Couldn\'t delete the note: ' + e.message, {kind: 'error'});
      return;
    }
    if (!this.windowed) await this.animate(null, false);
    this.hide();
    this.app.editorClosed(null, {deleted: true});
  }

  /* ---------- typing ---------- */

  onInput(e) {
    this.wrapLooseText();
    if (e.inputType === 'insertParagraph') {
      // a new checklist item starts unticked
      const li = this.closest('li');
      if (li && !li.textContent.trim()) li.classList.remove('done');
    }
    CSS.highlights?.delete('ns-search');
    this.updateEmpty();
    this.markDirty();
  }

  updateEmpty() {
    this.body.classList.toggle('empty', !this.body.textContent.trim() && !this.body.querySelector('li'));
  }

  // Text typed straight into the editor (after everything was deleted) goes into a paragraph
  wrapLooseText() {
    const loose = [...this.body.childNodes].filter(n => (n.nodeType === 3 && n.data.length) || (n.nodeType === 1 && !/^(P|UL|OL|DIV)$/.test(n.tagName) && n.tagName !== 'BR'));
    if (!loose.length) return;
    const sel = getSelection();
    const an = sel.anchorNode, ao = sel.anchorOffset;
    const p = document.createElement('p');
    loose[0].before(p);
    for (const n of loose) p.append(n);
    if (an && this.body.contains(an)) sel.collapse(an, ao);
  }

  closest(sel) {
    const s = getSelection();
    let n = s.anchorNode;
    if (!n || !this.body.contains(n)) return null;
    if (n.nodeType === 3) n = n.parentElement;
    const el = n.closest(sel);
    return el && this.body.contains(el) ? el : null;
  }

  listKind() {
    const li = this.closest('li');
    const l = li?.parentElement;
    if (!l) return null;
    return l.tagName === 'OL' ? 'ol' : l.classList.contains('check') ? 'check' : 'ul';
  }

  updateTools() {
    const kind = this.listKind();
    this.layer.querySelectorAll('.ed-tools [data-cmd]').forEach(b => {
      if (['ul', 'ol', 'check'].includes(b.dataset.cmd)) b.classList.toggle('on', b.dataset.cmd === kind);
      if (b.dataset.cmd === 'indent' || b.dataset.cmd === 'outdent') b.disabled = !kind;
    });
  }

  command(cmd) {
    this.body.focus();
    if (cmd === 'indent' || cmd === 'outdent') this.indent(cmd === 'outdent');
    else this.applyList(cmd);
  }

  /** Bullets, numbers or checkboxes on the current lines; the same again turns the list off. */
  applyList(kind, tick = false) {
    const li = this.closest('li');
    const list = li?.parentElement;
    const exec = k => document.execCommand(k === 'ol' ? 'insertOrderedList' : 'insertUnorderedList');
    if (list && (list.tagName === 'UL' || list.tagName === 'OL')) {
      const cur = list.tagName === 'OL' ? 'ol' : list.classList.contains('check') ? 'check' : 'ul';
      if (cur === kind) exec(kind);
      else if (kind === 'ol' || cur === 'ol') {
        exec(kind);
        const nl = this.closest('ul, ol');
        if (nl) {
          nl.classList.toggle('check', kind === 'check');
          if (kind !== 'check') nl.querySelectorAll(':scope > li.done').forEach(x => x.classList.remove('done'));
          if (!nl.className) nl.removeAttribute('class');
        }
      } else {
        list.classList.toggle('check', kind === 'check');
        if (kind !== 'check') list.querySelectorAll(':scope > li.done').forEach(x => x.classList.remove('done'));
        if (!list.className) list.removeAttribute('class');
      }
    } else {
      exec(kind);
      if (kind === 'check') {
        const ul = this.closest('ul');
        ul?.classList.add('check');
        if (tick) this.closest('li')?.classList.add('done');
      }
    }
    this.unnestLists();
    this.updateTools();
    this.updateEmpty();
    this.markDirty();
  }

  // The browser's list command can leave a list inside a paragraph (<p><ul>…</ul></p>); lift it out
  unnestLists() {
    const sel = getSelection();
    const an = sel.anchorNode, ao = sel.anchorOffset;
    for (const list of [...this.body.querySelectorAll('p > ul, p > ol')]) {
      const p = list.parentElement;
      const rest = document.createElement('p');
      let n = list.nextSibling;
      while (n) { const next = n.nextSibling; rest.append(n); n = next; }
      p.after(list);
      if (rest.textContent.trim() || rest.querySelector('ul, ol')) list.after(rest);
      if (!p.textContent.trim() && !p.querySelector('ul, ol')) p.remove();
    }
    if (an && this.body.contains(an)) sel.collapse(an, ao);
  }

  /**
   * Moves the current list item in (under the item above it) or out (up a level; at the top level it
   * becomes an ordinary line). Done here rather than with the browser's indent and outdent commands, which
   * mishandle lists nested inside list items (the way notes are stored) and leave items where they were.
   */
  indent(out) {
    const li = this.closest('li');
    if (!li) return;
    const sel = getSelection();
    const saved = [sel.anchorNode, sel.anchorOffset, sel.focusNode, sel.focusOffset];
    fixNesting(this.body);
    const moved = out ? outdentItem(li) : indentItem(li);
    if (!moved) return;
    if (saved[0] && this.body.contains(saved[0]) && this.body.contains(saved[2])) {
      try { sel.setBaseAndExtent(...saved); } catch { sel.collapse(moved, 0); }
    } else sel.collapse(moved, 0);
    this.updateTools();
    this.updateEmpty();
    this.markDirty();
  }

  onKey(e) {
    if (e.key === 'Tab') {
      e.preventDefault();
      if (this.listKind()) this.indent(e.shiftKey);
      else if (!e.shiftKey) document.execCommand('insertText', false, '\t');
      return;
    }
    // Enter on an empty list item, or Backspace at the start of one, moves it out a level
    // (the browser's own handling of both has the same fault as its outdent command)
    if ((e.key === 'Enter' && !e.shiftKey && !e.ctrlKey) || (e.key === 'Backspace' && !e.ctrlKey)) {
      const li = this.closest('li');
      const sel = getSelection();
      if (li && sel.isCollapsed) {
        const own = [...li.childNodes].filter(n => !isList(n));
        const empty = !own.some(n => n.textContent.trim());
        const r = document.createRange();
        r.setStart(li, 0);
        r.setEnd(sel.anchorNode, sel.anchorOffset);
        const atStart = !r.toString() && !(sel.anchorNode !== li && [...li.children].some(c => isList(c) && c.contains(sel.anchorNode)));
        if ((e.key === 'Enter' && empty) || (e.key === 'Backspace' && atStart)) {
          e.preventDefault();
          this.indent(true);
          return;
        }
      }
    }
    if (e.key === 'Enter' && e.ctrlKey) {
      const li = this.closest('li');
      if (li && li.parentElement.classList.contains('check')) {
        e.preventDefault();
        li.classList.toggle('done');
        this.markDirty();
      }
      return;
    }
    // "- ", "* ", "1. ", "[] " or "[x] " at the start of a line starts a list
    if (e.key === ' ' && !e.ctrlKey && !e.altKey && !this.listKind()) {
      const sel = getSelection();
      if (!sel.isCollapsed || !sel.anchorNode) return;
      const block = this.closest('p, div') || this.body;
      const r = document.createRange();
      r.setStart(block, 0);
      r.setEnd(sel.anchorNode, sel.anchorOffset);
      const before = r.toString();
      const kind = /^[-*•]$/.test(before) ? 'ul' : /^1[.)]$/.test(before) ? 'ol' : /^\[ ?\]$/.test(before) ? 'check' : /^\[[xX]\]$/.test(before) ? 'tick' : null;
      if (!kind) return;
      e.preventDefault();
      sel.removeAllRanges();
      sel.addRange(r);
      document.execCommand('delete');
      this.applyList(kind === 'tick' ? 'check' : kind, kind === 'tick');
    }
  }

  // Clicking a checkbox ticks it (the box sits in the item's left padding)
  onCheckboxClick(e) {
    const li = e.target.closest?.('li');
    if (!li || e.target !== li || !li.parentElement?.classList.contains('check')) return;
    const pad = parseFloat(getComputedStyle(li).paddingLeft) || 26;
    if (e.clientX - li.getBoundingClientRect().left > pad) return;
    e.preventDefault();
    li.classList.toggle('done');
    this.markDirty();
  }

  // Pasted text keeps its lines and lists but loses its fonts, colours, links and pictures
  onPaste(e) {
    const dt = e.clipboardData;
    if (!dt) return;
    e.preventDefault();
    const html = dt.getData('text/html'), text = dt.getData('text/plain');
    let blocks = html ? parseHtml(html, {paste: true}) : textToBlocks(text);
    if (!blocks.length) return;
    if (blocks.length === 1 && blocks[0].t === 'p') document.execCommand('insertText', false, text.replace(/\r?\n/g, ' ') || blocks[0].inl.filter(x => typeof x === 'string').join(''));
    else document.execCommand('insertHTML', false, toHtml(blocks));
  }

  onDrop(e) {
    const dt = e.dataTransfer;
    if (!dt) return;
    e.preventDefault();
    if ([...dt.types].includes('Files')) return;   // pictures and files can't go in a note
    const text = dt.getData('text/plain');
    if (!text) return;
    const pos = document.caretRangeFromPoint?.(e.clientX, e.clientY);
    if (pos) { const s = getSelection(); s.removeAllRanges(); s.addRange(pos); }
    document.execCommand('insertText', false, text);
  }

  /** Marks the search words in the note (without changing it). */
  highlight(terms) {
    if (!CSS.highlights || !terms.length) return;
    const ranges = [];
    const walker = document.createTreeWalker(this.body, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const lower = n.data.toLocaleLowerCase();
      for (const t of terms) {
        for (let i = lower.indexOf(t); i >= 0; i = lower.indexOf(t, i + t.length)) {
          const r = document.createRange();
          r.setStart(n, i);
          r.setEnd(n, i + t.length);
          ranges.push(r);
        }
      }
    }
    if (!ranges.length) return;
    CSS.highlights.set('ns-search', new Highlight(...ranges));
    const first = ranges.sort((a, b) => a.compareBoundaryPoints(Range.START_TO_START, b))[0];
    requestAnimationFrame(() => first.startContainer.parentElement?.scrollIntoView({block: 'center'}));
  }
}
