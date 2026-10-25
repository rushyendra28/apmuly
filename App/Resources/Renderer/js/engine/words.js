import * as THREE from 'three';
import { TextGeometry } from 'three/addons/geometries/TextGeometry.js';

const DEPTH = 0.32;
/** Vertical offset that centres capital letters on y = 0 for a size-1 font. */
const CAP_CENTER = 0.36;
const CAP_HEIGHT = 0.72;
const MAX_CACHE = 400;
const FALLBACK_FONT = "800 128px 'SF Pro Rounded', ui-rounded, -apple-system, 'Hiragino Maru Gothic ProN', "
  + "'Hiragino Sans', 'Kohinoor Devanagari', 'Apple SD Gothic Neo', 'PingFang SC', sans-serif";
const FALLBACK_PX = 128;
const WHITE = new THREE.Color('#ffffff');
const MAX_EMISSIVE = 0.55;

/**
 * Builds word meshes. Text the 3D font can draw becomes extruded, bevelled geometry;
 * anything else (e.g. non-Latin scripts) becomes a layered "sticker" from a canvas texture.
 * Geometry and textures are cached by text and shared between meshes.
 */
export class WordFactory {
  constructor(font) {
    this.font = font;
    this.geometries = new Map();
    this.textures = new Map();
    this.points = new Map();
  }

  /** Display form of a lyric word (punchy uppercase). */
  display(text) {
    return text.toLocaleUpperCase();
  }

  canExtrude(text) {
    const glyphs = this.font.data.glyphs;
    return [...text].every((ch) => /\s/.test(ch) || glyphs[ch]);
  }

  /** @returns {{geometry: THREE.BufferGeometry, width: number}} centred on the origin. */
  geometry(text) {
    return this.#cached(this.geometries, text, () => {
      const geometry = new TextGeometry(text, {
        font: this.font, size: 1, height: DEPTH, curveSegments: 5,
        bevelEnabled: true, bevelThickness: 0.05, bevelSize: 0.03, bevelSegments: 3,
      });
      geometry.computeBoundingBox();
      const box = geometry.boundingBox;
      const width = Math.max(0.3, box.max.x - box.min.x);
      geometry.translate(-(box.min.x + width / 2), -CAP_CENTER, -DEPTH / 2);
      return { geometry, width, dispose: () => geometry.dispose() };
    });
  }

  /** @returns {{texture: THREE.Texture, width: number, height: number}} world-space size. */
  texture(text) {
    return this.#cached(this.textures, text, () => {
      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d');
      ctx.font = FALLBACK_FONT;
      const measured = Math.ceil(ctx.measureText(text).width);
      canvas.width = measured + 48;
      canvas.height = Math.round(FALLBACK_PX * 1.5);
      ctx.font = FALLBACK_FONT;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.lineJoin = 'round';
      ctx.lineWidth = 14;
      ctx.strokeStyle = 'rgba(0,0,0,0.85)';
      ctx.strokeText(text, canvas.width / 2, canvas.height / 2);
      ctx.fillStyle = '#ffffff';
      ctx.fillText(text, canvas.width / 2, canvas.height / 2);

      const texture = new THREE.CanvasTexture(canvas);
      texture.colorSpace = THREE.SRGBColorSpace;
      const worldPerPx = CAP_HEIGHT / (FALLBACK_PX * 0.72);
      return {
        texture,
        canvas,
        width: canvas.width * worldPerPx,
        height: canvas.height * worldPerPx,
        dispose: () => texture.dispose(),
      };
    });
  }

  /** Letter-by-letter layout for per-letter animation: [{ch, x, width}] with x = centre. */
  letters(text) {
    if (!this.canExtrude(text)) {
      return [{ ch: text, x: 0, width: this.texture(text).width }];
    }
    const { glyphs, resolution } = this.font.data;
    const advances = [...text].map((ch) => ({ ch, advance: (glyphs[ch]?.ha ?? 500) / resolution }));
    const total = advances.reduce((sum, a) => sum + a.advance, 0);
    let cursor = -total / 2;
    return advances.flatMap(({ ch, advance }) => {
      const item = { ch, x: cursor + advance / 2, width: advance };
      cursor += advance;
      return /\s/.test(ch) ? [] : [item];
    });
  }

  /**
   * @param {object} [style] MeshPhysicalMaterial overrides (metalness, roughness, iridescence…).
   * @param {number} [glowLimit] Max emissive intensity (themes with white-on-dark text can go brighter).
   */
  make(text, color, style = {}, glowLimit = MAX_EMISSIVE) {
    return new WordMesh(this, text, color, style, glowLimit);
  }

  /** Random points on the word's surface, in the same local space as its mesh. */
  surfacePoints(text, count) {
    return this.#cached(this.points, `${count}|${text}`, () => ({
      points: this.canExtrude(text)
        ? sampleGeometry(this.geometry(text).geometry, count)
        : sampleCanvas(this.texture(text), count),
      dispose: () => {},
    })).points;
  }

  #cached(map, key, build) {
    const hit = map.get(key);
    if (hit) return hit;
    const value = build();
    map.set(key, value);
    if (map.size > MAX_CACHE) {
      const [oldestKey, oldest] = map.entries().next().value;
      map.delete(oldestKey);
      oldest.dispose();
    }
    return value;
  }
}

/** One renderable word with colour, opacity and glow controls. */
export class WordMesh {
  constructor(factory, text, color, style = {}, glowLimit = MAX_EMISSIVE) {
    this.text = text;
    this.object = new THREE.Group();
    this.color = new THREE.Color(color);
    this.ownedGeometries = [];
    this.glowLimit = glowLimit;

    if (factory.canExtrude(text)) {
      const { geometry, width } = factory.geometry(text);
      this.kind = 'solid';
      this.material = new THREE.MeshPhysicalMaterial({
        color: this.color, emissive: this.color, emissiveIntensity: 0,
        roughness: 0.28, metalness: 0.05, clearcoat: 1, clearcoatRoughness: 0.12, transparent: true,
        ...style,
      });
      this.materials = [this.material];
      this.object.add(new THREE.Mesh(geometry, this.material));
      this.width = width;
      this.height = CAP_HEIGHT;
    } else {
      const { texture, width, height } = factory.texture(text);
      const plane = new THREE.PlaneGeometry(width, height);
      this.ownedGeometries.push(plane);
      this.kind = 'flat';
      this.front = new THREE.MeshBasicMaterial({ map: texture, color: this.color, transparent: true, depthWrite: false });
      const layers = [3, 2, 1].map((depth) => {
        const material = new THREE.MeshBasicMaterial({
          map: texture, color: this.color.clone().multiplyScalar(0.35 + 0.1 * (3 - depth)),
          transparent: true, depthWrite: false,
        });
        const mesh = new THREE.Mesh(plane, material);
        mesh.position.set(0.012 * depth, -0.012 * depth, -0.05 * depth);
        this.object.add(mesh);
        return material;
      });
      this.object.add(new THREE.Mesh(plane, this.front));
      this.materials = [...layers, this.front];
      this.width = width * 0.85;
      this.height = CAP_HEIGHT;
    }
    this.glow = 0;
  }

  setColor(color) {
    this.color.set(color);
    if (this.kind === 'solid') {
      this.material.color.copy(this.color);
      this.material.emissive.copy(this.color);
    }
    this.setGlow(this.glow);
  }

  setOpacity(opacity) {
    const o = Math.min(1, Math.max(0, opacity));
    this.materials.forEach((m) => { m.opacity = o; });
    this.object.visible = o > 0.003;
  }

  /**
   * Brightens the word for emphasis. Capped so the colour stays saturated and the
   * letter edges stay crisp (no halo / wash-out to white).
   */
  setGlow(glow) {
    this.glow = glow;
    if (this.kind === 'solid') {
      this.material.emissiveIntensity = Math.min(this.glowLimit, Math.max(0, glow) * 0.3 * (this.glowLimit / MAX_EMISSIVE));
    } else {
      this.front.color.copy(this.color).lerp(WHITE, Math.min(0.3, Math.max(0, glow) * 0.12));
    }
  }

  dispose() {
    this.materials.forEach((m) => m.dispose());
    this.ownedGeometries.forEach((g) => g.dispose());
    this.object.removeFromParent();
  }
}

function sampleGeometry(geometry, count) {
  const position = geometry.getAttribute('position');
  const triangles = Math.floor(position.count / 3);
  const out = new Float32Array(count * 3);
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  const c = new THREE.Vector3();
  for (let i = 0; i < count; i++) {
    const t = Math.floor(Math.random() * triangles) * 3;
    a.fromBufferAttribute(position, t);
    b.fromBufferAttribute(position, t + 1);
    c.fromBufferAttribute(position, t + 2);
    let u = Math.random();
    let v = Math.random();
    if (u + v > 1) { u = 1 - u; v = 1 - v; }
    out[i * 3] = a.x + (b.x - a.x) * u + (c.x - a.x) * v;
    out[i * 3 + 1] = a.y + (b.y - a.y) * u + (c.y - a.y) * v;
    out[i * 3 + 2] = a.z + (b.z - a.z) * u + (c.z - a.z) * v;
  }
  return out;
}

function sampleCanvas({ canvas, width, height }, count) {
  const ctx = canvas.getContext('2d');
  const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height);
  const out = new Float32Array(count * 3);
  for (let i = 0, tries = 0; i < count && tries < count * 60; tries++) {
    const x = Math.floor(Math.random() * canvas.width);
    const y = Math.floor(Math.random() * canvas.height);
    const p = (y * canvas.width + x) * 4;
    if (data[p + 3] > 128 && data[p] > 128) {
      out[i * 3] = (x / canvas.width - 0.5) * width;
      out[i * 3 + 1] = (0.5 - y / canvas.height) * height;
      out[i * 3 + 2] = (Math.random() - 0.5) * 0.1;
      i++;
    }
  }
  return out;
}
