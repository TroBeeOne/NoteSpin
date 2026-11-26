// What every view of the cards shares (the card wheel in wheelview.js, and any other): the 3D view, the cards'
// faces, turning and settling on a card (dragging, flicking, the mouse wheel; step and goTo for the keys and
// buttons), clicking or right-clicking a card, the click sound, the background, and what the app asks of a view.
// A view says where its cards stand and what else it shows.
//
// Only the cards near the front exist at any moment (a pool, reused as the cards turn), so a notebook can hold
// any number of notes. Positions are counted in cards: pos 0 is the first item at the front, pos 1 the second,
// and so on; it keeps counting past the end and wraps (with fewer than minRing items there's an empty stretch
// between the last and the first).

import * as THREE from 'three';
import {drawFace, FACE_W, FACE_H} from './cardface.js';
import {shade, muted} from './notes.js';

export const BG = 0x15171c;   // the dark around the cards (unless it's see-through)
const TEX_CACHE = 22;

export const mod = (n, m) => ((n % m) + m) % m;
export const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

/* ---------- the click of a card going by (made on the fly, no sound files) ---------- */

export class ClickSound {
  constructor() { this.ctx = null; this.on = true; this.noise = null; this.last = 0; }

  init() {
    if (!this.ctx) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      this.ctx = new AC();
      const rate = this.ctx.sampleRate, len = Math.floor(rate * 0.045);
      this.noise = this.ctx.createBuffer(1, len, rate);
      const d = this.noise.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * Math.exp(-i / (rate * 0.008));
    }
    if (this.ctx.state === 'suspended') this.ctx.resume().catch(() => {});
  }

  play(strength = 1) {
    if (!this.on || !this.ctx || this.ctx.state !== 'running') return;
    const now = this.ctx.currentTime;
    if (now - this.last < 0.03) return;
    this.last = now;
    const vol = clamp(strength, 0.25, 1) * 0.8;

    const src = this.ctx.createBufferSource();
    src.buffer = this.noise;
    const bp = this.ctx.createBiquadFilter();
    bp.type = 'bandpass';
    bp.frequency.setValueAtTime(1900 + Math.random() * 400, now);
    bp.Q.setValueAtTime(2.2, now);
    const g1 = this.ctx.createGain();
    g1.gain.setValueAtTime(0.3 * vol, now);
    g1.gain.exponentialRampToValueAtTime(0.001, now + 0.038);
    src.connect(bp).connect(g1).connect(this.ctx.destination);
    src.start(now);

    const osc = this.ctx.createOscillator();
    const g2 = this.ctx.createGain();
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(185 + Math.random() * 25, now);
    osc.frequency.exponentialRampToValueAtTime(52, now + 0.035);
    g2.gain.setValueAtTime(0.22 * vol, now);
    g2.gain.exponentialRampToValueAtTime(0.001, now + 0.036);
    osc.connect(g2).connect(this.ctx.destination);
    osc.start(now);
    osc.stop(now + 0.04);
  }
}

// The back of a card shows its front faintly, like writing seen through paper: the same picture,
// mostly washed out (the back's UVs mirror it, as seen through the card)
export function seeThrough(shader) {
  shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `
    #ifdef USE_MAP
      vec4 seen = texture2D( map, vMapUv );
      diffuseColor.rgb *= mix( vec3( 1.0 ), seen.rgb, 0.3 );
    #endif`);
}

/* ---------- shapes ---------- */

/**
 * A card w wide and h high (t thick), its rounded corners r, standing on its bottom edge's middle (0, 0). slots: two
 * T-shaped slots along the bottom edge, where it hangs on a drum's spindles. Its triangles are ordered front, back,
 * edges, so each card draws in three calls; the face's picture fills the front.
 */
export function cardGeometry({w, h, t, r = 0.14, slots = false}) {
  const hw = w / 2;
  const notch = w * 0.24, stem = 0.075, hole = 0.165, stemH = 0.11, holeH = 0.27;
  const s = new THREE.Shape();
  s.moveTo(-hw + r, 0);
  if (slots) {
    for (const cx of [-notch, notch]) {
      s.lineTo(cx - stem, 0); s.lineTo(cx - stem, stemH); s.lineTo(cx - hole, stemH); s.lineTo(cx - hole, holeH);
      s.lineTo(cx + hole, holeH); s.lineTo(cx + hole, stemH); s.lineTo(cx + stem, stemH); s.lineTo(cx + stem, 0);
    }
  }
  s.lineTo(hw - r, 0);
  s.quadraticCurveTo(hw, 0, hw, r);
  s.lineTo(hw, h - r);
  s.quadraticCurveTo(hw, h, hw - r, h);
  s.lineTo(-hw + r, h);
  s.quadraticCurveTo(-hw, h, -hw, h - r);
  s.lineTo(-hw, r);
  s.quadraticCurveTo(-hw, 0, -hw + r, 0);

  const g = new THREE.ExtrudeGeometry(s, {depth: t, bevelEnabled: false, curveSegments: 8, steps: 1});
  g.translate(0, 0, -t / 2);
  const geo = g.index ? g.toNonIndexed() : g;
  const pos = geo.attributes.position, nor = geo.attributes.normal;
  const buckets = [[], [], []];
  for (let k = 0; k < pos.count / 3; k++) {
    const nz = nor.getZ(k * 3);
    buckets[nz > 0.5 ? 0 : nz < -0.5 ? 1 : 2].push(k);
  }
  const order = buckets.flat();
  const P = new Float32Array(pos.count * 3), N = new Float32Array(pos.count * 3), U = new Float32Array(pos.count * 2);
  order.forEach((tri, i) => {
    for (let k = 0; k < 3; k++) {
      const a = tri * 3 + k, b = i * 3 + k;
      const x = pos.getX(a), y = pos.getY(a);
      P[b * 3] = x; P[b * 3 + 1] = y; P[b * 3 + 2] = pos.getZ(a);
      N[b * 3] = nor.getX(a); N[b * 3 + 1] = nor.getY(a); N[b * 3 + 2] = nor.getZ(a);
      U[b * 2] = (x + hw) / w; U[b * 2 + 1] = y / h;
    }
  });
  const out = new THREE.BufferGeometry();
  out.setAttribute('position', new THREE.BufferAttribute(P, 3));
  out.setAttribute('normal', new THREE.BufferAttribute(N, 3));
  out.setAttribute('uv', new THREE.BufferAttribute(U, 2));
  const n0 = buckets[0].length * 3, n1 = buckets[1].length * 3, n2 = buckets[2].length * 3;
  out.addGroup(0, n0, 0);
  out.addGroup(n0, n1, 1);
  out.addGroup(n0 + n1, n2, 2);
  out.computeBoundingSphere();
  g.dispose();
  if (geo !== g) geo.dispose();
  return out;
}

// A soft studio around the scene, for the cards' sheen and the metal parts to reflect
export function studioEnvironment(renderer) {
  const env = new THREE.Scene();
  const sky = new THREE.SphereGeometry(10, 32, 16);
  const cols = [];
  const p = sky.attributes.position;
  for (let i = 0; i < p.count; i++) {
    const t = clamp((p.getY(i) / 10 + 1) / 2, 0, 1);
    const c = new THREE.Color(0x101216).lerp(new THREE.Color(0x7d8591), Math.pow(t, 1.4));
    cols.push(c.r, c.g, c.b);
  }
  sky.setAttribute('color', new THREE.Float32BufferAttribute(cols, 3));
  env.add(new THREE.Mesh(sky, new THREE.MeshBasicMaterial({vertexColors: true, side: THREE.BackSide})));
  const panel = (w, h, x, y, z, k) => {
    const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({color: new THREE.Color(k, k, k), side: THREE.DoubleSide}));
    m.position.set(x, y, z);
    m.lookAt(0, 0, 0);
    env.add(m);
  };
  panel(6, 3, 3, 7, 5, 3.2);
  panel(4, 4, -7, 3, 3, 1.4);
  panel(5, 2, 0, 4, -8, 1.0);
  const pm = new THREE.PMREMGenerator(renderer);
  const tex = pm.fromScene(env, 0.04).texture;
  pm.dispose();
  env.traverse(o => { o.geometry?.dispose(); o.material?.dispose(); });
  return tex;
}

/* ---------- a view of the cards ---------- */

/**
 * The base of every view. A view (a subclass) gives:
 *   initScene()    the scene, camera and lights, its own parts, and its cards (makePool), with cardW and cardH set
 *   layout(dt)     which cards are drawn (syncCards) and where they stand; true while anything still moves
 *   fitView()      frames the camera for the view's size (after the renderer was sized)
 *   snugWidth()    the narrowest the view can be and show everything (the window frame hidden)
 * and may give minRing, frontChanged(item), look(dx) (a sideways drag), dragTurn(dy), applyOptions(opts) (from
 * setItems), setBaseHidden(hidden), resetView(), hintAt() (the cue under the front card), and more to setBackground. Views set up their own state in
 * initScene: it runs from this constructor, before a subclass's constructor would.
 */
export class CardView {
  /**
   * host: the element the 3D view fills.
   * onOpen(item, index): the front card was clicked.  onChange(index): another card is now at the front.
   * onMenu(item, {x, y}): a card's menu is asked for (right-click, a long press, or the Menu key for the front card).
   * Items: {key (changes when the face must be redrawn), id, face (see cardface.js)}.
   */
  constructor(host, {onOpen, onChange, onMenu} = {}) {
    this.host = host;
    this.onOpen = onOpen || (() => {});
    this.onChange = onChange || (() => {});
    this.onMenu = onMenu || (() => {});
    this.items = [];
    this.N = 0;
    this.L = this.minRing;
    this.pos = 0;
    this.vel = 0;
    this.target = 0;
    this.mode = 'idle';           // idle | target (easing to a card) | free (coasting after a flick) | drag
    this._enabled = true;
    this.hint = null;             // the cue under the front card (hintAt)
    this.touch = false;           // the last press was a finger (the cue says Tap, not Click)
    this.sound = new ClickSound();
    this.assigned = new Map();    // position → card
    this.free = [];
    this.tex = new Map();         // face key → {tex, users}, oldest first
    this.shown = -1;
    this.idle = 0;
    this.raf = 0;
    this.last = 0;
    this.wheelAcc = 0;

    this.initRenderer();
    this.initScene();
    this.initInput();
    this.resizer = new ResizeObserver(() => this.resize());
    this.resizer.observe(host);
    this.resize();
  }

  /** The fewest positions round: with fewer items, an empty stretch follows the last. */
  get minRing() { return 10; }

  /** Whether the cards take clicks, drags and the wheel (not while a note is open over them). */
  get enabled() { return this._enabled; }
  set enabled(on) {
    this._enabled = on;
    this.wake();   // the cue under the front card comes and goes with it
  }

  initRenderer() {
    // alpha: the space around the cards can be see-through (setBackground)
    const r = this.renderer = new THREE.WebGLRenderer({antialias: true, alpha: true, powerPreference: 'high-performance'});
    r.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    r.toneMapping = THREE.NoToneMapping;
    this.host.appendChild(r.domElement);
    this.maxAniso = r.capabilities.getMaxAnisotropy();
  }

  /** count cards to draw with, all of this shape: a pool, each card reused for whichever item comes near the front. */
  makePool(geo, count) {
    const blank = new THREE.CanvasTexture(document.createElement('canvas'));
    for (let i = 0; i < count; i++) {
      const front = new THREE.MeshStandardMaterial({map: blank, roughness: 0.86, metalness: 0, transparent: true});
      const back = new THREE.MeshStandardMaterial({map: blank, color: 0xeeeeee, roughness: 0.9, metalness: 0, transparent: true});
      back.onBeforeCompile = seeThrough;
      const edge = new THREE.MeshStandardMaterial({color: 0xdddddd, roughness: 0.9, metalness: 0, transparent: true});
      const mesh = new THREE.Mesh(geo, [front, back, edge]);
      mesh.castShadow = mesh.receiveShadow = true;
      const hinge = new THREE.Group();
      hinge.add(mesh);
      const group = new THREE.Group();
      group.add(hinge);
      group.visible = false;
      this.scene.add(group);
      const card = {group, hinge, mesh, mats: [front, back, edge], v: null, item: null, key: null, tilt: 0, tvel: 0, flipped: false, fresh: true};
      mesh.userData.card = card;
      this.free.push(card);
    }
  }

  /* ---------- items ---------- */

  /** Shows these items. focusId: the item to put at the front (else the same position as before). opts: for the view. */
  setItems(items, focusId, opts = {}) {
    this.applyOptions(opts);
    const prevIndex = this.index();
    for (const v of [...this.assigned.keys()]) this.release(v);
    this.items = items;
    this.N = items.length;
    this.L = Math.max(this.N, this.minRing);
    let i = focusId != null ? items.findIndex(it => it.id === focusId) : -1;
    if (i < 0) i = clamp(prevIndex < 0 ? 0 : prevIndex, 0, Math.max(0, this.N - 1));
    this.pos = this.target = i;
    this.vel = 0;
    this.mode = 'idle';
    this.shown = -1;
    this.dropUnusedTextures();
    this.wake();
  }

  applyOptions() {}

  /** Forget every drawn face (after a category colour changes, for instance). */
  clearFaces() {
    for (const v of [...this.assigned.keys()]) this.release(v);
    for (const [, e] of this.tex) e.tex.dispose();
    this.tex.clear();
    this.wake();
  }

  faceFor(item) {
    let e = this.tex.get(item.key);
    if (e) {
      this.tex.delete(item.key);   // most recently used goes last
      this.tex.set(item.key, e);
      return e;
    }
    const c = document.createElement('canvas');
    c.width = FACE_W;
    c.height = FACE_H;
    drawFace(c.getContext('2d'), item.face);
    const tex = new THREE.CanvasTexture(c);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = this.maxAniso;
    e = {tex, users: 0};
    this.tex.set(item.key, e);
    this.dropUnusedTextures(TEX_CACHE);
    return e;
  }

  // keeps the most recently used faces, never one a card is showing
  dropUnusedTextures(keep = TEX_CACHE) {
    for (const [k, e] of this.tex) {
      if (this.tex.size <= keep) break;
      if (e.users > 0) continue;
      e.tex.dispose();
      this.tex.delete(k);
    }
  }

  acquire(v) {
    const card = this.free.pop();
    if (!card) return;
    const item = this.items[mod(v, this.L)];
    const face = this.faceFor(item);
    face.users++;
    card.v = v;
    card.item = item;
    card.key = item.key;
    const [front, back, edge] = card.mats;
    front.map = face.tex;
    back.map = face.tex;                      // the front's writing shows through, faintly and reversed
    back.color.set(muted(item.face.color));   // a card's back is kept quiet, so the front card stands out
    edge.color.set(shade(item.face.color, 0.2));
    card.fresh = true;
    card.group.visible = true;
    this.assigned.set(v, card);
  }

  release(v) {
    const card = this.assigned.get(v);
    if (!card) return;
    const e = this.tex.get(card.key);
    if (e) e.users--;
    card.group.visible = false;
    card.v = card.item = card.key = null;
    this.assigned.delete(v);
    this.free.push(card);
  }

  /** Draws the cards at positions lo to hi that have an item (the others go back to the pool). */
  syncCards(lo, hi) {
    for (const v of [...this.assigned.keys()]) if (v < lo || v > hi) this.release(v);
    if (this.N) for (let v = lo; v <= hi; v++) if (mod(v, this.L) < this.N && !this.assigned.has(v)) this.acquire(v);
  }

  /* ---------- moving ---------- */

  /** The index of the item at the front (or heading there), -1 when there's none. */
  index() {
    if (!this.N) return -1;
    const s = mod(Math.round(this.mode === 'target' ? this.target : this.pos), this.L);
    return s < this.N ? s : -1;
  }

  current() { const i = this.index(); return i < 0 ? null : this.items[i]; }

  // the next filled position from v in direction dir (skipping the empty stretch)
  neighbour(v, dir) {
    let n = v + dir;
    const s = mod(n, this.L);
    if (s >= this.N) n = dir > 0 ? n + (this.L - s) : n - (s - (this.N - 1));
    return n;
  }

  // the filled position to settle on from x (leaning the way it's moving)
  snap(x, vel = 0) {
    const r = Math.round(x + clamp(vel * 0.08, -0.45, 0.45));
    const s = mod(r, this.L);
    if (s < this.N) return r;
    const before = r - (s - (this.N - 1)), after = r + (this.L - s);
    return Math.abs(x - before) <= Math.abs(after - x) ? before : after;
  }

  /** Moves exactly n cards (n < 0: back), counting from where it's already heading. */
  step(n) {
    if (!this.N || !n) return;
    this.sound.init();
    let v = this.mode === 'target' ? this.target : this.snap(this.pos);
    for (let i = 0; i < Math.abs(n); i++) v = this.neighbour(v, Math.sign(n));
    this.target = v;
    this.mode = 'target';
    this.wake();
  }

  next() { this.step(1); }
  prev() { this.step(-1); }

  /** Turns to the item at index i the short way round. */
  goTo(i, instant = false) {
    if (!this.N || i < 0 || i >= this.N) return;
    const cur = Math.round(this.mode === 'target' ? this.target : this.pos);
    const base = cur - mod(cur, this.L) + i;
    const v = [base - this.L, base, base + this.L].reduce((a, b) => Math.abs(b - this.pos) < Math.abs(a - this.pos) ? b : a);
    if (instant) { this.pos = this.target = v; this.vel = 0; this.mode = 'idle'; }
    else { this.target = v; this.mode = 'target'; }
    this.wake();
  }

  // Easing to a card (a spring that settles without overshooting), or coasting after a flick until it's slow
  // enough to settle; true while it moves
  physics(dt) {
    let moving = false;
    if (this.mode === 'target') {
      const k = 150, c = 2 * Math.sqrt(k);
      this.vel += (k * (this.target - this.pos) - c * this.vel) * dt;
      this.pos += this.vel * dt;
      if (Math.abs(this.target - this.pos) < 0.002 && Math.abs(this.vel) < 0.05) {
        this.pos = this.target;
        this.vel = 0;
        this.mode = 'idle';
      }
      moving = true;
    } else if (this.mode === 'free') {
      this.pos += this.vel * dt;
      this.vel *= Math.pow(0.1, dt);
      if (Math.abs(this.vel) < 1.6) { this.target = this.snap(this.pos, this.vel); this.mode = 'target'; }
      moving = true;
    } else if (this.mode === 'drag') moving = true;
    return moving;
  }

  update(dt) {
    let moving = this.physics(dt);
    if (this.layout(dt)) moving = true;
    const shown = this.N ? mod(Math.round(this.pos), this.L) : -1;
    if (shown !== this.shown) {
      this.shown = shown;
      if (shown < this.N) {
        this.frontChanged(this.items[shown]);
        this.onChange(shown);
      }
    }
    return moving;
  }

  frontChanged() {}

  /* ---------- drawing ---------- */

  /**
   * The space around the cards: solid (1), or see-through, keeping this much (0 to 1) of the dark
   * background over whatever the window shows behind it.
   */
  setBackground(alpha) {
    const clear = alpha < 1;
    this.scene.background = clear ? null : new THREE.Color(BG);
    this.renderer.setClearColor(BG, clear ? clamp(alpha, 0, 1) : 1);
    this.wake();
  }

  setBaseHidden() {}
  resetView() {}

  wake() {
    this.idle = 0;
    if (!this.raf) this.raf = requestAnimationFrame(t => this.frame(t));
  }

  frame(t) {
    this.raf = 0;
    const dt = this.last ? clamp((t - this.last) / 1000, 0, 0.05) : 1 / 60;
    this.last = t;
    const moving = this.update(dt);
    this.renderer.render(this.scene, this.camera);
    this.placeHint();
    this.idle = moving ? 0 : this.idle + 1;
    if (this.idle < 20) this.raf = requestAnimationFrame(tt => this.frame(tt));
    else this.last = 0;   // rests until something moves
  }

  resize() {
    const w = Math.max(1, this.host.clientWidth), h = Math.max(1, this.host.clientHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.setSize(w, h);
    this.camera.aspect = w / h;
    this.fitView();
    this.wake();
  }

  /* ---------- the cue under the front card ---------- */

  // A view that wants the cue gives where its middle goes, in the scene, while the front card is at rest
  hintAt() { return null; }

  // The front card at rest: "Click to edit" (or to unlock, or to open a category) shows under it; it goes
  // while the cards move, and while a note is open over them. Clicking it opens the card, as clicking the card does.
  placeHint() {
    const at = this.hintAt();
    if (!at) return;
    let el = this.hint;
    if (!el) {
      el = this.hint = document.createElement('div');
      el.className = 'front-hint';
      el.setAttribute('aria-hidden', 'true');   // the canvas's label says what Enter does
      this.host.append(el);
    }
    const item = this.current();
    const show = !!item && this.enabled && this.mode === 'idle' && Math.abs(this.pos - Math.round(this.pos)) < 0.01;
    if (show) {
      const p = at.clone().project(this.camera);
      el.style.left = (p.x + 1) / 2 * this.host.clientWidth + 'px';
      el.style.top = (1 - p.y) / 2 * this.host.clientHeight + 'px';
      const verb = this.touch ? 'Tap' : 'Click';
      const [icon, text] = item.kind === 'category' ? ['cards', `${verb} to open`]
        : item.face?.locked ? ['lock', `${verb} to unlock`] : ['pencil', `${verb} to edit`];
      if (el.dataset.text !== text) {
        el.dataset.text = text;
        el.innerHTML = `<svg><use href="#i-${icon}"/></svg><span>${text}</span>`;
      }
    }
    el.classList.toggle('on', show);
  }

  // whether page point (x, y) is on the cue, while it shows
  onHint(x, y) {
    if (!this.hint?.classList.contains('on')) return false;
    const r = this.hint.getBoundingClientRect();
    return x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
  }

  /** Where the front card is on the page (for the editor to grow out of). */
  frontRect() {
    const card = [...this.assigned.values()].find(c => Math.abs(c.v - this.pos) < 0.5);
    if (!card) return null;
    card.group.updateMatrixWorld(true);
    const box = this.renderer.domElement.getBoundingClientRect();
    const W = this.cardW, H = this.cardH;
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const [x, y] of [[-W / 2, 0], [W / 2, 0], [-W / 2, H], [W / 2, H]]) {
      const p = new THREE.Vector3(x, y, 0).applyMatrix4(card.mesh.matrixWorld).project(this.camera);
      const sx = box.left + (p.x + 1) / 2 * box.width, sy = box.top + (1 - p.y) / 2 * box.height;
      x0 = Math.min(x0, sx); x1 = Math.max(x1, sx); y0 = Math.min(y0, sy); y1 = Math.max(y1, sy);
    }
    return {left: x0, top: y0, width: x1 - x0, height: y1 - y0};
  }

  setSound(on) { this.sound.on = on; }

  /** Takes the view away: its drawing, its listeners, its graphics memory. */
  dispose() {
    cancelAnimationFrame(this.raf);
    this.raf = 0;
    this.resizer.disconnect();
    this.abort.abort();
    for (const [, e] of this.tex) e.tex.dispose();
    this.tex.clear();
    this.scene.traverse(o => {
      o.geometry?.dispose();
      for (const m of [].concat(o.material || [])) m.dispose();
    });
    this.renderer.dispose();
    this.renderer.domElement.remove();
    this.hint?.remove();
  }

  /* ---------- input: drag up/down to turn, sideways for the view's look (if it has one), tap a card, mouse wheel ---------- */

  // How many cards a drag of dy pixels (down) turns
  dragTurn(dy) { return dy / Math.max(40, this.host.clientHeight / 11); }

  // A sideways drag (dx pixels since the last move): what the view does with it
  look() {}

  initInput() {
    const el = this.renderer.domElement;
    el.style.touchAction = 'none';
    this.abort = new AbortController();
    const on = (type, fn, opts = {}) => el.addEventListener(type, fn, {...opts, signal: this.abort.signal});
    const ray = new THREE.Raycaster();
    const ndc = new THREE.Vector2();
    const pick = (x, y) => {
      const b = el.getBoundingClientRect();
      ndc.set((x - b.left) / b.width * 2 - 1, -((y - b.top) / b.height) * 2 + 1);
      ray.setFromCamera(ndc, this.camera);
      // the cue under the front card counts as the front card
      if (this.onHint(x, y)) return [...this.assigned.values()].find(c => c.v === Math.round(this.pos)) || null;
      const meshes = [...this.assigned.values()].filter(c => c.mats[0].opacity > 0.5).map(c => c.mesh);
      const hit = ray.intersectObjects(meshes, false)[0];
      return hit ? hit.object.userData.card : null;
    };

    // Pressing on cards that are visibly turning grabs them (stops them) rather than clicking a card. The last
    // moments of easing into place don't count: by then they look still, and a click there opens the card
    // they're settling on.
    const visiblyMoving = () =>
      this.mode === 'drag' ||
      (this.mode === 'free' && Math.abs(this.vel) > 1.5) ||
      (this.mode === 'target' && (Math.abs(this.target - this.pos) > 0.2 || Math.abs(this.vel) > 2));

    let g = null;   // the gesture in progress
    let rightDownAt = -1e9;   // when the right button (or a pen's barrel button) last went down
    on('pointerdown', e => {
      if (e.button === 2) rightDownAt = performance.now();
      this.touch = e.pointerType === 'touch';
      if (!this.enabled || e.button !== 0) return;
      this.sound.init();
      el.setPointerCapture(e.pointerId);
      const grabbed = visiblyMoving();
      // a finger wobbles more than a mouse before a tap becomes a drag
      const slop = e.pointerType === 'mouse' ? 8 : 14;
      g = {id: e.pointerId, x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY, moved: 0, axis: null, samples: [], grabbed, slop};
      if (grabbed) { this.vel = 0; this.mode = 'drag'; }
      else if (this.mode === 'free') { this.target = this.snap(this.pos, this.vel); this.mode = 'target'; }
    });

    on('pointermove', e => {
      if (!g) {
        if (this.enabled && e.pointerType === 'mouse') el.style.cursor = pick(e.clientX, e.clientY) ? 'pointer' : 'grab';
        return;
      }
      if (e.pointerId !== g.id) return;
      const dx = e.clientX - g.x, dy = e.clientY - g.y;
      g.moved += Math.hypot(dx, dy);
      g.x = e.clientX;
      g.y = e.clientY;
      if (!g.axis && Math.hypot(e.clientX - g.x0, e.clientY - g.y0) > g.slop) {
        g.axis = Math.abs(e.clientY - g.y0) >= Math.abs(e.clientX - g.x0) * 0.75 ? 'spin' : 'look';
        if (g.axis === 'spin') { this.mode = 'drag'; this.vel = 0; }
      }
      if (g.axis === 'spin') {
        this.pos += this.dragTurn(dy);
        const t = performance.now();
        g.samples.push({t, pos: this.pos});
        while (g.samples.length > 2 && t - g.samples[0].t > 90) g.samples.shift();
        this.wake();
      } else if (g.axis === 'look') this.look(dx);
    });

    const end = e => {
      if (!g || e.pointerId !== g.id) return;
      const gesture = g;
      g = null;
      if (gesture.axis === 'spin') {
        const s = gesture.samples, first = s[0], lastS = s[s.length - 1];
        const vel = s.length > 1 && lastS.t > first.t ? (lastS.pos - first.pos) / ((lastS.t - first.t) / 1000) : 0;
        this.vel = clamp(vel, -30, 30);
        this.mode = 'free';
        this.wake();
        return;
      }
      if (this.mode === 'drag') { this.target = this.snap(this.pos); this.mode = 'target'; this.wake(); }
      if (gesture.axis || e.type === 'pointercancel' || gesture.grabbed || gesture.menu) return;
      const card = pick(e.clientX, e.clientY);
      if (!card) return;
      // the front card is the one resting at the front, or settling there
      const front = this.mode === 'target' ? this.target : Math.round(this.pos);
      if (card.v === front) this.onOpen(card.item, mod(card.v, this.L));
      else { this.target = card.v; this.mode = 'target'; this.wake(); }
    };
    on('pointerup', end);
    on('pointercancel', end);

    // A card's menu instead of the browser's (whose "Copy image" can't copy the 3D view). Right-click a card, or
    // press and hold one on a touch screen: that card's menu, by the pointer, and the card comes to the front.
    // From the keyboard (the Menu key, Shift+F10): the front card's.
    on('contextmenu', e => {
      e.preventDefault();
      if (!this.enabled) return;
      const fromPointer = !!g || performance.now() - rightDownAt < 2000;
      rightDownAt = -1e9;
      if (!fromPointer) {
        const item = this.current(), r = this.frontRect();
        if (item && r) this.onMenu(item, {x: r.left + r.width / 2, y: r.top + r.height / 3});
        return;
      }
      if (g) {
        if (g.axis) return;   // turning the cards, not asking for a menu
        g.menu = true;        // the finger coming up isn't a tap
      }
      const card = pick(e.clientX, e.clientY);
      if (!card) return;
      const front = this.mode === 'target' ? this.target : Math.round(this.pos);
      if (card.v !== front) { this.target = card.v; this.mode = 'target'; this.wake(); }
      this.onMenu(card.item, {x: e.clientX, y: e.clientY});
    });

    // One notch of a mouse wheel turns one card; a touchpad's small steps add up to one
    on('wheel', e => {
      e.preventDefault();
      if (!this.enabled || !this.N) return;
      const dy = e.deltaMode === 1 ? e.deltaY * 40 : e.deltaY;
      let n = 0;
      if (Math.abs(dy) >= 40) { n = Math.sign(dy) * Math.max(1, Math.round(Math.abs(dy) / 120)); this.wheelAcc = 0; }
      else {
        this.wheelAcc += dy;
        if (Math.abs(this.wheelAcc) >= 60) { n = Math.sign(this.wheelAcc); this.wheelAcc = 0; }
      }
      if (n) this.step(n);
    }, {passive: false});
  }
}
