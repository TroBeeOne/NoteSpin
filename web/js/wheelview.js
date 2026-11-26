// The card wheel (the first view, and the default): notes stand on a drum between two side frames. Turning the
// drum brings the next card up from behind; at the top it tips over and falls forward onto the deck. What all
// views share (turning, clicking, the cards' faces, the pool of cards) is in cardview.js.

import * as THREE from 'three';
import {CardView, BG, mod, clamp, cardGeometry, studioEnvironment} from './cardview.js';

const CARD_W = 3.4, CARD_H = 2.6, CARD_T = 0.016;
const STEP = Math.PI * 2 / 16;      // drum angle between neighbouring cards
const R = 0.56;                     // drum radius at the card spindles
const AXLE_Y = -0.15;
const FLIP = 0.16;                  // just past the top a card falls forward
const BEHIND = 4;                   // cards drawn behind the front one (three, and a fourth fading in)
const FRONT = 1.9;                  // fallen cards drawn on the deck: the last one, and the one before fading out
const PEEK = 0.3;                   // how much of each card behind shows above the one in front (on screen): its band...
// ...or its band and the first line of its title: the two cards right behind (the second a little less: further
// from the camera it looks smaller, so the same height shows more of it)
const TITLE_PEEKS = [0.645, 0.63];
const TITLE_BACK = 0.56;            // a card raised for its title moves back this much for each unit up
const POOL = 12;                    // cards to draw with
const FRAME_INNER_W = 3.75, FRAME_WALL_T = 0.22;
const FLOOR = -1.4;                 // the desk top; the frame stands on it

export class WheelView extends CardView {
  /* ---------- scene ---------- */

  initScene() {
    this.theta = 0;
    this.phi = 0.46;
    this.baseHidden = false;      // only the cards show (setBaseHidden)
    this.titlesBehind = true;     // the cards right behind show their titles too (see applyOptions)
    this.cardW = CARD_W;
    this.cardH = CARD_H;
    const r = this.renderer;
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFShadowMap;
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

    this.makePool(cardGeometry({w: CARD_W, h: CARD_H, t: CARD_T, slots: true}), POOL);
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
    // everything but the cards: hidden together when the spinner base is hidden (setBaseHidden)
    const mount = this.mount = new THREE.Group();
    this.scene.add(mount);
    const add = (m, parent = mount) => { m.castShadow = m.receiveShadow = true; parent.add(m); return m; };

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
    mount.add(rotor);
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

  // How far above the front card's top the top of card d behind it reaches: the strips that show of the cards
  // up to it. The cards right behind show their band and title, the rest just the band; in between
  // (the wheel turning) it's in between.
  peekTo(d) {
    let t = 0;
    for (let k = 0; k < BEHIND; k++) t += (this.titlesBehind && k < TITLE_PEEKS.length ? TITLE_PEEKS[k] : PEEK) * clamp(d - k, 0, 1);
    return t;
  }

  // How high a point sits on the screen, looking straight on, in units of the view at the distance the camera
  // looks at. With the perspective: the cards behind are raised by it, so their strips show the same whether
  // the camera is near (a wide window) or far (a narrow one).
  screenUp(y, z) {
    const c = Math.cos(this.phi), s = Math.sin(this.phi);
    const dy = y - this.lookAt.y, dz = z - this.lookAt.z;
    return (dy * c - dz * s) * this.dist / (this.dist - (dy * s + dz * c));
  }

  // How deep behind the front the card at v stands, counting only the slots with a card in them. With fewer
  // notes than minRing there's an empty stretch between the last and the first: past it, the first cards stand
  // right behind the last, as if it weren't there (it shows as empty slots at the front as the wheel turns past).
  // An empty slot counts for less as it nears the front, so nothing jumps as it goes by.
  depthAt(v) {
    const d = v - this.pos;
    if (d <= 0 || this.N >= this.L) return d;
    let empty = 0;
    for (let u = Math.floor(this.pos) + 1; u < v; u++) if (mod(u, this.L) >= this.N) empty += clamp(u - this.pos, 0, 1);
    return d - empty;
  }

  // Raised further for a title, a card also moves back: the cards lean back, so straight up its lower part
  // would come through the face of the card in front
  backFor(d) {
    if (d <= 0) return 0;
    const dd = Math.min(d, BEHIND);
    return (this.peekTo(dd) - PEEK * dd) / Math.cos(this.phi) * TITLE_BACK;
  }

  // On the drum alone the cards behind would hide completely behind the front one, so each is raised
  // until its strip shows above the card in front of it (their lower parts stay hidden behind it).
  liftFor(d) {
    if (d <= 0) return 0;
    const dd = Math.min(d, BEHIND);
    const top = (x, back) => {   // a card's top edge, as it stands on the drum
      const a = -x * STEP, t = this.tiltFor(a, 8), m = this.mountAt(a);
      return {y: m.y + Math.cos(t) * CARD_H, z: m.z - back + Math.sin(t) * CARD_H};
    };
    const front = top(0, 0), card = top(dd, this.backFor(dd));
    const want = this.screenUp(front.y, front.z) + this.peekTo(dd);
    // the height that puts its top edge there (Newton's method: it rises steadily with the height)
    const c = Math.cos(this.phi), s = Math.sin(this.phi), D = this.dist;
    let h = 0;
    for (let i = 0; i < 4; i++) {
      const dy = card.y + h - this.lookAt.y, dz = card.z - this.lookAt.z;
      const near = D - (dy * s + dz * c), along = dy * c - dz * s;
      h -= (along * D / near - want) / (D * (c * near + along * s) / (near * near));
    }
    return Math.max(0, h);
  }

  /** Where a card d places behind the front one (d < 0: fallen in front) stands, and how it leans. */
  place(d, v) {
    const a = -d * STEP;
    const m = this.mountAt(a);
    m.y += this.liftFor(d);
    m.z -= this.backFor(d);
    return {a, mount: m, tilt: this.tiltFor(a, v)};
  }

  // Points that must stay in view: the front card, the fallen ones, the cards behind, the frame (unless it's hidden)
  computeFitPoints() {
    const pts = [];
    const cardTop = (d, along = CARD_H) => {
      const {mount: m, tilt: t} = this.place(d, 8);
      for (const x of [-CARD_W / 2, CARD_W / 2]) pts.push(new THREE.Vector3(x, m.y + Math.cos(t) * along, m.z + Math.sin(t) * along));
    };
    cardTop(0);
    cardTop(-1);
    cardTop(1); cardTop(2); cardTop(3);
    if (this.baseHidden) return pts;
    const bx = FRAME_INNER_W / 2 + FRAME_WALL_T + 0.15;
    for (const x of [-bx, bx]) for (const z of [-1.7, 1.9]) pts.push(new THREE.Vector3(x, FLOOR, z));
    const kx = FRAME_INNER_W / 2 + FRAME_WALL_T + 0.45;
    for (const x of [-kx, kx]) for (const [y, z] of [[0.6, 0], [-0.6, 0], [0, 0.6], [0, -0.6]]) pts.push(new THREE.Vector3(x, AXLE_Y + y, z));
    return pts;
  }

  /* ---------- items ---------- */

  // setItems' options: titlesBehind, the cards right behind the front one show their titles (else just their bands)
  applyOptions({titlesBehind = true} = {}) {
    if (titlesBehind !== this.titlesBehind) {
      this.titlesBehind = titlesBehind;
      this.resize();
    }
  }

  /* ---------- moving ---------- */

  // Where the cards stand: the fallen ones, the front one, and those behind it, each leaning on a springy hinge
  layout(dt) {
    let moving = false;
    // which cards are near enough the front to draw: the fallen ones, and those up to BEHIND deep (see depthAt),
    // though not so far round that they're among the fallen ones again (a ring with very few notes)
    const far = this.L - FRONT + 0.8;
    const lo = Math.ceil(this.pos - FRONT);
    let hi = Math.floor(this.pos);
    while (hi + 1 - this.pos < far && this.depthAt(hi + 1) <= BEHIND) hi++;
    this.syncCards(lo, hi);

    for (const card of this.assigned.values()) {
      const d = card.v - this.pos, depth = this.depthAt(card.v);
      const {a, mount, tilt: want} = this.place(depth, card.v);
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
      // fading in at the back, out after falling; and a card that's also lying among the fallen ones (a ring of
      // very few notes) fades in as that one fades out
      const op = Math.min(depth > BEHIND - 1 ? clamp(BEHIND - depth, 0, 1) : d < -(FRONT - 0.8) ? clamp((d + FRONT) / 0.8, 0, 1) : 1,
        clamp((far - d) / 0.8, 0, 1));
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
    return moving;
  }

  // the knobs and spindle discs take on the front card's colour
  frontChanged(item) { this.wheelTarget.set(item.face.color); }

  // A sideways drag turns the view (not with the base hidden: only the cards show, straight on)
  look(dx) {
    if (this.baseHidden) return;
    this.theta = clamp(this.theta - dx * 0.0065, -0.85, 0.85);
    this.placeCamera();
    this.wake();
  }

  /* ---------- drawing ---------- */

  /** The background (see CardView): see-through, the desk goes and its shadows stay, unless shadows is false (they
   * reach well beyond the frame, so a tight window cuts them off). */
  setBackground(alpha, {shadows = true} = {}) {
    super.setBackground(alpha);
    const clear = alpha < 1;
    this.desk.visible = !clear;
    this.deskShadows.visible = clear && shadows;
  }

  /**
   * The spinner base hidden (Settings): the frame, knobs, axle and stand go, and their shadows with them.
   * The cards move as before; the view is framed around them alone (so it can be narrower) and stays straight on.
   */
  setBaseHidden(hidden) {
    this.baseHidden = hidden;
    this.mount.visible = !hidden;
    if (hidden) this.theta = 0;
    this.resize();
  }

  /**
   * The narrowest the view can be (CSS pixels) and still show everything at the size it's shown now. The view
   * is fitted to whichever of its width and height is tighter, so a wide one has room to spare at the sides.
   */
  snugWidth() {
    // measured straight on, as fit() does (the view may be turned sideways just now)
    const cam = this.camera.clone();
    cam.position.copy(this.lookAt).addScaledVector(new THREE.Vector3(0, Math.sin(this.phi), Math.cos(this.phi)), this.dist);
    cam.lookAt(this.lookAt);
    cam.updateMatrixWorld();
    cam.updateProjectionMatrix();
    const v = new THREE.Vector3();
    let x = 0;
    for (const p of this.fitPoints) x = Math.max(x, Math.abs(v.copy(p).project(cam).x));
    return Math.ceil(this.host.clientWidth * Math.min(1, x / 0.93)) + 2;
  }

  // the cards behind are raised for the camera's distance, and the distance is found for the cards: a few
  // rounds settle both
  fitView() {
    for (let i = 0; i < 3; i++) {
      this.fitPoints = this.computeFitPoints();
      this.fit();
    }
    this.placeCamera();
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
}
