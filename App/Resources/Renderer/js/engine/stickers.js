import * as THREE from '../../vendor/three.module.min.js';

const EMOJI_PX = 128;
const EMOJI_SIZE = 1.5;
/** Extra room around the glyph so no emoji is ever clipped by its texture edge. */
const PADDING = 1.45;
/** An emoji measuring wider than this many standard emoji is a joined sequence that fell apart. */
const SPLIT_RATIO = 1.6;
const EMOJI_FONT = `${EMOJI_PX}px 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif`;

/** Builds emoji sticker meshes. Emoji textures are cached and shared. */
export class StickerFactory {
  constructor() {
    this.textures = new Map();
    this.measure = document.createElement('canvas').getContext('2d');
    this.measure.font = EMOJI_FONT;
    this.standardWidth = this.measure.measureText('😀').width || EMOJI_PX;
  }

  /** @returns {{object: THREE.Mesh, material: THREE.Material, size: number, dispose: () => void}|null} */
  make(cue) {
    if (!cue.emoji) return null;
    const geometry = new THREE.PlaneGeometry(EMOJI_SIZE, EMOJI_SIZE);
    // Always drawn on top, so streaks and words passing in front never slice through an emoji.
    const material = new THREE.MeshBasicMaterial({
      map: this.#texture(cue.emoji), transparent: true, depthWrite: false, depthTest: false, toneMapped: false,
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.renderOrder = 20;
    return {
      object: mesh,
      material,
      size: EMOJI_SIZE,
      dispose: () => {
        geometry.dispose();
        material.dispose();
        mesh.removeFromParent();
      },
    };
  }

  /** The emoji itself, or its first character if this system draws the sequence in pieces. */
  displayable(emoji) {
    const width = this.measure.measureText(emoji).width;
    return width > this.standardWidth * SPLIT_RATIO ? Array.from(emoji)[0] : emoji;
  }

  #texture(emoji) {
    const hit = this.textures.get(emoji);
    if (hit) return hit;
    const glyph = this.displayable(emoji);
    const metrics = this.measure.measureText(glyph);
    const ascent = metrics.actualBoundingBoxAscent || EMOJI_PX * 0.8;
    const descent = metrics.actualBoundingBoxDescent || EMOJI_PX * 0.2;
    const inkWidth = Math.max(metrics.width, (metrics.actualBoundingBoxLeft || 0) + (metrics.actualBoundingBoxRight || 0));
    const side = Math.ceil(Math.max(inkWidth, ascent + descent, EMOJI_PX) * PADDING);

    const canvas = document.createElement('canvas');
    canvas.width = side;
    canvas.height = side;
    const ctx = canvas.getContext('2d');
    ctx.font = EMOJI_FONT;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'alphabetic';
    // Centre the glyph's ink box, not its baseline.
    ctx.fillText(glyph, side / 2, side / 2 + (ascent - descent) / 2);

    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    this.textures.set(emoji, texture);
    return texture;
  }
}
