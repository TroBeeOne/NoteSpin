// NoteSpin: the side panel, the views (all notes, categories, one category), search, notebooks and backups.

import {Spinner} from './spinner.js';
import {Editor, askPassword} from './editor.js';
import {LockSession} from './crypto.js';
import {PALETTE, GENERAL_ID, pickColor, analyze, parseHtml, textToBlocks} from './notes.js';
import {dialog, confirmDlg, promptDlg, alertDlg, toast, menu, esc, dialogOpen, menuOpen} from './ui.js';

const $ = id => document.getElementById(id);
const collator = new Intl.Collator(undefined, {sensitivity: 'base', numeric: true});
const plural = (n, one, many = one + 's') => `${n} ${n === 1 ? one : many}`;

const state = {
  nb: null,              // the open notebook {name, path, notes, categories}
  notebooks: [],
  cats: [],
  catMap: new Map(),
  notes: [],             // {id, cat, title, preview, lines, locked, created, updated} (no bodies)
  view: 'all',           // all | categories | category
  catId: null,           // the category open in view 'category'
  order: 'az',           // az | recent
  query: '',
  terms: [],
  matches: null,         // ids of the notes found by the search (null: no search)
  items: [],
};

let spinner, editor, searchSeq = 0;

// What the editor needs from the app
const app = {
  get spinner() { return spinner; },
  category: id => state.catMap.get(id),
  categoryByName: name => state.cats.find(c => c.name.toLowerCase() === String(name).trim().toLowerCase()),
  categories: () => state.cats,
  async reloadCategories() {
    const d = await NS.call('NotesAll');
    state.cats = d.categories;
    state.catMap = new Map(d.categories.map(c => [c.id, c]));
  },
  noteChanged() {},
  async editorClosed(id, {emptied, deleted} = {}) {
    await loadNotes(id);
    noteGone({emptied, deleted});
    $('stage').querySelector('canvas')?.focus();
  },
  popOut: opts => popOut(opts),
};

// After a note closed with nothing in it (deleted, with Undo), or was deleted
function noteGone({emptied, deleted}) {
  if (emptied) toast('The empty note was deleted.', {action: {label: 'Undo', fn: () => restoreNote(emptied)}, timeout: 7000});
  if (deleted) toast('Note deleted.');
}

/* ---------- loading ---------- */

function applyNotebook(st) {
  state.nb = st.current;
  state.notebooks = st.notebooks;
  state.dir = st.dir;
  state.defaultDir = st.defaultDir;
  $('nbName').textContent = st.current.name;
  $('nbBtn').title = `${st.current.name}\n${st.current.path}\n\nNotebooks and backups`;
  // the window's title bar: the notebook, and where its file is (with the frame hidden, the title strip: the notebook)
  document.title = `${st.current.name} — ${st.current.path} — NoteSpin`;
  $('stripTitle').textContent = `${st.current.name} — NoteSpin`;
  $('stripTitle').title = st.current.path;
}

const inOneDrive = p => /\\OneDrive( - [^\\]+)?(\\|$)/i.test(p || '');

async function loadNotebook() {
  applyNotebook(await NS.call('NbState'));
  await loadNotes();
}

async function loadNotes(focusId) {
  const d = await NS.call('NotesAll');
  state.cats = d.categories;
  state.catMap = new Map(d.categories.map(c => [c.id, c]));
  state.notes = d.notes;
  if (state.view === 'category' && !state.catMap.has(state.catId)) { state.view = 'categories'; state.catId = null; }
  if (state.query) await runSearch({rebuildAfter: false});
  rebuild(focusId);
}

/* ---------- what's on the cards ---------- */

const linesOf = n => {
  if (!n.lines) { try { n.lines = n.preview ? JSON.parse(n.preview) : []; } catch { n.lines = []; } }
  return n.lines;
};

/** When something was last edited, kept short: Today, Yesterday, Nov 12, Nov 12, 2025. */
function shortDate(ms) {
  if (!ms) return '';
  const d = new Date(ms), now = new Date();
  if (d.toDateString() === now.toDateString()) return 'Today';
  const y = new Date(now);
  y.setDate(now.getDate() - 1);
  if (d.toDateString() === y.toDateString()) return 'Yesterday';
  return d.toLocaleDateString(undefined, {month: 'short', day: 'numeric', year: d.getFullYear() === now.getFullYear() ? undefined : 'numeric'});
}

// The tab on a note's card: its first letter in A–Z order (in Recent order the date on the band says it)
function tabFor(n) {
  if (state.order === 'recent') return '';
  const ch = (n.title || '').trim().charAt(0).toLocaleUpperCase();
  return /[\p{L}\p{N}]/u.test(ch) ? ch : '#';
}

function sortNotes(list) {
  if (state.order === 'recent') return [...list].sort((a, b) => b.updated - a.updated);
  return [...list].sort((a, b) => (!a.title - !b.title) || collator.compare(a.title, b.title) || b.updated - a.updated);
}

function noteItem(n) {
  const c = state.catMap.get(n.cat) || state.catMap.get(GENERAL_ID);
  const tab = tabFor(n), date = shortDate(n.updated);
  return {
    id: n.id, kind: 'note',
    key: `n|${n.id}|${n.updated}|${c.color}|${c.name}|${tab}|${date}|${n.locked}`,
    face: {color: c.color, band: c.name, date, tab, title: n.title || 'Untitled', lines: linesOf(n), locked: n.locked},
  };
}

function categoryItem(c, notes) {
  const titles = sortNotes(notes).slice(0, 14).map(n => ({k: 'ul', d: 0, x: (n.locked ? '🔒 ' : '') + (n.title || 'Untitled')}));
  const label = state.matches ? plural(notes.length, 'match', 'matches') : plural(notes.length, 'note');
  const date = shortDate(Math.max(0, ...notes.map(n => n.updated)));
  return {
    id: c.id, kind: 'category',
    key: `c|${c.id}|${c.color}|${c.name}|${label}|${date}|${titles.map(t => t.x).join('\u0001')}`,
    // the band is all that shows of the cards behind, so it carries the name (and the latest edit); the tab, how many notes
    face: {color: c.color, band: c.name, date, tab: String(notes.length), title: c.name, lines: titles,
      empty: state.matches ? 'No matching notes' : 'No notes yet'},
  };
}

function buildItems() {
  const notes = state.matches ? state.notes.filter(n => state.matches.has(n.id)) : state.notes;
  if (state.view === 'all') return sortNotes(notes).map(noteItem);
  if (state.view === 'category') return sortNotes(notes.filter(n => n.cat === state.catId)).map(noteItem);
  const by = new Map(state.cats.map(c => [c.id, []]));
  for (const n of notes) (by.get(n.cat) || by.get(GENERAL_ID)).push(n);
  const latest = c => Math.max(0, ...by.get(c.id).map(n => n.updated));
  let cats = state.cats.filter(c => !state.matches || by.get(c.id).length);
  cats = state.order === 'recent'
    ? cats.sort((a, b) => latest(b) - latest(a) || collator.compare(a.name, b.name))
    : cats.sort((a, b) => collator.compare(a.name, b.name));
  return cats.map(c => categoryItem(c, by.get(c.id)));
}

// The cards right behind the front one show their titles, except category cards: their band has the name already
const titlesBehind = () => state.view !== 'categories';

function rebuild(focusId) {
  state.items = buildItems();
  spinner.setItems(state.items, focusId, {titlesBehind: titlesBehind()});
  updatePanel();
}

/* ---------- panel ---------- */

function updatePanel() {
  $('viewSeg').querySelectorAll('button').forEach(b =>
    b.setAttribute('aria-checked', String(b.dataset.view === (state.view === 'all' ? 'all' : 'categories'))));
  $('orderSeg').querySelectorAll('button').forEach(b => b.setAttribute('aria-checked', String(b.dataset.order === state.order)));
  const inCat = state.view === 'category';
  $('crumb').hidden = !inCat;
  if (inCat) {
    const c = state.catMap.get(state.catId);
    $('crumbName').textContent = c?.name || '';
    $('crumbDot').style.background = c?.color || '';
  }
  // the folded panel's one button for both: its icon shows the view that's on, as the sound button does
  const cats = state.view !== 'all', vt = $('viewToggle');
  vt.innerHTML = `<svg><use href="#i-${cats ? 'grid' : 'cards'}"/></svg>`;
  vt.title = !cats ? 'Showing all notes (click to show categories)'
    : inCat ? `Showing “${state.catMap.get(state.catId)?.name || ''}” (click to show all notes)`
    : 'Showing categories (click to show all notes)';
  vt.setAttribute('aria-label', vt.title);
  $('searchClear').hidden = !state.query;
  $('searchBox').classList.toggle('active', !!state.query);
  $('searchInfo').innerHTML = state.query && state.matches
    ? (state.matches.size ? `<strong>${plural(state.matches.size, 'note')}</strong> found` : 'Nothing found')
    : '';
  updatePosition(spinner.index());
  updateEmpty();
}

function updatePosition(i) {
  const n = state.items.length;
  const what = state.view === 'categories' ? 'category' : 'note';
  $('posInfo').textContent = n ? `${i + 1} of ${what === 'category' ? plural(n, 'category', 'categories') : plural(n, 'note')}` : '';
}

function updateEmpty() {
  const box = $('empty'), btn = $('emptyBtn');
  let text = '', action = null;
  if (!state.items.length) {
    if (state.query) { text = `Nothing matches “${state.query}”.`; action = ['Clear search', clearSearch]; }
    else if (state.view === 'category') { text = `No notes in ${state.catMap.get(state.catId)?.name || 'this category'} yet.`; action = ['New note', newNote]; }
    else { text = 'No notes yet.'; action = ['Write your first note', newNote]; }
  }
  box.hidden = !text;
  $('emptyText').textContent = text;
  btn.hidden = !action;
  if (action) { btn.textContent = action[0]; btn.onclick = action[1]; }
}

function setView(v) {
  if (v === 'categories' && state.view === 'category') { leaveCategory(); return; }
  if (v === state.view) return;
  const keep = state.view === 'all' && v === 'categories' ? spinner.current() : null;
  state.view = v;
  state.catId = null;
  NS.prefSet('view', v);
  // from a note to the categories: start at that note's category
  const cat = keep ? state.notes.find(n => n.id === keep.id)?.cat : null;
  rebuild(cat ?? state.items[0]?.id ?? null);
  if (!cat) spinner.goTo(0, true);
}

function setOrder(o) {
  if (o === state.order) return;
  const cur = spinner.current()?.id;
  state.order = o;
  NS.prefSet('order', o);
  rebuild(cur);
}

function enterCategory(id) {
  state.view = 'category';
  state.catId = id;
  rebuild(null);
  spinner.goTo(0, true);
  updatePosition(spinner.index());
}

function leaveCategory() {
  const id = state.catId;
  state.view = 'categories';
  state.catId = null;
  rebuild(id);
}

function setCollapsed(on, remember = true) {
  document.body.classList.toggle('collapsed', on);
  $('collapseBtn').innerHTML = `<svg><use href="#i-${on ? 'expand' : 'collapse'}"/></svg>`;
  $('collapseBtn').title = on ? 'Show the panel' : 'Hide the panel';
  if (remember) NS.prefSet('collapsed', on ? '1' : '0');
  if (frameHidden()) reportFrame();   // the part of the window shown starts where the panel does
}

// Narrow windows fold the panel away, and unfold it again when there's room. Not with the window frame hidden:
// the panel has room of its own there, beside the cards.
let panelFoldedForRoom = false;
function fitPanel() {
  const narrow = !frameHidden() && innerWidth < 760;
  if (narrow && !document.body.classList.contains('collapsed')) { panelFoldedForRoom = true; setCollapsed(true, false); }
  else if (!narrow && panelFoldedForRoom) { panelFoldedForRoom = false; setCollapsed(NS.prefGet('collapsed') === '1', false); }
}

function setSound(on) {
  spinner.setSound(on);
  $('soundBtn').innerHTML = `<svg><use href="#i-${on ? 'sound' : 'mute'}"/></svg>`;
  $('soundBtn').classList.toggle('off', !on);
  $('soundBtn').title = on ? 'Sound on (click to turn off)' : 'Sound off (click to turn on)';
  NS.prefSet('sound', on ? '1' : '0');
}

/* ---------- see-through background (Settings) ---------- */

// how much of the dark background stays over the desktop, in %
const SEE_LEVEL = {min: 10, max: 95, start: 80};
const seeThroughOn = () => NS.prefGet('seeThrough') === '1';
function seeThroughLevel() {
  const v = parseInt(NS.prefGet('seeThroughLevel'), 10);
  return Number.isFinite(v) ? Math.min(SEE_LEVEL.max, Math.max(SEE_LEVEL.min, v)) : SEE_LEVEL.start;
}

// The space around the cards: see-through (what's behind the window shows under `level` % of the dark
// background) or solid. Windows goes see-through before the page does, and solid after it, so the page
// is never transparent over nothing. Whether it's see-through now. shadows: the stand's shadows stay.
async function setSeeThrough(on, level = seeThroughLevel(), {shadows = true} = {}) {
  const ok = on && await NS.call('SeeThroughSet', true).catch(() => false) === true;
  document.body.classList.toggle('see-through', ok);
  spinner.setBackground(ok ? level / 100 : 1, {shadows});
  if (!ok) await NS.call('SeeThroughSet', false).catch(() => {});
  return ok;
}

// The space around the cards: clear with the window frame hidden, else as Settings → Background has it.
// Clear, the stand's shadow on the desktop goes too: the window's edge, close around the cards, would cut it off.
function applyBackground() {
  return frameHidden() ? setSeeThrough(true, 0, {shadows: false}) : setSeeThrough(seeThroughOn());
}

/* ---------- the spinner base hidden (Settings) ---------- */

const baseHiddenOn = () => NS.prefGet('hideBase') === '1';

// Only the cards show, framed on their own, so the window can be narrower; there's no view to straighten
function setBaseHidden(on) {
  spinner.setBaseHidden(on);
  $('viewBtn').hidden = on;
  NS.call('BaseHiddenSet', on).catch(() => {});
}

/* ---------- the window frame hidden (Settings) ----------
   No title bar or border, and clear around the cards, so they seem to stand on the desktop. The panel and a
   title strip above it and the cards (the toolbar) come and go: with them hidden, a small button by the cards
   brings them back, or moves the window when it's dragged. The page keeps room for the toolbar to the left of
   the cards and above them (the CSS for body.frameless); the app shows only the part in use (see MainForm). */

let frameCapable = false;   // whether the host supports hiding the window frame
const frameHidden = () => document.body.classList.contains('frameless');
const barOut = () => document.body.classList.contains('bar-out');

// Where the page's parts are, in CSS pixels: the cards' box, and the part of the window shown (with the
// toolbar out or not)
function frameLayout(out = barOut()) {
  const cs = getComputedStyle(document.body), px = name => parseFloat(cs.getPropertyValue(name)) || 0;
  const left = px('--pwmax'), top = px('--strip');
  return {left, top, shownLeft: out ? left - px('--pw') : left, shownTop: out ? 0 : top};
}

// Tells the app where the parts are now (fitWidth: make the cards' box this narrow, if it's wider)
function reportFrame(fitWidth = 0) {
  const l = frameLayout();
  return NS.call('FrameSet', frameHidden(), l.left, l.top, l.shownLeft, l.shownTop, fitWidth).catch(() => false);
}

// A wide box has room to spare at the sides of the cards: the window gives it up (the cards stay the same size)
function fitFrameToCards() {
  // once the spinner has been fitted to its new size
  setTimeout(() => requestAnimationFrame(() => {
    const w = spinner.snugWidth();
    if (frameHidden() && w < $('stage').clientWidth - 4) reportFrame(w);
  }), 120);
}

async function setFrameHidden(on) {
  if (on === frameHidden()) return;
  if (on) {
    document.body.classList.add('frameless', 'bar-out');
    NS.prefSet('frameBar', '1');
    await applyBackground();
    fitPanel();   // a panel folded for room unfolds: it has room of its own now
    await reportFrame();
    fitFrameToCards();
  } else {
    // the frame goes back around the panel and the cards, where they are with the toolbar out
    const l = frameLayout(true);
    await NS.call('FrameSet', false, l.left, l.top, l.shownLeft, l.shownTop, 0).catch(() => {});
    document.body.classList.remove('frameless', 'bar-out');
    fitPanel();
    await applyBackground();
  }
}

function setBar(out) {
  if (out === barOut()) return;
  document.body.classList.toggle('bar-out', out);
  NS.prefSet('frameBar', out ? '1' : '0');
  reportFrame();
  if (!out) spinner.renderer.domElement.focus();
}

function wireFrame() {
  // the title strip moves the window; its buttons hide the toolbar, minimize and close
  $('titleStrip').addEventListener('pointerdown', e => {
    if (e.button !== 0 || e.target.closest('button')) return;
    e.preventDefault();
    NS.call('FrameDrag', e.pointerType === 'mouse').catch(() => {});
  });
  $('barHideBtn').addEventListener('click', () => setBar(false));
  $('minBtn').addEventListener('click', () => NS.call('FrameCommand', 'minimize').catch(() => {}));
  $('closeBtn').addEventListener('click', () => NS.call('FrameCommand', 'close').catch(() => {}));
  // the button by the cards: a click brings the toolbar out, a drag moves the window
  const show = $('barShowBtn');
  show.addEventListener('pointerdown', async e => {
    if (e.button !== 0) return;
    e.preventDefault();
    if (!isLinux) {
      const moved = await NS.call('FrameDrag', e.pointerType === 'mouse').catch(() => false);
      if (!moved) setBar(true);
      return;
    }
    // Linux/GTK: begin_move_drag is asynchronous, so tell a click from a drag using a small movement threshold
    const id = e.pointerId, x0 = e.clientX, y0 = e.clientY, mouse = e.pointerType === 'mouse';
    show.setPointerCapture(id);
    const cleanup = () => {
      show.removeEventListener('pointermove', move);
      show.removeEventListener('pointerup', up);
      show.removeEventListener('pointercancel', cancel);
      try { if (show.hasPointerCapture(id)) show.releasePointerCapture(id); } catch {}
    };
    const move = ev => {
      if (ev.pointerId !== id || Math.hypot(ev.clientX - x0, ev.clientY - y0) < 5) return;
      cleanup();
      NS.call('FrameDrag', mouse).catch(() => {});
    };
    const up = ev => {
      if (ev.pointerId !== id) return;
      cleanup();
      setBar(true);
    };
    const cancel = ev => { if (ev.pointerId === id) cleanup(); };
    show.addEventListener('pointermove', move);
    show.addEventListener('pointerup', up);
    show.addEventListener('pointercancel', cancel);
  });
  show.addEventListener('click', e => { if (e.detail === 0) setBar(true); });   // the keyboard
  // the edges resize the window
  $('frameEdges').addEventListener('pointerdown', e => {
    const edge = e.target.dataset.edge;
    if (e.button !== 0 || !edge) return;
    e.preventDefault();
    NS.call('FrameSize', edge, e.pointerType === 'mouse').catch(() => {});
  });
}

/* ---------- search ---------- */

async function runSearch({rebuildAfter = true} = {}) {
  const q = $('searchInput').value.trim();
  const seq = ++searchSeq;
  state.query = q;
  if (!q) {
    state.matches = null;
    state.terms = [];
  } else {
    const terms = q.toLocaleLowerCase().split(/\s+/).filter(Boolean);
    let ids;
    try { ids = await NS.call('NotesSearch', JSON.stringify(terms)); }
    catch (e) { toast('Search failed: ' + e.message, {kind: 'error'}); return; }
    if (seq !== searchSeq) return;   // a newer search has started
    const found = new Set(ids);
    // a category's name counts too
    const ql = q.toLocaleLowerCase();
    for (const c of state.cats) if (c.name.toLocaleLowerCase().includes(ql)) for (const n of state.notes) if (n.cat === c.id) found.add(n.id);
    state.terms = terms;
    state.matches = found;
  }
  if (rebuildAfter) {
    state.items = buildItems();
    spinner.setItems(state.items, state.items[0]?.id ?? null, {titlesBehind: titlesBehind()});
    updatePanel();
  }
}

let searchTimer = 0;
function onSearchInput() {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(runSearch, 160);
  $('searchClear').hidden = !$('searchInput').value;
}

function clearSearch() {
  $('searchInput').value = '';
  const cur = spinner.current()?.id;
  state.query = '';
  state.matches = null;
  state.terms = [];
  rebuild(cur);
}

function focusSearch() {
  if (frameHidden() && !barOut()) setBar(true);
  if (document.body.classList.contains('collapsed')) setCollapsed(false, false);
  $('searchInput').focus();
  $('searchInput').select();
}

/* ---------- notes ---------- */

/**
 * Opens a note: over the cards, or in a window of its own when that's the setting. A note is open in one
 * place at a time: one that's out in its window already just comes to the front.
 * inWindow: open it in a window of its own whatever the setting. activate: the window takes the focus
 * (not when NoteSpin starts with notes out).
 */
async function openItem(item, {inWindow = false, activate = true} = {}) {
  if (!item || editor.isOpen) return;
  if (item.kind === 'category') { enterCategory(item.id); return; }
  if (isOut(item.id)) { NS.call('NoteWinFocus', item.id); return; }
  const from = spinner.frontRect();
  let note;
  try { note = await NS.call('NoteGet', item.id); }
  catch (e) { toast('Couldn\'t open the note: ' + e.message, {kind: 'error'}); return; }
  if (!note) { toast('That note isn\'t there any more.'); await loadNotes(); return; }
  const cat = state.catMap.get(note.cat);
  const stored = state.notes.find(n => n.id === note.id);
  const original = {
    title: note.title, body: note.body, locked: note.locked,
    preview: stored?.preview || '', search: note.locked ? note.title.toLocaleLowerCase() : null,
    category: cat && cat.id !== GENERAL_ID ? cat.name : '', categoryColor: cat?.color,
  };
  let html = note.body, session = null;
  if (note.locked) {
    let r;
    for (;;) {   // backing out of "Delete?" returns to the password
      r = await askPassword(note.title, pw => LockSession.open(note.body, pw));
      if (r !== 'delete') break;
      if (await deleteLockedNote(note, original)) return;
    }
    if (!r) return;
    html = r.text;
    session = r.session;
  }
  if (editor.isOpen) return;   // another note opened while the password was asked for
  if (isOut(note.id)) { NS.call('NoteWinFocus', note.id); return; }   // or it went out in the meantime
  if (inWindow || ownWindow()) {
    const {body, ...meta} = note;
    NS.call('NoteWinOpen', JSON.stringify({
      note: meta, categoryId: note.cat, html, key: session ? await session.export() : null, original, highlight: state.terms,
    }), activate);
    return;
  }
  editor.open(note, {html, session, from, highlight: state.terms, original});
}

function newNote() {
  if (editor.isOpen || dialogOpen()) return;
  const categoryId = state.view === 'category' ? state.catId : GENERAL_ID;
  if (ownWindow()) NS.call('NoteWinOpen', JSON.stringify({note: null, categoryId, html: ''}), true);
  else editor.open(null, {categoryId});
}

/* ---------- note windows: notes outside this window, each in a window of its own ---------- */

const ownWindow = () => NS.prefGet('noteWindow') === '1';

/** The note windows with a note in them: [{win, id}] (id null for a new note that isn't saved yet). */
function noteWindowState() {
  try { return NS.callSync('NoteWinState') || []; } catch { return []; }
}

const isOut = id => !!id && noteWindowState().some(w => w.id === id);

/** Before the notebook changes: every note in a note window closes, saved (they belong to this notebook). */
async function closeNoteWindow() {
  await Promise.all(noteWindowState().map(w => NS.closeNoteWindow(w.win)));
}

/** The open note moves to a window of its own (the button, or dragged out: drag carries on with the window). */
async function popOut({drag = false, grabX = 0, grabY = 0, pointerType = 'mouse'} = {}) {
  if (!editor.isOpen) return;
  const st = await editor.exportState();
  editor.detach();
  try { await NS.call('NoteWinPopOut', JSON.stringify(st), drag, grabX, grabY, pointerType); }
  catch (e) {
    await editor.openState(st);   // it stays here
    toast('The note window couldn\'t open: ' + e.message, {kind: 'error'});
    return;
  }
  await loadNotes(st.note?.id ?? spinner.current()?.id);
}

// Note windows save as you type: their cards here are brought up to date once the wheel is still
let refreshTimer = 0;
function refreshSoon() {
  clearTimeout(refreshTimer);
  const tick = () => {
    if (spinner.mode !== 'idle' || editor.isOpen) { refreshTimer = setTimeout(tick, 300); return; }
    loadNotes(spinner.current()?.id).catch(() => {});
  };
  refreshTimer = setTimeout(tick, 350);
}

async function noteWindowClosed(m) {
  // a note open here too: the cards catch up when it closes (they're behind it)
  if (editor.isOpen) refreshSoon();
  else {
    clearTimeout(refreshTimer);
    await loadNotes(spinner.current()?.id);
  }
  noteGone(m);
}

/**
 * A note in a note window comes back here: it opens over the cards, from its own card. A note open here
 * already closes first (saved).
 */
async function dockIn(st) {
  hideDropZone();
  if (editor.isOpen) await editor.close();
  await app.reloadCategories();
  await loadNotes(st.note?.id ?? spinner.current()?.id);
  // the wheel draws its cards on the next frames; the note grows out of its card once it's there
  await Promise.race([new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))), new Promise(r => setTimeout(r, 120))]);
  await editor.openState(st, {from: st.note ? spinner.frontRect() : null});
}

// While a note window is moved over this window, a drop zone shows over the front card: let go there
// and the note comes back. (The rest of the window is free, so a note can still sit over NoteSpin.)
// It shows over a note open here too, which then makes way.
function dropZoneRect() {
  const s = $('stage').getBoundingClientRect();
  const r = spinner.frontRect();
  if (r && r.width > 40) {
    const left = Math.max(s.left + 8, r.left - 18), top = Math.max(s.top + 8, r.top - 18);
    return {left, top, width: Math.min(s.right - 8, r.left + r.width + 18) - left, height: Math.min(s.bottom - 8, r.top + r.height + 18) - top};
  }
  const w = Math.min(320, s.width * 0.6), h = Math.min(380, s.height * 0.6);
  return {left: s.left + (s.width - w) / 2, top: s.top + (s.height - h) / 2, width: w, height: h};
}

function dockHover(x, y) {
  const z = $('dropZone');
  if (x == null || editor.closing || dialogOpen()) { hideDropZone(); return false; }
  if (z.hidden) {
    const r = dropZoneRect();
    Object.assign(z.style, {left: r.left + 'px', top: r.top + 'px', width: r.width + 'px', height: r.height + 'px'});
    z.hidden = false;
  }
  const r = z.getBoundingClientRect();
  const hit = x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
  z.classList.toggle('on', hit);
  return hit;
}

function hideDropZone() {
  $('dropZone').hidden = true;
  $('dropZone').classList.remove('on');
}

// A locked note whose password is forgotten can still be deleted (with Undo, in case it was the wrong one).
// True when it was deleted.
async function deleteLockedNote(note, original) {
  const ok = await confirmDlg({
    title: `Delete “${note.title || 'Untitled'}”?`,
    message: 'It\'s locked, so it can\'t be opened without its password. Deleting it removes it from this notebook.',
    ok: 'Delete note', danger: true,
  });
  if (!ok) return false;
  try { await NS.call('NoteDelete', note.id); }
  catch (e) { toast('Couldn\'t delete the note: ' + e.message, {kind: 'error'}); return true; }
  await loadNotes();
  toast('Locked note deleted.', {action: {label: 'Undo', fn: () => restoreNote(original)}, timeout: 8000});
  return true;
}

// Puts back a note that was deleted (still locked if it was locked: its stored text is put back as it was)
async function restoreNote(o) {
  try {
    const search = o.search ?? analyze(parseHtml(o.body)).search;
    const r = await NS.call('NoteSave', JSON.stringify({...o, id: '', search}));
    await loadNotes(r.id);
  } catch (e) { toast('Couldn\'t put the note back: ' + e.message, {kind: 'error'}); }
}

/* ---------- notebooks and backups ---------- */

function notebookMenu() {
  const name = state.nb.name;
  menu($('nbBtn'), [
    {heading: 'Notebooks'},
    ...state.notebooks.map(nb => ({label: nb.name, checked: nb.current, onClick: () => { if (!nb.current) switchNotebook(nb.path); }})),
    '-',
    {label: 'New notebook…', onClick: newNotebook},
    {label: `Rename “${name}”…`, onClick: renameNotebook},
    {label: `Delete “${name}”…`, danger: true, onClick: deleteNotebook},
    '-',
    {label: 'Export a backup…', hint: '.notespin', onClick: exportBackup},
    {label: 'Import a backup…', onClick: importBackup},
    ...(isLinux ? [] : [{label: 'Import from Sticky Notes…', onClick: importStickyNotes}]),
    '-',
    {label: 'Open the notebooks folder', onClick: () => NS.call('ShowFolder', 'notebooks')},
  ]);
}

async function afterNotebookChange(st) {
  applyNotebook(st);
  state.catId = null;
  if (state.view === 'category') state.view = 'categories';
  $('searchInput').value = '';
  state.query = '';
  state.matches = null;
  state.terms = [];
  await loadNotes();
  spinner.goTo(0, true);
}

async function switchNotebook(path) {
  try {
    await closeNoteWindow();
    await afterNotebookChange(await NS.call('NbOpen', path));
  } catch (e) { alertDlg('Couldn\'t open the notebook', e.message); }
}

async function newNotebook() {
  const name = await promptDlg({title: 'New notebook', label: 'Name', placeholder: 'e.g. Work', ok: 'Create'});
  if (!name) return;
  try {
    await closeNoteWindow();
    await afterNotebookChange(await NS.call('NbCreate', name));
    toast(`Created “${state.nb.name}”.`);
  } catch (e) { alertDlg('Couldn\'t create the notebook', e.message); }
}

async function renameNotebook() {
  const name = await promptDlg({title: 'Rename notebook', label: 'Name', value: state.nb.name, ok: 'Rename'});
  if (!name || name === state.nb.name) return;
  try { applyNotebook(await NS.call('NbRename', name)); }
  catch (e) { alertDlg('Couldn\'t rename the notebook', e.message); }
}

async function deleteNotebook() {
  const nb = state.nb;
  const ok = await confirmDlg({
    title: `Delete “${nb.name}”?`,
    message: `The notebook and its ${plural(nb.notes, 'note')} go to the Recycle Bin. Export a backup first if you might want them again.`,
    ok: 'Delete notebook', danger: true,
  });
  if (!ok) return;
  try {
    await closeNoteWindow();
    await afterNotebookChange(await NS.call('NbDelete', nb.path));
    toast(`“${nb.name}” is in the Recycle Bin.`);
  } catch (e) { alertDlg('Couldn\'t delete the notebook', e.message); }
}

async function exportBackup() {
  try {
    const p = await NS.exportDialog();
    if (p) toast(`Backup saved: ${p.split('\\').pop()}`);
  } catch (e) { alertDlg('The backup wasn\'t saved', e.message); }
}

async function importBackup() {
  const p = await NS.importDialog();
  if (p) await importFile(p);
}

/** A backup chosen, dropped on the window or double-clicked: add it as a notebook, or replace the open one. */
async function importFile(path) {
  if (editor.isOpen) await editor.close();
  let info;
  try { info = await NS.call('NbInspect', path); }
  catch (e) { alertDlg('This file can\'t be imported', e.message); return; }
  if (info.isCurrent) { toast(`“${info.name}” is the notebook that's open.`); return; }
  if (info.inLibrary) { await switchNotebook(info.path); return; }
  const cur = state.nb.name;
  const when = new Date(info.modified).toLocaleString(undefined, {dateStyle: 'medium', timeStyle: 'short'});
  const choice = await dialog({
    title: 'Import a backup',
    wide: true,
    html: `<p class="lead">“${esc(info.name)}”</p>
      <p>${plural(info.notes, 'note')} in ${plural(info.categories, 'category', 'categories')}${info.locked ? `, ${info.locked} locked` : ''}. Saved ${esc(when)}.</p>
      <p class="hint">Add it as a notebook of its own, or replace the notes in “${esc(cur)}” with it.
      Before anything is replaced, a copy of “${esc(cur)}” as it is now goes to Safety Backups.</p>`,
    buttons: [
      {label: 'Cancel', value: null, left: true},
      {label: `Replace “${cur}”`, value: 'replace', danger: true},
      {label: 'Add as a new notebook', value: 'add', primary: true},
    ],
  });
  if (choice === 'add') {
    try {
      await closeNoteWindow();
      await afterNotebookChange(await NS.call('NbImportAsNew', path));
      toast(`Added “${state.nb.name}” to your notebooks.`);
    } catch (e) { alertDlg('The backup wasn\'t imported', e.message); }
  } else if (choice === 'replace') {
    const ok = await confirmDlg({
      title: `Replace “${cur}”?`,
      message: `Its ${plural(state.nb.notes, 'note')} will be replaced by the ${plural(info.notes, 'note')} in the backup. A copy of “${cur}” as it is now is kept in Safety Backups.`,
      ok: 'Replace', danger: true,
    });
    if (!ok) return;
    try {
      await closeNoteWindow();
      const st = await NS.call('NbReplaceCurrent', path);
      await afterNotebookChange(st);
      toast(`“${cur}” now holds the backup's notes.`, {action: {label: 'Show the safety copy', fn: () => NS.call('ShowFolder', 'safety')}, timeout: 9000});
    } catch (e) { alertDlg('The backup wasn\'t restored', e.message); }
  }
}

/* ---------- copying notes from Microsoft Sticky Notes ----------
   A one-off copy: the notes picked become new notes in this notebook, with the dates they had there.
   Sticky Notes isn't changed (the app reads a copy of its files, see src\StickyNotes.cs). Only the text
   comes across. Its colours become categories: yellow, the colour its notes start with, is General (yellow
   here too); the others get a category named after the colour, made if there isn't one. Notes copied
   before are marked, and start unticked, so running it again doesn't make doubles by accident. */

const STICKY_COLORS = {Green: '#CDEFC0', Pink: '#FFCFE2', Blue: '#CBE4FF', Purple: '#E2D4FA', Gray: '#E4E6EA', Charcoal: '#D3D6DC', White: '#F4F4F1'};

function stickyCategory(theme) {
  const name = String(theme || '').trim();
  const general = state.catMap.get(GENERAL_ID);
  if (!name || (/^yellow$/i.test(name) && !app.categoryByName(name))) return {name: '', label: general?.name || 'General', color: general?.color};
  const have = app.categoryByName(name);
  const label = have?.name || name.charAt(0).toUpperCase() + name.slice(1);
  return {name: label, label, color: have?.color || STICKY_COLORS[label] || pickColor(state.cats)};
}

async function importStickyNotes() {
  if (editor.isOpen) await editor.close();
  let r;
  try { r = await NS.call('StickyNotesRead'); }
  catch (e) { alertDlg('Couldn\'t read Sticky Notes', e.message); return; }
  if (!r.found) {
    alertDlg('Sticky Notes isn\'t on this PC', 'Microsoft Sticky Notes hasn\'t kept any notes on this PC, so there\'s nothing to copy.');
    return;
  }
  const notes = r.notes.map(n => {
    const text = n.lines.filter(l => l.trim());
    return {...n, title: text[0]?.trim() || 'Untitled', prev: text.slice(1, 3).join(' · '), cat: stickyCategory(n.theme)};
  });
  if (!notes.length) { alertDlg('No notes in Sticky Notes', 'Sticky Notes has no notes with text in them.'); return; }

  const wrap = document.createElement('div');
  wrap.innerHTML = `<p class="hint">Tick the notes to copy into “${esc(state.nb.name)}”. Each becomes a new note here, with the
    dates it had; Sticky Notes itself isn't changed. Only the text comes across (no pictures or handwriting).
    Its colours become categories.</p>
    <div class="sn-tools"><button class="btn" type="button" data-sel="all">Select all</button>
      <button class="btn" type="button" data-sel="none">Select none</button><span class="hint sn-count"></span></div>
    <div class="sn-list">${notes.map((n, i) => `
      <label class="sn-row"><input type="checkbox" data-i="${i}"${n.copied ? '' : ' checked'}>
        <span class="swatch" style="background:${esc(n.cat.color || '')}" title="${esc(n.theme || 'No colour')} in Sticky Notes: ${esc(n.cat.label)} here"></span>
        <span class="sn-text"><span class="sn-title">${esc(n.title)}</span><span class="sn-prev">${esc(n.prev) || '&nbsp;'}</span></span>
        <span class="sn-side"><span>${esc(shortDate(n.updated))}</span>${n.copied ? '<span class="sn-done">Copied before</span>' : ''}</span>
      </label>`).join('')}</div>`;
  const boxes = [...wrap.querySelectorAll('input[data-i]')];
  const picked = () => boxes.filter(b => b.checked).map(b => notes[+b.dataset.i]);

  const count = await dialog({
    title: 'Import from Sticky Notes',
    wide: true,
    node: wrap,
    buttons: [{label: 'Cancel', value: null, left: true}, {label: 'Copy', value: 'copy', primary: true}],
    init: box => {
      const go = box.querySelector('.dlg-buttons .btn.primary');
      const update = () => {
        const n = picked().length;
        wrap.querySelector('.sn-count').textContent = `${n} of ${notes.length} ticked`;
        go.textContent = n ? `Copy ${plural(n, 'note')}` : 'Copy';
        go.disabled = !n;
      };
      boxes.forEach(b => b.addEventListener('change', update));
      wrap.querySelectorAll('[data-sel]').forEach(b => b.addEventListener('click', () => {
        boxes.forEach(x => { x.checked = b.dataset.sel === 'all'; });
        update();
      }));
      update();
    },
    submit: async () => {
      const list = picked();
      if (!list.length) return false;
      const out = [];
      for (const n of list) {
        const doc = analyze(textToBlocks(n.lines.join('\n')));
        if (doc.empty) continue;
        out.push({
          title: doc.title, body: doc.html, preview: JSON.stringify(doc.lines), search: doc.search,
          category: n.cat.name, categoryColor: n.cat.color, created: n.created, updated: n.updated, source: 'sticky:' + n.sid,
        });
      }
      try { return await NS.call('StickyNotesCopy', JSON.stringify(out)); }
      catch (e) { alertDlg('The notes weren\'t copied', e.message); return false; }
    },
  });
  if (!count) return;
  await loadNotes(spinner.current()?.id);
  NS.call('ToNote', JSON.stringify({type: 'categoriesChanged'})).catch(() => {});   // new categories show in note windows
  toast(`Copied ${plural(count, 'note')} from Sticky Notes.`);
}

/* ---------- notes from text files dropped on the window ----------
   Each .txt file becomes a new note in General, after asking: the first line is its title, each line a
   paragraph (spaces and tabs kept). A file over TEXT_MAX, empty, or not plain text is left out, saying why. */

const TEXT_MAX = 1024 * 1024;

// A text file's bytes as text: UTF-8 or UTF-16 (with or without the mark Notepad starts them with), else the
// PC's own "ANSI" code page (older Notepad files)
function decodeText(buf, ansi) {
  const b = new Uint8Array(buf);
  const dec = (enc, from = 0, fatal = false) => new TextDecoder(enc, {fatal}).decode(b.subarray(from));
  if (b[0] === 0xEF && b[1] === 0xBB && b[2] === 0xBF) return dec('utf-8', 3);
  if (b[0] === 0xFF && b[1] === 0xFE) return dec('utf-16le', 2);
  if (b[0] === 0xFE && b[1] === 0xFF) return dec('utf-16be', 2);
  // UTF-16 without the mark: in mostly-English text every other byte is 0
  const n = Math.min(b.length, 400);
  let odd = 0, even = 0;
  for (let i = 0; i < n; i++) if (!b[i]) i % 2 ? odd++ : even++;
  if (n >= 4 && odd > n / 4 && even < n / 40) return dec('utf-16le');
  if (n >= 4 && even > n / 4 && odd < n / 40) return dec('utf-16be');
  try { return dec('utf-8', 0, true); } catch { /* not UTF-8 */ }
  try { return dec(ansi || 'windows-1252'); } catch { return dec('windows-1252'); }
}

const mb = bytes => (bytes / 1024 / 1024).toFixed(1).replace(/\.0$/, '') + ' MB';

// What each file would make: {name, doc, lines} or {name, skip: why}
async function readTextFile(f, ansi) {
  if (f.size > TEXT_MAX) return {name: f.name, skip: `too big (${mb(f.size)}; up to ${mb(TEXT_MAX)})`};
  let text;
  try { text = decodeText(await f.arrayBuffer(), ansi); }
  catch { return {name: f.name, skip: 'couldn\'t be read'}; }
  if (text.includes('\0')) return {name: f.name, skip: 'isn\'t plain text'};
  // blank lines at the start and end go; the first line is the title
  text = text.replace(/\r\n?/g, '\n').replace(/^(?:[ \t]*\n)+/, '').replace(/(?:\n[ \t]*)+$/, '');
  if (!text.trim()) return {name: f.name, skip: 'empty'};
  const doc = analyze(textToBlocks(text));
  return {name: f.name, doc, lines: text.split('\n').length};
}

async function notesFromTextFiles(files) {
  if (editor.isOpen || dialogOpen()) return;
  const ansi = await NS.call('AppInfo').then(i => i.ansi).catch(() => null);
  const read = [];
  for (const f of files) read.push(await readTextFile(f, ansi));
  const ok = read.filter(r => !r.skip), skipped = read.filter(r => r.skip);
  const skipText = skipped.map(r => `“${r.name}”: ${r.skip}`).join('; ');
  if (!ok.length) {
    alertDlg(files.length === 1 ? 'No note made from this file' : 'No notes made from these files', skipText.charAt(0).toUpperCase() + skipText.slice(1) + '.');
    return;
  }
  const general = state.catMap.get(GENERAL_ID)?.name || 'General';
  const where = ok.length === 1 ? `It goes in ${general}; you can change its category in the note.`
    : `They go in ${general}; you can change a note's category in the note.`;
  const go = await dialog({
    title: ok.length === 1 ? `Create a note from “${ok[0].name}”?` : `Create ${ok.length} notes from these files?`,
    wide: true,
    html: (ok.length === 1
      ? `<p class="lead">${esc(ok[0].doc.title)}</p>
        <p class="hint">${plural(ok[0].lines, 'line')}. The first line is the note's title. ${esc(where)}</p>`
      : `<ul class="txt-list">${ok.map(r => `<li><strong>${esc(r.name)}</strong><span>${esc(r.doc.title)}</span></li>`).join('')}</ul>
        <p class="hint">Each file becomes a note, its first line the title. ${esc(where)}</p>`) +
      (skipped.length ? `<p class="hint">Left out: ${esc(skipText)}.</p>` : ''),
    buttons: [{label: 'Cancel', value: null, left: true}, {label: ok.length === 1 ? 'Create note' : `Create ${ok.length} notes`, value: true, primary: true}],
  });
  if (!go) return;
  const ids = [];
  try {
    for (const r of ok) {
      const res = await NS.call('NoteSave', JSON.stringify({
        id: '', title: r.doc.title, body: r.doc.html, preview: JSON.stringify(r.doc.lines), search: r.doc.search,
        locked: false, category: '', categoryColor: undefined,
      }));
      ids.push(res.id);
    }
  } catch (e) {
    alertDlg('The note wasn\'t made', e.message);
  }
  if (!ids.length) return;
  await loadNotes(ids[0]);
  if (ids.length === 1 && ok.length === 1) await openItem({id: ids[0], kind: 'note'});
  else toast(`Created ${plural(ids.length, 'note')} in ${general}.`);
}

/* ---------- categories ---------- */

function pickPaletteColor(anchor, current) {
  return new Promise(resolve => {
    const pop = document.createElement('div');
    pop.className = 'palette';
    pop.innerHTML = PALETTE.map(p => `<button type="button" data-hex="${p.hex}" title="${p.name}" style="background:${p.hex}" class="${p.hex === current ? 'on' : ''}"></button>`).join('');
    document.body.append(pop);
    const r = anchor.getBoundingClientRect();
    pop.style.left = Math.min(r.left, innerWidth - pop.offsetWidth - 8) + 'px';
    pop.style.top = (r.bottom + 6 + pop.offsetHeight > innerHeight ? r.top - pop.offsetHeight - 6 : r.bottom + 6) + 'px';
    const done = v => { pop.remove(); document.removeEventListener('pointerdown', away, true); document.removeEventListener('keydown', key, true); resolve(v); };
    const away = e => { if (!pop.contains(e.target)) done(null); };
    const key = e => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); done(null); } };
    pop.addEventListener('click', e => { const b = e.target.closest('button'); if (b) done(b.dataset.hex); });
    setTimeout(() => document.addEventListener('pointerdown', away, true));
    document.addEventListener('keydown', key, true);
    pop.querySelector('button.on, button')?.focus();
  });
}

async function manageCategories() {
  const wrap = document.createElement('div');
  const counts = () => {
    const m = new Map();
    for (const n of state.notes) m.set(n.cat, (m.get(n.cat) || 0) + 1);
    return m;
  };
  const changed = async () => {
    await loadNotes(spinner.current()?.id);
    render();
    NS.call('ToNote', JSON.stringify({type: 'categoriesChanged'})).catch(() => {});   // a note in the note window shows the change
  };
  const render = () => {
    const cnt = counts();
    const cats = [...state.cats].sort((a, b) => (b.id === GENERAL_ID) - (a.id === GENERAL_ID) || collator.compare(a.name, b.name));
    wrap.innerHTML = `<p class="hint">Every note belongs to a category, and its colour shows on the card. General is used when a note's category is left blank.</p>
      <div class="cat-list">${cats.map(c => `
        <div class="cat-row" data-id="${c.id}">
          <button class="swatch" type="button" style="background:${c.color}" title="Change the colour"></button>
          <input type="text" maxlength="60" value="${esc(c.name)}" ${c.id === GENERAL_ID ? 'disabled title="General can\'t be renamed"' : ''} spellcheck="false">
          <span class="count">${plural(cnt.get(c.id) || 0, 'note')}</span>
          <button class="icon-btn small" type="button" title="Delete category" ${c.id === GENERAL_ID ? 'disabled' : ''}><svg><use href="#i-trash"/></svg></button>
        </div>`).join('')}</div>
      <div class="add-row"><input type="text" id="newCat" maxlength="60" placeholder="New category" spellcheck="false"><button class="btn" type="button" id="addCat">Add</button></div>`;

    wrap.querySelectorAll('.cat-row').forEach(row => {
      const c = state.catMap.get(row.dataset.id);
      const input = row.querySelector('input');
      row.querySelector('.swatch').addEventListener('click', async e => {
        const hex = await pickPaletteColor(e.currentTarget, c.color);
        if (!hex || hex === c.color) return;
        try { await NS.call('CategorySave', c.id, c.name, hex); await changed(); }
        catch (err) { alertDlg('Couldn\'t change the colour', err.message); }
      });
      const rename = async () => {
        const name = input.value.trim();
        if (!name || name === c.name) { input.value = c.name; return; }
        try { await NS.call('CategorySave', c.id, name, c.color); await changed(); }
        catch (err) { input.value = c.name; alertDlg('Couldn\'t rename the category', err.message); }
      };
      input.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); input.blur(); } });
      input.addEventListener('change', rename);
      row.querySelector('.icon-btn').addEventListener('click', async () => {
        const n = counts().get(c.id) || 0;
        const ok = await confirmDlg({
          title: `Delete “${c.name}”?`,
          message: n ? `Its ${plural(n, 'note')} move to General.` : 'It has no notes.',
          ok: 'Delete category', danger: true,
        });
        if (!ok) return;
        try { await NS.call('CategoryDelete', c.id); await changed(); }
        catch (err) { alertDlg('Couldn\'t delete the category', err.message); }
      });
    });
    const add = async () => {
      const input = wrap.querySelector('#newCat');
      const name = input.value.trim();
      if (!name) { input.focus(); return; }
      try { await NS.call('CategorySave', '', name, pickColor(state.cats)); await changed(); wrap.querySelector('#newCat')?.focus(); }
      catch (err) { alertDlg('Couldn\'t add the category', err.message); }
    };
    wrap.querySelector('#addCat').addEventListener('click', add);
    wrap.querySelector('#newCat').addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); add(); } });
  };
  render();
  await dialog({title: 'Categories', node: wrap, wide: true, buttons: [{label: 'Done', value: true, primary: true}], cancelValue: true});
}

/* ---------- settings ---------- */

// portable: the NoteSpin Data folder beside the executable, where everything is kept (null when it isn't portable)
let portableDir = null;
let isLinux = false;
const normDir = d => (d || '').replace(/[\\/]+$/, '');
const inFolder = (p, dir) => {
  if (!dir || !p) return false;
  const a = isLinux ? p : p.toLowerCase(), b = isLinux ? normDir(dir) : normDir(dir).toLowerCase();
  return a === b || a.startsWith(b + '/') || a.startsWith(b + '\\');
};

const folderNote = dir => inFolder(dir, portableDir)
  ? 'In NoteSpin\'s own folder, so the notebooks go wherever the NoteSpin folder goes. Keep backups somewhere else too: a USB drive is easy to lose.'
  : isLinux
  ? 'Stored in this folder on this PC (and wherever else you keep backups or sync this folder).'
  : inOneDrive(dir)
  ? 'In OneDrive, so your notebooks are backed up to the cloud. Use NoteSpin on one PC at a time: OneDrive can\'t merge a notebook changed in two places.'
  : 'Not in a OneDrive folder, so the notebooks are only on this PC (and wherever else you keep backups).';

/* ---------- portable: everything kept beside NoteSpin (Settings) ---------- */

// Turning it on or off restarts NoteSpin, which moves things over on the way (PortableSwitch in the C# side)
async function switchPortable(on, bring) {
  try {
    if (await NS.call('PortableSet', on, bring)) toast('NoteSpin is restarting…', {timeout: 20000});
  } catch (e) {
    alertDlg(on ? 'NoteSpin can\'t be portable here' : 'NoteSpin couldn\'t switch', e.message);
  }
}

async function makePortable(info) {
  const n = state.notebooks.length;
  const exeName = isLinux ? 'NoteSpin' : 'NoteSpin.exe';
  const runtimeHint = isLinux ? '' : ' The PC needs the WebView2 Runtime, which comes with Windows 11.';
  const choice = await dialog({
    title: 'Make NoteSpin portable?',
    wide: true,
    html: `<p>NoteSpin will keep its settings and notebooks in this folder, beside ${exeName}, and restart:</p>
      <p class="lead path">${esc(info.portableDir)}</p>
      <p>Copy your ${plural(n, 'notebook')} there? The ones on this PC stay as they are.</p>
      <p class="hint">To take NoteSpin and your notes to another PC, copy the whole NoteSpin folder to a USB drive and
      run it from there.${runtimeHint} Close NoteSpin before unplugging the drive.</p>`,
    buttons: [
      {label: 'Cancel', value: null, left: true},
      {label: 'Start empty', value: 'empty'},
      {label: n === 1 ? 'Copy my notebook' : 'Copy my notebooks', value: 'copy', primary: true},
    ],
  });
  if (choice) await switchPortable(true, choice === 'copy');
}

async function stopPortable(info) {
  const n = state.notebooks.length;
  const choice = await dialog({
    title: 'Keep everything on this PC again?',
    wide: true,
    html: `<p>NoteSpin will keep its settings and notebooks on this PC again, and restart.</p>
      <p>Copy the ${plural(n, 'notebook')} and settings NoteSpin is using now to this PC? The notebooks go to this
      PC's notebooks folder (normally ${esc(info.pcNotebooksDir)}).</p>
      <p class="hint">A notebook already on this PC is never replaced: one with the same name comes in as “Name (2)”.
      <strong>Leave them</strong> goes back to this PC's own notebooks and settings, as they were.
      Either way the ${esc(PORTABLE_NAME)} folder is renamed “${esc(PORTABLE_NAME)} (off)”, with everything still in
      it: rename it back to make NoteSpin portable again, or delete it once you don't need it.</p>`,
    buttons: [
      {label: 'Cancel', value: null, left: true},
      {label: 'Leave them', value: 'leave'},
      {label: 'Copy them to this PC', value: 'copy', primary: true},
    ],
  });
  if (choice) await switchPortable(false, choice === 'copy');
}

const PORTABLE_NAME = 'NoteSpin Data';

/**
 * Uses another folder for the notebooks, moving them there or switching to what's already there.
 * Resolves with the new folder, or null when nothing changed.
 */
async function changeNotebooksFolder() {
  const dir = await NS.folderDialog();
  if (!dir) return null;
  const sameDir = isLinux ? normDir(dir) === normDir(state.dir) : normDir(dir).toLowerCase() === normDir(state.dir).toLowerCase();
  if (sameDir) { toast('That\'s the folder already in use.'); return null; }
  const n = state.notebooks.length;
  const choice = await dialog({
    title: 'Use this folder for notebooks?',
    wide: true,
    html: `<p class="lead path">${esc(dir)}</p>
      <p>Move your ${plural(n, 'notebook')} there (with the safety backups), or switch to the notebooks already in that folder and leave yours where they are?</p>
      <p class="hint">${esc(folderNote(dir))}</p>`,
    buttons: [
      {label: 'Cancel', value: null, left: true},
      {label: 'Just switch', value: 'switch'},
      {label: n === 1 ? 'Move my notebook' : 'Move my notebooks', value: 'move', primary: true},
    ],
  });
  if (!choice) return null;
  try {
    await closeNoteWindow();
    await afterNotebookChange(await NS.call('NbSetFolder', dir, choice === 'move'));
    toast(choice === 'move' ? `Notebooks moved to ${dir}` : `Now using the notebooks in ${dir}`, {timeout: 6000});
  } catch (e) {
    // the folder may have changed even if some notebook couldn't move
    try { await afterNotebookChange(await NS.call('NbState')); } catch { }
    alertDlg('Not everything moved', e.message);
  }
  return state.dir;
}

async function showSettings() {
  let info;
  try { info = await NS.call('AppInfo'); } catch { info = {}; }
  const assocLabel = on => on ? 'Turn off' : 'Turn on';
  await dialog({
    title: 'Settings',
    wide: true,
    html: `
      <h3>Notebooks folder</h3>
      <p class="path" id="dirPath">${esc(state.dir || info.notebooksDir || '')}</p>
      <p class="hint" id="dirNote">${esc(folderNote(state.dir || info.notebooksDir))}</p>
      <div class="row-btns"><button class="btn" type="button" id="dirChange">Change…</button>
        <button class="btn" type="button" data-folder="notebooks">Open folder</button>
        <button class="btn" type="button" data-folder="safety">Safety Backups</button></div>
      <h3>Portable</h3>
      ${info.portable ? `<p>NoteSpin is portable: its settings and notebooks are kept in</p>
      <p class="path">${esc(info.portableDir)}</p>
      <p class="hint">Copy or move the whole NoteSpin folder, ${esc(PORTABLE_NAME)} and all, and your notes go with it:
      to a USB drive, say, to use NoteSpin on any PC.</p>
      <div class="row-btns"><button class="btn" type="button" id="portableBtn">Keep everything on this PC…</button></div>`
      : `<p class="hint">Keep NoteSpin's settings and notebooks in its own folder, beside ${isLinux ? 'NoteSpin' : 'NoteSpin.exe'}, so the whole
      NoteSpin folder can go on a USB drive and run on any PC, with your notes.</p>
      <div class="row-btns"><button class="btn" type="button" id="portableBtn">Make portable…</button></div>`}
      <h3>Notes</h3>
      <label class="check"><input type="checkbox" id="ownWindow"${ownWindow() ? ' checked' : ''}> Always open notes in their own window</label>
      <p class="hint">Otherwise a note opens over the cards. To give it a window of its own, drag it out of NoteSpin by its
      top strip, or click <svg class="inline-icon"><use href="#i-popout"/></svg>. Any number of notes can be out at once,
      each in its own window.</p>
      <h3>Spinner</h3>
      <label class="check"><input type="checkbox" id="hideBase"${baseHiddenOn() ? ' checked' : ''}> Hide the spinner base</label>
      <p class="hint">Only the cards show, without the frame, knobs and stand, so the window can be made narrower. The
      cards turn and fall as before; dragging sideways no longer turns the view.</p>
      ${frameCapable ? `<h3>Window</h3>
      <label class="check"><input type="checkbox" id="hideFrame"${frameHidden() ? ' checked' : ''}> Hide the window frame</label>
      <p class="hint">No title bar or border, and the space around the cards is clear, so they seem to stand on the
      desktop. <svg class="inline-icon"><use href="#i-toolbar"/></svg> on the strip at the top hides this panel and the
      strip too; the small button by the cards brings them back, or moves NoteSpin when you drag it. With them out,
      drag the strip to move NoteSpin and its edges to resize it.</p>` : ''}
      <h3>Background</h3>
      <label class="check"><input type="checkbox" id="seeThrough"${seeThroughOn() ? ' checked' : ''}> See-through background around the cards</label>
      <div class="range-row${seeThroughOn() ? '' : ' off'}" id="seeRow"><span>Opacity</span>
        <input type="range" id="seeLevel" min="${SEE_LEVEL.min}" max="${SEE_LEVEL.max}" step="5" value="${seeThroughLevel()}"${seeThroughOn() ? '' : ' disabled'} aria-label="Opacity of the background">
        <output id="seeLevelText">${seeThroughLevel()}%</output></div>
      <p class="hint" id="seeHint"></p>
      ${isLinux ? '' : `<h3>Microsoft Sticky Notes</h3>
      <p class="hint">Copy notes from the Sticky Notes app into this notebook, picking which ones. Sticky Notes isn't changed.</p>
      <div class="row-btns" style="margin-bottom:4px"><button class="btn" type="button" id="stickyBtn">Import from Sticky Notes…</button></div>`}
      <h3>Opening backups</h3>
      <p>Open <strong>.notespin</strong> files with NoteSpin when they're double-clicked in ${isLinux ? 'your file manager' : 'File Explorer'}.</p>
      ${info.portable && !info.fileAssoc ? `<p class="hint">Not while NoteSpin is portable: a USB drive's ${isLinux ? 'mount path' : 'letter'}
      changes from PC to PC.</p>` : `<div class="row-btns"><button class="btn" type="button" id="assocBtn">${assocLabel(info.fileAssoc)}</button>
        <span class="hint" id="assocState" style="align-self:center">${info.fileAssoc ? 'On for this copy of NoteSpin.' : 'Off.'}</span></div>`}
      <h3>Keyboard</h3>
      <div class="keys">
        <kbd>↑ ↓</kbd><span>Previous / next card (also the mouse wheel, or drag up and down)</span>
        <kbd>Enter</kbd><span>Open the front card</span>
        <kbd>Ctrl+N</kbd><span>New note</span>
        <kbd>Ctrl+F</kbd><span>Search</span>
        <kbd>Esc</kbd><span>Close a note, clear the search, or go back to the categories</span>
        <kbd>Tab</kbd><span>In a list: move an item in (Shift+Tab: out)</span>
        <kbd>Ctrl+Enter</kbd><span>In a checklist: tick or untick</span>
      </div>
      <div class="about">NoteSpin ${esc(info.version || '')} · SQLite ${esc(info.sqlite || '')} · <a href="#" id="licLink">Licenses</a></div>`,
    buttons: [{label: 'Close', value: true, primary: true}],
    cancelValue: true,
    init: box => {
      box.querySelector('#portableBtn').addEventListener('click', () => info.portable ? stopPortable(info) : makePortable(info));
      box.querySelector('#assocBtn')?.addEventListener('click', async e => {
        try {
          const on = await NS.call('FileAssocSet', !info.fileAssoc);
          info.fileAssoc = on;
          e.target.textContent = assocLabel(on);
          box.querySelector('#assocState').textContent = on ? 'On for this copy of NoteSpin.' : 'Off.';
        } catch (err) { alertDlg('Couldn\'t change it', err.message); }
      });
      box.querySelector('#ownWindow').addEventListener('change', e => NS.prefSet('noteWindow', e.target.checked ? '1' : '0'));
      box.querySelector('#stickyBtn')?.addEventListener('click', importStickyNotes);
      box.querySelector('#hideBase').addEventListener('change', e => {
        setBaseHidden(e.target.checked);
        NS.prefSet('hideBase', e.target.checked ? '1' : '0');
        if (frameHidden()) fitFrameToCards();
      });
      const seeBox = box.querySelector('#seeThrough'), seeLevel = box.querySelector('#seeLevel'), seeRow = box.querySelector('#seeRow');
      // with the window frame hidden it's always clear: the setting waits for the frame to come back
      const seeState = () => {
        const fixed = frameHidden(), on = seeBox.checked && !fixed;
        seeBox.disabled = fixed;
        seeLevel.disabled = !on;
        seeRow.classList.toggle('off', !on);
        box.querySelector('#seeHint').textContent = fixed ? 'Always clear while the window frame is hidden.'
          : 'The desktop, and any window behind NoteSpin, shows through the space around the cards. The higher the ' +
            'opacity, the darker and more solid that space is. The panel, the cards and open notes stay solid.';
      };
      seeState();
      seeBox.addEventListener('change', async () => {
        const ok = await setSeeThrough(seeBox.checked, +seeLevel.value);
        if (seeBox.checked && !ok) { seeBox.checked = false; toast(isLinux ? "Your desktop didn't let the window go see-through." : "Windows didn't let the window go see-through."); }
        NS.prefSet('seeThrough', ok ? '1' : '0');
        seeState();
      });
      box.querySelector('#hideFrame')?.addEventListener('change', async e => {
        e.target.disabled = true;
        await setFrameHidden(e.target.checked);
        NS.prefSet('hideFrame', frameHidden() ? '1' : '0');
        e.target.checked = frameHidden();
        e.target.disabled = false;
        seeState();
      });
      seeLevel.addEventListener('input', () => {
        box.querySelector('#seeLevelText').textContent = seeLevel.value + '%';
        if (seeBox.checked) spinner.setBackground(+seeLevel.value / 100);
      });
      seeLevel.addEventListener('change', () => NS.prefSet('seeThroughLevel', seeLevel.value));
      // while the slider is held, the dialog's shade lifts, so the background shows as it will be
      const back = box.closest('.dlg-back'), peek = on => back?.classList.toggle('peek', on);
      seeLevel.addEventListener('pointerdown', () => peek(true));
      for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) seeLevel.addEventListener(ev, () => peek(false));
      box.querySelectorAll('[data-folder]').forEach(b => b.addEventListener('click', () => NS.call('ShowFolder', b.dataset.folder)));
      box.querySelector('#dirChange').addEventListener('click', async () => {
        const dir = await changeNotebooksFolder();
        if (!dir) return;
        box.querySelector('#dirPath').textContent = dir;
        box.querySelector('#dirNote').textContent = folderNote(dir);
      });
      box.querySelector('#licLink').addEventListener('click', e => { e.preventDefault(); NS.call('ShowLicenses'); });
    },
  });
}

/* ---------- the first notes in a new install ---------- */

const SAMPLES = [
  {cat: '', html: `<p>Welcome to NoteSpin</p><p>Your notes stand on a wheel of cards. The first line of a note is its title.</p>
    <ul><li>Turn the wheel with the mouse wheel, by dragging up or down, or with the arrow keys.</li>
    <li>Click the front card to open it. Click a card behind it to bring that one forward.</li>
    <li>Drag sideways to look at the wheel from another side.</li>
    <li>Notes save as you type. Esc closes a note.</li></ul>
    <p>Every note has a category, shown by its colour. Leave the category blank for General.</p>`},
  {cat: '', html: `<p>Backing up a notebook</p><ol><li>Click the notebook name at the top of the panel.</li>
    <li>Choose Export a backup… and keep the .notespin file somewhere safe.</li>
    <li>To bring it back, choose Import a backup… and add it as a notebook or replace this one.</li></ol>
    <p>Locked notes stay locked in a backup, and open on any PC with their password.</p>`},
  {cat: 'Home', html: `<p>Groceries</p><ul class="check"><li class="done">Milk</li><li>Bread</li><li>Apples</li><li>Coffee beans</li></ul>`},
  {cat: 'Personal', html: `<p>Weekend ideas</p><ul><li>Try the new bakery</li><li>Bike ride if it's sunny<ul><li>Pump up the tyres first</li></ul></li><li>Finish the jigsaw puzzle</li></ul>`},
];

async function addSamples() {
  if (NS.prefGet('seeded')) return;
  NS.prefSet('seeded', '1');
  if (state.notes.length) return;
  let welcome = null;
  for (const s of SAMPLES) {
    const doc = analyze(parseHtml(s.html.replace(/>\s+</g, '><')));
    const color = s.cat ? (app.categoryByName(s.cat)?.color || pickColor(state.cats)) : undefined;
    const r = await NS.call('NoteSave', JSON.stringify({
      id: '', title: doc.title, body: doc.html, preview: JSON.stringify(doc.lines), search: doc.search,
      locked: false, category: s.cat, categoryColor: color,
    }));
    if (r.newCategory) await app.reloadCategories();
    welcome ??= r.id;
  }
  await loadNotes(welcome);   // a new install starts at the welcome note
}

/* ---------- keyboard ---------- */

function onKey(e) {
  if (dialogOpen() || menuOpen() || editor.isOpen) return;
  const el = document.activeElement;
  const inSearch = el === $('searchInput');
  const typing = !inSearch && el && (/^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName) || el.isContentEditable);
  const k = e.key.toLowerCase();
  if (e.ctrlKey && !e.altKey && k === 'n') { e.preventDefault(); newNote(); return; }
  if (e.ctrlKey && !e.altKey && k === 'f') { e.preventDefault(); focusSearch(); return; }
  if (typing || e.ctrlKey || e.altKey || e.metaKey) return;
  const nav = {ArrowDown: 1, PageDown: 1, ArrowUp: -1, PageUp: -1};
  if (!inSearch) Object.assign(nav, {ArrowRight: 1, ArrowLeft: -1});
  if (nav[e.key]) { e.preventDefault(); spinner.step(nav[e.key]); return; }
  if (!inSearch && e.key === 'Home') { e.preventDefault(); spinner.goTo(0); return; }
  if (!inSearch && e.key === 'End') { e.preventDefault(); spinner.goTo(state.items.length - 1); return; }
  if (e.key === 'Enter' || (!inSearch && e.key === ' ')) { e.preventDefault(); openItem(spinner.current()); return; }
  if (e.key === 'Escape') {
    if (state.query) { e.preventDefault(); clearSearch(); if (inSearch) $('searchInput').blur(); }
    else if (state.view === 'category') leaveCategory();
    else if (inSearch) $('searchInput').blur();
    return;
  }
  if (!inSearch && e.key === 'Backspace' && state.view === 'category') { e.preventDefault(); leaveCategory(); }
}

/* ---------- start ---------- */

function wire() {
  $('collapseBtn').addEventListener('click', () => setCollapsed(!document.body.classList.contains('collapsed')));
  $('nbBtn').addEventListener('click', notebookMenu);
  $('newBtn').addEventListener('click', newNote);
  $('searchInput').addEventListener('input', onSearchInput);
  $('searchClear').addEventListener('click', () => { clearSearch(); $('searchInput').focus(); });
  $('searchBox').addEventListener('click', () => { if (document.body.classList.contains('collapsed')) focusSearch(); });
  $('viewSeg').addEventListener('click', e => { const b = e.target.closest('button'); if (b) setView(b.dataset.view); });
  $('viewToggle').addEventListener('click', () => setView(state.view === 'all' ? 'categories' : 'all'));
  $('orderSeg').addEventListener('click', e => { const b = e.target.closest('button'); if (b) setOrder(b.dataset.order); });
  $('crumbBack').addEventListener('click', leaveCategory);
  $('prevBtn').addEventListener('click', () => spinner.prev());
  $('nextBtn').addEventListener('click', () => spinner.next());
  $('catsBtn').addEventListener('click', manageCategories);
  $('soundBtn').addEventListener('click', () => setSound(!spinner.sound.on));
  $('viewBtn').addEventListener('click', () => spinner.resetView());
  $('settingsBtn').addEventListener('click', showSettings);
  document.addEventListener('keydown', onKey);
  // a .notespin file dropped on the window is offered as a backup to import
  NS.on('importFile', m => importFile(m.path));
  // Linux GTK drag-and-drop fallback (handles .notespin and .txt drops when WebKit doesn't expose HTML5 File objects)
  let lastDropAt = 0;
  async function handleDroppedPaths(paths) {
    if (!Array.isArray(paths) || !paths.length) return;
    if (Date.now() - lastDropAt < 400) return;
    lastDropAt = Date.now();
    if (editor.isOpen || dialogOpen()) return;
    const backup = paths.find(p => /\.notespin$/i.test(p));
    if (backup) { importFile(backup); return; }
    const texts = paths.filter(p => /\.txt$/i.test(p));
    if (!texts.length) {
      toast('Only NoteSpin backups (.notespin) and text files (.txt) can be dropped here.');
      return;
    }
    const files = [];
    for (const p of texts) {
      try {
        const d = await NS.call('ReadTextDrop', p);
        files.push({
          name: d.name,
          size: d.size,
          async arrayBuffer() {
            if (d.tooBig || !d.base64) return new ArrayBuffer(0);
            const bin = atob(d.base64);
            const u8 = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) u8[i] = bin.charCodeAt(i);
            return u8.buffer;
          },
        });
      } catch {
        const name = p.split(/[\\/]/).pop() || p;
        files.push({name, size: 0, async arrayBuffer() { throw new Error('unreadable'); }});
      }
    }
    notesFromTextFiles(files);
  }
  NS.on('droppedFiles', m => handleDroppedPaths(m.paths).catch(() => {}));
  // from the note window
  NS.on('noteSaved', refreshSoon);
  NS.on('categoriesChanged', refreshSoon);
  NS.on('noteClosed', m => noteWindowClosed(m).catch(() => {}));
  NS.on('dockIn', m => dockIn(m.state).catch(e => toast('Couldn\'t bring the note back: ' + e.message, {kind: 'error'})));
  window.__nsDockHover = dockHover;
  window.__nsDockDrop = (x, y) => { const hit = dockHover(x, y); hideDropZone(); return hit; };
  const hasFiles = e => {
    const t = [...(e.dataTransfer?.types || [])];
    return t.includes('Files') || (isLinux && t.includes('text/uri-list'));
  };
  addEventListener('dragover', e => { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = editor.isOpen ? 'none' : 'copy'; } });
  addEventListener('drop', async e => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    if (editor.isOpen || dialogOpen()) return;
    // a NoteSpin backup is offered for importing; text files become notes
    const files = [...e.dataTransfer.files];
    if (!files.length) return;   // on Linux WebKitGTK, GTK's droppedFiles handler takes over when dataTransfer.files is empty
    lastDropAt = Date.now();
    const backup = files.find(f => /\.notespin$/i.test(f.name)), texts = files.filter(f => /\.txt$/i.test(f.name));
    if (!backup) {
      if (texts.length) notesFromTextFiles(texts);
      else if (files.length) toast('Only NoteSpin backups (.notespin) and text files (.txt) can be dropped here.');
      return;
    }
    const p = await NS.filePath(backup).catch(() => null);
    if (p) importFile(p);
  });

  addEventListener('resize', fitPanel);
  fitPanel();
  wireFrame();
}

// After NoteSpin restarted to turn portable on or off (see PortableSwitch)
function switchedNotice(n) {
  const failed = n.failed?.length ? ` ${plural(n.failed.length, 'notebook')} couldn't be copied: ${n.failed.join(', ')}.` : '';
  if (n.switched === 'on') {
    toast(`NoteSpin is portable now: everything is kept in ${n.dir}.` +
      (n.copied ? ` ${plural(n.copied, 'notebook')} copied there.` : '') + failed, {timeout: 12000});
  } else {
    dialog({
      title: 'Everything is on this PC again',
      wide: true,
      html: (n.dir
        ? `<p>${n.copied ? `${plural(n.copied, 'notebook')} and NoteSpin's settings copied` : 'NoteSpin\'s settings copied'} to this PC. The notebooks are in</p>
          <p class="path">${esc(n.dir)}</p>`
        : '<p>NoteSpin is using this PC\'s own notebooks and settings again.</p>') +
        `<p>What NoteSpin kept in its own folder is still in</p><p class="path">${esc(n.off)}</p>
        <p class="hint">Rename it back to “${esc(PORTABLE_NAME)}” to make NoteSpin portable again, or delete it once
        you don't need it.${esc(failed)}</p>`,
      buttons: [{label: 'OK', value: true, primary: true}],
      cancelValue: true,
    });
  }
}

async function start() {
  if (!window.NS) {
    document.body.innerHTML = '<p style="padding:40px;color:#aaa;font:15px Segoe UI,sans-serif">NoteSpin\'s page only works inside the NoteSpin app.</p>';
    return;
  }
  state.view = NS.prefGet('view') === 'categories' ? 'categories' : 'all';
  state.order = NS.prefGet('order') === 'recent' ? 'recent' : 'az';
  spinner = new Spinner($('stage'), {onOpen: item => openItem(item), onChange: i => updatePosition(i)});
  spinner.renderer.domElement.tabIndex = 0;
  spinner.renderer.domElement.setAttribute('aria-label', 'Note cards: arrow keys turn, Enter opens');
  editor = new Editor(app);
  // the window frame hidden: the app started without it (and told the page so in its address)
  let frame = null;
  try { frame = NS.callSync('FrameInfo'); } catch { /* an app that can't hide it */ }
  frameCapable = !!frame;
  document.body.classList.toggle('frameless', !!frame?.hidden);
  document.body.classList.toggle('bar-out', !!frame?.hidden && NS.prefGet('frameBar') === '1');
  setCollapsed(NS.prefGet('collapsed') === '1', false);
  setSound(NS.prefGet('sound') !== '0');
  if (seeThroughOn() || frameHidden()) applyBackground();
  if (baseHiddenOn()) setBaseHidden(true);
  wire();
  try {
    await loadNotebook();
    await addSamples();
  } catch (e) {
    alertDlg('NoteSpin couldn\'t read the notebook', e.message);
  }
  NS.call('AppInfo').then(i => {
    portableDir = i.portable ? i.portableDir : null;
    isLinux = i.platform === 'linux';
  }).catch(() => {});
  // notebooks moved to the notebooks folder, or a chosen folder that isn't there; portable turned on or off
  const notice = await NS.call('TakeNotice').catch(() => null);
  if (notice?.switched) switchedNotice(notice);
  if (notice?.moved) {
    toast(`Your ${plural(notice.moved, 'notebook')} moved to ${notice.to}${inOneDrive(notice.to) ? ', where OneDrive backs them up' : ''}.`, {timeout: 10000});
  } else if (notice?.missing) {
    alertDlg('Notebooks folder not available',
      `“${notice.missing}” isn't there (an unplugged drive, perhaps), so NoteSpin is using ${notice.using} for now. It goes back to the chosen folder when it's available again.`);
  }
  window.__nsBusy = () => editor.busy();
  window.__ns = {state, spinner, editor, app, importFile, openItem, popOut, noteWindowState, closeNoteWindow};   // for testing
  const file = await NS.call('TakeStartupFile').catch(() => null);
  if (file) { importFile(file); return; }
  // the notes that were out in their windows when NoteSpin closed go out again
  const reopen = await NS.call('TakeNoteWindowNotes').catch(() => null) || [];
  for (const id of reopen) {
    if (state.notes.some(n => n.id === id && !n.locked)) await openItem({id, kind: 'note'}, {inWindow: true, activate: false});
  }
}

start();
