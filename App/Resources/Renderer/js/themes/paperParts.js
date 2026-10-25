import * as THREE from '../../vendor/three.module.min.js';
import { NOISE, canvasTexture } from '../engine/ink.js';

// Building blocks for the Paper Brush theme: scene backgrounds (lined paper, painted colour, night sky)
// with a brush-edged wipe between them, self-drawing brush strokes, die-cut emoji stickers and torn
// paper labels. Shaders work in sRGB and convert to linear at the end.

export const SCENE = { paper: 0, paint: 1, night: 2 };

const PLANE_VERTEX = `
  varying vec2 vPos;
  void main() {
    vPos = (modelMatrix * vec4(position, 1.0)).xy;
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(position, 1.0);
  }`;

const BACKDROP_FRAGMENT = `
  uniform vec2 view;
  uniform float time;
  uniform float seed;
  uniform vec3 colorA;
  uniform vec3 colorB;
  uniform float kindA;
  uniform float kindB;
  uniform float wipe;
  uniform float wipeDir;
  varying vec2 vPos;
  ${NOISE}
  vec3 scene(vec3 color, float kind, vec2 q) {
    if (kind < 0.5) {
      // Notebook paper: soft fibres and faint ruled lines.
      vec3 c = color * (1.0 + (noise(q * 60.0) - 0.5) * 0.03 + (noise(q * 4.0 + seed) - 0.5) * 0.05);
      float rule = abs(fract(q.y / 0.055) - 0.5) * 0.055;
      return c * (1.0 - 0.07 * (1.0 - smoothstep(0.0, 0.0012, rule)));
    }
    if (kind < 1.5) {
      // Painted colour: horizontal brush streaks.
      float streak = noise(vec2(q.x * 1.5 + seed, q.y * 28.0)) * 0.6 + noise(vec2(q.x * 3.0, q.y * 70.0)) * 0.4;
      return color * (0.93 + streak * 0.12 + (noise(q * 2.5 + seed) - 0.5) * 0.08);
    }
    // Night sky: a deep gradient with twinkling stars.
    vec3 c = mix(color * 0.55, color * 1.1, smoothstep(-0.6, 0.7, q.y)) + (noise(q * 3.0 + seed) - 0.5) * 0.04;
    vec2 cell = floor(q * 70.0);
    float star = step(0.986, hash(cell + seed));
    float twinkle = 0.55 + 0.45 * sin(time * (1.5 + hash(cell) * 3.0) + hash(cell.yx) * 6.28);
    float d = length(fract(q * 70.0) - 0.5);
    return c + star * twinkle * smoothstep(0.22, 0.0, d) * 0.9;
  }
  void main() {
    vec2 q = vPos / view.y;
    vec3 a = scene(colorA, kindA, q);
    vec3 b = scene(colorB, kindB, q);
    // A wide brush sweeping across, with a ragged edge.
    float across = (q.x * wipeDir) / (view.x / view.y) + 0.5;
    float edge = wipe * 1.5 - 0.25 + (noise(vec2(q.y * 7.0, seed)) - 0.5) * 0.18 + (noise(vec2(q.y * 40.0, seed)) - 0.5) * 0.04;
    vec3 color = mix(a, b, step(across, edge));
    color += (hash(gl_FragCoord.xy + tick(time, 24.0) * 31.0) - 0.5) * 0.035;
    gl_FragColor = vec4(pow(clamp(color, 0.0, 1.0), vec3(2.2)), 1.0);
  }`;

const BRUSH_VERTEX = `
  attribute float along;
  attribute float across;
  varying float vAlong;
  varying float vAcross;
  void main() {
    vAlong = along;
    vAcross = across;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }`;

/** Dry-brush paint: bristle streaks along the stroke, ragged edges, drawn on as `progress` grows. */
const BRUSH_FRAGMENT = `
  uniform vec3 color;
  uniform float opacity;
  uniform float progress;
  uniform float seed;
  uniform float strokeLength;
  varying float vAlong;
  varying float vAcross;
  ${NOISE}
  void main() {
    if (vAlong > progress) discard;
    float s = small(seed);
    float bristles = noise(vec2(vAlong * strokeLength * 5.0 + s, vAcross * 6.0));
    float edge = 1.0 - smoothstep(0.6, 1.0, abs(vAcross) + (bristles - 0.5) * 0.4);
    float dry = smoothstep(0.1, 0.5, noise(vec2(vAlong * strokeLength * 1.6 + s * 2.0, vAcross * 2.5)) + 0.3);
    float alpha = edge * mix(0.72, 1.0, dry) * opacity;
    vec3 c = color * (0.88 + bristles * 0.24);
    gl_FragColor = vec4(pow(clamp(c, 0.0, 1.0), vec3(2.2)), alpha);
  }`;

const srgb = (color) => color.clone().convertLinearToSRGB();

export function backdropMaterial() {
  return new THREE.ShaderMaterial({
    vertexShader: PLANE_VERTEX,
    fragmentShader: BACKDROP_FRAGMENT,
    uniforms: {
      view: { value: new THREE.Vector2(1, 1) },
      time: { value: 0 },
      seed: { value: 0 },
      colorA: { value: new THREE.Color() },
      colorB: { value: new THREE.Color() },
      kindA: { value: 0 },
      kindB: { value: 0 },
      wipe: { value: 1 },
      wipeDir: { value: 1 },
    },
    depthWrite: false,
  });
}

/** Sets a scene colour uniform from a linear THREE.Color (the shader works in sRGB). */
export function setSceneColor(uniform, color) {
  uniform.value.copy(srgb(color));
}

/**
 * A brush stroke along `points` (world units) that draws itself on. Width tapers at both ends.
 * @returns {{mesh: THREE.Mesh, length: number}}
 */
export function brushStroke(points, width, color, seed) {
  const n = points.length;
  const lengths = [0];
  for (let i = 1; i < n; i++) lengths.push(lengths[i - 1] + Math.hypot(points[i][0] - points[i - 1][0], points[i][1] - points[i - 1][1]));
  const total = Math.max(1e-6, lengths[n - 1]);
  const positions = new Float32Array(n * 2 * 3);
  const along = new Float32Array(n * 2);
  const across = new Float32Array(n * 2);
  for (let i = 0; i < n; i++) {
    const [x0, y0] = points[Math.max(0, i - 1)];
    const [x1, y1] = points[Math.min(n - 1, i + 1)];
    const len = Math.hypot(x1 - x0, y1 - y0) || 1;
    const nx = -(y1 - y0) / len;
    const ny = (x1 - x0) / len;
    const t = lengths[i] / total;
    const taper = 0.45 + 0.55 * Math.min(1, t * 6) * Math.min(1, (1 - t) * 5);
    const half = (width / 2) * taper;
    positions.set([points[i][0] + nx * half, points[i][1] + ny * half, 0, points[i][0] - nx * half, points[i][1] - ny * half, 0], i * 6);
    along.set([t, t], i * 2);
    across.set([1, -1], i * 2);
  }
  const index = [];
  for (let i = 0; i < n - 1; i++) {
    const a = i * 2;
    index.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('along', new THREE.BufferAttribute(along, 1));
  geometry.setAttribute('across', new THREE.BufferAttribute(across, 1));
  geometry.setIndex(index);
  const material = new THREE.ShaderMaterial({
    vertexShader: BRUSH_VERTEX,
    fragmentShader: BRUSH_FRAGMENT,
    uniforms: {
      color: { value: srgb(color) },
      opacity: { value: 1 },
      progress: { value: 0 },
      seed: { value: seed },
      strokeLength: { value: total },
    },
    transparent: true,
    depthTest: false,
    depthWrite: false,
    side: THREE.DoubleSide,
  });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.frustumCulled = false;
  return { mesh, length: total };
}

/** An emoji as a die-cut sticker: white border and a soft shadow (texture, square). */
export function stickerTexture(emoji) {
  const size = 224;
  const glyphPx = 128;
  const glyph = document.createElement('canvas');
  glyph.width = size;
  glyph.height = size;
  const g = glyph.getContext('2d');
  g.font = `${glyphPx}px 'Apple Color Emoji', 'Segoe UI Emoji', 'Noto Color Emoji', sans-serif`;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillText(emoji, size / 2, size / 2 + glyphPx * 0.06);

  const outline = document.createElement('canvas');
  outline.width = size;
  outline.height = size;
  const o = outline.getContext('2d');
  const border = 11;
  for (let k = 0; k < 24; k++) {
    const a = (k / 24) * Math.PI * 2;
    o.drawImage(glyph, Math.cos(a) * border, Math.sin(a) * border);
  }
  o.drawImage(glyph, 0, 0);
  o.globalCompositeOperation = 'source-in';
  o.fillStyle = '#fffdf7';
  o.fillRect(0, 0, size, size);

  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d');
  ctx.shadowColor = 'rgba(30, 20, 10, 0.28)';
  ctx.shadowBlur = 10;
  ctx.shadowOffsetY = 5;
  ctx.drawImage(outline, 0, 0);
  ctx.shadowColor = 'transparent';
  ctx.drawImage(glyph, 0, 0);
  return canvasTexture(canvas);
}

/** A torn scrap of paper (label behind a word), `aspect` = width / height. */
export function labelTexture(aspect, random) {
  const height = 160;
  const width = Math.max(height, Math.min(2048, Math.round(height * aspect)));
  const canvas = document.createElement('canvas');
  canvas.width = width + 40;
  canvas.height = height + 40;
  const ctx = canvas.getContext('2d');
  const jag = (n) => (random() - 0.5) * n;
  ctx.beginPath();
  ctx.moveTo(20 + jag(6), 20 + jag(6));
  for (let x = 20; x <= width + 20; x += 14) ctx.lineTo(x, 20 + jag(10));
  for (let y = 20; y <= height + 20; y += 14) ctx.lineTo(width + 20 + jag(10), y);
  for (let x = width + 20; x >= 20; x -= 14) ctx.lineTo(x, height + 20 + jag(10));
  for (let y = height + 20; y >= 20; y -= 14) ctx.lineTo(20 + jag(10), y);
  ctx.closePath();
  ctx.shadowColor = 'rgba(30, 20, 10, 0.22)';
  ctx.shadowBlur = 12;
  ctx.shadowOffsetY = 4;
  ctx.fillStyle = '#fffaf0';
  ctx.fill();
  return { texture: canvasTexture(canvas), aspect: canvas.width / canvas.height, inner: width / canvas.width };
}
