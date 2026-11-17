// Draws the front of a card: the sticky-note paper in its category colour, a darker band across the top
// (the category, and a tab on the right), the title, then the start of the note with its bullets, numbers
// and checkboxes. The 3D view turns the drawing into the card's texture.

import {bandColor} from './notes.js';

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

  // title (up to two lines)
  ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = INK;
  ctx.font = `600 50px ${FONT}`;
  let y = BAND + 74;
  const titleLines = wrap(ctx, f.title || 'Untitled', W - PAD * 2);
  titleLines.slice(0, 2).forEach((t, i) => {
    const last = i === 1 && titleLines.length > 2;
    ctx.fillText(last ? ellipsize(ctx, t + '…', W - PAD * 2) : t, PAD, y);
    y += 62;
  });
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
  ctx.font = `400 35px ${FONT}`;
  let overflow = false;
  const lines = f.lines || [];
  if (!lines.length && f.empty) {
    ctx.fillStyle = INK_SOFT;
    ctx.fillText(f.empty, PAD, y + LH);
  }
  outer:
  for (const l of lines) {
    const indent = PAD + Math.min(l.d || 0, 5) * 46;
    const textX = l.k === 'p' ? indent : indent + 50;
    const pieces = l.x.trim() || l.k !== 'p' ? wrap(ctx, l.x, W - PAD - textX) : [''];
    for (let i = 0; i < pieces.length; i++) {
      if (y + LH > BODY_BOTTOM) { overflow = true; break outer; }
      y += LH;
      if (i === 0) {
        ctx.fillStyle = INK;
        if (l.k === 'ul') {
          ctx.beginPath();
          ctx.arc(indent + 18, y - 12, 7, 0, Math.PI * 2);
          ctx.fill();
        } else if (l.k === 'ol') {
          ctx.textAlign = 'right';
          ctx.fillText(`${l.n}.`, indent + 38, y);
          ctx.textAlign = 'left';
        } else if (l.k === 'check') {
          drawCheckbox(ctx, indent + 2, y - 31, 32, l.done);
        }
      }
      ctx.fillStyle = l.done ? INK_SOFT : INK;
      ctx.fillText(pieces[i], textX, y);
      if (l.done && pieces[i]) {
        const w = ctx.measureText(pieces[i]).width;
        ctx.fillRect(textX, y - 11, w, 3);
      }
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
    ctx.fillText('…', PAD, BODY_BOTTOM + 6);
  }
  ctx.restore();
}

function hexA(hex, a) {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${n >> 16 & 255}, ${n >> 8 & 255}, ${n & 255}, ${a})`;
}
