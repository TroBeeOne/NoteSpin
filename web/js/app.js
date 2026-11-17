// NoteSpin: the side panel, the views (all notes, categories, one category), search, notebooks and backups.

import {Spinner} from './spinner.js';
import {Editor, askPassword} from './editor.js';
import {LockSession} from './crypto.js';
import {PALETTE, GENERAL_ID, pickColor, analyze, parseHtml} from './notes.js';
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
    if (emptied) toast('The empty note was deleted.', {action: {label: 'Undo', fn: () => restoreNote(emptied)}, timeout: 7000});
    if (deleted) toast('Note deleted.');
    $('stage').querySelector('canvas')?.focus();
  },
};

/* ---------- loading ---------- */

function applyNotebook(st) {
  state.nb = st.current;
  state.notebooks = st.notebooks;
  state.dir = st.dir;
  state.defaultDir = st.defaultDir;
  $('nbName').textContent = st.current.name;
  $('nbBtn').title = `${st.current.name}\n${st.current.path}\n\nNotebooks and backups`;
  // the window's title bar: the notebook, and where its file is
  document.title = `${st.current.name} — ${st.current.path} — NoteSpin`;
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

function rebuild(focusId) {
  state.items = buildItems();
  spinner.setItems(state.items, focusId);
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
}

function setSound(on) {
  spinner.setSound(on);
  $('soundBtn').innerHTML = `<svg><use href="#i-${on ? 'sound' : 'mute'}"/></svg>`;
  $('soundBtn').classList.toggle('off', !on);
  $('soundBtn').title = on ? 'Sound on (click to turn off)' : 'Sound off (click to turn on)';
  NS.prefSet('sound', on ? '1' : '0');
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
    spinner.setItems(state.items, state.items[0]?.id ?? null);
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
  if (document.body.classList.contains('collapsed')) setCollapsed(false, false);
  $('searchInput').focus();
  $('searchInput').select();
}

/* ---------- notes ---------- */

async function openItem(item) {
  if (!item || editor.isOpen) return;
  if (item.kind === 'category') { enterCategory(item.id); return; }
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
  if (note.locked) {
    let r;
    for (;;) {   // backing out of "Delete?" returns to the password
      r = await askPassword(note.title, pw => LockSession.open(note.body, pw));
      if (r !== 'delete') break;
      if (await deleteLockedNote(note, original)) return;
    }
    if (!r) return;
    editor.open(note, {html: r.text, session: r.session, from, highlight: state.terms, original});
  } else {
    editor.open(note, {html: note.body, from, highlight: state.terms, original});
  }
}

function newNote() {
  if (editor.isOpen || dialogOpen()) return;
  editor.open(null, {categoryId: state.view === 'category' ? state.catId : GENERAL_ID});
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
  try { await afterNotebookChange(await NS.call('NbOpen', path)); }
  catch (e) { alertDlg('Couldn\'t open the notebook', e.message); }
}

async function newNotebook() {
  const name = await promptDlg({title: 'New notebook', label: 'Name', placeholder: 'e.g. Work', ok: 'Create'});
  if (!name) return;
  try {
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
      const st = await NS.call('NbReplaceCurrent', path);
      await afterNotebookChange(st);
      toast(`“${cur}” now holds the backup's notes.`, {action: {label: 'Show the safety copy', fn: () => NS.call('ShowFolder', 'safety')}, timeout: 9000});
    } catch (e) { alertDlg('The backup wasn\'t restored', e.message); }
  }
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
  const changed = async () => { await loadNotes(spinner.current()?.id); render(); };
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

const folderNote = dir => inOneDrive(dir)
  ? 'In OneDrive, so your notebooks are backed up to the cloud. Use NoteSpin on one PC at a time: OneDrive can\'t merge a notebook changed in two places.'
  : 'Not in a OneDrive folder, so the notebooks are only on this PC (and wherever else you keep backups).';

/**
 * Uses another folder for the notebooks, moving them there or switching to what's already there.
 * Resolves with the new folder, or null when nothing changed.
 */
async function changeNotebooksFolder() {
  const dir = await NS.folderDialog();
  if (!dir) return null;
  if (dir.toLowerCase() === (state.dir || '').toLowerCase()) { toast('That\'s the folder already in use.'); return null; }
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
      <h3>Opening backups</h3>
      <p>Open <strong>.notespin</strong> files with NoteSpin when they're double-clicked in File Explorer.</p>
      <div class="row-btns"><button class="btn" type="button" id="assocBtn">${assocLabel(info.fileAssoc)}</button>
        <span class="hint" id="assocState" style="align-self:center">${info.fileAssoc ? 'On for this copy of NoteSpin.' : 'Off.'}</span></div>
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
      box.querySelector('#assocBtn').addEventListener('click', async e => {
        try {
          const on = await NS.call('FileAssocSet', !info.fileAssoc);
          info.fileAssoc = on;
          e.target.textContent = assocLabel(on);
          box.querySelector('#assocState').textContent = on ? 'On for this copy of NoteSpin.' : 'Off.';
        } catch (err) { alertDlg('Couldn\'t change it', err.message); }
      });
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
  const hasFiles = e => [...(e.dataTransfer?.types || [])].includes('Files');
  addEventListener('dragover', e => { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = editor.isOpen ? 'none' : 'copy'; } });
  addEventListener('drop', async e => {
    if (!hasFiles(e)) return;
    e.preventDefault();
    if (editor.isOpen || dialogOpen()) return;
    const f = e.dataTransfer.files[0];
    const p = f ? await NS.filePath(f).catch(() => null) : null;
    if (!p) return;
    if (!/\.notespin$/i.test(p)) { toast('Only NoteSpin backups (.notespin files) can be dropped here.'); return; }
    importFile(p);
  });

  // narrow windows fold the panel away (and unfold it again when there's room)
  let auto = false;
  const fit = () => {
    const narrow = innerWidth < 760;
    if (narrow && !document.body.classList.contains('collapsed')) { auto = true; setCollapsed(true, false); }
    else if (!narrow && auto) { auto = false; setCollapsed(NS.prefGet('collapsed') === '1', false); }
  };
  addEventListener('resize', fit);
  fit();
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
  setCollapsed(NS.prefGet('collapsed') === '1', false);
  setSound(NS.prefGet('sound') !== '0');
  wire();
  try {
    await loadNotebook();
    await addSamples();
  } catch (e) {
    alertDlg('NoteSpin couldn\'t read the notebook', e.message);
  }
  // notebooks moved to the notebooks folder, or a chosen folder that isn't there
  const notice = await NS.call('TakeNotice').catch(() => null);
  if (notice?.moved) {
    toast(`Your ${plural(notice.moved, 'notebook')} moved to ${notice.to}${inOneDrive(notice.to) ? ', where OneDrive backs them up' : ''}.`, {timeout: 10000});
  } else if (notice?.missing) {
    alertDlg('Notebooks folder not available',
      `“${notice.missing}” isn't there (an unplugged drive, perhaps), so NoteSpin is using ${notice.using} for now. It goes back to the chosen folder when it's available again.`);
  }
  window.__nsBusy = () => editor.busy();
  window.__ns = {state, spinner, editor, app, importFile};   // for testing
  const file = await NS.call('TakeStartupFile').catch(() => null);
  if (file) importFile(file);
}

start();
