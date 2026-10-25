import * as THREE from 'three';

const EMOJI_PX = 160;
const EMOJI_SIZE = 1.5;
const EMOJI_FONT = `${EMOJI_PX}px 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif`;

/** Builds emoji sticker meshes. Emoji textures are cached and shared. */
export class StickerFactory {
  constructor() {
    this.textures = new Map();
  }

  /** @returns {{object: THREE.Mesh, material: THREE.Material, size: number, dispose: () => void}|null} */
  make(cue) {
    if (!cue.emoji) return null;
    const geometry = new THREE.PlaneGeometry(EMOJI_SIZE, EMOJI_SIZE);
    const material = new THREE.MeshBasicMaterial({
      map: this.#texture(cue.emoji), transparent: true, depthWrite: false, toneMapped: false,
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.renderOrder = 5;
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

  #texture(emoji) {
    const hit = this.textures.get(emoji);
    if (hit) return hit;
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(EMOJI_PX * 1.25);
    canvas.height = canvas.width;
    const ctx = canvas.getContext('2d');
    ctx.font = EMOJI_FONT;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(emoji, canvas.width / 2, canvas.height / 2 + EMOJI_PX * 0.06);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    this.textures.set(emoji, texture);
    return texture;
  }
}
