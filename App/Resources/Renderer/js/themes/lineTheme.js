import * as THREE from '../../vendor/three.module.min.js';
import { gapState, retimedLine } from '../core/timeline.js';
import { layoutRows } from '../core/layout.js';
import { stickerCues } from '../core/stickers.js';
import { clamp01, smoothstep, easeInCubic, easeOutCubic, easeOutBack, damp, seeded } from '../core/easing.js';
import { CAMERA_Z } from '../engine/stage.js';

/** Only show the "waiting" dots for breaks at least this long. */
const MIN_GAP_VIEW = 2.5;
const GAP_EXIT = 0.35;
const APPEAR = 0.35;
const DEFAULT_SMOOTHING = 10;
const STICKER_POP = 0.45;
const STICKER_EXIT = 0.35;
const STICKER_MARGIN = 1.3;
const CONFETTI_LIFE = 2.6;
const CONFETTI_RISE = 5;

/**
 * Base for all themes. Tracks the active lyric line per track (so during an AutoMix
 * blend the outgoing song keeps its own views), creates a view when a line starts,
 * keeps retired views alive while they animate out, and manages emoji/photo stickers.
 *
 * Subclasses set `static font` / `static textColor` / `static textStyle` and implement:
 * build(), updateScene(frame), createLine(deck, line, index, frame),
 * updateLine(view, deck, frame), exitLine(view, deck, frame) → still animating?
 * They may override stickerAnchor() / updateSticker() for custom sticker behaviour.
 */
export class LineTheme {
  static font = 'bold';
  static textColor = '#ffffff';
  static textStyle = {};
  /** Max emissive for words; higher lets white text on dark scenes light up fully. */
  static glowLimit = 0.55;
  /** Seconds before its cue a sticker starts animating, so its entrance can land on the word. */
  static stickerLead = 0;

  constructor(engine) {
    this.engine = engine;
    this.stage = engine.stage;
    this.factory = engine.fonts[this.constructor.font] ?? engine.fonts.bold;
    this.sparkles = engine.sparkles;
    this.root = new THREE.Group();
    this.tracks = new Map();
    this.ownedDisposables = [];
  }

  // MARK: Lifecycle

  activate() {
    this.stage.reset();
    this.stage.scene.add(this.root);
    this.build();
  }

  deactivate() {
    this.tracks.forEach((track) => [track.view, ...track.retired].forEach((v) => v && this.disposeView(v)));
    this.tracks.clear();
    this.root.removeFromParent();
    this.root.traverse((object) => {
      object.geometry?.dispose();
      [object.material].flat().forEach((m) => m?.dispose());
    });
    this.ownedDisposables.forEach((d) => d.dispose());
    this.stage.reset();
  }

  setArtwork() {}
  build() {}
  updateScene() {}

  // MARK: Frame

  update(frame) {
    this.updateScene(frame);
    const seen = new Set(frame.decks.map((d) => d.trackID));

    frame.decks.forEach((deck) => {
      const track = this.tracks.get(deck.trackID) ?? { index: null, view: null, retired: [], deck };
      this.tracks.set(deck.trackID, track);
      track.deck = deck;
      const index = deck.doc ? deck.lineIndex : null;
      if (index !== track.index) {
        this.#retire(track, frame);
        track.view = index === null ? null : this.#createView(deck, index, frame);
        track.index = index;
      }
      if (track.view) {
        this.#adoptRetiming(track.view, deck);
        this.#placeHolder(track.view, deck);
        if (track.view.kind === 'gap') {
          this.updateGap(track.view, deck, frame);
        } else {
          this.updateLine(track.view, deck, frame);
          this.#updateStickers(track.view, deck, frame);
        }
      }
      this.#updateRetired(track, deck, frame);
    });

    this.tracks.forEach((track, id) => {
      if (seen.has(id)) return;
      this.#retire(track, frame);
      const gone = { ...track.deck, opacity: Math.max(0, track.deck.opacity - frame.dt * 3) };
      track.deck = gone;
      this.#updateRetired(track, gone, frame);
      if (track.retired.length === 0) this.tracks.delete(id);
    });
  }

  #createView(deck, index, frame) {
    const lines = deck.doc.lines;
    const gap = gapState(lines, index, deck.position);
    if (gap.inGap) return gap.untilNext >= MIN_GAP_VIEW ? this.createGap(deck, index, frame) : null;
    return this.createLine(deck, lines[index], index, frame);
  }

  #retire(track, frame) {
    if (!track.view) return;
    track.view.exitStart = frame.now;
    track.view.items?.forEach((item) => {
      item.exitFrom = item.object.position.clone();
      item.exitScale = item.object.scale.clone();
    });
    track.view.stickers?.forEach((s) => {
      s.exitScale = s.object.scale.x;
      s.exitFrom = s.object.position.clone();
      s.exitOpacity = s.material.opacity;
    });
    this.onExitStart(track.view, frame);
    track.retired.push(track.view);
    track.view = null;
  }

  #updateRetired(track, deck, frame) {
    track.retired = track.retired.filter((view) => {
      const stickersAlive = this.#exitStickers(view, deck, frame);
      const alive = view.kind === 'gap' ? this.exitGap(view, deck, frame) : this.exitLine(view, deck, frame);
      if (!alive && !stickersAlive) this.disposeView(view);
      return alive || stickersAlive;
    });
  }

  /** Switches a line on screen to updated word timings (learned singer pace) without restarting it. */
  #adoptRetiming(view, deck) {
    if (view.kind !== 'line') return;
    const latest = retimedLine(view.line, deck.doc, view.index);
    if (!latest) return;
    view.line = latest;
    view.items.forEach((item) => { item.word = latest.w[item.i] ?? item.word; });
  }

  /** Outgoing songs drift up and back as they fade. */
  #placeHolder(view, deck) {
    const fade = deck.role === 'outgoing' ? 1 - deck.opacity : 0;
    view.holder.position.set(0, fade * 2.4, -fade * 6);
  }

  onExitStart() {}

  // MARK: View helpers

  newView(kind, line, index, frame) {
    const holder = new THREE.Group();
    const group = new THREE.Group();
    holder.add(group);
    this.root.add(holder);
    return { kind, line, index, holder, group, items: [], extras: [], stickers: new Map(), born: frame.now, exitStart: null };
  }

  disposeView(view) {
    view.items.forEach((item) => item.mesh?.dispose());
    view.stickers.forEach((sticker) => sticker.dispose());
    view.extras.forEach((object) => {
      object.removeFromParent();
      object.traverse?.((o) => {
        o.geometry?.dispose();
        [o.material].flat().forEach((m) => m?.dispose());
      });
    });
    view.holder.removeFromParent();
  }

  /** Width of a word at font size 1. */
  measure(text) {
    return this.factory.canExtrude(text) ? this.factory.geometry(text).width : this.factory.texture(text).width * 0.85;
  }

  /**
   * Lays a line's words out in centred rows at depth `z`.
   * @returns {{text: string, word: object, home: THREE.Vector3, size: number}[]}
   */
  layout(line, { fontSize = 1.2, z = 0, gap = 0.45, rowGap = 1.6, widthFactor = 0.78 } = {}) {
    const texts = line.w.map((w) => this.factory.display(w.t));
    const maxWidth = this.stage.viewSize(CAMERA_Z - z).width * widthFactor;
    const rows = layoutRows(texts.map((t) => this.measure(t) * fontSize), maxWidth, gap * fontSize);
    const size = fontSize * rows.scale;
    const top = ((rows.rowCount - 1) * rowGap * size) / 2;
    return texts.map((text, i) => ({
      text,
      word: line.w[i],
      size,
      home: new THREE.Vector3(rows.items[i].x, top - rows.items[i].row * rowGap * size, z),
    }));
  }

  /** Creates a WordMesh per laid-out word in the theme's text colour and material. */
  addWords(view, laid) {
    const { textColor, textStyle, glowLimit } = this.constructor;
    view.items = laid.map((entry, i) => {
      const mesh = this.factory.make(entry.text, textColor, textStyle, glowLimit);
      mesh.object.scale.setScalar(entry.size);
      mesh.object.position.copy(entry.home);
      view.group.add(mesh.object);
      return { ...entry, mesh, object: mesh.object, color: textColor, width: this.measure(entry.text) * entry.size, i, fired: false };
    });
    return view.items;
  }

  /**
   * Eases a per-item visual value toward `target` (frame-rate independent) so glow,
   * opacity and scale never change in a single frame. Starts at the target the first time.
   */
  smooth(item, key, target, dt, rate = DEFAULT_SMOOTHING) {
    const value = item[key] === undefined ? target : damp(item[key], target, rate, dt);
    item[key] = value;
    return value;
  }

  /** 0→1 fade-in over the first moments of a view's life. */
  appear(view, now) {
    return easeOutCubic(clamp01((now - view.born) / APPEAR));
  }

  /** World position of an object (for sparkle bursts). */
  worldOf(object) {
    return object.getWorldPosition(new THREE.Vector3());
  }

  /** One-shot sparkle burst the first time a word starts being sung (not when seeking past it). */
  popOnce(item, phase, { count = 14, speed = 4.5, kick = 0.35 } = {}) {
    if (item.fired) return;
    if (phase.state === 'ahead') return;
    item.fired = true;
    if (phase.since > 0.3) return;
    this.sparkles.burst(this.worldOf(item.object), '#ffffff', count, speed);
    this.engine.kick(kick);
  }

  // MARK: Stickers (emotion emoji + pictures of things mentioned)

  #updateStickers(view, deck, frame) {
    const insight = this.engine.insights.get(deck.trackID)?.get(view.index) ?? null;
    const cues = stickerCues(insight, view.line);
    const objectCues = cues.filter((c) => c.role === 'object');
    cues.forEach((cue) => {
      if (view.stickers.has(cue.key)) return;
      const made = this.engine.stickers.make(cue);
      if (!made) return;
      made.object.visible = false;
      const slot = cue.role === 'object' ? objectCues.indexOf(cue) : Number(cue.key.split(':')[1] ?? 0);
      const sticker = {
        ...made, cue, slot, count: Math.max(1, objectCues.length), born: null,
        anchor: cue.role === 'confetti'
          ? this.#confettiOrigin(view.index, slot)
          : this.stickerAnchor(view, cue, slot, objectCues.length, made.kind),
      };
      view.group.add(made.object);
      view.stickers.set(cue.key, sticker);
    });
    const lead = this.constructor.stickerLead;
    view.stickers.forEach((sticker) => {
      if (sticker.born === null && deck.position >= sticker.cue.appearAt - lead) sticker.born = frame.now;
      if (sticker.born === null || sticker.expired) {
        sticker.object.visible = false;
        return;
      }
      sticker.object.visible = true;
      if (sticker.cue.role === 'confetti') {
        this.#updateConfetti(sticker, deck, frame);
      } else {
        this.updateSticker(sticker, view, deck, frame);
        this.#applyLifetime(sticker, frame.now, lead);
      }
    });
  }

  /** Short-lived stickers (the mood emoji) shrink away after their lifetime instead of lingering. */
  #applyLifetime(sticker, now, lead) {
    const lifetime = sticker.cue.lifetime;
    if (!lifetime) return;
    const end = lead + lifetime;
    const k = smoothstep(end - STICKER_EXIT, end, now - sticker.born);
    sticker.material.opacity *= 1 - k;
    sticker.object.scale.multiplyScalar(Math.max(0.001, 1 - k));
    if (k >= 1) sticker.expired = true;
  }

  /** Mood-change shower: emoji rise from the screen edges, spin gently and fade. */
  #confettiOrigin(lineIndex, slot) {
    const { width, height } = this.stage.viewSize(CAMERA_Z);
    const r = (k) => seeded(lineIndex * 53 + slot * 11 + k);
    const side = (slot + lineIndex) % 2 === 0 ? -1 : 1;
    return new THREE.Vector3(side * (width / 2 - 1.2 - r(1) * 2.5), -height / 2 + 1 + r(2) * 2, 1);
  }

  #updateConfetti(sticker, deck, { now }) {
    const age = now - sticker.born;
    const k = clamp01(age / CONFETTI_LIFE);
    sticker.object.position.copy(sticker.anchor);
    sticker.object.position.y += easeOutCubic(k) * CONFETTI_RISE;
    sticker.object.position.x += Math.sin(age * 3 + sticker.slot) * 0.3;
    sticker.object.rotation.z = Math.sin(age * 2 + sticker.slot) * 0.5;
    sticker.object.scale.setScalar(Math.max(0.001, 0.7 * easeOutBack(clamp01(age / 0.35), 2)));
    sticker.material.opacity = (1 - smoothstep(0.6, 1, k)) * deck.opacity;
    if (k >= 1) sticker.object.visible = false;
  }

  /** Default placement: around the text block, kept on screen. */
  stickerAnchor(view, cue, slot) {
    const halfW = Math.max(1.5, ...view.items.map((it) => Math.abs(it.home.x) + it.width / 2));
    const halfH = Math.max(0.6, ...view.items.map((it) => Math.abs(it.home.y) + it.size * 0.5));
    const maxX = this.stage.viewSize(CAMERA_Z).width / 2 - STICKER_MARGIN;
    const spots = cue.role === 'emotion'
      ? [[halfW * 0.55, halfH + 1.3]]
      : [
        [-halfW - 1.5, 0.4], [halfW + 1.5, -0.4], [-halfW * 0.45, -halfH - 1.5], [halfW * 0.45, -halfH - 1.5],
        [-halfW * 0.6, halfH + 1.4], [-halfW - 1.6, -1.8], [halfW + 1.6, 1.6], [0, -halfH - 2.8],
      ];
    const [x, y] = spots[slot % spots.length];
    // Not enough room beside the text: tuck above/below instead.
    const fitted = Math.abs(x) > maxX ? [Math.sign(x) * Math.min(maxX, halfW * 0.6), y + (y >= 0 ? halfH + 1.2 : -halfH - 1.2)] : [x, y];
    return new THREE.Vector3(fitted[0], fitted[1], 0.6);
  }

  /** Default motion: springy pop-in, gentle bob and sway. */
  updateSticker(sticker, view, deck, { now }) {
    const age = now - sticker.born;
    const pop = Math.max(0.001, easeOutBack(clamp01(age / STICKER_POP), 2.2));
    const settle = 1 - clamp01(age / STICKER_POP);
    sticker.object.position.copy(sticker.anchor);
    sticker.object.position.y += Math.sin(now * 1.6 + sticker.slot * 1.3) * 0.12;
    sticker.object.rotation.z = Math.sin(now * 1.1 + sticker.slot * 2) * 0.1 + settle * 0.6 * (sticker.slot % 2 ? 1 : -1);
    sticker.object.scale.setScalar(pop);
    sticker.material.opacity = clamp01(age / 0.2) * deck.opacity;
  }

  /** @returns {boolean} whether any sticker is still animating out. */
  #exitStickers(view, deck, { now }) {
    if (!view.stickers?.size) return false;
    const k = clamp01((now - view.exitStart) / STICKER_EXIT);
    view.stickers.forEach((sticker) => {
      if (sticker.born === null || sticker.expired) return;
      this.exitSticker(sticker, deck, k);
    });
    return k < 1;
  }

  /** Default sticker exit: shrink and fade with the line. `k` runs 0→1. */
  exitSticker(sticker, deck, k) {
    sticker.object.scale.setScalar(Math.max(0.001, (sticker.exitScale ?? 1) * (1 - easeInCubic(k))));
    sticker.material.opacity = (1 - k) * deck.opacity;
  }

  // MARK: Default instrumental-break view: three dots that fill up until the next line.

  createGap(deck, index, frame) {
    const view = this.newView('gap', deck.doc.lines[index] ?? null, index, frame);
    const geometry = new THREE.SphereGeometry(0.3, 32, 20);
    view.dots = [0, 1, 2].map((i) => {
      const material = new THREE.MeshPhysicalMaterial({
        color: this.constructor.textColor, emissive: this.constructor.textColor, emissiveIntensity: 0,
        roughness: 0.2, clearcoat: 1, transparent: true, ...this.constructor.textStyle,
      });
      const mesh = new THREE.Mesh(geometry, material);
      mesh.position.x = (i - 1) * 1.1;
      view.group.add(mesh);
      return mesh;
    });
    view.extras.push(view.group);
    return view;
  }

  updateGap(view, deck, frame) {
    const gap = gapState(deck.doc.lines, deck.lineIndex, deck.position);
    const appear = clamp01((frame.now - view.born) / 0.4);
    const anticipation = gap.untilNext < 0.6 ? clamp01(gap.untilNext / 0.6) : 1;
    view.dots.forEach((dot, i) => {
      const lit = smoothstep(i / 3, (i + 1) / 3, gap.progress);
      dot.position.y = Math.abs(Math.sin(frame.now * 4 + i * 0.7)) * 0.3 * (0.4 + lit);
      dot.scale.setScalar((0.6 + 0.5 * lit + frame.pulse * 0.1) * appear * anticipation);
      dot.material.emissiveIntensity = lit * 0.4;
      dot.material.opacity = (0.3 + 0.7 * lit) * deck.opacity;
    });
  }

  exitGap(view, deck, frame) {
    const k = clamp01((frame.now - view.exitStart) / GAP_EXIT);
    view.dots.forEach((dot) => {
      dot.scale.multiplyScalar(1 - easeInCubic(k) * 0.5);
      dot.material.opacity *= 1 - k;
    });
    return k < 1;
  }
}
