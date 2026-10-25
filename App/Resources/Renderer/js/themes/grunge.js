import * as THREE from '../../vendor/three.module.min.js';
import { chooseComposition, composeLine, besideWord } from '../core/grungeLayout.js';
import { retimedLine } from '../core/timeline.js';
import { FALLBACK_PALETTE } from '../core/palette.js';
import { clamp01, easeOutCubic, seeded } from '../core/easing.js';
import { CAMERA_Z } from '../engine/stage.js';
import { PaletteBlend } from '../engine/palette.js';
import { paperMaterial, overlayMaterial, PaperSheet, BLOBS } from './grungeParts.js';
import { inkMesh, inkColor, InkTextures, loadDisplayFont } from '../engine/ink.js';
import { FrameChrome } from '../engine/frameChrome.js';

const INK = '#151515';
const BAR = '#d9d9d6';
/** The reference look's acid lime, used when the artwork has no strong colour. */
const LIME = '#cff24a';

/** A sung word sprays in from a grainy blur and is sharp this soon after it starts. */
const APPEAR = 0.16;
const APPEAR_BLUR = 0.9;
const EXIT = 0.5;
const ECHO_DELAY = 0.09;
const SMEAR_CHANCE = 0.28;
const STAMP_SHARE = 0.7;
/** Keep stamps clear of the corner captions (in margins). */
const STAMP_INSET = 0.11;
const BLOB_RATE = 1.6;
const PAPER_DEPTH = 1;

/**
 * Grunge — distressed poster typography. Each line becomes a new composition on creased paper:
 * black grotesk words spray on out of a grainy blur exactly as they're sung, over soft spray-paint
 * shapes in the album's colour; lines leave by dissolving into grain. Emoji are printed as ink stamps.
 */
export class GrungeTheme {
  constructor(engine) {
    this.engine = engine;
    this.stage = engine.stage;
    this.root = new THREE.Group();
    this.textures = new InkTextures();
    this.colors = new PaletteBlend();
    this.shared = { ink: { value: inkColor(INK) }, time: { value: 0 } };
    this.accent = new THREE.Color(LIME);
    this.accentTarget = new THREE.Color(LIME);
    this.view = null;
    this.exiting = [];
    this.trackID = undefined;
    this.fontReady = false;
    this.blobs = Array.from({ length: BLOBS }, () => new THREE.Vector4(0, 0, 1, 0));
    this.band = new THREE.Vector4(0, 1, 0, 0);
  }

  // MARK: Lifecycle

  activate() {
    this.stage.reset();
    this.stage.renderer.toneMapping = THREE.NoToneMapping;
    this.sheet = new PaperSheet();
    this.seed = 0;
    this.crease = new THREE.Vector4(0.02, -0.03, 0.1, 0.05);
    this.paper = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), paperMaterial(this.sheet));
    this.paper.position.z = -PAPER_DEPTH;
    this.paper.renderOrder = -10;
    this.overlay = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), overlayMaterial(this.paper.material));
    this.overlay.position.z = 0.5;
    this.overlay.renderOrder = 50;
    this.root.add(this.paper, this.overlay);
    this.chrome = new FrameChrome(this.stage, this.textures, { ink: INK });
    this.stage.scene.add(this.root);
    loadDisplayFont().then(() => {
      if (!this.root.parent) return;
      this.fontReady = true;
      this.chrome.refresh();
      this.#retireView(this.shared.time.value);
    });
  }

  deactivate() {
    [this.view, ...this.exiting].forEach((v) => v && this.#disposeView(v));
    this.view = null;
    this.exiting = [];
    this.chrome?.dispose();
    this.sheet?.dispose();
    this.textures.dispose();
    this.root.removeFromParent();
    this.root.traverse((o) => {
      o.geometry?.dispose();
      [o.material].flat().forEach((m) => m?.dispose());
    });
    this.stage.reset();
  }

  setPalette(palette, instant = false) {
    this.colors.setTarget(palette, instant);
    if (palette.highlight === FALLBACK_PALETTE.highlight) {
      this.accentTarget.set(LIME);
    } else {
      // Spray paint: the album's highlight hue, pushed vivid and light.
      const hsl = {};
      new THREE.Color(palette.highlight).getHSL(hsl);
      this.accentTarget.setHSL(hsl.h, Math.max(0.72, hsl.s), 0.64);
    }
    if (instant) this.accent.copy(this.accentTarget);
  }

  // MARK: Frame

  update(frame) {
    const { now, dt, pulse, level, bands, decks } = frame;
    this.shared.time.value = now;
    this.accent.lerp(this.accentTarget, 1 - Math.exp(-dt * 3));
    const view = this.stage.viewSize(CAMERA_Z);
    this.#updatePaper(view, now);

    const deck = decks.find((d) => d.role === 'current') ?? null;
    if ((deck?.trackID ?? null) !== this.trackID) this.#changeTrack(deck, now);
    const info = deck ? this.engine.tracks.get(deck.trackID) ?? null : null;
    if (this.fontReady) this.#followLyrics(deck, info, view, now);

    if (this.view) this.#updateView(this.view, deck, view, frame);
    this.exiting = this.exiting.filter((v) => {
      const alive = this.#exitView(v, now);
      if (!alive) this.#disposeView(v);
      return alive;
    });
    this.#updateBlobs(dt, pulse, level ?? 0);
    if (this.fontReady) {
      const noLyrics = !deck?.doc;
      this.chrome.update(now, info, deck ? deck.position : null, bands, noLyrics && Boolean(deck), deck ? deck.opacity : 1);
    }
  }

  #updatePaper(view, now) {
    this.sheet.bake(this.stage.renderer, this.seed, this.crease);
    const back = this.stage.viewSize(CAMERA_Z + PAPER_DEPTH);
    this.paper.scale.set(back.width * 1.02, back.height * 1.02, 1);
    const front = this.stage.viewSize(CAMERA_Z - 0.5);
    this.overlay.scale.set(front.width * 1.02, front.height * 1.02, 1);
    const u = this.paper.material.uniforms;
    u.view.value.set(view.width, view.height);
    u.time.value = now;
    u.accent.value.copy(this.accent).convertLinearToSRGB();
    u.blobs.value.forEach((b, i) => b.copy(this.blobs[i]));
    u.band.value.copy(this.band);
  }

  #changeTrack(deck, now) {
    this.trackID = deck?.trackID ?? null;
    this.#retireView(now);
    // Each song gets its own sheet of paper.
    const seed = [...(this.trackID ?? 'apmuly')].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 9973, 7);
    this.seed = seed * 0.37;
    this.crease.set((seeded(seed) - 0.5) * 0.4, (seeded(seed + 1) - 0.5) * 0.3, (seeded(seed + 2) - 0.5) * 0.6, (seeded(seed + 3) - 0.5) * 0.4);
  }

  /** Which composition should be on screen: the sung line, a break, or (no lyrics) the title. */
  #followLyrics(deck, info, view, now) {
    let key = 'idle';
    let make = () => this.#titleView(info, view, now, 'idle');
    if (deck?.doc) {
      const index = deck.lineIndex;
      const line = index >= 0 ? deck.doc.lines[index] : null;
      if (line && line.t !== '' && line.w.length > 0) {
        key = `line:${index}`;
        make = () => this.#lineView(deck, line, index, view, now);
      } else {
        key = 'break';
        make = () => this.#titleView(info, view, now, 'break');
      }
    } else if (deck) {
      key = `title:${info?.title ?? ''}`;
      make = () => this.#titleView(info, view, now, 'title');
    }
    if (this.view?.key === key) return;
    this.#retireView(now);
    this.view = make();
    this.view.key = key;
  }

  #retireView(now) {
    if (!this.view) return;
    this.view.exitStart = now;
    this.exiting.push(this.view);
    this.view = null;
  }

  // MARK: Compositions

  #lineView(deck, line, index, view, now) {
    const kind = chooseComposition(line.w.length, index);
    const view0 = this.#newView(kind, line.w.map((w) => w.t), view, index, now);
    view0.line = line;
    view0.index = index;
    view0.mode = 'line';
    this.#addStamps(view0, deck, index);
    return view0;
  }

  /** Breaks and songs without lyrics: the title as a poster that breathes with the beat. */
  #titleView(info, view, now, mode) {
    const words = (info ? `${info.title}` : 'Apmuly').split(/\s+/).filter(Boolean).slice(0, 6);
    const kind = words.length <= 2 ? 'stack' : 'left';
    const v = this.#newView(kind, words, view, 0, now);
    v.mode = mode;
    return v;
  }

  #newView(kind, texts, view, index, now) {
    const group = new THREE.Group();
    this.root.add(group);
    const entries = texts.map((t) => this.textures.word(t));
    const v = { kind, texts, entries, group, born: now, exitStart: null, index, layoutFor: null, words: [], echoes: [], bars: [], stamps: [] };
    v.words = entries.map((entry, i) => {
      const mesh = inkMesh(entry, this.shared, index * 31 + i * 7);
      const r = (k) => seeded(index * 97 + i * 13 + k);
      // Some words trail a grainy smear off their end, like a print that slipped.
      mesh.material.uniforms.smear.value = r(1) < SMEAR_CHANCE ? 0.2 + r(2) * 0.25 : 0;
      mesh.material.uniforms.side.value = 1;
      group.add(mesh);
      return { mesh, entry };
    });
    this.#layout(v, view);
    this.#targetBlobs(v, view);
    return v;
  }

  /** Positions everything for the current window; re-run when the window size changes. */
  #layout(v, view) {
    const layout = composeLine(v.entries.map((e) => e.ink), view, v.kind);
    v.layout = layout;
    v.layoutFor = `${view.width.toFixed(2)}x${view.height.toFixed(2)}`;
    v.words.forEach(({ mesh }, i) => {
      mesh.position.set(layout.words[i].x, layout.words[i].y, 0);
      mesh.scale.setScalar(layout.size);
    });
    // Repeated copies (stack): echoes of each word below the live one.
    this.#clearMeshes(v.echoes);
    v.echoes = layout.copies.slice(1).flatMap((y, k) => v.words.map(({ entry }, i) => {
      const mesh = inkMesh(entry, this.shared, v.index * 53 + k * 11 + i);
      const r = seeded(v.index * 59 + k * 19 + i * 3);
      mesh.material.uniforms.smear.value = r < 0.45 ? 0.15 + r * 0.4 : 0;
      mesh.material.uniforms.side.value = 1;
      mesh.position.set(layout.words[i].x, y, 0);
      mesh.scale.setScalar(layout.size);
      v.group.add(mesh);
      return { mesh, word: i, copy: k + 1 };
    }));
    this.#clearMeshes(v.bars);
    v.bars = layout.bars.map((bar) => {
      const mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ color: BAR, transparent: true, opacity: 0 }));
      mesh.position.set((bar.left + bar.right) / 2, bar.y, -0.05);
      mesh.scale.set(bar.right - bar.left, bar.height, 1);
      mesh.renderOrder = 4;
      v.group.add(mesh);
      return { mesh, width: bar.right - bar.left, left: bar.left };
    });
    v.stamps.forEach((stamp) => this.#placeStamp(v, stamp, view));
  }

  #clearMeshes(list) {
    list.forEach(({ mesh }) => {
      mesh.removeFromParent();
      mesh.geometry.dispose();
      mesh.material.dispose();
    });
  }

  // MARK: Emoji stamps

  #addStamps(v, deck, index) {
    const insight = this.engine.insights.get(deck.trackID)?.get(index) ?? null;
    if (!insight) return;
    const cues = [];
    if (insight.emoji) cues.push({ emoji: insight.emoji, word: 0, mood: true });
    (insight.objects ?? []).forEach((o) => {
      if (o.emoji && Number.isInteger(o.wordIndex) && v.words[o.wordIndex]) cues.push({ emoji: o.emoji, word: o.wordIndex, mood: false });
    });
    v.stamps = cues.slice(0, 3).map((cue, i) => {
      const mesh = inkMesh(this.textures.stamp(cue.emoji), this.shared, index * 17 + i * 5);
      mesh.rotation.z = (seeded(index * 7 + i) - 0.5) * 0.4;
      v.group.add(mesh);
      return { ...cue, mesh };
    });
    const view = this.stage.viewSize(CAMERA_Z);
    v.stamps.forEach((stamp) => this.#placeStamp(v, stamp, view));
  }

  /** Mood stamp above the first word; object stamps above the end of their word. */
  #placeStamp(v, stamp, view) {
    const size = v.layout.size * STAMP_SHARE;
    const word = v.layout.words[stamp.word];
    const half = (v.entries[stamp.word].ink * v.layout.size) / 2;
    const inset = STAMP_INSET * Math.min(view.width, view.height);
    const maxX = view.width / 2 - inset - size / 2;
    const maxY = view.height / 2 - inset - size / 2;
    // Mood: above the first word's start. Objects: beside their word, clear of the other rows.
    const spot = stamp.mood
      ? { x: word.x - half + size * 0.4, y: word.y + v.layout.size * 0.62 + size * 0.3 }
      : besideWord(v.layout, v.entries.map((e) => e.ink), stamp.word, size, view);
    const { x, y } = spot;
    stamp.mesh.scale.setScalar(size);
    stamp.mesh.position.set(Math.max(-maxX, Math.min(maxX, x)), Math.max(-maxY, Math.min(maxY, y)), 0.01);
  }

  // MARK: Animation

  #updateView(v, deck, view, { now, pulse }) {
    const key = `${view.width.toFixed(2)}x${view.height.toFixed(2)}`;
    if (v.layoutFor !== key) this.#layout(v, view);
    const opacity = deck ? deck.opacity : 1;
    if (v.mode === 'line') {
      const latest = retimedLine(v.line, deck?.doc, v.index);
      if (latest) v.line = latest;
      this.#animateLine(v, deck?.position ?? 0, now, opacity, pulse);
    } else {
      this.#animateTitle(v, now, opacity, pulse);
    }
  }

  /** Words appear exactly at their start time: grainy blur → sharp in APPEAR seconds. */
  #animateLine(v, position, now, opacity, pulse) {
    const words = v.line.w;
    v.words.forEach(({ mesh }, i) => {
      const since = position - words[i].s;
      this.#ink(mesh, since, opacity, pulse * (since >= 0 && position < words[i].e ? 0.06 : 0));
    });
    v.echoes.forEach(({ mesh, word, copy }) => {
      const since = position - words[word].s - copy * ECHO_DELAY;
      this.#ink(mesh, since, opacity * (1 - copy * 0.1), 0.03 + copy * 0.04);
    });
    const barIn = easeOutCubic(clamp01((now - v.born) / 0.3));
    v.bars.forEach(({ mesh, width, left }) => {
      mesh.scale.x = Math.max(0.001, width * barIn);
      mesh.position.x = left + (width * barIn) / 2;
      mesh.material.opacity = 0.92 * opacity;
    });
    v.stamps.forEach((stamp) => {
      const since = position - words[stamp.word].s - (stamp.mood ? 0 : 0.05);
      this.#ink(stamp.mesh, since, opacity * 0.9, 0);
    });
  }

  #ink(mesh, since, opacity, extraBlur) {
    const u = mesh.material.uniforms;
    if (since < 0) {
      u.opacity.value = 0;
      mesh.visible = false;
      return;
    }
    mesh.visible = true;
    const k = easeOutCubic(clamp01(since / APPEAR));
    u.opacity.value = opacity * Math.min(1, 0.35 + k);
    u.blur.value = (1 - k) * APPEAR_BLUR + extraBlur;
    u.dissolve.value = 0;
  }

  /** The title breathes: blur and the echo copies pulse with the beat. */
  #animateTitle(v, now, opacity, pulse) {
    const age = now - v.born;
    const dim = v.mode === 'break' ? 0.55 : 1;
    v.words.forEach(({ mesh }, i) => this.#ink(mesh, age - i * 0.12, opacity * dim, 0.02 + pulse * 0.12));
    v.echoes.forEach(({ mesh, word, copy }) => this.#ink(mesh, age - word * 0.12 - copy * 0.15, opacity * dim * 0.9, 0.02 + copy * 0.03 + pulse * 0.15));
  }

  /** Lines leave by dissolving into grain. */
  #exitView(v, now) {
    const k = clamp01((now - v.exitStart) / EXIT);
    const all = [...v.words, ...v.echoes, ...v.stamps];
    all.forEach(({ mesh }) => {
      const u = mesh.material.uniforms;
      u.dissolve.value = k ** 0.7;
      u.blur.value += k * 0.02;
    });
    v.bars.forEach(({ mesh }) => { mesh.material.opacity *= 1 - k; });
    return k < 1;
  }

  #disposeView(v) {
    this.#clearMeshes([...v.words, ...v.echoes, ...v.bars, ...v.stamps]);
    v.group.removeFromParent();
  }

  // MARK: Spray paint

  /** Where this composition's paint goes (seeded per line, so a song always looks the same). */
  #targetBlobs(v, view) {
    const r = (k) => seeded(v.index * 211 + k);
    const w = view.width;
    const h = view.height;
    const block = v.layout;
    const centreY = block.words.reduce((sum, word) => sum + word.y, 0) / Math.max(1, block.words.length);
    const targets = {
      center: [[(r(1) - 0.5) * w * 0.4, (r(2) - 0.5) * h * 0.3, h * 0.42, 1]],
      left: [[w * (0.12 + r(3) * 0.2), -h * (0.05 + r(4) * 0.2), h * 0.48, 1]],
      right: [[-w * 0.24, h * 0.12, h * 0.4, 1], [w * 0.08, -h * 0.05, h * 0.4, 0.95]],
      redacted: [],
      stack: [[(r(5) - 0.5) * w * 0.3, 0, h * 0.4, 1]],
      banner: [[w * (r(6) - 0.5) * 0.5, h * 0.18, h * 0.32, 0.9]],
    }[v.kind] ?? [];
    v.blobTargets = Array.from({ length: BLOBS }, (_, i) => targets[i] ?? [0, 0, h * 0.3, 0]);
    v.bandTarget = v.kind === 'redacted' ? [centreY, h * 0.42, 1]
      : v.kind === 'banner' ? [block.words[0].y + block.size * 0.15, block.size * 0.62, 1] : [0, h * 0.2, 0];
  }

  #updateBlobs(dt, pulse, level) {
    const v = this.view ?? this.exiting[this.exiting.length - 1];
    if (!v?.blobTargets) return;
    const k = 1 - Math.exp(-dt * BLOB_RATE);
    const swell = 1 + pulse * 0.05 + level * 0.08;
    this.blobs.forEach((blob, i) => {
      const [x, y, radius, strength] = v.blobTargets[i];
      blob.x += (x - blob.x) * k;
      blob.y += (y - blob.y) * k;
      blob.z += (radius * swell - blob.z) * k;
      blob.w += (strength - blob.w) * k;
    });
    const [y, height, strength] = v.bandTarget;
    this.band.x += (y - this.band.x) * k;
    this.band.y += (height - this.band.y) * k;
    this.band.z += (strength - this.band.z) * k;
  }
}

