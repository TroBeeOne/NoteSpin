// The scroll (a second view, Settings → Spinner → Style): the cards ride round the outside of a drum lying on its
// side, which isn't drawn. The front card faces you; the ones before it curve away above it and the ones after it
// below, so the cards read down the screen like a list, fading out as they go over the top and under the bottom.
// The mouse wheel and the arrow keys go to the next card as on the card wheel, and the cards follow a drag. Just
// the cards: no base, no desk, so the space around them can be see-through or the desktop, as with the card wheel.
// What all views share (turning and settling on a card, clicking, the faces, the pool of cards) is in cardview.js.

import * as THREE from 'three';
import {CardView, BG, clamp, cardGeometry, studioEnvironment} from './cardview.js';

const CARD_W = 3.4, CARD_H = 2.6, CARD_T = 0.016;   // the card wheel's cards, without the slots for its drum
const GAP = 0.24;                   // between neighbouring cards, round the drum
const STEP = 0.56;                  // the drum's angle from one card to the next
const R = (CARD_H + GAP) / STEP;    // the drum's radius at the cards
const FADE_FROM = 0.85, FADE_TO = 1.4;   // the cards fade out between these angles from the front (radians)
const REACH = Math.ceil(FADE_TO / STEP);  // cards drawn before and after the front one
const POOL = REACH * 2 + 2;

const smooth = (a, b, x) => { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); };
const opacityAt = a => 1 - smooth(FADE_FROM, FADE_TO, Math.abs(a));

// Where a card d places after the front one (d < 0: before it) stands: at angle a round the drum (after the front:
// down and under), its middle at (0, y, z), facing out from the drum
function place(d) {
  const a = -d * STEP;
  return {a, y: R * Math.sin(a), z: R * Math.cos(a) - R};
}

export class ScrollView extends CardView {
  // the same card never shows twice: with fewer items, an empty stretch follows the last (as on the card wheel)
  get minRing() { return REACH * 2 + 2; }

  /* ---------- scene ---------- */

  initScene() {
    this.cardW = CARD_W;
    this.cardH = CARD_H;
    this.dist = 10;
    this.cardPx = 0;
    this.lastNear = null;
    const scene = this.scene = new THREE.Scene();
    scene.background = new THREE.Color(BG);
    scene.environment = studioEnvironment(this.renderer);
    scene.environmentIntensity = 0.55;
    this.camera = new THREE.PerspectiveCamera(36, 1, 0.1, 80);

    // the card wheel's lights, the main one more from the front: the cards curving away above and below are lit
    // alike, a little less as they turn from it
    scene.add(new THREE.AmbientLight(0xeef2ff, 1.1));
    const key = new THREE.DirectionalLight(0xfff6ea, 2.2);
    key.position.set(3, 3.5, 9);
    scene.add(key);
    const fill = new THREE.DirectionalLight(0xb9d3ff, 0.55);
    fill.position.set(-6, 4, 4);
    scene.add(fill);
    const rim = new THREE.DirectionalLight(0xffd88a, 0.45);
    rim.position.set(0, 6, -7);
    scene.add(rim);

    this.makePool(cardGeometry({w: CARD_W, h: CARD_H, t: CARD_T}), POOL);
    for (const card of this.free) card.mesh.position.y = -CARD_H / 2;   // each card turns about its middle

    // the cue under the front card ("Click to edit"): in the middle of the gap between it and the next one down
    const next = place(1), a = next.a;
    this.hintPoint = new THREE.Vector3(0,
      (-CARD_H / 2 + next.y + CARD_H / 2 * Math.cos(a)) / 2,
      (0 + next.z - CARD_H / 2 * Math.sin(a)) / 2);
  }

  hintAt() { return this.hintPoint; }

  /* ---------- moving ---------- */

  layout() {
    this.syncCards(Math.ceil(this.pos - REACH), Math.floor(this.pos + REACH));
    for (const card of this.assigned.values()) {
      const {a, y, z} = place(card.v - this.pos);
      const op = opacityAt(a);
      card.group.visible = op > 0.002;
      card.group.position.set(0, y, z);
      card.hinge.rotation.x = -a;
      for (const mat of card.mats) mat.opacity = op;
    }
    // a click as each card goes by the front (not when the cards are just put in place)
    const near = Math.round(this.pos);
    if (near !== this.lastNear) {
      if (this.lastNear != null && this.mode !== 'idle' && Math.abs(near - this.lastNear) === 1) this.sound.play(0.35 + Math.abs(this.vel) * 0.06);
      this.lastNear = near;
    }
    return false;
  }

  // The cards follow the pointer: dragging up by the distance between two cards' middles brings the next one up
  dragTurn(dy) { return -dy / Math.max(40, this.cardPx); }

  /* ---------- drawing ---------- */

  // The camera, straight on, as near as it can be with everything that shows inside the view: the front card's
  // width, and each card's far edge wherever it is as it curves away, until it has all but faded out (so nothing
  // is ever cut off at the view's edge: with the window frame hidden, that edge is on the desktop)
  fitView() {
    const cam = this.camera, tan = Math.tan(THREE.MathUtils.degToRad(cam.fov / 2));
    let forHeight = 0;
    for (let a = 0; opacityAt(a) > 0.06; a += 0.01) {
      const y = R * Math.sin(a) + CARD_H / 2 * Math.cos(a), z = R * Math.cos(a) - R - CARD_H / 2 * Math.sin(a);
      forHeight = Math.max(forHeight, y / (0.97 * tan) + z);   // as near as the edge can be and stay in view
    }
    const forWidth = CARD_W / 2 / (0.9 * tan * cam.aspect);
    this.dist = Math.max(forHeight, forWidth);
    cam.position.set(0, 0, this.dist);
    cam.lookAt(0, 0, 0);
    cam.updateProjectionMatrix();
    cam.updateMatrixWorld();
    const next = place(1);
    this.cardPx = Math.abs(new THREE.Vector3(0, next.y, next.z).project(cam).y) * this.host.clientHeight / 2;
  }

  /** The narrowest the view can be (CSS pixels) and still show everything: the front card is the widest. */
  snugWidth() {
    const x = Math.abs(new THREE.Vector3(CARD_W / 2, 0, 0).project(this.camera).x);
    return Math.ceil(this.host.clientWidth * Math.min(1, x / 0.9)) + 2;
  }
}
