// What a note is made of, and the category colours.
//
// A note is stored as a small, fixed kind of HTML: paragraphs (<p>, with <br> for a line break) and
// three kinds of list: <ul> bullets, <ol> numbers, <ul class="check"> checkboxes (<li class="done"> is ticked).
// Lists can be nested inside list items. Text can be bold, italic, underlined or struck through (<b>, <i>, <u>,
// <s>), and a paragraph or list item set to read right to left or left to right keeps it (dir="rtl" or "ltr";
// without one, each follows its first letter). Nothing else is kept: no fonts, sizes, colours, links or images,
// so every note looks the same. The note's first line is its title.

/* ---------- category colours (sticky-note pastels) ---------- */

export const PALETTE = [
  {name: 'Yellow', hex: '#FFF2A6'},
  {name: 'Green', hex: '#CDEFC0'},
  {name: 'Pink', hex: '#FFCFE2'},
  {name: 'Blue', hex: '#CBE4FF'},
  {name: 'Purple', hex: '#E2D4FA'},
  {name: 'Orange', hex: '#FFDDB8'},
  {name: 'Teal', hex: '#C4EEE8'},
  {name: 'Lime', hex: '#E8F5A8'},
  {name: 'Coral', hex: '#FFC9BF'},
  {name: 'Lilac', hex: '#D9DDFF'},
  {name: 'Sand', hex: '#ECDFC8'},
  {name: 'Gray', hex: '#E4E6EA'},
];

export const GENERAL_ID = 'general';

/** The palette colour used least by the existing categories (so new categories look different). */
export function pickColor(categories) {
  const used = new Map(PALETTE.map(p => [p.hex, 0]));
  for (const c of categories) if (used.has(c.color)) used.set(c.color, used.get(c.color) + 1);
  let best = PALETTE[0].hex, n = Infinity;
  for (const [hex, count] of used) if (count < n) { best = hex; n = count; }
  return best;
}

function hexToHsl(hex) {
  const n = parseInt(hex.slice(1), 16);
  const r = (n >> 16 & 255) / 255, g = (n >> 8 & 255) / 255, b = (n & 255) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min, s = l > .5 ? d / (2 - max - min) : d / (max + min);
  const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h * 60, s, l];
}

function hslToHex(h, s, l) {
  const k = n => (n + h / 30) % 12, a = s * Math.min(l, 1 - l);
  const f = n => Math.round(255 * (l - a * Math.max(-1, Math.min(k(n) - 3, 9 - k(n), 1))));
  return '#' + [f(0), f(8), f(4)].map(v => v.toString(16).padStart(2, '0')).join('');
}

/** The darker strip across the top of a card in this colour. */
export function bandColor(hex) {
  const [h, s, l] = hexToHsl(hex);
  return hslToHex(h, Math.min(1, s * 0.9 + 0.08), Math.max(0.3, l - 0.15));
}

/** The note's paper a shade darker (the back of a card). */
export function shade(hex, amount = 0.06) {
  const [h, s, l] = hexToHsl(hex);
  return hslToHex(h, s, Math.max(0, l - amount));
}

/** A quieter, greyer version of the colour (the back of a card lying on the deck). */
export function muted(hex) {
  const [h, s, l] = hexToHsl(hex);
  return hslToHex(h, s * 0.45, Math.max(0, l - 0.3));
}

/* ---------- reading a note's HTML ---------- */

const BLOCK = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'PRE', 'SECTION', 'ARTICLE', 'HEADER',
  'FOOTER', 'ASIDE', 'NAV', 'MAIN', 'FIGURE', 'FIGCAPTION', 'ADDRESS', 'DL', 'DT', 'DD', 'TABLE', 'THEAD', 'TBODY', 'TFOOT',
  'TR', 'TD', 'TH', 'CAPTION', 'HR', 'DETAILS', 'SUMMARY', 'CENTER', 'FORM', 'FIELDSET', 'LI']);
const SKIP = new Set(['SCRIPT', 'STYLE', 'TEMPLATE', 'IMG', 'PICTURE', 'VIDEO', 'AUDIO', 'SOURCE', 'TRACK', 'IFRAME', 'OBJECT',
  'EMBED', 'CANVAS', 'SVG', 'MATH', 'NOSCRIPT', 'INPUT', 'BUTTON', 'SELECT', 'TEXTAREA', 'OPTION', 'META', 'LINK', 'TITLE', 'HEAD']);
const BR = {br: true};

/* Text styles: b bold, i italic, u underlined, s struck through. A run of styled text in a line is {s: text, m: styles},
   the styles in this order ('bi', 'us', …); plain text is just a string. */
const STYLES = 'bius';
const TAG_STYLE = {B: 'b', STRONG: 'b', I: 'i', EM: 'i', U: 'u', INS: 'u', S: 's', STRIKE: 's', DEL: 's'};
const runText = x => x === BR ? '\n' : typeof x === 'string' ? x : x.s;
const runStyle = x => x === BR || typeof x === 'string' ? '' : x.m;

// The styles inside an element: its parent's (m), with what its tag and its own style attribute add or take away
// (pasted HTML often says it with styles: <span style="font-weight:700">, or <b style="font-weight:normal">)
function stylesOf(el, m) {
  const set = new Set(m);
  if (TAG_STYLE[el.tagName]) set.add(TAG_STYLE[el.tagName]);
  const st = el.style;
  if (st) {
    const w = st.fontWeight;
    if (w === 'bold' || w === 'bolder' || +w >= 600) set.add('b');
    else if (w === 'normal' || w === 'lighter' || +w > 0) set.delete('b');
    if (st.fontStyle === 'italic' || st.fontStyle.startsWith('oblique')) set.add('i');
    else if (st.fontStyle === 'normal') set.delete('i');
    const d = st.textDecorationLine || st.textDecoration || '';
    if (d.includes('underline')) set.add('u');
    if (d.includes('line-through')) set.add('s');
  }
  return [...STYLES].filter(c => set.has(c)).join('');
}

// An element's reading direction if it sets one ('rtl' or 'ltr'; '' for "follow the text"), else the one around it.
// The browser's own Ctrl+Right Shift / Ctrl+Left Shift puts it in the style attribute (direction: rtl).
function dirOf(el, around) {
  const s = el.style?.direction;
  if (s === 'rtl' || s === 'ltr') return s;
  const a = (el.getAttribute?.('dir') || '').toLowerCase();
  if (a === 'rtl' || a === 'ltr') return a;
  return a === 'auto' ? '' : around;
}

// Adds text to a line, joined to the run before when it has the same styles
function addText(inl, s, m) {
  if (!s) return;
  const last = inl[inl.length - 1];
  if (last !== undefined && last !== BR && runStyle(last) === m) {
    if (typeof last === 'string') inl[inl.length - 1] = last + s;
    else last.s += s;
  } else inl.push(m ? {s, m} : s);
}

const para = dir => dir ? {t: 'p', inl: [], dir} : {t: 'p', inl: []};
const item = (dir, done = false) => dir ? {inl: [], done, sub: [], dir} : {inl: [], done, sub: []};

/**
 * The note as blocks: {t:'p', inl, dir?} or {t:'list', kind:'ul'|'ol'|'check', items:[{inl, done, sub:[list…], dir?}]}.
 * inl is a list of strings, styled runs {s, m} and BR; dir is there when the block's direction was set ('rtl' or
 * 'ltr'). paste: true tidies HTML from elsewhere (collapses its whitespace).
 */
export function parse(root, {paste = false} = {}) {
  const blocks = [];
  let cur = null;
  const flush = () => { if (cur) { blocks.push(cur); cur = null; } };
  const text = (s, pre, m, dir) => {
    if (paste && !pre) s = s.replace(/\s+/g, ' ');
    s = s.replace(/\r\n?/g, '\n').replace(/​/g, '');
    if (!cur && !s.trim()) return;   // whitespace between blocks
    s.split('\n').forEach((p, i) => {
      if (i > 0) { cur ??= para(dir); cur.inl.push(BR); }
      if (p) { cur ??= para(dir); addText(cur.inl, p, m); }
    });
  };
  const walk = (node, pre, m, dir) => {
    for (const ch of node.childNodes) {
      if (ch.nodeType === 3) { text(ch.data, pre, m, dir); continue; }
      if (ch.nodeType !== 1) continue;
      const tag = ch.tagName;
      if (SKIP.has(tag)) continue;
      if (tag === 'BR') {
        // a line break ends the line; a break on its own (an empty line) is an empty paragraph
        if (cur) { if (ch.nextSibling) cur.inl.push(BR); else flush(); }
        else blocks.push(para(dir));
        continue;
      }
      if (tag === 'UL' || tag === 'OL') { flush(); blocks.push(parseList(ch, paste, stylesOf(ch, m), dirOf(ch, dir))); continue; }
      if (BLOCK.has(tag)) {
        flush();
        const before = blocks.length, d = dirOf(ch, dir);
        walk(ch, pre || tag === 'PRE', stylesOf(ch, m), d);
        flush();
        if (blocks.length === before && (tag === 'P' || tag === 'DIV')) blocks.push(para(d));
        continue;
      }
      walk(ch, pre, stylesOf(ch, m), dir);   // inline element (span, b, a…): its text, and bold, italic, underline, strikethrough
    }
  };
  walk(root, false, '', '');
  flush();
  for (const b of blocks) if (b.t === 'p') trimBr(b.inl);
  while (blocks.length && blocks[blocks.length - 1].t === 'p' && !inlText(blocks[blocks.length - 1].inl).trim()) blocks.pop();
  return blocks;
}

function parseList(el, paste, m, dir) {
  const kind = el.tagName === 'OL' ? 'ol' : el.classList.contains('check') ? 'check' : 'ul';
  const items = [];
  const lastItem = () => items[items.length - 1] || (items.push(item(dir)), items[items.length - 1]);
  for (const ch of el.childNodes) {
    if (ch.nodeType === 1 && ch.tagName === 'LI') items.push(parseItem(ch, paste, m, dir));
    else if (ch.nodeType === 1 && (ch.tagName === 'UL' || ch.tagName === 'OL')) lastItem().sub.push(parseList(ch, paste, stylesOf(ch, m), dirOf(ch, dir)));   // a list directly in a list: it belongs to the item above
    else if ((ch.nodeType === 3 && ch.data.trim()) || (ch.nodeType === 1 && !SKIP.has(ch.tagName))) {
      const it = item(dir);
      collectInline(ch, it, paste, m, dir);
      trimBr(it.inl);
      if (inlText(it.inl).trim()) items.push(it);
    }
  }
  return {t: 'list', kind, items};
}

function parseItem(li, paste, m, dir) {
  const d = dirOf(li, dir);
  const it = item(d, li.classList.contains('done'));
  for (const ch of li.childNodes) collectInline(ch, it, paste, stylesOf(li, m), d);
  trimBr(it.inl);
  return it;
}

// The text of a list item; paragraphs inside it become line breaks, lists inside it become sub-lists
function collectInline(node, it, paste, m, dir) {
  if (node.nodeType === 3) {
    let s = node.data.replace(/​/g, '');
    if (paste) s = s.replace(/\s+/g, ' ');
    s.replace(/\r\n?/g, '\n').split('\n').forEach((p, i) => { if (i > 0) it.inl.push(BR); addText(it.inl, p, m); });
    return;
  }
  if (node.nodeType !== 1 || SKIP.has(node.tagName)) return;
  const tag = node.tagName;
  if (tag === 'BR') { it.inl.push(BR); return; }
  if (tag === 'UL' || tag === 'OL') { it.sub.push(parseList(node, paste, stylesOf(node, m), dirOf(node, dir))); return; }
  if (BLOCK.has(tag) && tag !== 'LI') {
    if (it.inl.length && it.inl[it.inl.length - 1] !== BR) it.inl.push(BR);
    // the browser may set an item's direction on a block it wraps the item's text in (only the first block counts)
    if (!it.dir && !inlText(it.inl).trim()) { const d = dirOf(node, ''); if (d) it.dir = d; }
  }
  const mm = stylesOf(node, m);
  for (const ch of node.childNodes) collectInline(ch, it, paste, mm, dir);
}

function trimBr(inl) {
  while (inl.length && inl[inl.length - 1] === BR) inl.pop();
  while (inl.length && inl[0] === BR) inl.shift();
}

const inlText = inl => inl.map(runText).join('');

/* ---------- reading direction ---------- */

const RTL_LETTER = /[֐-ࣿיִ-﷿ﹰ-﻿‏\u{10800}-\u{10FFF}\u{1E800}-\u{1EFFF}]/u;

/** Whether text reads right to left: its first letter is Hebrew, Arabic or another right-to-left script (as dir="auto"). */
export function isRtlText(text) {
  const c = String(text || '').match(/[\p{L}‎‏]/u);
  return !!c && RTL_LETTER.test(c[0]);
}

const rtlOf = (dir, inl) => dir ? dir === 'rtl' : isRtlText(inlText(inl));

/* ---------- writing it back ---------- */

const esc = s => s.replace(/[&<>"]/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[c]));
const dirAttr = dir => dir ? ` dir="${dir}"` : '';

/** A line's text as HTML: its line breaks, bold, italic, underline and strikethrough. */
export function inlineHtml(inl) {
  return inl.map(x => {
    if (x === BR) return '<br>';
    if (typeof x === 'string') return esc(x);
    const m = [...x.m];
    return m.map(c => `<${c}>`).join('') + esc(x.s) + m.reverse().map(c => `</${c}>`).join('');
  }).join('');
}

/** Whether any text in the blocks is styled (bold, italic…). */
export function hasStyles(blocks) {
  const inItems = l => l.items.some(it => it.inl.some(runStyle) || it.sub.some(inItems));
  return blocks.some(b => b.t === 'p' ? b.inl.some(runStyle) : inItems(b));
}

function listHtml(l) {
  const open = l.kind === 'ol' ? '<ol>' : l.kind === 'check' ? '<ul class="check">' : '<ul>';
  const items = l.items.map(it => {
    const content = inlineHtml(it.inl) || (it.sub.length ? '' : '<br>');
    return `<li${l.kind === 'check' && it.done ? ' class="done"' : ''}${dirAttr(it.dir)}>${content}${it.sub.map(listHtml).join('')}</li>`;
  }).join('');
  return open + items + (l.kind === 'ol' ? '</ol>' : '</ul>');
}

/** Clean HTML for the blocks (what's stored, and what the editor shows). */
export function toHtml(blocks) {
  return blocks.map(b => b.t === 'p' ? `<p${dirAttr(b.dir)}>${inlineHtml(b.inl) || '<br>'}</p>` : listHtml(b)).join('');
}

/** The note as plain text (Copy text): a line per line, list items marked • 1. ☐ ☑ and indented by level. */
export function toText(blocks) {
  const out = [];
  const addList = (l, depth) => l.items.forEach((it, i) => {
    const mark = l.kind === 'ol' ? `${i + 1}. ` : l.kind === 'check' ? (it.done ? '☑ ' : '☐ ') : '• ';
    const pad = '    '.repeat(depth);
    inlText(it.inl).split('\n').forEach((x, j) => out.push(pad + (j ? ' '.repeat(mark.length) : mark) + x));
    it.sub.forEach(s => addList(s, depth + 1));
  });
  for (const b of blocks) {
    if (b.t === 'p') out.push(...inlText(b.inl).split('\n'));
    else addList(b, 0);
  }
  return out.join('\n');
}

/* ---------- the parts the app keeps alongside ---------- */

// A line's styled stretches, for the card: [[from, to, styles], …] in its text (none: no entry)
function stylesIn(inl) {
  const f = [];
  let at = 0;
  for (const x of inl) {
    const s = runText(x), m = runStyle(x);
    if (m && s) f.push([at, at + s.length, m]);
    at += s.length;
  }
  return f;
}

// Splits a line at its line breaks
function splitAtBreaks(inl) {
  const out = [[]];
  for (const x of inl) { if (x === BR) out.push([]); else out[out.length - 1].push(x); }
  return out;
}

// One of the card's lines: {k, d, x, …}, with f: its styled stretches, and r when it doesn't read the way its first
// letter says (its direction was set, or it's a later line of a paragraph that starts in another script): 1 right to
// left, 0 left to right. Without r the card goes by the first letter, as it does for notes saved before there was r.
function cardLine(base, inl, rtl) {
  const l = {...base, x: inlText(inl)};
  if (rtl !== isRtlText(l.x)) l.r = rtl ? 1 : 0;
  const f = stylesIn(inl);
  if (f.length) l.f = f;
  return l;
}

// Cut to the first n characters (the styled stretches too)
function clipLine(l, n) {
  if (l.x.length <= n) return l;
  const c = {...l, x: l.x.slice(0, n)};
  if (l.f) {
    c.f = l.f.filter(([a]) => a < n).map(([a, b, m]) => [a, Math.min(b, n), m]);
    if (!c.f.length) delete c.f;
  }
  return c;
}

/**
 * Everything about a note that's worked out from its text:
 *   html     the cleaned note
 *   title    the first line with text in it
 *   lines    what the card shows below the title: [{k:'p'|'ul'|'ol'|'check', d:depth, n:number, done, x:text,
 *            f: [[from, to, styles], …] its bold, italic… stretches, r: its direction when not its first letter's}]
 *   search   all the text, lower-cased, for searching
 *   empty    no text at all
 */
export function analyze(blocks) {
  const all = [];
  const addList = (l, d) => l.items.forEach((it, i) => {
    // a list item's line breaks become spaces on the card
    const inl = it.inl.map(x => x === BR ? ' ' : x);
    all.push(cardLine({k: l.kind, d, n: i + 1, done: !!it.done}, inl, rtlOf(it.dir, it.inl)));
    it.sub.forEach(s => addList(s, d + 1));
  });
  for (const b of blocks) {
    if (b.t === 'p') {
      const rtl = rtlOf(b.dir, b.inl);   // the whole paragraph reads one way, as in the note
      for (const line of splitAtBreaks(b.inl)) all.push(cardLine({k: 'p', d: 0}, line, rtl));
    } else addList(b, 0);
  }
  const ti = all.findIndex(l => l.x.trim());
  const title = ti < 0 ? '' : all[ti].x.trim().slice(0, 200);
  const lines = (ti < 0 ? [] : all.slice(ti + 1)).slice(0, 24).map(l => clipLine(l, 240));
  while (lines.length && lines[0].k === 'p' && !lines[0].x.trim()) lines.shift();   // the gap under the title
  const textAll = all.map(l => l.x).join('\n');
  return {
    html: toHtml(blocks),
    title,
    lines,
    search: textAll.toLocaleLowerCase(),
    empty: !textAll.trim(),
  };
}

/** Shortcut: read HTML text (a stored note, or pasted HTML) into blocks. */
export function parseHtml(html, opts) {
  const doc = new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html');
  return parse(doc.body, opts);
}

/** Plain text (pasted) into blocks: one paragraph per line. */
export function textToBlocks(text) {
  return text.replace(/\r\n?/g, '\n').split('\n').map(x => ({t: 'p', inl: x ? [x] : []}));
}
