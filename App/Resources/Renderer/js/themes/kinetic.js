import * as THREE from '../../vendor/three.module.min.js';
import { LineTheme } from './lineTheme.js';
import { wordPhase } from '../core/timeline.js';
import { clamp01, lerp, easeOutBack, easeOutCubic, easeInCubic, smoothstep, seeded, damp } from '../core/easing.js';
import { studioEnvironment } from '../engine/environment.js';
import { CAMERA_Z } from '../engine/stage.js';
import { composeLine } from '../core/grungeLayout.js';
import { InkTextures, loadDisplayFont } from '../engine/ink.js';
import { FrameChrome } from '../engine/frameChrome.js';
import { FilmGrain } from '../engine/filmGrain.js';

// Lyrics
const FLIGHT = 0.9;
const MIN_FLIGHT = 0.28;
const EXIT = 0.75;
const STAGGER = 0.035;
/** Depth karaoke: the word being sung pushes toward you; sung words settle back. */
const SINGING_DEPTH = 0.7;
const SUNG_DEPTH = -0.3;
/** Words that have landed but aren't sung yet wait dimmed, so the sung word stands out. */
const WAITING_OPACITY = 0.5;
const SUNG_OPACITY = 0.85;
const STICKER_FLIGHT = 0.5;
const STICKER_DEPTH = 0.6;
const STICKER_REACH = 1.5;
const DOLLY = 1.1;
const FOV = 45;
const FOV_PUNCH = 1.6;
const SCRIM_Z = -2.5;
const CHROME_INK = '#efece6';
/** Kinetic words stay smaller than the 2D themes' posters: they fly in 3D and push toward you. */
const MAX_WORD_SIZE = 1.7;
/** Layouts centre rows on the em box; the extruded font is centred on its capitals. */
const CAP_OFFSET = 0;
const SCRIM_OPACITY = 0.85;

// Warp streaks (two parallax layers)
const STREAKS = 800;
const FIELD_DEPTH = 220;
const BASE_SPEED = 22;
const FAR_LAYER_SPEED = 0.35;

// Visualizer (songs without lyrics, instrumental breaks, behind Lyrics mode)
const BARS = 48;
const BAR_RING = 4.6;
const BAR_WIDTH = 0.16;
const BAR_MAX = 4.2;
const RING_Z = -4;
const TUNNEL_RINGS = 10;
const TUNNEL_DEPTH = 140;
const VIZ_FULL = 1;
const VIZ_IDLE = 0.35;
const VIZ_GAP = 0.65;
const VIZ_BEHIND_SHEET = 0.4;
/** Title sits low and slightly forward, in front of the lower spectrum bars. */
/** Title baseline sits this far above the bottom of the window (world units at TITLE_Z). */
const TITLE_MARGIN = 2.1;
const TITLE_Z = 2;

/**
 * Kinetic Depth — chrome words, tinted with the album artwork's colours, fly out of warp-speed light streaks and land with a springy bounce
 * exactly as they're sung; the sung word pushes toward you; emoji fly in to land
 * on their words; finished lines rocket past the camera. Songs without lyrics become an audio
 * visualizer: a chrome spectrum ring, a pulsing core and a rushing tunnel, titled in chrome.
 */
export class KineticDepth extends LineTheme {
  static font = 'bold';
  static textColor = '#f4f5f7';
  /** Satin, not mirror chrome: the face keeps the song's colour and stays readable from any angle. */
  static textStyle = { metalness: 0.3, roughness: 0.3, clearcoat: 1, clearcoatRoughness: 0.05 };
  static glowLimit = 0.8;
  static baseGlow = 0.3;
  static stickerLead = STICKER_FLIGHT;

  build() {
    // The same printed frame as the other themes (song, artist, timer, marks), in light ink.
    this.chromeTextures = new InkTextures();
    this.chrome = new FrameChrome(this.stage, this.chromeTextures, { ink: CHROME_INK });
    this.grain = new FilmGrain(this.stage);
    this.chromeReady = false;
    loadDisplayFont().then(() => {
      if (!this.root.parent) return;
      this.chromeReady = true;
      this.chrome.refresh();
    });
    this.stage.scene.background = new THREE.Color('#000000');
    const environment = studioEnvironment(this.stage.renderer);
    this.stage.scene.environment = environment.texture;
    this.ownedDisposables.push(environment);

    this.streaks = this.#buildStreaks();
    const key = new THREE.DirectionalLight('#ffffff', 1.3);
    key.position.set(4, 6, 10);
    // A light that sweeps across the scene so the chrome letters glint.
    this.glint = new THREE.PointLight('#ffffff', 40, 30);
    this.scrim = this.#buildScrim();
    this.root.add(this.streaks, this.scrim, key, this.glint, new THREE.AmbientLight('#ffffff', 0.2));

    this.barColor = new THREE.Color();
    this.viz = this.#buildVisualizer();
    this.speed = BASE_SPEED;
    this.dolly = 0;
    this.vizAmount = 0;
    this.coreAmount = 0;
    this.gapSeen = -Infinity;
    this.ringSpin = 0;
    this.title = null;
  }

  // MARK: Scene

  /** A soft dark pool behind the lyric line, so streaks never compete with the words. */
  #buildScrim() {
    const canvas = document.createElement('canvas');
    canvas.width = 256;
    canvas.height = 256;
    const ctx = canvas.getContext('2d');
    const gradient = ctx.createRadialGradient(128, 128, 0, 128, 128, 128);
    gradient.addColorStop(0, 'rgba(0,0,0,1)');
    gradient.addColorStop(0.55, 'rgba(0,0,0,0.8)');
    gradient.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, 256, 256);
    const texture = new THREE.CanvasTexture(canvas);
    this.ownedDisposables.push(texture);
    const scrim = new THREE.Mesh(new THREE.PlaneGeometry(1, 1),
      new THREE.MeshBasicMaterial({ map: texture, transparent: true, opacity: 0, depthWrite: false, toneMapped: false }));
    scrim.position.z = SCRIM_Z;
    scrim.renderOrder = -1;
    return scrim;
  }

  #buildStreaks() {
    this.streakBase = new Float32Array(STREAKS * 3);
    this.streakLayer = new Float32Array(STREAKS);
    for (let i = 0; i < STREAKS; i++) {
      const angle = Math.random() * Math.PI * 2;
      const radius = 4 + Math.random() * 40;
      this.streakBase.set([Math.cos(angle) * radius, Math.sin(angle) * radius * 0.6, CAMERA_Z - Math.random() * FIELD_DEPTH], i * 3);
      this.streakLayer[i] = Math.random() < 0.4 ? FAR_LAYER_SPEED : 1;
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(STREAKS * 6), 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(new Float32Array(STREAKS * 6), 3));
    const streaks = new THREE.LineSegments(geometry, new THREE.LineBasicMaterial({ vertexColors: true, transparent: true }));
    streaks.frustumCulled = false;
    return streaks;
  }

  #buildVisualizer() {
    const group = new THREE.Group();
    // Rounded pills, each shaded from the lyric colour toward the highlight by its level.
    const chrome = new THREE.MeshPhysicalMaterial({
      color: '#ffffff', emissive: '#000000', emissiveIntensity: 0.25, metalness: 0.35, roughness: 0.28, clearcoat: 1, transparent: true,
    });
    const bars = new THREE.InstancedMesh(new THREE.CapsuleGeometry(0.5, 1, 6, 16), chrome, BARS);
    bars.frustumCulled = false;
    this.ownedDisposables.push(bars); // frees its instance buffers

    // A smooth, iridescent pearl that breathes with the music.
    const core = new THREE.Mesh(new THREE.IcosahedronGeometry(1.25, 8), new THREE.MeshPhysicalMaterial({
      color: '#f4f5f7', metalness: 0.9, roughness: 0.1, clearcoat: 1, iridescence: 1, iridescenceIOR: 1.35,
      iridescenceThicknessRange: [200, 600], transparent: true,
    }));
    const shell = new THREE.Mesh(new THREE.TorusGeometry(1.85, 0.012, 8, 160),
      new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, opacity: 0.25 }));
    // Ring, core and shell scale together to fit any window shape.
    const ring = new THREE.Group();
    ring.position.z = RING_Z;
    ring.add(bars, core, shell);

    const ringGeometry = new THREE.RingGeometry(0.985, 1, 128);
    const tunnel = Array.from({ length: TUNNEL_RINGS }, (_, i) => {
      const hoop = new THREE.Mesh(ringGeometry, new THREE.MeshBasicMaterial({
        color: '#ffffff', transparent: true, opacity: 0, depthWrite: false, side: THREE.DoubleSide,
      }));
      hoop.position.z = RING_Z - (i / TUNNEL_RINGS) * TUNNEL_DEPTH;
      group.add(hoop);
      return hoop;
    });
    group.add(ring);
    group.visible = false;
    this.root.add(group);
    return { group, ring, bars, chrome, core, shell, tunnel, dummy: new THREE.Object3D() };
  }

  updateScene(frame) {
    const { now, dt, pulse, level, decks, bands } = frame;
    this.grain.update(now);
    if (this.chromeReady) {
      const deck = decks.find((d) => d.role === 'current') ?? null;
      const info = deck ? this.engine.tracks.get(deck.trackID) ?? null : null;
      this.chrome.update(now, info, deck ? deck.position : null, bands, false, deck ? deck.opacity : 1);
    }
    this.speed = damp(this.speed, BASE_SPEED + pulse * 70 + (level ?? 0) * 40, 6, dt);
    this.#updateStreaks(dt);
    this.#updateVisualizer(frame);
    const view = this.stage.viewSize(CAMERA_Z - SCRIM_Z);
    this.scrim.scale.set(view.width * 0.95, view.height * 0.6, 1);
    this.scrim.material.opacity = SCRIM_OPACITY * clamp01(1 - this.vizAmount / VIZ_GAP);

    this.glint.position.set(Math.sin(now * 0.7) * 14, 3 + Math.cos(now * 0.5) * 2, 7);
    this.dolly = damp(this.dolly, 0, 2.5, dt);
    const camera = this.stage.camera;
    camera.fov = FOV - pulse * FOV_PUNCH;
    camera.updateProjectionMatrix();
    camera.position.set(Math.sin(now * 0.17) * 0.6, Math.cos(now * 0.13) * 0.3, CAMERA_Z + this.dolly * DOLLY);
    camera.lookAt(0, 0, 0);
    camera.rotation.z += Math.sin(now * 0.2) * 0.03;
  }

  /** Near streaks are fast and bright, far ones slow and dim; all fade with distance. */
  #updateStreaks(dt) {
    const positions = this.streaks.geometry.attributes.position;
    const colors = this.streaks.geometry.attributes.color;
    for (let i = 0; i < STREAKS; i++) {
      const k = i * 3;
      const layer = this.streakLayer[i];
      let z = this.streakBase[k + 2] + this.speed * layer * dt;
      if (z > CAMERA_Z + 2) z -= FIELD_DEPTH;
      this.streakBase[k + 2] = z;
      const length = (0.4 + this.speed * 0.07) * layer;
      positions.setXYZ(i * 2, this.streakBase[k], this.streakBase[k + 1], z);
      positions.setXYZ(i * 2 + 1, this.streakBase[k], this.streakBase[k + 1], z - length);
      // Distant streaks bunch up behind the words, so they fade out with depth.
      const fade = clamp01(1 - (CAMERA_Z - z) / FIELD_DEPTH) ** 1.6;
      const near = layer === 1;
      const tint = near ? this.colors.lyric : this.colors.highlight;
      const brightness = fade * (near ? 0.8 : 0.5);
      colors.setXYZ(i * 2, tint.r * brightness, tint.g * brightness, tint.b * brightness);
      colors.setXYZ(i * 2 + 1, 0, 0, 0);
    }
    positions.needsUpdate = true;
    colors.needsUpdate = true;
  }

  // MARK: Visualizer

  #updateVisualizer({ now, dt, pulse, bands, level, mode, decks }) {
    const current = decks.find((d) => d.role === 'current') ?? null;
    const info = current ? this.engine.tracks.get(current.trackID) : null;
    const noLyrics = Boolean(current && info?.lyrics === 'none');
    const inGap = now - this.gapSeen < 0.25;
    const target = !current ? VIZ_IDLE
      : noLyrics ? VIZ_FULL
        : mode === 'lyrics' ? VIZ_BEHIND_SHEET
          : inGap ? VIZ_GAP : 0;
    this.vizAmount = damp(this.vizAmount, target, 2.2, dt);
    this.coreAmount = damp(this.coreAmount, noLyrics || !current ? 1 : 0, 2.2, dt);
    this.#updateTitle(noLyrics ? current.trackID : null, info, dt);

    const viz = this.vizAmount;
    const { group, ring, bars, chrome, core, shell, tunnel, dummy } = this.viz;
    group.visible = viz > 0.01;
    if (!group.visible) return;
    const view = this.stage.viewSize(CAMERA_Z - RING_Z);
    ring.scale.setScalar(Math.min(1, Math.min(view.width, view.height * 1.15) / (2 * (BAR_RING + BAR_MAX) + 1)));

    const spectrum = bands?.length ? bands : null;
    this.ringSpin += dt * (0.12 + pulse * 0.6);
    const half = BARS / 2;
    for (let i = 0; i < BARS; i++) {
      const band = spectrum ? spectrum[Math.floor(((i < half ? i : BARS - 1 - i) / half) * spectrum.length)] ?? 0
        : 0.12 + 0.08 * Math.sin(now * 2 + i * 0.5);
      const height = (0.12 + band * BAR_MAX) * viz;
      const angle = (i / BARS) * Math.PI * 2 + this.ringSpin;
      const radius = BAR_RING + height / 2;
      dummy.position.set(Math.cos(angle) * radius, Math.sin(angle) * radius, 0);
      dummy.rotation.set(0, 0, angle - Math.PI / 2);
      dummy.scale.set(BAR_WIDTH, Math.max(0.001, height / 2), BAR_WIDTH);
      dummy.updateMatrix();
      bars.setMatrixAt(i, dummy.matrix);
      bars.setColorAt(i, this.barColor.copy(this.colors.lyric).lerp(this.colors.highlight, clamp01(band * 1.4)));
    }
    bars.instanceMatrix.needsUpdate = true;
    bars.instanceColor.needsUpdate = true;
    chrome.opacity = clamp01(viz * 1.5);
    chrome.emissive.copy(this.colors.highlight);
    core.material.color.copy(this.colors.lyric);
    shell.material.color.copy(this.colors.highlight);

    const coreScale = Math.max(0.001, this.coreAmount * viz * (1 + pulse * 0.25 + (level ?? 0) * 0.6));
    core.scale.setScalar(coreScale);
    core.rotation.x += dt * (0.25 + pulse);
    core.rotation.y += dt * (0.35 + (level ?? 0) * 2);
    core.material.opacity = clamp01(this.coreAmount * viz * 1.5);
    shell.scale.setScalar(coreScale * (1 + pulse * 0.15));
    shell.rotation.x -= dt * 0.2;
    shell.rotation.y -= dt * 0.3;
    shell.material.opacity = 0.25 * this.coreAmount * viz;

    const lows = spectrum ? (spectrum[0] + spectrum[1] + spectrum[2]) / 3 : 0.2;
    tunnel.forEach((hoop) => {
      hoop.position.z += this.speed * 0.6 * dt;
      if (hoop.position.z > CAMERA_Z - 1) hoop.position.z -= TUNNEL_DEPTH;
      const depth = clamp01((hoop.position.z - (RING_Z - TUNNEL_DEPTH)) / TUNNEL_DEPTH);
      hoop.scale.setScalar(6.5 + lows * 2.5);
      hoop.material.color.copy(this.colors.highlight);
      hoop.material.opacity = 0.3 * depth * (1 - smoothstep(0.85, 1, depth)) * viz;
    });
  }

  /** The song's title in chrome under the spectrum ring, for songs without lyrics. */
  #updateTitle(trackID, info, dt) {
    if (this.title && this.title.trackID !== trackID) {
      this.title.fading = true;
    }
    if (!this.title && trackID && info) {
      const group = new THREE.Group();
      const make = (text, fontSize, y) => {
        const words = text.split(/\s+/).filter(Boolean).slice(0, 8).map((t) => ({ t, s: 0, e: 0 }));
        return this.layout({ w: words }, { fontSize, widthFactor: 0.7 }).map((entry) => {
          const { textColor, textStyle, glowLimit, baseGlow } = this.constructor;
          const mesh = this.factory.make(entry.text, textColor, textStyle, glowLimit, baseGlow);
          mesh.object.scale.setScalar(entry.size);
          mesh.object.position.set(entry.home.x, entry.home.y + y, TITLE_Z);
          group.add(mesh.object);
          return mesh;
        });
      };
      const meshes = [...make(info.title, 0.75, 0), ...make(info.artist, 0.4, -1.0)];
      this.root.add(group);
      this.title = { trackID, group, meshes, opacity: 0, fading: false };
    }
    if (!this.title) return;
    this.title.group.position.y = -this.stage.viewSize(CAMERA_Z - TITLE_Z).height / 2 + TITLE_MARGIN;
    this.title.opacity = damp(this.title.opacity, this.title.fading ? 0 : 1, 3, dt);
    this.title.meshes.forEach((mesh) => {
      mesh.setTint(this.colors.lyric, this.colors.highlight);
      mesh.setOpacity(this.title.opacity * this.vizAmount);
    });
    if (this.title.fading && this.title.opacity < 0.01) {
      this.title.meshes.forEach((mesh) => mesh.dispose());
      this.title.group.removeFromParent();
      this.title = null;
    }
  }

  updateGap(view, deck, frame) {
    this.gapSeen = frame.now;
    super.updateGap(view, deck, frame);
  }

  deactivate() {
    this.chrome?.dispose();
    this.grain?.dispose();
    this.chromeTextures?.dispose();
    this.title?.meshes.forEach((mesh) => mesh.dispose());
    this.title = null;
    super.deactivate();
  }

  // MARK: Lines

  /** Poster-style placement like the 2D themes: left, centred or right from line to line, fitted to the window. */
  #compose(line, index) {
    const texts = line.w.map((w) => this.factory.display(w.t));
    const kind = ['center', 'left', 'center', 'right'][index % 4];
    const layout = composeLine(texts.map((t) => this.measure(t)), this.stage.viewSize(CAMERA_Z), kind, { maxSize: MAX_WORD_SIZE });
    return texts.map((text, i) => ({
      text,
      word: line.w[i],
      size: layout.size,
      home: new THREE.Vector3(layout.words[i].x, layout.words[i].y - layout.size * CAP_OFFSET, 0),
    }));
  }

  createLine(deck, line, index, frame) {
    const view = this.newView('line', line, index, frame);
    this.addWords(view, this.#compose(line, index)).forEach((item) => {
      const r = (k) => seeded(index * 97 + item.i * 13 + k);
      item.start = new THREE.Vector3(item.home.x * 3 + (r(1) - 0.5) * 16, item.home.y * 3 + (r(2) - 0.5) * 10, -70 - r(3) * 50);
      item.spin = r(4) > 0.5 ? 1 : -1;
      item.object.visible = false;
    });
    if (deck.role === 'current') this.dolly = 1;
    return view;
  }

  updateLine(view, deck, { now, dt, pulse }) {
    const pos = deck.position;
    view.items.forEach((item) => {
      const launch = Math.max(view.line.s, item.word.s - FLIGHT);
      const arrive = Math.max(item.word.s, launch + MIN_FLIGHT);
      // Eased so a word re-timed to the singer mid-flight doesn't skip.
      const t = this.smooth(item, 'flight', clamp01((pos - launch) / (arrive - launch)), dt, 30);
      const e = easeOutCubic(t);
      const object = item.object;

      const land = clamp01((pos - arrive) / 0.45);
      const phase = wordPhase(item.word, pos);
      const depthTarget = phase.state === 'singing' ? SINGING_DEPTH : phase.state === 'done' ? SUNG_DEPTH : 0;
      const depth = this.smooth(item, 'depth', depthTarget, dt, 8) * land;
      object.position.set(
        lerp(item.start.x, item.home.x, e),
        lerp(item.start.y, item.home.y, e) + Math.sin(now * 2.4 + item.i) * 0.06 * land,
        lerp(item.start.z, item.home.z, easeOutBack(t, 2.4)) + depth,
      );
      // Spin settles well before landing, so a landing word never swings into its neighbours.
      const turn = (1 - e) ** 2;
      object.rotation.set(turn * 0.7 * item.spin, turn * Math.PI * 1.6 * item.spin, 0);

      // Springy landing squash; the sung word swells a little.
      const squash = Math.sin(land * Math.PI) * (1 - land) * 0.45;
      const swell = this.smooth(item, 'swell', phase.state === 'singing' ? 0.05 + pulse * 0.02 : 0, dt, 10);
      object.scale.set(1 + squash + swell, 1 - squash * 0.6 + swell, 1 + squash).multiplyScalar(item.size);

      // Album-art colours: tinted chrome, and the word being sung glows in the highlight colour.
      item.mesh.setTint(this.colors.lyric, this.colors.highlight);
      const glow = phase.state === 'singing' ? 1.7 - phase.t * 0.5 : phase.state === 'done' ? 0.3 : 0;
      item.mesh.setGlow(this.smooth(item, 'glow', glow, dt, 12));
      const rest = phase.state === 'singing' ? 1 : phase.state === 'done' ? SUNG_OPACITY : WAITING_OPACITY;
      // Flying in at full strength, then settling to the waiting level until the word is sung.
      const level = lerp(1, rest, land);
      item.mesh.setOpacity(clamp01(t * 3) * this.smooth(item, 'dim', level, dt, 8) * deck.opacity);

      if (t >= 1) this.popOnce(item, phase);
    });
  }

  exitLine(view, deck, { now }) {
    const age = now - view.exitStart;
    view.items.forEach((item) => {
      const k = clamp01((age - item.i * STAGGER) / EXIT);
      const e = easeInCubic(k);
      // Immediate push toward the camera (clears the incoming line), then accelerate past it.
      const push = k * 6 + e * 16;
      item.object.position.set(item.exitFrom.x * (1 + e * 1.4), item.exitFrom.y + 1.2 * k + e * 2, item.exitFrom.z + push);
      item.object.rotation.z = e * item.spin * 0.9;
      // Gone before it gets close enough to fill the screen.
      item.mesh.setOpacity((1 - smoothstep(0.02, 0.32, k)) * deck.opacity);
    });
    return age < EXIT + view.items.length * STAGGER + 0.05;
  }

  // MARK: Emoji fly in from deep space and land on their word

  /** Above words on the top row, below words on the bottom row, beside words on middle rows. */
  stickerAnchor(view, cue, slot, count) {
    const maxX = this.stage.viewSize(CAMERA_Z).width / 2 - 1.3;
    const clampX = (x) => Math.max(-maxX, Math.min(maxX, x));
    if (cue.role === 'emotion' && view.items[0]) {
      const first = view.items[0];
      return new THREE.Vector3(clampX(first.home.x - first.width / 2 - 0.9), first.home.y + 1.0, STICKER_DEPTH);
    }
    const item = Number.isInteger(cue.wordIndex) ? view.items[cue.wordIndex] : null;
    if (!item) return super.stickerAnchor(view, cue, slot, count);

    const rows = view.items.map((it) => it.home.y);
    const top = Math.max(...rows);
    const bottom = Math.min(...rows);
    const singleRow = top - bottom < 0.1;
    if (singleRow || item.home.y >= top - 0.05 || item.home.y <= bottom + 0.05) {
      const above = singleRow ? slot % 2 === 0 : item.home.y >= top - 0.05;
      return new THREE.Vector3(clampX(item.home.x), item.home.y + (above ? STICKER_REACH : -STICKER_REACH), STICKER_DEPTH);
    }
    const side = item.home.x >= 0 ? 1 : -1;
    return new THREE.Vector3(clampX(item.home.x + side * (item.width / 2 + 1.1)), item.home.y, STICKER_DEPTH);
  }

  updateSticker(sticker, view, deck, { now }) {
    const age = now - sticker.born;
    const t = clamp01(age / STICKER_FLIGHT);
    const e = easeOutCubic(t);
    if (!sticker.start) {
      const r = (k) => seeded(view.index * 41 + sticker.slot * 7 + k);
      sticker.start = sticker.anchor.clone().add(new THREE.Vector3((r(1) - 0.5) * 8, (r(2) - 0.5) * 5, -55));
      sticker.spin = r(3) > 0.5 ? 1 : -1;
    }
    const land = clamp01((age - STICKER_FLIGHT) / 0.4);
    sticker.object.position.set(
      lerp(sticker.start.x, sticker.anchor.x, e),
      lerp(sticker.start.y, sticker.anchor.y, e) + Math.sin(now * 1.6 + sticker.slot * 1.3) * 0.1 * land,
      lerp(sticker.start.z, sticker.anchor.z, easeOutBack(t, 2)),
    );
    sticker.object.rotation.set(0, (1 - e) * Math.PI * 1.2 * sticker.spin, Math.sin(now * 1.1 + sticker.slot * 2) * 0.08 * land);
    const squash = Math.sin(land * Math.PI) * (1 - land) * 0.25;
    sticker.object.scale.set(1 + squash, 1 - squash * 0.6, 1).multiplyScalar(this.stickerScale());
    sticker.material.opacity = clamp01(t * 2.5) * deck.opacity;
  }

  /** Emoji leave with their line: straight past the camera. */
  exitSticker(sticker, deck, k) {
    const e = easeInCubic(k);
    sticker.object.position.z = sticker.exitFrom.z + k * 9 + e * 30;
    sticker.material.opacity = (sticker.exitOpacity ?? 1) * (1 - smoothstep(0.05, 0.6, k)) * deck.opacity;
  }
}
