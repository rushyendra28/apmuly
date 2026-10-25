import * as THREE from '../../vendor/three.module.min.js';

// Printed type shared by the 2D themes and the frame furniture: crisp canvas text drawn by a shader
// that can blur it into grain, smear it and dissolve it. Shaders work in sRGB and convert to linear
// at the end (the stage's output pass converts back).

export const DISPLAY_FONT = 'Apmuly Display';
export const DISPLAY_STACK = `"${DISPLAY_FONT}", "Helvetica Neue", "Segoe UI", Arial, "Hiragino Sans", "PingFang SC", "Microsoft YaHei", sans-serif`;
const FONT_PX = 180;
/** Canvases wider than this (very long unbroken words) are drawn smaller instead of failing. */
const MAX_CANVAS = 4096;
/** Room around each word for its grainy blur to spread into (ems). */
const PAD_X = 0.5;
const PAD_Y = 0.35;
const MAX_TEXTURES = 300;

export const NOISE = `
  // Precision-safe for large pixel coordinates (no visible patterns at any window size).
  float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
  }
  // Frame counters and seeds are kept small so float precision never degrades over long sessions.
  float tick(float time, float rate) { return mod(floor(time * rate), 64.0); }
  float small(float seed) { return fract(seed * 0.1373) * 97.0; }
  float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
  }
  float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
    return v;
  }`;

const INK_VERTEX = `
  varying vec2 vUv;
  void main() {
    vUv = uv;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }`;

/** A grainy, stochastic blur (each pixel samples a random nearby point), plus smear and dissolve. */
const INK_FRAGMENT = `
  uniform sampler2D map;
  uniform vec3 ink;
  uniform float opacity;
  uniform float blur;
  uniform float smear;
  uniform float side;
  uniform float dissolve;
  uniform float time;
  uniform float seed;
  uniform float aspect;
  uniform float grit;
  varying vec2 vUv;
  ${NOISE}
  void main() {
    vec2 cell = gl_FragCoord.xy;
    float frame = tick(time, 24.0) * 7.0;
    float s = small(seed);
    vec2 jitter = vec2(hash(cell + s + frame), hash(cell.yx + s * 1.7 + frame * 1.3)) - 0.5;
    float along = side > 0.0 ? vUv.x : 1.0 - vUv.x;
    float smeared = smear * smoothstep(0.3, 1.0, along);
    vec2 offset = jitter * vec2((blur + smeared * 1.6) / aspect, blur + smeared * 0.3) * 0.5;
    float alpha = texture2D(map, vUv + offset).a;
    alpha = clamp(alpha * 1.15 - hash(cell * 0.7 + s) * grit, 0.0, 1.0);
    if (hash(cell + s * 3.0 + frame) < dissolve) discard;
    gl_FragColor = vec4(pow(ink, vec3(2.2)), alpha * opacity);
  }`;

/** An sRGB ink colour for the shader (`#rrggbb` → 0…1 components). */
export function inkColor(hex) {
  const value = parseInt(hex.slice(1), 16);
  return new THREE.Vector3(((value >> 16) & 255) / 255, ((value >> 8) & 255) / 255, (value & 255) / 255);
}

/**
 * A plane of ink (word, caption, mark or stamp); its size is in ems / world units as given.
 * `shared` carries the `ink` and `time` uniforms so a whole theme can be recoloured at once.
 */
export function inkMesh(entry, shared, seed, { grit = 0.18 } = {}) {
  const material = new THREE.ShaderMaterial({
    vertexShader: INK_VERTEX,
    fragmentShader: INK_FRAGMENT,
    uniforms: {
      map: { value: entry.texture },
      ink: shared.ink,
      time: shared.time,
      opacity: { value: 0 },
      blur: { value: 0 },
      smear: { value: 0 },
      side: { value: 1 },
      dissolve: { value: 0 },
      seed: { value: seed },
      aspect: { value: entry.width / entry.height },
      grit: { value: grit },
    },
    transparent: true,
    depthTest: false,
    depthWrite: false,
  });
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(entry.width, entry.height), material);
  mesh.renderOrder = 5;
  return mesh;
}

export function canvasTexture(canvas) {
  const t = new THREE.CanvasTexture(canvas);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 8;
  return t;
}

/** Draws `text` into a new canvas no wider than MAX_CANVAS, shrinking the font if needed. */
function textCanvas(text, font, px, padX, padY, measure) {
  measure.font = `${font.replace('{px}', px)}`;
  const natural = measure.measureText(text).width;
  const scale = Math.min(1, (MAX_CANVAS - padX * 2 * px) / Math.max(1, natural));
  const drawPx = Math.max(8, Math.floor(px * scale));
  const canvas = document.createElement('canvas');
  canvas.width = Math.ceil(natural * (drawPx / px) + padX * 2 * px);
  canvas.height = Math.ceil(px * (1 + padY * 2));
  const ctx = canvas.getContext('2d');
  ctx.font = font.replace('{px}', drawPx);
  ctx.fillStyle = '#ffffff';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(text, canvas.width / 2, canvas.height / 2 + px * 0.05);
  return { canvas, ink: (natural * (drawPx / px)) / px };
}

/**
 * Cached ink textures: words (with blur room), small captions, print marks and emoji stamps.
 * The cache keeps recently used entries (a hit refreshes it), so textures on screen aren't evicted.
 */
export class InkTextures {
  constructor({ font = `700 {px}px ${DISPLAY_STACK}` } = {}) {
    this.font = font;
    this.cache = new Map();
    this.measureContext = document.createElement('canvas').getContext('2d');
  }

  #get(key, build) {
    const hit = this.cache.get(key);
    if (hit) {
      this.cache.delete(key);
      this.cache.set(key, hit);
      return hit;
    }
    const value = build();
    this.cache.set(key, value);
    if (this.cache.size > MAX_TEXTURES) {
      const [oldest, entry] = this.cache.entries().next().value;
      this.cache.delete(oldest);
      entry.texture.dispose();
    }
    return value;
  }

  /** Width of `text` at font size 1 (ems). */
  measure(text) {
    this.measureContext.font = this.font.replace('{px}', FONT_PX);
    return Math.min(this.measureContext.measureText(text).width, MAX_CANVAS) / FONT_PX;
  }

  /** A word in ems: `width`/`height` include blur room; `ink` is the glyph advance width. */
  word(text) {
    return this.#get(`w:${text}`, () => {
      const { canvas, ink } = textCanvas(text, this.font, FONT_PX, PAD_X, PAD_Y, this.measureContext);
      return { texture: canvasTexture(canvas), width: canvas.width / FONT_PX, height: canvas.height / FONT_PX, ink };
    });
  }

  /**
   * Small caption text, 1 unit tall. `volatile` captions (a running clock) aren't cached: the caller
   * owns the returned texture and disposes it when replaced.
   */
  caption(text, volatile = false) {
    const build = () => {
      const px = 64;
      const { canvas } = textCanvas(text, `700 {px}px ${DISPLAY_STACK}`, px, 0.1, 0.15, this.measureContext);
      return { texture: canvasTexture(canvas), width: canvas.width / canvas.height, height: 1 };
    };
    return volatile ? build() : this.#get(`c:${text}`, build);
  }

  /** Registration cross "+" or asterisk "*", 1 unit square. */
  mark(kind) {
    return this.#get(`m:${kind}`, () => {
      const canvas = document.createElement('canvas');
      canvas.width = 96;
      canvas.height = 96;
      const ctx = canvas.getContext('2d');
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = kind === '+' ? 5 : 6;
      ctx.lineCap = 'round';
      ctx.beginPath();
      const arms = kind === '+' ? [0, Math.PI / 2] : [Math.PI / 2, Math.PI / 2 + Math.PI / 3, Math.PI / 2 - Math.PI / 3];
      arms.forEach((a) => {
        ctx.moveTo(48 - Math.cos(a) * 34, 48 - Math.sin(a) * 34);
        ctx.lineTo(48 + Math.cos(a) * 34, 48 + Math.sin(a) * 34);
      });
      ctx.stroke();
      return { texture: canvasTexture(canvas), width: 1, height: 1 };
    });
  }

  /** An emoji printed as a dithered ink stamp (1 unit square): halftone silhouette, solid dark features. */
  stamp(emoji) {
    return this.#get(`s:${emoji}`, () => {
      const size = 128;
      const canvas = document.createElement('canvas');
      canvas.width = size;
      canvas.height = size;
      const ctx = canvas.getContext('2d');
      ctx.font = `${size * 0.72}px 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(emoji, size / 2, size / 2 + size * 0.04);
      const image = ctx.getImageData(0, 0, size, size);
      const bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
      for (let i = 0; i < size * size; i++) {
        const p = i * 4;
        const lum = (0.2126 * image.data[p] + 0.7152 * image.data[p + 1] + 0.0722 * image.data[p + 2]) / 255;
        const dark = Math.min(1, Math.max(0, (0.72 - lum) / 0.45));
        const inkAmount = (image.data[p + 3] / 255) * (0.28 + 0.72 * dark * dark);
        const threshold = (bayer[((i / size) & 3) * 4 + (i % size & 3)] + 0.5) / 16;
        image.data[p] = 255;
        image.data[p + 1] = 255;
        image.data[p + 2] = 255;
        image.data[p + 3] = inkAmount > threshold ? 255 : 0;
      }
      ctx.putImageData(image, 0, 0);
      return { texture: canvasTexture(canvas), width: 1, height: 1 };
    });
  }

  dispose() {
    this.cache.forEach((entry) => entry.texture.dispose());
    this.cache.clear();
  }
}

/** Resolves once the display font is ready (falls back to system fonts if it can't load). */
export function loadDisplayFont() {
  if (!document.fonts?.load) return Promise.resolve();
  return document.fonts.load(`700 64px "${DISPLAY_FONT}"`).catch(() => undefined);
}
