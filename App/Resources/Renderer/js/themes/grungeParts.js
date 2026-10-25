import * as THREE from '../../vendor/three.module.min.js';
import { NOISE } from '../engine/ink.js';

// Creased paper and spray paint for the Grunge theme. The paper's fibres, stains and folds don't
// change during a song, so they're baked into a texture once (per song and window size); each frame
// then only adds the spray paint and film grain — two texture reads per pixel instead of ~180 noise
// lookups.

export const BLOBS = 4;
/** The baked sheet covers a little more than the view, for the camera's perspective at the edges. */
const BAKE_SPAN = 1.2;
const MAX_BAKE = 4096;

const FOLDS = `
  // Fold lines and a wrinkle: x = shadow (0…1), y = highlight. q is position / view height.
  vec2 folds(vec2 q, vec4 crease, float seed) {
    float wiggle = (fbm(q * 6.0 + seed) - 0.5) * 0.012;
    float dv = q.x - crease.x + wiggle;
    float dh = q.y - crease.y + wiggle;
    float shadow = exp(-abs(dv) * 900.0) * 0.32 + exp(-abs(dh) * 900.0) * 0.28;
    float light = exp(-abs(dv - 0.003) * 700.0) * 0.22 * step(0.0, dv) + exp(-abs(dh + 0.003) * 700.0) * 0.2 * step(dh, 0.0);
    float ring = abs(length((q - crease.zw) / vec2(0.55, 0.42)) - 1.0);
    shadow += exp(-ring * 300.0) * 0.16 * smoothstep(0.3, 0.7, fbm(q * 4.0 + seed));
    return vec2(shadow, light);
  }`;

/** Baked sheet: r = paper brightness, g = fold shadow, b = fold highlight, a = stain. */
const BAKE_FRAGMENT = `
  uniform float aspect;
  uniform float seed;
  uniform vec4 crease;
  varying vec2 vUv;
  ${NOISE}
  ${FOLDS}
  void main() {
    vec2 q = (vUv - 0.5) * vec2(aspect, 1.0) * ${BAKE_SPAN.toFixed(2)};
    float s = small(seed);
    float paper = 0.885 + (fbm(q * 3.0 + s) - 0.5) * 0.05 + (fbm(q * vec2(40.0, 8.0) + s * 2.0) - 0.5) * 0.02;
    float stain = smoothstep(0.62, 0.8, fbm(q * 1.7 + s * 3.1));
    vec2 f = folds(q, crease, s);
    gl_FragColor = vec4(paper - stain * 0.05, f.x, f.y, stain);
  }`;

const BAKE_VERTEX = `
  varying vec2 vUv;
  void main() { vUv = uv; gl_Position = vec4(position.xy * 2.0, 0.0, 1.0); }`;

const PLANE_VERTEX = `
  uniform vec2 view;
  varying vec2 vSheet;
  varying vec2 vPos;
  void main() {
    vPos = (modelMatrix * vec4(position, 1.0)).xy;
    vSheet = vPos / (view * ${BAKE_SPAN.toFixed(2)}) + 0.5;
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(position, 1.0);
  }`;

const PAPER_FRAGMENT = `
  uniform sampler2D sheet;
  uniform float time;
  uniform float grain;
  uniform vec3 accent;
  uniform vec4 blobs[${BLOBS}];
  uniform vec4 band;
  varying vec2 vSheet;
  varying vec2 vPos;
  ${NOISE}
  void main() {
    vec4 baked = texture2D(sheet, vSheet);
    vec3 paper = vec3(baked.r, baked.r, baked.r - 0.015);

    // Spray paint: soft shapes whose edges break up into grain.
    float cover = 0.0;
    for (int i = 0; i < ${BLOBS}; i++) {
      vec4 b = blobs[i];
      if (b.w <= 0.0) continue;
      cover = max(cover, (1.0 - smoothstep(0.3, 1.0, length(vPos - b.xy) / b.z)) * b.w);
    }
    cover = max(cover, (1.0 - smoothstep(0.35, 1.0, abs(vPos.y - band.x) / max(band.y, 0.001))) * band.z);
    float sprayed = step(hash(gl_FragCoord.xy + tick(time, 12.0) * 17.0), pow(cover, 0.8) * 1.03);
    vec3 color = mix(paper, accent * (0.94 + (baked.r - 0.885) * 4.0), sprayed);

    color = color * (1.0 - baked.g) + baked.b;
    color += (hash(gl_FragCoord.xy + tick(time, 24.0) * 31.0) - 0.5) * grain;
    gl_FragColor = vec4(pow(clamp(color, 0.0, 1.0), vec3(2.2)), 1.0);
  }`;

/** Printed over everything (multiply): folds and specks show through the ink too. */
const OVERLAY_FRAGMENT = `
  uniform sampler2D sheet;
  uniform float time;
  varying vec2 vSheet;
  ${NOISE}
  void main() {
    vec4 baked = texture2D(sheet, vSheet);
    float speck = step(0.985, hash(gl_FragCoord.xy * 0.37 + tick(time, 12.0) * 13.0));
    float shade = 1.0 - baked.g * 0.55 - speck * 0.25 - baked.a * 0.03;
    gl_FragColor = vec4(vec3(pow(clamp(shade, 0.0, 1.0), 2.2)), 1.0);
  }`;

/** Bakes the paper sheet for a song (seed / creases) at the window's resolution. */
export class PaperSheet {
  constructor() {
    this.target = new THREE.WebGLRenderTarget(16, 16);
    this.material = new THREE.ShaderMaterial({
      vertexShader: BAKE_VERTEX,
      fragmentShader: BAKE_FRAGMENT,
      uniforms: { aspect: { value: 1 }, seed: { value: 0 }, crease: { value: new THREE.Vector4() } },
      depthTest: false,
      depthWrite: false,
    });
    this.scene = new THREE.Scene();
    this.scene.add(new THREE.Mesh(new THREE.PlaneGeometry(1, 1), this.material));
    this.camera = new THREE.Camera();
    this.key = '';
  }

  get texture() {
    return this.target.texture;
  }

  /** Re-bakes only when the song's paper or the window size changed. */
  bake(renderer, seed, crease) {
    const size = renderer.getDrawingBufferSize(new THREE.Vector2());
    const scale = Math.min(BAKE_SPAN, MAX_BAKE / Math.max(size.x, size.y));
    const width = Math.max(16, Math.round(size.x * scale));
    const height = Math.max(16, Math.round(size.y * scale));
    const key = `${width}x${height}:${seed}:${crease.toArray().join(',')}`;
    if (key === this.key) return;
    this.key = key;
    this.target.setSize(width, height);
    this.material.uniforms.aspect.value = size.x / Math.max(1, size.y);
    this.material.uniforms.seed.value = seed;
    this.material.uniforms.crease.value.copy(crease);
    const previous = renderer.getRenderTarget();
    renderer.setRenderTarget(this.target);
    renderer.render(this.scene, this.camera);
    renderer.setRenderTarget(previous);
  }

  dispose() {
    this.target.dispose();
    this.material.dispose();
    this.scene.children.forEach((mesh) => mesh.geometry.dispose());
  }
}

export function paperMaterial(sheet) {
  return new THREE.ShaderMaterial({
    vertexShader: PLANE_VERTEX,
    fragmentShader: PAPER_FRAGMENT,
    uniforms: {
      sheet: { value: sheet.texture },
      view: { value: new THREE.Vector2(1, 1) },
      time: { value: 0 },
      grain: { value: 0.07 },
      accent: { value: new THREE.Color() },
      blobs: { value: Array.from({ length: BLOBS }, () => new THREE.Vector4(0, 0, 1, 0)) },
      band: { value: new THREE.Vector4(0, 1, 0, 0) },
    },
    depthWrite: false,
  });
}

export function overlayMaterial(paper) {
  const { sheet, view, time } = paper.uniforms;
  return new THREE.ShaderMaterial({
    vertexShader: PLANE_VERTEX,
    fragmentShader: OVERLAY_FRAGMENT,
    uniforms: { sheet, view, time },
    transparent: true,
    depthTest: false,
    depthWrite: false,
    blending: THREE.CustomBlending,
    blendEquation: THREE.AddEquation,
    blendSrc: THREE.DstColorFactor,
    blendDst: THREE.ZeroFactor,
  });
}
