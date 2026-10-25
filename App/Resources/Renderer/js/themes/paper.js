import * as THREE from '../../vendor/three.module.min.js';
import { composeLine, besideWord } from '../core/grungeLayout.js';
import { doodle, chooseDoodle, rng } from '../core/doodles.js';
import { retimedLine } from '../core/timeline.js';
import { FALLBACK_PALETTE } from '../core/palette.js';
import { clamp01, easeOutBack, easeOutCubic, seeded } from '../core/easing.js';
import { CAMERA_Z } from '../engine/stage.js';
import { inkMesh, inkColor, InkTextures, loadDisplayFont } from '../engine/ink.js';
import { FrameChrome } from '../engine/frameChrome.js';
import { backdropMaterial, setSceneColor, brushStroke, stickerTexture, labelTexture, SCENE } from './paperParts.js';

const BRUSH_FONT = '800 {px}px "Avenir Next", Avenir, "Segoe UI", "Trebuchet MS", "Helvetica Neue", Arial, sans-serif';
const CREAM = '#efe5cf';
const NIGHT = '#1d2254';
const DARK_INK = '#222222';
const LIGHT_INK = '#f8f2e6';
/** Hand-picked brush colours (ochre, cobalt, orange, periwinkle) mixed with the album's own. */
const BRUSH_COLOURS = ['#e0aa3e', '#3543b9', '#d5622f', '#7b80d4'];

const POP = 0.22;
const WIPE = 0.5;
const EXIT = 0.32;
const DOODLE_DRAW = 0.45;
const STICKER_SHARE = 0.85;
const TITLE_SCENE_SECONDS = 8;
const SCENE_RHYTHM = [SCENE.paper, SCENE.paint, SCENE.paper, SCENE.paint, SCENE.night, SCENE.paper, SCENE.paint, SCENE.paint];

/**
 * Paper Brush — hand-made kinetic type. Bold friendly words pop onto notebook paper, painted colour or a
 * starry night exactly as they're sung, while brush doodles draw themselves around them (circles,
 * underlines, arrows, sparkles), with emoji as die-cut stickers and torn paper labels.
 */
export class PaperBrushTheme {
  constructor(engine) {
    this.engine = engine;
    this.stage = engine.stage;
    this.root = new THREE.Group();
    this.textures = new InkTextures({ font: BRUSH_FONT });
    this.shared = { ink: { value: inkColor(DARK_INK) }, time: { value: 0 } };
    this.accent = new THREE.Color(BRUSH_COLOURS[1]);
    this.view = null;
    this.exiting = [];
    this.trackID = undefined;
    this.fontReady = false;
    this.stickers = new Map();
    this.scene = { kind: SCENE.paper, color: new THREE.Color(CREAM) };
    this.wipeStart = -Infinity;
  }

  // MARK: Lifecycle

  activate() {
    this.stage.reset();
    this.stage.renderer.toneMapping = THREE.NoToneMapping;
    this.backdrop = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), backdropMaterial());
    this.backdrop.position.z = -1;
    this.backdrop.renderOrder = -10;
    const u = this.backdrop.material.uniforms;
    setSceneColor(u.colorA, this.scene.color);
    setSceneColor(u.colorB, this.scene.color);
    this.root.add(this.backdrop);
    this.chrome = new FrameChrome(this.stage, this.textures, { ink: DARK_INK });
    this.stage.scene.add(this.root);
    loadDisplayFont().then(() => document.fonts?.load?.(BRUSH_FONT.replace('{px}', 64)).catch(() => undefined)).then(() => {
      if (!this.root.parent) return;
      this.fontReady = true;
      this.chrome.refresh();
    });
  }

  deactivate() {
    [this.view, ...this.exiting].forEach((v) => v && this.#disposeView(v));
    this.view = null;
    this.exiting = [];
    this.chrome?.dispose();
    this.stickers.forEach((texture) => texture.dispose());
    this.stickers.clear();
    this.textures.dispose();
    this.root.removeFromParent();
    this.root.traverse((o) => {
      o.geometry?.dispose();
      [o.material].flat().forEach((m) => m?.dispose());
    });
    this.stage.reset();
  }

  setPalette(palette) {
    if (palette.highlight === FALLBACK_PALETTE.highlight) {
      this.accent.set(BRUSH_COLOURS[1]);
      return;
    }
    const hsl = {};
    new THREE.Color(palette.highlight).getHSL(hsl);
    this.accent.setHSL(hsl.h, Math.max(0.6, hsl.s), 0.5);
  }

  // MARK: Frame

  update(frame) {
    const { now, decks, bands, pulse } = frame;
    this.shared.time.value = now;
    const view = this.stage.viewSize(CAMERA_Z);
    this.#updateBackdrop(view, now);

    const deck = decks.find((d) => d.role === 'current') ?? null;
    if ((deck?.trackID ?? null) !== this.trackID) this.#changeTrack(deck, now);
    const info = deck ? this.engine.tracks.get(deck.trackID) ?? null : null;
    if (this.fontReady) this.#followLyrics(deck, info, view, now);
    if (this.view) this.#updateView(this.view, deck, view, now, pulse);
    this.exiting = this.exiting.filter((v) => {
      const alive = this.#exitView(v, now);
      if (!alive) this.#disposeView(v);
      return alive;
    });
    if (this.fontReady) this.chrome.update(now, info, deck ? deck.position : null, bands, Boolean(deck) && !deck.doc, deck ? deck.opacity : 1);
  }

  #changeTrack(deck, now) {
    this.trackID = deck?.trackID ?? null;
    this.#retire(now);
    this.backdrop.material.uniforms.seed.value = [...(this.trackID ?? 'apmuly')].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 997, 3);
  }

  // MARK: Scenes

  /** A rhythm of notebook paper, painted colour (the album's or a brush colour) and the odd starry night. */
  #sceneFor(index) {
    const kind = index < 0 ? SCENE.paper : SCENE_RHYTHM[index % SCENE_RHYTHM.length];
    if (kind === SCENE.paper) return { kind, color: new THREE.Color(CREAM) };
    if (kind === SCENE.night) return { kind, color: new THREE.Color(NIGHT) };
    const pick = seeded(index * 71 + 5);
    const color = pick < 0.5 ? this.accent.clone() : new THREE.Color(BRUSH_COLOURS[Math.floor(pick * 8) % BRUSH_COLOURS.length]);
    return { kind, color };
  }

  /** Brush-wipes to a new scene (the old one stays underneath until the wipe passes). */
  #showScene(scene, now, index) {
    if (scene.kind === this.scene.kind && scene.color.equals(this.scene.color)) return;
    const u = this.backdrop.material.uniforms;
    u.colorA.value.copy(u.colorB.value);
    u.kindA.value = u.kindB.value;
    setSceneColor(u.colorB, scene.color);
    u.kindB.value = scene.kind;
    u.wipeDir.value = index % 2 === 0 ? 1 : -1;
    u.wipe.value = 0;
    this.wipeStart = now;
    this.scene = scene;
    const ink = this.#isDark(scene) ? LIGHT_INK : DARK_INK;
    this.shared.ink.value.copy(inkColor(ink));
    this.chrome.setInk(ink);
  }

  #isDark(scene) {
    if (scene.kind === SCENE.night) return true;
    const hsl = {};
    scene.color.clone().convertLinearToSRGB().getHSL(hsl);
    return hsl.l < 0.58;
  }

  #updateBackdrop(view, now) {
    const back = this.stage.viewSize(CAMERA_Z + 1);
    this.backdrop.scale.set(back.width * 1.02, back.height * 1.02, 1);
    const u = this.backdrop.material.uniforms;
    u.view.value.set(view.width, view.height);
    u.time.value = now;
    const k = clamp01((now - this.wipeStart) / WIPE);
    u.wipe.value = k < 0.5 ? 2 * k * k : 1 - (-2 * k + 2) ** 2 / 2;
  }

  // MARK: Compositions

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
    this.#retire(now);
    this.view = make();
    this.view.key = key;
  }

  #retire(now) {
    if (!this.view) return;
    this.view.exitStart = now;
    this.exiting.push(this.view);
    this.view = null;
  }

  #lineView(deck, line, index, view, now) {
    const count = line.w.length;
    const kind = count <= 2 && index % 3 === 1 ? 'banner' : ['center', 'left', 'center', 'right'][index % 4];
    const v = this.#newView(kind, line.w.map((w) => w.t), view, index, now);
    v.mode = 'line';
    v.line = line;
    v.index = index;
    this.#showScene(this.#sceneFor(index), now, index);

    const insight = this.engine.insights.get(deck.trackID)?.get(index) ?? null;
    const objectWord = (insight?.objects ?? []).find((o) => o.emoji && Number.isInteger(o.wordIndex) && o.wordIndex < count)?.wordIndex;
    const longest = line.w.reduce((best, w, i) => (w.t.length > line.w[best].t.length ? i : best), 0);
    v.focus = objectWord ?? longest;
    v.doodleKind = chooseDoodle(index, count);
    v.withLabel = kind !== 'banner' && seeded(index * 23 + 9) < 0.28 && !['box', 'circle', 'scribble'].includes(v.doodleKind);
    v.stickerCues = [];
    if (insight?.emoji) v.stickerCues.push({ emoji: insight.emoji, word: 0, mood: true });
    (insight?.objects ?? []).forEach((o) => {
      if (o.emoji && Number.isInteger(o.wordIndex) && o.wordIndex < count) v.stickerCues.push({ emoji: o.emoji, word: o.wordIndex, mood: false });
    });
    v.stickerCues = v.stickerCues.slice(0, 3);
    this.#decorate(v, view);
    return v;
  }

  /** Breaks and songs without lyrics: the title, circled and sparkled, with scenes changing slowly. */
  #titleView(info, view, now, mode) {
    const words = (info ? info.title : 'Apmuly').split(/\s+/).filter(Boolean).slice(0, 5);
    const v = this.#newView('center', words, view, -1, now);
    v.mode = mode;
    v.focus = 0;
    v.doodleKind = 'swirl';
    v.withLabel = false;
    v.stickerCues = [];
    v.sceneClock = now;
    this.#decorate(v, view);
    return v;
  }

  #newView(kind, texts, view, index, now) {
    const group = new THREE.Group();
    this.root.add(group);
    const entries = texts.map((t) => this.textures.word(t));
    const v = { kind, texts, entries, group, born: now, exitStart: null, index, words: [], doodles: [], stickers: [], label: null };
    v.words = entries.map((entry, i) => {
      const mesh = inkMesh(entry, this.shared, (index + 2) * 31 + i * 7, { grit: 0.02 });
      const r = (k) => seeded((index + 3) * 97 + i * 13 + k);
      group.add(mesh);
      return { mesh, entry, tilt: (r(1) - 0.5) * 0.11, kick: (r(2) - 0.5) * 0.5, lift: (r(3) - 0.5) * 0.16 };
    });
    this.#layout(v, view);
    return v;
  }

  #layout(v, view) {
    v.layout = composeLine(v.entries.map((e) => e.ink), view, v.kind);
    v.layoutFor = `${view.width.toFixed(2)}x${view.height.toFixed(2)}`;
    v.words.forEach((w, i) => {
      w.home = new THREE.Vector3(v.layout.words[i].x, v.layout.words[i].y + w.lift * v.layout.size, 0);
      w.mesh.position.copy(w.home);
    });
  }

  /** The focus word's box in world units. */
  #focusBox(v) {
    const i = Math.min(v.focus, v.words.length - 1);
    const w = v.words[i];
    return { x: w.home.x, y: w.home.y, width: v.entries[i].ink * v.layout.size, height: v.layout.size * 0.78 };
  }

  #decorate(v, view) {
    this.#clear(v.doodles);
    this.#clear(v.stickers);
    if (v.label) this.#clear([v.label]);
    const box = this.#focusBox(v);
    const random = rng((v.index + 5) * 7919);
    const strokes = doodle(v.doodleKind, box, random);
    const behind = v.doodleKind === 'strokes' || v.doodleKind === 'swirl';
    const color = this.#doodleColor(v.index);
    const width = v.layout.size * (v.doodleKind === 'strokes' ? 0.26 : v.doodleKind === 'sparkle' ? 0.07 : 0.09);
    v.doodles = strokes.map((points, k) => {
      const { mesh } = brushStroke(points, width, color, (v.index + 7) * 13 + k);
      mesh.renderOrder = behind ? 1 : 6;
      mesh.position.z = behind ? -0.02 : 0.02;
      v.group.add(mesh);
      return { mesh, order: k, count: strokes.length };
    });
    if (v.withLabel) {
      const label = labelTexture(box.width / box.height + 0.6, rng((v.index + 11) * 31));
      const mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: label.texture, transparent: true, depthTest: false, depthWrite: false }));
      mesh.renderOrder = 2;
      const width = (box.width + box.height * 0.6) / label.inner;
      mesh.scale.set(width, width / label.aspect, 1);
      mesh.position.set(box.x, box.y, -0.01);
      mesh.rotation.z = (seeded(v.index * 3 + 1) - 0.5) * 0.08;
      v.group.add(mesh);
      v.label = { mesh, ownsTexture: label.texture, width };
    }
    v.stickers = (v.stickerCues ?? []).map((cue, k) => {
      const texture = this.#sticker(cue.emoji);
      const mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthTest: false, depthWrite: false }));
      mesh.renderOrder = 7;
      const size = v.layout.size * STICKER_SHARE;
      const word = v.words[Math.min(cue.word, v.words.length - 1)];
      const half = (v.entries[Math.min(cue.word, v.words.length - 1)].ink * v.layout.size) / 2;
      const maxX = view.width / 2 - size * 0.7;
      const maxY = view.height / 2 - size * 0.9;
      // Mood: above the line's first word. Objects: beside their word, clear of the other rows.
      const spot = cue.mood
        ? { x: word.home.x - half + size * 0.15, y: word.home.y + v.layout.size * 0.55 + size * 0.45 }
        : besideWord(v.layout, v.entries.map((e) => e.ink), Math.min(cue.word, v.words.length - 1), size, view);
      const { x, y } = spot;
      mesh.position.set(Math.max(-maxX, Math.min(maxX, x)), Math.max(-maxY, Math.min(maxY, y)), 0.03);
      mesh.userData = { size, tilt: (seeded(v.index * 17 + k) - 0.5) * 0.5 };
      v.group.add(mesh);
      return { mesh, cue };
    });
  }

  #doodleColor(index) {
    if (this.#isDark(this.scene)) return new THREE.Color(index % 2 ? '#fff6e5' : BRUSH_COLOURS[0]);
    const pick = seeded(index * 53 + 29);
    if (this.scene.kind === SCENE.paper && pick < 0.5) return this.accent.clone();
    const options = BRUSH_COLOURS.filter((c) => !new THREE.Color(c).equals(this.scene.color));
    return new THREE.Color(options[Math.floor(pick * 10) % options.length]);
  }

  #sticker(emoji) {
    if (!this.stickers.has(emoji)) this.stickers.set(emoji, stickerTexture(emoji));
    return this.stickers.get(emoji);
  }

  #clear(list) {
    list.forEach((item) => {
      item.mesh.removeFromParent();
      item.mesh.geometry.dispose();
      item.mesh.material.dispose();
      item.ownsTexture?.dispose();
    });
  }

  // MARK: Animation

  #updateView(v, deck, view, now, pulse) {
    const key = `${view.width.toFixed(2)}x${view.height.toFixed(2)}`;
    if (v.layoutFor !== key) {
      this.#layout(v, view);
      this.#decorate(v, view);
    }
    const opacity = deck ? deck.opacity : 1;
    if (v.mode === 'line') {
      const latest = retimedLine(v.line, deck?.doc, v.index);
      if (latest) v.line = latest;
      const words = v.line.w;
      const position = deck?.position ?? 0;
      v.words.forEach((w, i) => this.#pop(w, position - words[i].s, v.layout.size, opacity, 0));
      const focusStart = words[Math.min(v.focus, words.length - 1)].s;
      this.#draw(v, position - focusStart - 0.04, opacity);
      v.stickers.forEach(({ mesh, cue }) => this.#stick(mesh, position - words[cue.word].s, opacity));
    } else {
      const age = now - v.born;
      if (now - v.sceneClock > TITLE_SCENE_SECONDS) {
        v.sceneClock = now;
        v.sceneStep = (v.sceneStep ?? 0) + 1;
        this.#showScene(this.#sceneFor(v.sceneStep * 3 + 1), now, v.sceneStep);
      }
      v.words.forEach((w, i) => this.#pop(w, age - i * 0.12, v.layout.size, opacity * (v.mode === 'break' ? 0.75 : 1), pulse));
      // The swirl redraws itself every few seconds.
      this.#draw(v, (age % 6) - 0.3, opacity);
    }
  }

  /** A word pops in exactly when it's sung: a springy scale-up that settles into its tilt. */
  #pop(w, since, size, opacity, pulse) {
    const u = w.mesh.material.uniforms;
    if (since < 0) {
      u.opacity.value = 0;
      w.mesh.visible = false;
      return;
    }
    w.mesh.visible = true;
    const k = clamp01(since / POP);
    w.mesh.scale.setScalar(Math.max(0.001, size * easeOutBack(k, 2.4) * (1 + pulse * 0.03)));
    w.mesh.rotation.z = w.tilt + (1 - easeOutCubic(k)) * w.kick;
    u.opacity.value = opacity;
    u.blur.value = 0;
  }

  /** Doodle strokes draw themselves on one after another. */
  #draw(v, since, opacity) {
    v.doodles.forEach(({ mesh, order, count }) => {
      const slot = DOODLE_DRAW / count;
      mesh.material.uniforms.progress.value = clamp01((since - order * slot * 0.9) / (slot * 1.1));
      mesh.material.uniforms.opacity.value = opacity;
    });
    if (v.label) {
      const k = easeOutCubic(clamp01(since / 0.25));
      v.label.mesh.scale.x = Math.max(0.001, v.label.width * k);
      v.label.mesh.material.opacity = opacity * (since >= 0 ? 1 : 0);
    }
  }

  #stick(mesh, since, opacity) {
    if (since < 0) {
      mesh.visible = false;
      return;
    }
    mesh.visible = true;
    const k = clamp01(since / 0.3);
    const { size, tilt } = mesh.userData;
    mesh.scale.setScalar(Math.max(0.001, size * easeOutBack(k, 2.6)));
    mesh.rotation.z = tilt * (1 - 0.6 * k) + Math.sin(since * 2.2) * 0.04;
    mesh.material.opacity = opacity;
  }

  /** Lines leave quickly: words drop a little and fade, doodles fade with them. */
  #exitView(v, now) {
    const k = clamp01((now - v.exitStart) / EXIT);
    const e = k * k;
    v.words.forEach((w) => {
      w.mesh.position.y = w.home.y - e * v.layout.size * 0.35;
      w.mesh.material.uniforms.opacity.value *= 1 - k;
    });
    v.doodles.forEach(({ mesh }) => { mesh.material.uniforms.opacity.value *= 1 - k; });
    v.stickers.forEach(({ mesh }) => { mesh.material.opacity *= 1 - k; });
    if (v.label) v.label.mesh.material.opacity *= 1 - k;
    return k < 1;
  }

  #disposeView(v) {
    this.#clear([...v.words, ...v.doodles, ...v.stickers]);
    if (v.label) this.#clear([v.label]);
    v.group.removeFromParent();
  }
}
