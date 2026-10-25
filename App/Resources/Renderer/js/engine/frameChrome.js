import * as THREE from '../../vendor/three.module.min.js';
import { inkMesh, inkColor } from './ink.js';

// The frame every theme shares, like a printed poster: the song and artist top-left, a running
// timer bottom-right, registration crosses in the corners, an asterisk on top, the title running up
// the right edge, and a spectrum "barcode" for songs without lyrics. It rides with the camera, so it
// stays put while a theme's camera moves.

const CAPTION_SHARE = 0.022;
const MARK_SHARE = 0.03;
const VERTICAL_SHARE = 0.034;
const SPECTRUM_BARS = 40;
const SPECTRUM_WIDTH_SHARE = 0.26;
const SPECTRUM_HEIGHT_SHARE = 0.07;
/** Distance in front of the camera (inside the near plane of any theme's content). */
const DISTANCE = 4;

const clock = (seconds) => {
  const s = Math.max(0, Math.floor(seconds));
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
};

export class FrameChrome {
  /**
   * @param {import('./stage.js').Stage} stage
   * @param {import('./ink.js').InkTextures} textures
   * @param {{ink?: string}} [options] ink colour (#rrggbb): dark on light themes, light on dark ones
   */
  constructor(stage, textures, { ink = '#151515' } = {}) {
    this.stage = stage;
    this.textures = textures;
    this.shared = { ink: { value: inkColor(ink) }, time: { value: 0 } };
    this.group = new THREE.Group();
    this.group.position.z = -DISTANCE;
    if (!stage.camera.parent) stage.scene.add(stage.camera);
    stage.camera.add(this.group);
    this.marks = ['+', '+', '+', '+', '*'].map((kind, i) => this.#mesh(textures.mark(kind), 100 + i));
    this.captions = new Map();
    this.spectrum = Array.from({ length: SPECTRUM_BARS }, () => {
      const bar = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ color: ink, transparent: true, depthTest: false, toneMapped: false }));
      bar.renderOrder = 60;
      this.group.add(bar);
      return bar;
    });
  }

  setInk(hex) {
    this.shared.ink.value.copy(inkColor(hex));
    this.spectrum.forEach((bar) => bar.material.color.set(hex));
  }

  #mesh(entry, seed) {
    const mesh = inkMesh(entry, this.shared, seed, { grit: 0.08 });
    mesh.material.uniforms.opacity.value = 1;
    mesh.renderOrder = 60;
    this.group.add(mesh);
    return mesh;
  }

  /** One caption slot; rebuilt only when its text changes. The clock's textures are owned here. */
  #caption(slot, text, volatile = false) {
    const current = this.captions.get(slot);
    if (current?.text === text) return current.mesh;
    if (current) this.#drop(current);
    const entry = this.textures.caption(text, volatile);
    const mesh = this.#mesh(entry, 200 + this.captions.size);
    this.captions.set(slot, { text, mesh, owned: volatile ? entry.texture : null });
    return mesh;
  }

  #drop(caption) {
    caption.mesh.removeFromParent();
    caption.mesh.geometry.dispose();
    caption.mesh.material.dispose();
    caption.owned?.dispose();
  }

  /** Captions are rebuilt (e.g. once the display font has loaded). */
  refresh() {
    this.captions.forEach((caption) => this.#drop(caption));
    this.captions.clear();
  }

  /**
   * @param {{title: string, artist: string}|null} info
   * @param {number|null} position song position (s)
   * @param {number[]|null} bands spectrum, shown only when `showSpectrum`
   */
  update(now, info, position, bands, showSpectrum, opacity) {
    this.shared.time.value = now;
    const view = this.stage.viewSize(DISTANCE);
    const unit = Math.min(view.height, view.width * 0.75);
    const margin = 0.045 * Math.min(view.width, view.height);
    const halfW = view.width / 2 - margin;
    const halfH = view.height / 2 - margin;
    const mark = unit * MARK_SHARE;
    [[-halfW, halfH], [halfW, halfH], [-halfW, -halfH], [halfW, -halfH], [0, halfH]].forEach(([x, y], i) => {
      const mesh = this.marks[i];
      mesh.position.set(x, y, 0);
      mesh.scale.setScalar(mark);
      mesh.material.uniforms.opacity.value = 0.85 * opacity;
    });

    const size = unit * CAPTION_SHARE;
    const left = -halfW + mark * 1.4;
    const place = (slot, text, x, y, anchorLeft = true, volatile = false) => {
      const mesh = this.#caption(slot, text, volatile);
      const width = mesh.geometry.parameters.width * size;
      mesh.scale.setScalar(size);
      mesh.position.set(anchorLeft ? x + width / 2 : x - width / 2, y, 0);
      mesh.material.uniforms.opacity.value = opacity;
      return mesh;
    };
    place('title', info?.title ?? 'Apmuly', left, halfH, true);
    place('artist', info?.artist || ' ', left, halfH - size * 1.25, true);
    place('time', position === null ? '--:--' : clock(position), halfW - mark * 1.4, -halfH, false, true);
    place('brand', 'APMULY / LYRICS', left, -halfH, true);

    const vertical = place('vertical', (info?.title ?? 'Apmuly').toUpperCase(), 0, 0, true);
    const verticalSize = unit * VERTICAL_SHARE;
    vertical.scale.setScalar(verticalSize);
    vertical.rotation.z = Math.PI / 2;
    vertical.position.set(view.width / 2 - margin * 0.6 - verticalSize / 2, 0, 0);
    const fits = vertical.geometry.parameters.width * verticalSize < view.height * 0.8 && view.width > view.height;
    vertical.material.uniforms.opacity.value = fits ? opacity : 0;

    this.#updateSpectrum(view, bands, showSpectrum ? opacity : 0);
  }

  #updateSpectrum(view, bands, opacity) {
    const width = view.width * SPECTRUM_WIDTH_SHARE;
    const maxHeight = view.height * SPECTRUM_HEIGHT_SHARE;
    const baseY = -view.height / 2 + view.height * 0.12;
    const step = width / SPECTRUM_BARS;
    this.spectrum.forEach((bar, i) => {
      const level = bands?.length ? bands[Math.floor((i / SPECTRUM_BARS) * bands.length)] ?? 0 : 0.2;
      const height = Math.max(0.02 * (view.height / 13), level * maxHeight);
      bar.scale.set(step * (i % 3 === 0 ? 0.55 : 0.28), height, 1);
      bar.position.set(-width / 2 + (i + 0.5) * step, baseY + height / 2, 0);
      bar.material.opacity = opacity;
      bar.visible = opacity > 0.01;
    });
  }

  dispose() {
    this.refresh();
    this.group.removeFromParent();
    this.group.traverse((o) => {
      o.geometry?.dispose();
      [o.material].flat().forEach((m) => m?.dispose());
    });
  }
}
