// The 3D card wheel: notes stand on a drum between two side frames. Turning the drum brings the next
// card up from behind; at the top it tips over and falls forward onto the deck.
//
// Only the cards near the front exist at any moment (a pool of a dozen, reused as the drum turns), so a
// notebook can hold any number of notes. Positions are counted in cards: pos 0 is the first item at the
// front, pos 1 the second, and so on; it keeps counting past the end and wraps (with fewer than MIN_RING
// items there's an empty stretch of drum between the last and the first).

import * as THREE from 'three';
import {drawFace, FACE_W, FACE_H} from './cardface.js';
import {shade, muted} from './notes.js';

const CARD_W = 3.4, CARD_H = 2.6, CARD_T = 0.016;
const STEP = Math.PI * 2 / 16;      // drum angle between neighbouring cards
const R = 0.56;                     // drum radius at the card spindles
const AXLE_Y = -0.15;
const FLIP = 0.16;                  // just past the top a card falls forward
const BEHIND = 4;                   // cards drawn behind the front one (three, and a fourth fading in)
const FRONT = 1.9;                  // fallen cards drawn on the deck: the last one, and the one before fading out
const PEEK = 0.38;                  // how much of each card behind shows above the one in front (its band)
const MIN_RING = 10;
const POOL = 12;
const TEX_CACHE = 22;
const FRAME_INNER_W = 3.75, FRAME_WALL_T = 0.22;
const FLOOR = -1.4;                 // the desk top; the frame stands on it
const BG = 0x15171c;

const mod = (n, m) => ((n % m) + m) % m;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

/* ---------- the click of a card falling over (made on the fly, no sound files) ---------- */

class ClickSound {
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
function seeThrough(shader) {
  shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `
    #ifdef USE_MAP
      vec4 seen = texture2D( map, vMapUv );
      diffuseColor.rgb *= mix( vec3( 1.0 ), seen.rgb, 0.3 );
    #endif`);
}

/* ---------- shapes ---------- */

// A card with two T-shaped slots along the bottom edge, where it hangs on the drum's spindles.
// Its triangles are ordered front, back, edges, so each card draws in three calls.
function cardGeometry() {
  const hw = CARD_W / 2, r = 0.14;
  const notch = CARD_W * 0.24, stem = 0.075, hole = 0.165, stemH = 0.11, holeH = 0.27;
  const s = new THREE.Shape();
  s.moveTo(-hw + r, 0);
  for (const cx of [-notch, notch]) {
    s.lineTo(cx - stem, 0); s.lineTo(cx - stem, stemH); s.lineTo(cx - hole, stemH); s.lineTo(cx - hole, holeH);
    s.lineTo(cx + hole, holeH); s.lineTo(cx + hole, stemH); s.lineTo(cx + stem, stemH); s.lineTo(cx + stem, 0);
  }
  s.lineTo(hw - r, 0);
  s.quadraticCurveTo(hw, 0, hw, r);
  s.lineTo(hw, CARD_H - r);
  s.quadraticCurveTo(hw, CARD_H, hw - r, CARD_H);
  s.lineTo(-hw + r, CARD_H);
  s.quadraticCurveTo(-hw, CARD_H, -hw, CARD_H - r);
  s.lineTo(-hw, r);
  s.quadraticCurveTo(-hw, 0, -hw + r, 0);

  const g = new THREE.ExtrudeGeometry(s, {depth: CARD_T, bevelEnabled: false, curveSegments: 8, steps: 1});
  g.translate(0, 0, -CARD_T / 2);
  const geo = g.index ? g.toNonIndexed() : g;
  const pos = geo.attributes.position, nor = geo.attributes.normal;
  const buckets = [[], [], []];
  for (let t = 0; t < pos.count / 3; t++) {
    const nz = nor.getZ(t * 3);
    buckets[nz > 0.5 ? 0 : nz < -0.5 ? 1 : 2].push(t);
  }
  const order = buckets.flat();
  const P = new Float32Array(pos.count * 3), N = new Float32Array(pos.count * 3), U = new Float32Array(pos.count * 2);
  order.forEach((t, i) => {
    for (let k = 0; k < 3; k++) {
      const a = t * 3 + k, b = i * 3 + k;
      const x = pos.getX(a), y = pos.getY(a);
      P[b * 3] = x; P[b * 3 + 1] = y; P[b * 3 + 2] = pos.getZ(a);
      N[b * 3] = nor.getX(a); N[b * 3 + 1] = nor.getY(a); N[b * 3 + 2] = nor.getZ(a);
      U[b * 2] = (x + hw) / CARD_W; U[b * 2 + 1] = y / CARD_H;
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

// A soft studio around the scene for the metal parts to reflect
function studioEnvironment(renderer) {
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

/* ---------- the spinner ---------- */

export class Spinner {
  /**
   * host: the element the 3D view fills.
   * onOpen(item, index): the front card was clicked.  onChange(index): another card is now at the front.
   * Items: {key (changes when the face must be redrawn), id, face (see cardface.js)}.
   */
  constructor(host, {onOpen, onChange} = {}) {
    this.host = host;
    this.onOpen = onOpen || (() => {});
    this.onChange = onChange || (() => {});
    this.items = [];
    this.N = 0;
    this.L = MIN_RING;
    this.pos = 0;
    this.vel = 0;
    this.target = 0;
    this.mode = 'idle';           // idle | target (easing to a card) | free (coasting after a flick) | drag
    this.theta = 0;
    this.phi = 0.46;
    this.enabled = true;
    this.sound = new ClickSound();
    this.assigned = new Map();    // position → card
    this.free = [];
    this.tex = new Map();         // face key → {tex, users}, oldest first
    this.shown = -1;
    this.idle = 0;
    this.raf = 0;
    this.last = 0;
    this.wheelAcc = 0;

    this.initScene();
    this.initInput();
    new ResizeObserver(() => this.resize()).observe(host);
    this.resize();
  }

  /* ---------- scene ---------- */

  initScene() {
    // alpha: the space around the cards can be see-through (setBackground)
    const r = this.renderer = new THREE.WebGLRenderer({antialias: true, alpha: true, powerPreference: 'high-performance'});
    r.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFShadowMap;
    r.toneMapping = THREE.NoToneMapping;
    this.host.appendChild(r.domElement);
    this.maxAniso = r.capabilities.getMaxAnisotropy();

    const scene = this.scene = new THREE.Scene();
    scene.background = new THREE.Color(BG);
    scene.fog = new THREE.Fog(BG, 14, 30);
    scene.environment = studioEnvironment(r);
    scene.environmentIntensity = 0.55;
    this.camera = new THREE.PerspectiveCamera(36, 1, 0.1, 80);
    this.lookAt = new THREE.Vector3(0, 0.4, 0.3);
    this.dist = 10;

    scene.add(new THREE.AmbientLight(0xeef2ff, 1.1));
    const key = new THREE.DirectionalLight(0xfff6ea, 2.2);
    key.position.set(4.5, 8.5, 6.5);
    key.castShadow = true;
    key.shadow.mapSize.set(2048, 2048);
    Object.assign(key.shadow.camera, {near: 1, far: 26, left: -6, right: 6, top: 6, bottom: -6});
    key.shadow.bias = -0.0004;
    key.shadow.normalBias = 0.01;
    key.shadow.radius = 3;
    scene.add(key);
    const fill = new THREE.DirectionalLight(0xb9d3ff, 0.55);
    fill.position.set(-6, 4, 4);
    scene.add(fill);
    const rim = new THREE.DirectionalLight(0xffd88a, 0.45);
    rim.position.set(0, 6, -7);
    scene.add(rim);

    const desk = this.desk = new THREE.Mesh(new THREE.PlaneGeometry(60, 60), new THREE.MeshStandardMaterial({color: 0x1a1d23, roughness: 0.9, metalness: 0.05}));
    desk.rotation.x = -Math.PI / 2;
    desk.position.y = FLOOR;
    desk.receiveShadow = true;
    scene.add(desk);
    // with a see-through background the desk goes, but not the shadows on it
    const shadows = this.deskShadows = new THREE.Mesh(desk.geometry, new THREE.ShadowMaterial({opacity: 0.4}));
    shadows.rotation.x = -Math.PI / 2;
    shadows.position.y = FLOOR;
    shadows.receiveShadow = true;
    shadows.visible = false;
    scene.add(shadows);

    this.buildFrame();

    const geo = cardGeometry();
    const blank = new THREE.CanvasTexture(document.createElement('canvas'));
    for (let i = 0; i < POOL; i++) {
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
    this.fitPoints = this.computeFitPoints();
  }

  buildFrame() {
    const frameMat = new THREE.MeshStandardMaterial({color: 0x2a2f38, roughness: 0.42, metalness: 0.7});
    const steel = new THREE.MeshStandardMaterial({color: 0xc3cad4, roughness: 0.28, metalness: 0.9});
    const chrome = new THREE.MeshStandardMaterial({color: 0x48505e, roughness: 0.3, metalness: 0.85});
    // the knobs and spindle discs: pastel, taking on the colour of the card at the front
    const pastel = this.wheelMat = new THREE.MeshStandardMaterial({color: 0xfff2a6, roughness: 0.38, metalness: 0.08});
    this.wheelTarget = new THREE.Color(0xfff2a6);
    const rubber = new THREE.MeshStandardMaterial({color: 0x1b1d22, roughness: 0.8, metalness: 0.05});
    const add = (m, parent = this.scene) => { m.castShadow = m.receiveShadow = true; parent.add(m); return m; };

    // base tray and the deck the fallen cards rest above
    add(new THREE.Mesh(new THREE.BoxGeometry(FRAME_INNER_W + FRAME_WALL_T * 2 + 0.3, 0.26, 3.6), frameMat)).position.set(0, FLOOR + 0.13, 0.1);
    add(new THREE.Mesh(new THREE.BoxGeometry(FRAME_INNER_W - 0.1, 0.12, 1.85), rubber)).position.set(0, FLOOR + 0.3, 0.95);

    // side frames, shaped around the drum
    const wall = x => {
      const s = new THREE.Shape();
      const b = FLOOR + 0.25;
      s.moveTo(-1.6, b);
      s.lineTo(1.75, b);
      s.lineTo(1.75, b + 0.45);
      s.lineTo(0.85, 0.65);
      s.absarc(0, 0.35, 0.92, 0.3, Math.PI - 0.15, false);
      s.lineTo(-1.45, b + 0.6);
      s.lineTo(-1.6, b);
      const g = new THREE.ExtrudeGeometry(s, {depth: FRAME_WALL_T, bevelEnabled: true, bevelSegments: 3, steps: 1, bevelSize: 0.03, bevelThickness: 0.03});
      g.translate(0, 0, -FRAME_WALL_T / 2);
      const m = add(new THREE.Mesh(g, frameMat));
      m.rotation.y = Math.PI / 2;
      m.position.x = x;
    };
    wall(-(FRAME_INNER_W / 2 + FRAME_WALL_T / 2));
    wall(FRAME_INNER_W / 2 + FRAME_WALL_T / 2);

    // the bar the standing cards lean against
    const bar = add(new THREE.Mesh(new THREE.CylinderGeometry(0.065, 0.065, FRAME_INNER_W + FRAME_WALL_T * 2, 20), steel));
    bar.rotation.z = Math.PI / 2;
    bar.position.set(0, 0.72, -0.34);
    const sleeve = add(new THREE.Mesh(new THREE.CylinderGeometry(0.082, 0.082, FRAME_INNER_W - 0.18, 20), rubber));
    sleeve.rotation.z = Math.PI / 2;
    sleeve.position.set(0, 0.72, -0.34);

    // the drum: axle, spindle discs and the two knobs (they turn with the cards)
    const rotor = this.rotor = new THREE.Group();
    rotor.position.set(0, AXLE_Y, 0);
    this.scene.add(rotor);
    const axle = add(new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, FRAME_INNER_W + 1.35, 24), steel), rotor);
    axle.rotation.z = Math.PI / 2;
    for (const x of [-CARD_W * 0.24, CARD_W * 0.24]) {
      const disc = add(new THREE.Mesh(new THREE.CylinderGeometry(R + 0.06, R + 0.06, 0.07, 32), pastel), rotor);
      disc.rotation.z = Math.PI / 2;
      disc.position.x = x;
    }
    const knob = x => {
      const k = new THREE.Group();
      k.position.x = x;
      const body = add(new THREE.Mesh(new THREE.CylinderGeometry(0.58, 0.58, 0.34, 40), pastel), k);
      body.rotation.z = Math.PI / 2;
      const trim = add(new THREE.Mesh(new THREE.CylinderGeometry(0.6, 0.6, 0.06, 40), steel), k);
      trim.rotation.z = Math.PI / 2;
      const ridge = new THREE.BoxGeometry(0.32, 0.035, 0.035);
      for (let i = 0; i < 28; i++) {
        const a = i / 28 * Math.PI * 2;
        const m = new THREE.Mesh(ridge, steel);
        m.position.set(0, Math.cos(a) * 0.585, Math.sin(a) * 0.585);
        m.rotation.x = -a;
        k.add(m);
      }
      rotor.add(k);
    };
    const kx = FRAME_INNER_W / 2 + FRAME_WALL_T + 0.28;
    knob(-kx);
    knob(kx);
  }

  /* ---------- card positions ---------- */

  // How far a card leans (radians about the drum axis) at drum angle a: standing cards lean back
  // against the bar, fanning a little further back the further round they are; past the top they lie forward.
  tiltFor(a, v) {
    const bias = (mod(v, 16) - 8) * 0.0045;   // keeps neighbouring cards from sharing a plane
    if (a <= FLIP) return -0.22 + Math.max(-0.55, a * 0.24) + bias;
    return Math.min(2.45, 1.86 + (a - FLIP) * 0.18 + bias);
  }

  mountAt(a) { return new THREE.Vector3(0, AXLE_Y + Math.cos(a) * R, Math.sin(a) * R); }

  // On the drum alone the cards behind would hide completely behind the front one, so each is raised
  // until its top band shows above the card in front of it (their lower parts stay hidden behind it).
  liftFor(d) {
    if (d <= 0) return 0;
    const c = Math.cos(this.phi), s = Math.sin(this.phi);
    const up = dd => {   // how high a card's top edge sits on screen, roughly
      const a = -dd * STEP, t = this.tiltFor(a, 8), m = this.mountAt(a);
      return (m.y + Math.cos(t) * CARD_H) * c - (m.z + Math.sin(t) * CARD_H) * s;
    };
    return Math.max(0, (up(0) + PEEK * Math.min(d, BEHIND) - up(Math.min(d, BEHIND))) / c);
  }

  /** Where a card d places behind the front one (d < 0: fallen in front) stands, and how it leans. */
  place(d, v) {
    const a = -d * STEP;
    const m = this.mountAt(a);
    m.y += this.liftFor(d);
    return {a, mount: m, tilt: this.tiltFor(a, v)};
  }

  // Points that must stay in view: the front card, the fallen ones, the cards behind, the frame
  computeFitPoints() {
    const pts = [];
    const cardTop = (d, along = CARD_H) => {
      const {mount: m, tilt: t} = this.place(d, 8);
      for (const x of [-CARD_W / 2, CARD_W / 2]) pts.push(new THREE.Vector3(x, m.y + Math.cos(t) * along, m.z + Math.sin(t) * along));
    };
    cardTop(0);
    cardTop(-1);
    cardTop(1); cardTop(2); cardTop(3);
    const bx = FRAME_INNER_W / 2 + FRAME_WALL_T + 0.15;
    for (const x of [-bx, bx]) for (const z of [-1.7, 1.9]) pts.push(new THREE.Vector3(x, FLOOR, z));
    const kx = FRAME_INNER_W / 2 + FRAME_WALL_T + 0.45;
    for (const x of [-kx, kx]) for (const [y, z] of [[0.6, 0], [-0.6, 0], [0, 0.6], [0, -0.6]]) pts.push(new THREE.Vector3(x, AXLE_Y + y, z));
    return pts;
  }

  /* ---------- items ---------- */

  /** Shows these items. focusId: the item to put at the front (else the same position as before). */
  setItems(items, focusId) {
    const prevIndex = this.index();
    for (const v of [...this.assigned.keys()]) this.release(v);
    this.items = items;
    this.N = items.length;
    this.L = Math.max(this.N, MIN_RING);
    let i = focusId != null ? items.findIndex(it => it.id === focusId) : -1;
    if (i < 0) i = clamp(prevIndex < 0 ? 0 : prevIndex, 0, Math.max(0, this.N - 1));
    this.pos = this.target = i;
    this.vel = 0;
    this.mode = 'idle';
    this.shown = -1;
    this.dropUnusedTextures();
    this.wake();
  }

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
    back.color.set(muted(item.face.color));   // a fallen card's back faces the light: kept quiet so the front card stands out
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

  update(dt) {
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

    // which cards are near enough the front to draw
    const lo = Math.ceil(this.pos - FRONT), hi = Math.floor(this.pos + BEHIND);
    for (const v of [...this.assigned.keys()]) if (v < lo || v > hi) this.release(v);
    if (this.N) for (let v = lo; v <= hi; v++) if (mod(v, this.L) < this.N && !this.assigned.has(v)) this.acquire(v);

    for (const card of this.assigned.values()) {
      const d = card.v - this.pos;
      const {a, mount, tilt: want} = this.place(d, card.v);
      const flipped = a > FLIP;
      if (card.fresh) {
        card.tilt = want;
        card.tvel = 0;
        card.flipped = flipped;
        card.fresh = false;
      } else {
        // a springy hinge: the card swings and settles with a little bounce
        const k = flipped ? 195 : 150, damp = flipped ? 15.5 : 16.5;
        card.tvel += ((want - card.tilt) * k - card.tvel * damp) * dt;
        card.tilt += card.tvel * dt;
        if (flipped !== card.flipped) {
          card.flipped = flipped;
          this.sound.play(0.45 + Math.abs(this.vel) * 0.08);
        }
        if (Math.abs(card.tvel) > 0.002 || Math.abs(want - card.tilt) > 0.001) moving = true;
      }
      card.group.position.copy(mount);
      card.hinge.rotation.x = card.tilt;
      const op = d > BEHIND - 1 ? clamp(BEHIND - d, 0, 1) : d < -(FRONT - 0.8) ? clamp((d + FRONT) / 0.8, 0, 1) : 1;
      for (const mat of card.mats) mat.opacity = op;
      card.mesh.castShadow = op > 0.5;
    }
    this.rotor.rotation.x = this.pos * STEP;
    if (!this.wheelMat.color.equals(this.wheelTarget)) {
      this.wheelMat.color.lerp(this.wheelTarget, Math.min(1, dt * 6));
      const w = this.wheelMat.color, t = this.wheelTarget;
      if (Math.abs(w.r - t.r) + Math.abs(w.g - t.g) + Math.abs(w.b - t.b) < 0.006) w.copy(t);
      moving = true;
    }

    const shown = this.N ? mod(Math.round(this.pos), this.L) : -1;
    if (shown !== this.shown) {
      this.shown = shown;
      if (shown < this.N) {
        this.wheelTarget.set(this.items[shown].face.color);
        this.onChange(shown);
      }
    }
    return moving;
  }

  /* ---------- drawing ---------- */

  /**
   * The space around the cards: solid (1), or see-through, keeping this much (0 to 1) of the dark
   * background over whatever the window shows behind it. See-through, the desk goes and its shadows stay.
   */
  setBackground(alpha) {
    const clear = alpha < 1;
    this.scene.background = clear ? null : new THREE.Color(BG);
    this.renderer.setClearColor(BG, clear ? clamp(alpha, 0, 1) : 1);
    this.desk.visible = !clear;
    this.deskShadows.visible = clear;
    this.wake();
  }

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
    this.idle = moving ? 0 : this.idle + 1;
    if (this.idle < 20) this.raf = requestAnimationFrame(tt => this.frame(tt));
    else this.last = 0;   // rests until something moves
  }

  resize() {
    const w = Math.max(1, this.host.clientWidth), h = Math.max(1, this.host.clientHeight);
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.setSize(w, h);
    this.camera.aspect = w / h;
    this.fit();
    this.placeCamera();
    this.wake();
  }

  // Finds the camera distance (and height of the point it looks at) that shows all of fitPoints
  fit() {
    const cam = this.camera;
    const look = new THREE.Vector3(0, 0.4, 0.3);
    const dir = new THREE.Vector3(0, Math.sin(this.phi), Math.cos(this.phi));
    const v = new THREE.Vector3();
    const measure = dist => {
      cam.position.copy(look).addScaledVector(dir, dist);
      cam.lookAt(look);
      cam.updateMatrixWorld();
      cam.updateProjectionMatrix();
      let worst = 0, ymin = Infinity, ymax = -Infinity;
      for (const p of this.fitPoints) {
        v.copy(p).project(cam);
        worst = Math.max(worst, Math.abs(v.x) / 0.93, Math.abs(v.y) / 0.95);
        ymin = Math.min(ymin, v.y);
        ymax = Math.max(ymax, v.y);
      }
      return {worst, mid: (ymin + ymax) / 2};
    };
    let dist = 10;
    for (let round = 0; round < 3; round++) {
      let lo = 3, hi = 80;
      for (let i = 0; i < 24; i++) {
        const mid = (lo + hi) / 2;
        if (measure(mid).worst > 1) lo = mid; else hi = mid;
      }
      dist = hi;
      const m = measure(dist);
      look.y += m.mid * Math.tan(THREE.MathUtils.degToRad(cam.fov / 2)) * dist;
    }
    this.dist = dist;
    this.lookAt.copy(look);
  }

  placeCamera() {
    const c = this.camera, d = this.dist;
    c.position.set(
      this.lookAt.x + d * Math.sin(this.theta) * Math.cos(this.phi),
      this.lookAt.y + d * Math.sin(this.phi),
      this.lookAt.z + d * Math.cos(this.theta) * Math.cos(this.phi));
    c.lookAt(this.lookAt);
    c.updateProjectionMatrix();
  }

  resetView() {
    const from = this.theta, t0 = performance.now();
    const tick = () => {
      const k = clamp((performance.now() - t0) / 350, 0, 1), e = 1 - Math.pow(1 - k, 3);
      this.theta = from * (1 - e);
      this.placeCamera();
      this.wake();
      if (k < 1) requestAnimationFrame(tick);
    };
    tick();
  }

  /** Where the front card is on the page (for the editor to grow out of). */
  frontRect() {
    const card = [...this.assigned.values()].find(c => Math.abs(c.v - this.pos) < 0.5);
    if (!card) return null;
    card.group.updateMatrixWorld(true);
    const box = this.renderer.domElement.getBoundingClientRect();
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const [x, y] of [[-CARD_W / 2, 0], [CARD_W / 2, 0], [-CARD_W / 2, CARD_H], [CARD_W / 2, CARD_H]]) {
      const p = new THREE.Vector3(x, y, 0).applyMatrix4(card.mesh.matrixWorld).project(this.camera);
      const sx = box.left + (p.x + 1) / 2 * box.width, sy = box.top + (1 - p.y) / 2 * box.height;
      x0 = Math.min(x0, sx); x1 = Math.max(x1, sx); y0 = Math.min(y0, sy); y1 = Math.max(y1, sy);
    }
    return {left: x0, top: y0, width: x1 - x0, height: y1 - y0};
  }

  setSound(on) { this.sound.on = on; }

  /* ---------- input: drag up/down to turn, sideways to look around, tap a card, mouse wheel ---------- */

  initInput() {
    const el = this.renderer.domElement;
    el.style.touchAction = 'none';
    const ray = new THREE.Raycaster();
    const ndc = new THREE.Vector2();
    const pick = (x, y) => {
      const b = el.getBoundingClientRect();
      ndc.set((x - b.left) / b.width * 2 - 1, -((y - b.top) / b.height) * 2 + 1);
      ray.setFromCamera(ndc, this.camera);
      const meshes = [...this.assigned.values()].filter(c => c.mats[0].opacity > 0.5).map(c => c.mesh);
      const hit = ray.intersectObjects(meshes, false)[0];
      return hit ? hit.object.userData.card : null;
    };

    // Pressing on a wheel that's visibly turning grabs it (stops it) rather than clicking a card. The last
    // moments of easing into place don't count: by then the wheel looks still, and a click there opens
    // the card it's settling on.
    const visiblyMoving = () =>
      this.mode === 'drag' ||
      (this.mode === 'free' && Math.abs(this.vel) > 1.5) ||
      (this.mode === 'target' && (Math.abs(this.target - this.pos) > 0.2 || Math.abs(this.vel) > 2));

    let g = null;   // the gesture in progress
    el.addEventListener('pointerdown', e => {
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

    el.addEventListener('pointermove', e => {
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
        this.pos += dy / Math.max(40, this.host.clientHeight / 11);
        const t = performance.now();
        g.samples.push({t, pos: this.pos});
        while (g.samples.length > 2 && t - g.samples[0].t > 90) g.samples.shift();
        this.wake();
      } else if (g.axis === 'look') {
        this.theta = clamp(this.theta - dx * 0.0065, -0.85, 0.85);
        this.placeCamera();
        this.wake();
      }
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
      if (gesture.axis || e.type === 'pointercancel' || gesture.grabbed) return;
      const card = pick(e.clientX, e.clientY);
      if (!card) return;
      // the front card is the one the wheel is resting on, or settling on
      const front = this.mode === 'target' ? this.target : Math.round(this.pos);
      if (card.v === front) this.onOpen(card.item, mod(card.v, this.L));
      else { this.target = card.v; this.mode = 'target'; this.wake(); }
    };
    el.addEventListener('pointerup', end);
    el.addEventListener('pointercancel', end);

    // One notch of a mouse wheel turns one card; a touchpad's small steps add up to one
    el.addEventListener('wheel', e => {
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
