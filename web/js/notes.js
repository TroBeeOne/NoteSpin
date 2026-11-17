// What a note is made of, and the category colours.
//
// A note is stored as a small, fixed kind of HTML: paragraphs (<p>, with <br> for a line break) and
// three kinds of list: <ul> bullets, <ol> numbers, <ul class="check"> checkboxes (<li class="done"> is ticked).
// Lists can be nested inside list items. Nothing else is kept: no fonts, sizes, bold, links or images,
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

/**
 * The note as blocks: {t:'p', inl} or {t:'list', kind:'ul'|'ol'|'check', items:[{inl, done, sub:[list…]}]}.
 * inl is a list of strings and BR. paste: true tidies HTML from elsewhere (collapses its whitespace).
 */
export function parse(root, {paste = false} = {}) {
  const blocks = [];
  let cur = null;
  const flush = () => { if (cur) { blocks.push(cur); cur = null; } };
  const text = (s, pre) => {
    if (paste && !pre) s = s.replace(/\s+/g, ' ');
    s = s.replace(/\r\n?/g, '\n').replace(/​/g, '');
    if (!cur && !s.trim()) return;   // whitespace between blocks
    const parts = s.split('\n');
    parts.forEach((p, i) => {
      if (i > 0) { cur ??= {t: 'p', inl: []}; cur.inl.push(BR); }
      if (p) { cur ??= {t: 'p', inl: []}; cur.inl.push(p); }
    });
  };
  const walk = (node, pre) => {
    for (const ch of node.childNodes) {
      if (ch.nodeType === 3) { text(ch.data, pre); continue; }
      if (ch.nodeType !== 1) continue;
      const tag = ch.tagName;
      if (SKIP.has(tag)) continue;
      if (tag === 'BR') {
        // a line break ends the line; a break on its own (an empty line) is an empty paragraph
        if (cur) { if (ch.nextSibling) cur.inl.push(BR); else flush(); }
        else blocks.push({t: 'p', inl: []});
        continue;
      }
      if (tag === 'UL' || tag === 'OL') { flush(); blocks.push(parseList(ch, paste)); continue; }
      if (BLOCK.has(tag)) {
        flush();
        const before = blocks.length;
        walk(ch, pre || tag === 'PRE');
        flush();
        if (blocks.length === before && (tag === 'P' || tag === 'DIV')) blocks.push({t: 'p', inl: []});
        continue;
      }
      walk(ch, pre);   // inline element (span, b, a…): keep its text only
    }
  };
  walk(root, false);
  flush();
  for (const b of blocks) if (b.t === 'p') trimBr(b.inl);
  while (blocks.length && blocks[blocks.length - 1].t === 'p' && !inlText(blocks[blocks.length - 1].inl).trim()) blocks.pop();
  return blocks;
}

function parseList(el, paste) {
  const kind = el.tagName === 'OL' ? 'ol' : el.classList.contains('check') ? 'check' : 'ul';
  const items = [];
  const lastItem = () => items[items.length - 1] || (items.push({inl: [], done: false, sub: []}), items[items.length - 1]);
  for (const ch of el.childNodes) {
    if (ch.nodeType === 1 && ch.tagName === 'LI') items.push(parseItem(ch, paste));
    else if (ch.nodeType === 1 && (ch.tagName === 'UL' || ch.tagName === 'OL')) lastItem().sub.push(parseList(ch, paste));   // a list directly in a list: it belongs to the item above
    else if ((ch.nodeType === 3 && ch.data.trim()) || (ch.nodeType === 1 && !SKIP.has(ch.tagName))) {
      const it = {inl: [], done: false, sub: []};
      collectInline(ch, it, paste);
      trimBr(it.inl);
      if (inlText(it.inl).trim()) items.push(it);
    }
  }
  return {t: 'list', kind, items};
}

function parseItem(li, paste) {
  const it = {inl: [], done: li.classList.contains('done'), sub: []};
  collectInline(li, it, paste);
  trimBr(it.inl);
  return it;
}

// The text of a list item; paragraphs inside it become line breaks, lists inside it become sub-lists
function collectInline(node, it, paste) {
  if (node.nodeType === 3) {
    let s = node.data.replace(/​/g, '');
    if (paste) s = s.replace(/\s+/g, ' ');
    s.replace(/\r\n?/g, '\n').split('\n').forEach((p, i) => { if (i > 0) it.inl.push(BR); if (p) it.inl.push(p); });
    return;
  }
  if (node.nodeType !== 1 || SKIP.has(node.tagName)) return;
  const tag = node.tagName;
  if (tag === 'BR') { it.inl.push(BR); return; }
  if (tag === 'UL' || tag === 'OL') { it.sub.push(parseList(node, paste)); return; }
  const block = BLOCK.has(tag) && tag !== 'LI';
  if (block && it.inl.length && it.inl[it.inl.length - 1] !== BR) it.inl.push(BR);
  for (const ch of node.childNodes) collectInline(ch, it, paste);
}

function trimBr(inl) {
  while (inl.length && inl[inl.length - 1] === BR) inl.pop();
  while (inl.length && inl[0] === BR) inl.shift();
}

const inlText = inl => inl.map(x => x === BR ? '\n' : x).join('');

/* ---------- writing it back ---------- */

const esc = s => s.replace(/[&<>"]/g, c => ({'&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;'}[c]));
const inlHtml = inl => inl.map(x => x === BR ? '<br>' : esc(x)).join('');

function listHtml(l) {
  const open = l.kind === 'ol' ? '<ol>' : l.kind === 'check' ? '<ul class="check">' : '<ul>';
  const items = l.items.map(it => {
    const content = inlHtml(it.inl) || (it.sub.length ? '' : '<br>');
    return `<li${l.kind === 'check' && it.done ? ' class="done"' : ''}>${content}${it.sub.map(listHtml).join('')}</li>`;
  }).join('');
  return open + items + (l.kind === 'ol' ? '</ol>' : '</ul>');
}

/** Clean HTML for the blocks (what's stored, and what the editor shows). */
export function toHtml(blocks) {
  return blocks.map(b => b.t === 'p' ? `<p>${inlHtml(b.inl) || '<br>'}</p>` : listHtml(b)).join('');
}

/* ---------- the parts the app keeps alongside ---------- */

/**
 * Everything about a note that's worked out from its text:
 *   html     the cleaned note
 *   title    the first line with text in it
 *   lines    what the card shows below the title: [{k:'p'|'ul'|'ol'|'check', d:depth, n:number, done, x:text}]
 *   search   all the text, lower-cased, for searching
 *   empty    no text at all
 */
export function analyze(blocks) {
  const all = [];
  const addList = (l, d) => l.items.forEach((it, i) => {
    all.push({k: l.kind, d, n: i + 1, done: !!it.done, x: inlText(it.inl).replace(/\n/g, ' ')});
    it.sub.forEach(s => addList(s, d + 1));
  });
  for (const b of blocks) {
    if (b.t === 'p') inlText(b.inl).split('\n').forEach(x => all.push({k: 'p', d: 0, x}));
    else addList(b, 0);
  }
  const ti = all.findIndex(l => l.x.trim());
  const title = ti < 0 ? '' : all[ti].x.trim().slice(0, 200);
  const lines = (ti < 0 ? [] : all.slice(ti + 1)).slice(0, 24).map(l => ({...l, x: l.x.slice(0, 240)}));
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
