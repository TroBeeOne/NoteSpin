// The note window's page: one note, outside the main window (the window itself is src\NoteWindow.cs).
// The main window sends notes here (opened in their own window, popped out, or dragged out); this page edits
// them with the same editor, tells the main window when they change, and can send them back to it.
// Only one note is open at a time, in either window, so the two never edit the same note.

import {Editor} from './editor.js';
import {GENERAL_ID} from './notes.js';
import {cancelDialogs, dialogOpen, toast} from './ui.js';

const state = {cats: [], catMap: new Map()};
let editor, pinned = false, colors = {paper: null, band: null};

const setCats = cats => { state.cats = cats; state.catMap = new Map(cats.map(c => [c.id, c])); };
const loadCats = async () => setCats(await NS.call('CategoriesAll'));
const toMain = m => NS.call('ToMain', JSON.stringify(m)).catch(() => {});

// Messages are handled one at a time, in order (a note arriving waits for the one before to close)
let chain = Promise.resolve();
const queue = fn => (chain = chain.then(fn).catch(e => toast('Something went wrong: ' + e.message, {kind: 'error'})));

// What the window shows, for its taskbar button and its edge colour
let reportTimer = 0;
function report() {
  clearTimeout(reportTimer);
  reportTimer = setTimeout(() => {
    const n = editor.isOpen ? editor.note : null;
    NS.call('NoteWinSetState', JSON.stringify({
      open: !!n, id: n?.id || null, locked: !!editor.session, title: n?.title || '', band: colors.band, paper: colors.paper,
    })).catch(() => {});
  }, 40);
}

const app = {
  spinner: {enabled: true, frontRect: () => null},   // no wheel here
  category: id => state.catMap.get(id),
  categoryByName: name => state.cats.find(c => c.name.toLowerCase() === String(name).trim().toLowerCase()),
  categories: () => state.cats,
  async reloadCategories() {
    await loadCats();
    toMain({type: 'categoriesChanged'});
  },
  noteChanged(note) {
    report();
    toMain({type: 'noteSaved', id: note.id});
  },
  painted(paper, band) {
    colors = {paper, band};
    document.body.style.background = paper;
    report();
  },
  editorClosed(id, {emptied = null, deleted = false, keepWindow = false} = {}) {
    toMain({type: 'noteClosed', id, emptied, deleted});
    if (keepWindow) return;   // another note is about to open here
    report();
    NS.call('NoteWinHide').catch(() => {});
  },
  dragWindow(pointerType) { NS.call('NoteWinDrag', pointerType).catch(() => {}); },
  async togglePin() {
    pinned = await NS.call('NoteWinPin', !pinned);
    editor.setPinned(pinned);
  },
  popIn: () => queue(popIn),
};

/** Opens a note sent from the main window; the note that was here closes first (saved, or deleted if it's empty). */
async function openState(st) {
  cancelDialogs();
  while (editor.closing) await new Promise(r => setTimeout(r, 30));
  if (editor.isOpen) await editor.close({keepWindow: true});
  await loadCats();
  await editor.openState(st);
  report();
}

async function closeNote() {
  cancelDialogs();
  while (editor.closing) await new Promise(r => setTimeout(r, 30));
  if (editor.isOpen) await editor.close();
  else NS.call('NoteWinHide').catch(() => {});
}

/** Back into the main window (the button, or the window dropped on the cards there). */
async function popIn() {
  if (!editor.isOpen) return;
  cancelDialogs();
  const st = await editor.exportState();
  editor.detach();
  report();
  await NS.call('NoteWinDock', JSON.stringify(st));
}

// Categories were changed in the main window: a renamed or deleted category shows here at once
async function refreshCategories() {
  await loadCats();
  if (!editor.isOpen) return;
  await editor.save();
  if (editor.note?.id) {
    const n = await NS.call('NoteGet', editor.note.id);
    if (n) {
      editor.note.cat = n.cat;
      const c = state.catMap.get(n.cat);
      editor.catInput.value = c && c.id !== GENERAL_ID ? c.name : '';
    }
  }
  editor.paintCategory();
}

function start() {
  if (!window.NS) return;
  editor = new Editor(app, {windowed: true});
  NS.on('open', m => queue(() => openState(m.state)));
  NS.on('close', m => queue(async () => { await closeNote(); if (m.id) toMain({id: m.id}); }));
  NS.on('dock', () => queue(popIn));
  NS.on('categoriesChanged', () => queue(refreshCategories));
  // Ctrl+N: a new note here (the one that's open closes)
  document.addEventListener('keydown', e => {
    if (dialogOpen() || !e.ctrlKey || e.altKey || e.key.toLowerCase() !== 'n') return;
    e.preventDefault();
    queue(() => openState({note: null, categoryId: GENERAL_ID, html: ''}));
  });
  NS.call('NoteWinPinned').then(on => { pinned = !!on; editor.setPinned(pinned); }).catch(() => {});
  window.__nsBusy = () => editor.busy();
  window.__nsNote = {editor, state, app};   // for testing
  NS.call('NoteWinReady');
}

start();
