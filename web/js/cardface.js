// Draws the front of a card: the sticky-note paper in its category colour, a darker band across the top
// (the category, and a tab on the right), the title, then the start of the note with its bullets, numbers
// and checkboxes, its bold, italic, underlined and struck-through text, and right-to-left lines from the right.
// The 3D view turns the drawing into the card's texture (and Copy as picture copies it).

import {bandColor, isRtlText} from './notes.js';

export const FACE_W = 1024;
export const FACE_H = 784;
const FONT = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
const INK = '#24262c';
const INK_SOFT = 'rgba(36, 38, 44, 0.55)';
const PAD = 54;
const BAND = 108;
const BODY_BOTTOM = FACE_H - 104;   // the slots for the spindle are cut out below this

let grain = null;
function grainPattern(ctx) {
  if (!grain) {
    const c = document.createElement('canvas');
    c.width = c.height = 256;
    const g = c.getContext('2d');
    const img = g.createImageData(256, 256);
    let seed = 7;
    const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
    for (let i = 0; i < img.data.length; i += 4) {
      const v = rnd() < 0.5 ? 0 : 255;
      img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
      img.data[i + 3] = Math.floor(rnd() * 20);
    }
    g.putImageData(img, 0, 0);
    grain = c;
  }
  return ctx.createPattern(grain, 'repeat');
}

function roundRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
}

function ellipsize(ctx, text, max) {
  if (ctx.measureText(text).width <= max) return text;
  let lo = 0, hi = text.length;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (ctx.measureText(text.slice(0, mid) + '…').width <= max) lo = mid; else hi = mid - 1;
  }
  return text.slice(0, lo).trimEnd() + '…';
}

/* ---------- a line of the note, with its styles ---------- */

const BODY_PX = 35;
const bodyFont = m => `${m.includes('i') ? 'italic ' : ''}${m.includes('b') ? 700 : 400} ${BODY_PX}px ${FONT}`;

// A preview line as runs [{s, m}]: its text cut where its styles change (f: [[from, to, styles], …], see notes.js)
function runsOf(l) {
  const x = String(l.x ?? '');
  const out = [];
  let at = 0;
  if (Array.isArray(l.f)) {
    for (const s of l.f) {
      if (!Array.isArray(s)) continue;
      const a = Math.max(at, Math.min(x.length, s[0] | 0)), b = Math.min(x.length, s[1] | 0), m = typeof s[2] === 'string' ? s[2] : '';
      if (b <= a) continue;
      if (a > at) out.push({s: x.slice(at, a), m: ''});
      out.push({s: x.slice(a, b), m});
      at = b;
    }
  }
  if (at < x.length) out.push({s: x.slice(at), m: ''});
  return out;
}

const runsText = rs => rs.map(r => r.s).join('');
function runsWidth(ctx, rs) {
  let w = 0;
  for (const r of rs) { ctx.font = bodyFont(r.m); w += ctx.measureText(r.s).width; }
  return w;
}
function firstChars(rs, n) {
  const out = [];
  for (const r of rs) { if (n <= 0) break; out.push({s: r.s.slice(0, n), m: r.m}); n -= r.s.length; }
  return out;
}
function afterChars(rs, n) {
  const out = [];
  for (const r of rs) {
    if (n >= r.s.length) { n -= r.s.length; continue; }
    out.push({s: r.s.slice(n), m: r.m});
    n = 0;
  }
  return out;
}
function trimRunsEnd(rs) {
  const out = rs.map(r => ({...r}));
  while (out.length) {
    const last = out[out.length - 1];
    last.s = last.s.trimEnd();
    if (last.s) break;
    out.pop();
  }
  return out;
}

/** Splits styled runs into lines that fit max, as wrap does for plain text: [[runs of line 1], …]. */
function wrapRuns(ctx, runs, max) {
  // words: a word and the space after it, which may be in more than one style ("half-bold")
  const words = [];
  let word = [];
  for (const r of runs) {
    for (const part of r.s.replace(/\t/g, '    ').split(/(?<=\s)/)) {
      if (!part) continue;
      word.push({s: part, m: r.m});
      if (/\s$/.test(part)) { words.push(word); word = []; }
    }
  }
  if (word.length) words.push(word);
  const out = [];
  let line = [], lw = 0;
  for (const wd of words) {
    const ww = runsWidth(ctx, wd);
    if (lw + ww <= max) { line.push(...wd); lw += ww; continue; }
    if (runsText(line).trim()) out.push(trimRunsEnd(line));
    let rest = wd;
    while (runsWidth(ctx, rest) > max && runsText(rest).length > 1) {
      let n = runsText(rest).length;
      while (n > 1 && runsWidth(ctx, firstChars(rest, n)) > max) n--;
      out.push(firstChars(rest, n));
      rest = afterChars(rest, n);
    }
    line = rest;
    lw = runsWidth(ctx, rest);
  }
  if (runsText(line).trim() || !out.length) out.push(trimRunsEnd(line));
  return out.map(joinRuns);
}

// Joins neighbouring runs in the same style (wrapping leaves a run per word), so each is drawn in one go and the
// browser puts its words in order whichever way they read
function joinRuns(rs) {
  const out = [];
  for (const r of rs) {
    const last = out[out.length - 1];
    if (last && last.m === r.m) last.s += r.s;
    else if (r.s) out.push({...r});
  }
  return out;
}

const RTL_CH = /[֐-ࣿיִ-﷿ﹰ-﻿]/u;
// which way a run's letters go: 'rtl', 'ltr', 'both', or '' (no letters: spaces, digits, punctuation)
function lettersDir(s) {
  let r = false, l = false;
  for (const ch of s) {
    if (RTL_CH.test(ch)) r = true;
    else if (/\p{L}/u.test(ch)) l = true;
    if (r && l) return 'both';
  }
  return r ? 'rtl' : l ? 'ltr' : '';
}

/**
 * Draws a line of runs: from x rightwards, or (rtl) leftwards from x as its right edge. Each run is drawn in one go;
 * between runs, a simple version of what the browser does with mixed scripts: runs of the other direction (English in
 * a Hebrew line, say) stay in reading order among themselves, placed as one stretch. Underlined and struck-through
 * runs get their lines; strike: the whole line is struck through (a ticked checkbox).
 */
function drawRuns(ctx, rs, x, y, rtl, strike) {
  const base = rtl ? 'rtl' : 'ltr';
  const dirs = rs.map(r => { const d = lettersDir(r.s); return d === 'both' ? base : d; });
  // no letters: goes the way of the runs on both sides when they agree, else the line's way
  dirs.forEach((d, i) => {
    if (d) return;
    const before = dirs.slice(0, i).reverse().find(Boolean), after = dirs.slice(i + 1).find(Boolean);
    dirs[i] = before && before === after ? before : base;
  });
  const groups = [];
  rs.forEach((r, i) => {
    const g = groups[groups.length - 1];
    if (g && g.dir === dirs[i]) g.runs.push(r);
    else groups.push({dir: dirs[i], runs: [r]});
  });
  // left to right on the card
  const order = (rtl ? groups.reverse() : groups).flatMap(g => (g.dir === 'rtl' ? [...g.runs].reverse() : g.runs).map(r => ({r, dir: g.dir})));
  const widths = order.map(({r}) => { ctx.font = bodyFont(r.m); return ctx.measureText(r.s).width; });
  let left = rtl ? x - widths.reduce((a, b) => a + b, 0) : x;
  ctx.textAlign = 'left';
  order.forEach(({r, dir}, i) => {
    const w = widths[i];
    ctx.font = bodyFont(r.m);
    ctx.direction = dir;
    ctx.fillText(r.s, left, y);
    if (r.m.includes('u') && r.s.trim()) ctx.fillRect(left, y + 6, w, 2.5);
    if (strike || r.m.includes('s')) ctx.fillRect(left, y - 11, w, 3);
    left += w;
  });
  ctx.direction = 'ltr';
}

/** Splits text into lines that fit max (long words are broken too). */
function wrap(ctx, text, max) {
  const out = [];
  let line = '';
  for (const word of text.replace(/\t/g, '    ').split(/(?<=\s)/)) {
    if (ctx.measureText(line + word).width <= max) { line += word; continue; }
    if (line.trim()) { out.push(line.trimEnd()); line = ''; }
    let w = word;
    while (ctx.measureText(w).width > max && w.length > 1) {
      let n = w.length;
      while (n > 1 && ctx.measureText(w.slice(0, n)).width > max) n--;
      out.push(w.slice(0, n));
      w = w.slice(n);
    }
    line = w;
  }
  if (line.trim() || !out.length) out.push(line.trimEnd());
  return out;
}

function drawLock(ctx, cx, cy, s, color) {
  ctx.save();
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineWidth = s * 0.13;
  ctx.beginPath();
  ctx.arc(cx, cy - s * 0.18, s * 0.27, Math.PI, 0);
  ctx.lineTo(cx + s * 0.27, cy + s * 0.05);
  ctx.moveTo(cx - s * 0.27, cy + s * 0.05);
  ctx.lineTo(cx - s * 0.27, cy - s * 0.18);
  ctx.stroke();
  roundRect(ctx, cx - s * 0.42, cy - s * 0.02, s * 0.84, s * 0.6, s * 0.1);
  ctx.fill();
  ctx.restore();
}

function drawCheckbox(ctx, x, y, s, done) {
  ctx.save();
  roundRect(ctx, x, y, s, s, s * 0.2);
  ctx.lineWidth = 3.5;
  ctx.strokeStyle = 'rgba(36, 38, 44, 0.7)';
  if (done) {
    ctx.fillStyle = 'rgba(36, 38, 44, 0.78)';
    ctx.fill();
    ctx.beginPath();
    ctx.moveTo(x + s * 0.24, y + s * 0.52);
    ctx.lineTo(x + s * 0.43, y + s * 0.71);
    ctx.lineTo(x + s * 0.78, y + s * 0.3);
    ctx.lineWidth = 4.5;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.strokeStyle = '#fff';
  }
  ctx.stroke();
  ctx.restore();
}

/**
 * f: {color, band (text in the band), tab, title, lines, locked, empty (text when there's no body)}
 * lines are a note's preview lines (see notes.js analyze).
 */
export function drawFace(ctx, f) {
  const W = FACE_W, H = FACE_H;
  ctx.save();
  ctx.clearRect(0, 0, W, H);

  // paper, lit a little from the top, with a faint grain
  ctx.fillStyle = f.color;
  ctx.fillRect(0, 0, W, H);
  const light = ctx.createLinearGradient(0, 0, 0, H);
  light.addColorStop(0, 'rgba(255,255,255,0.20)');
  light.addColorStop(0.55, 'rgba(255,255,255,0)');
  light.addColorStop(1, 'rgba(0,0,0,0.07)');
  ctx.fillStyle = light;
  ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = grainPattern(ctx);
  ctx.fillRect(0, 0, W, H);

  // the band
  ctx.fillStyle = bandColor(f.color);
  ctx.fillRect(0, 0, W, BAND);
  ctx.fillStyle = 'rgba(255,255,255,0.16)';
  ctx.fillRect(0, 0, W, 5);
  ctx.fillStyle = 'rgba(0,0,0,0.08)';
  ctx.fillRect(0, BAND - 3, W, 3);

  // tab on the right of the band
  ctx.font = `600 34px ${FONT}`;
  ctx.textBaseline = 'middle';
  let right = W - 40;
  if (f.tab) {
    const tw = Math.min(260, ctx.measureText(f.tab).width);
    const pw = Math.max(92, tw + 52), ph = 64;
    roundRect(ctx, right - pw, (BAND - ph) / 2, pw, ph, 16);
    ctx.fillStyle = 'rgba(22, 24, 30, 0.84)';
    ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.textAlign = 'center';
    ctx.fillText(ellipsize(ctx, f.tab, 260), right - pw / 2, BAND / 2 + 2);
    right -= pw + 22;
  }
  if (f.locked) {
    drawLock(ctx, right - 24, BAND / 2 + 2, 46, 'rgba(25, 27, 32, 0.8)');
    right -= 64;
  }
  // the category, then (quieter) when it was last edited
  ctx.textAlign = 'left';
  let dateText = '', dateW = 0;
  if (f.date) {
    ctx.font = `400 31px ${FONT}`;
    dateText = '  ·  ' + f.date;
    dateW = ctx.measureText(dateText).width;
  }
  ctx.fillStyle = 'rgba(25, 27, 32, 0.8)';
  ctx.font = `600 34px ${FONT}`;
  const name = ellipsize(ctx, f.band || '', Math.max(80, right - PAD - 10 - dateW));
  ctx.fillText(name, PAD, BAND / 2 + 2);
  if (dateText) {
    const x = PAD + ctx.measureText(name).width;
    ctx.font = `400 31px ${FONT}`;
    ctx.fillStyle = 'rgba(25, 27, 32, 0.58)';
    ctx.fillText(ellipsize(ctx, dateText, Math.max(0, right - 10 - x)), x, BAND / 2 + 2);
  }

  // title (up to two lines; from the right when it's in a right-to-left script)
  ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = INK;
  ctx.font = `600 50px ${FONT}`;
  let y = BAND + 74;
  const titleRtl = isRtlText(f.title);
  const titleLines = wrap(ctx, f.title || 'Untitled', W - PAD * 2);
  ctx.direction = titleRtl ? 'rtl' : 'ltr';
  ctx.textAlign = titleRtl ? 'right' : 'left';
  titleLines.slice(0, 2).forEach((t, i) => {
    const last = i === 1 && titleLines.length > 2;
    ctx.fillText(last ? ellipsize(ctx, t + '…', W - PAD * 2) : t, titleRtl ? W - PAD : PAD, y);
    y += 62;
  });
  ctx.direction = 'ltr';
  ctx.textAlign = 'left';
  y += 8;

  if (f.locked) {
    drawLock(ctx, W / 2, y + (BODY_BOTTOM - y) / 2 - 30, 110, 'rgba(36, 38, 44, 0.32)');
    ctx.fillStyle = INK_SOFT;
    ctx.font = `400 34px ${FONT}`;
    ctx.textAlign = 'center';
    ctx.fillText('Locked', W / 2, y + (BODY_BOTTOM - y) / 2 + 80);
    ctx.restore();
    return;
  }

  // the start of the note
  const LH = 47;
  ctx.font = bodyFont('');
  let overflow = false, lastRtl = false;
  const lines = f.lines || [];
  if (!lines.length && f.empty) {
    ctx.fillStyle = INK_SOFT;
    ctx.fillText(f.empty, PAD, y + LH);
  }
  outer:
  for (const l of lines) {
    const x = String(l.x ?? '');
    // right to left: the same layout, mirrored (the bullet, number or checkbox on the right)
    const rtl = l.r === 1 || l.r === 0 ? !!l.r : isRtlText(x);
    const indent = PAD + Math.min(l.d || 0, 5) * 46;
    const inset = l.k === 'p' ? indent : indent + 50;
    const pieces = x.trim() || l.k !== 'p' ? wrapRuns(ctx, runsOf(l), W - PAD - inset) : [[]];
    for (let i = 0; i < pieces.length; i++) {
      if (y + LH > BODY_BOTTOM) { overflow = true; break outer; }
      y += LH;
      lastRtl = rtl;
      if (i === 0) {
        ctx.fillStyle = INK;
        ctx.font = bodyFont('');
        if (l.k === 'ul') {
          ctx.beginPath();
          ctx.arc(rtl ? W - indent - 18 : indent + 18, y - 12, 7, 0, Math.PI * 2);
          ctx.fill();
        } else if (l.k === 'ol') {
          ctx.direction = rtl ? 'rtl' : 'ltr';
          ctx.textAlign = rtl ? 'left' : 'right';
          ctx.fillText(`${l.n}.`, rtl ? W - indent - 38 : indent + 38, y);
          ctx.direction = 'ltr';
          ctx.textAlign = 'left';
        } else if (l.k === 'check') {
          drawCheckbox(ctx, rtl ? W - indent - 34 : indent + 2, y - 31, 32, l.done);
        }
      }
      ctx.fillStyle = l.done ? INK_SOFT : INK;
      drawRuns(ctx, pieces[i], rtl ? W - inset : inset, y, rtl, l.done);
    }
  }

  // more than fits: fade the last lines into the paper
  if (overflow) {
    const fade = ctx.createLinearGradient(0, BODY_BOTTOM - 90, 0, BODY_BOTTOM + 4);
    fade.addColorStop(0, hexA(f.color, 0));
    fade.addColorStop(1, hexA(f.color, 1));
    ctx.fillStyle = fade;
    ctx.fillRect(0, BODY_BOTTOM - 90, W, 94);
    ctx.fillStyle = INK_SOFT;
    ctx.font = `600 40px ${FONT}`;
    ctx.textAlign = lastRtl ? 'right' : 'left';
    ctx.fillText('…', lastRtl ? W - PAD : PAD, BODY_BOTTOM + 6);
    ctx.textAlign = 'left';
  }
  ctx.restore();
}

function hexA(hex, a) {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${n >> 16 & 255}, ${n >> 8 & 255}, ${n & 255}, ${a})`;
}
