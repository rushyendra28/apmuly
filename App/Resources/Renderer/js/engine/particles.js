import * as THREE from '../../vendor/three.module.min.js';

let dotTexture = null;

/** Shared soft round sprite for points (sparkles, stars, bokeh). */
export function softDot() {
  if (dotTexture) return dotTexture;
  const size = 64;
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d');
  const gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  gradient.addColorStop(0, 'rgba(255,255,255,1)');
  gradient.addColorStop(0.35, 'rgba(255,255,255,0.7)');
  gradient.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, size, size);
  dotTexture = new THREE.CanvasTexture(canvas);
  return dotTexture;
}

const GRAVITY = -4;
const DRAG = 2.2;

/** Pooled additive sparkle bursts (word pops, letter pops). */
export class Sparkles {
  constructor(capacity = 900) {
    this.capacity = capacity;
    this.cursor = 0;
    this.positions = new Float32Array(capacity * 3).fill(9999);
    this.colors = new Float32Array(capacity * 3);
    this.velocities = new Float32Array(capacity * 3);
    this.life = new Float32Array(capacity);
    this.maxLife = new Float32Array(capacity).fill(1);
    this.base = new Float32Array(capacity * 3);

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
    geometry.setAttribute('color', new THREE.BufferAttribute(this.colors, 3));
    this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
      size: 0.22, map: softDot(), vertexColors: true, transparent: true,
      blending: THREE.AdditiveBlending, depthWrite: false,
    }));
    this.points.frustumCulled = false;
    this.points.renderOrder = 10;
  }

  burst(origin, color, count = 18, speed = 5) {
    const c = new THREE.Color(color);
    for (let n = 0; n < count; n++) {
      const i = this.cursor;
      this.cursor = (this.cursor + 1) % this.capacity;
      const theta = Math.random() * Math.PI * 2;
      const phi = Math.acos(2 * Math.random() - 1);
      const s = speed * (0.4 + Math.random() * 0.8);
      this.positions.set([origin.x, origin.y, origin.z], i * 3);
      this.velocities.set([
        Math.sin(phi) * Math.cos(theta) * s,
        Math.sin(phi) * Math.sin(theta) * s + 1.5,
        Math.cos(phi) * s,
      ], i * 3);
      this.base.set([c.r, c.g, c.b], i * 3);
      this.maxLife[i] = 0.6 + Math.random() * 0.6;
      this.life[i] = this.maxLife[i];
    }
  }

  update(dt) {
    const drag = Math.exp(-DRAG * dt);
    for (let i = 0; i < this.capacity; i++) {
      if (this.life[i] <= 0) continue;
      this.life[i] -= dt;
      const k = i * 3;
      this.velocities[k] *= drag;
      this.velocities[k + 1] = this.velocities[k + 1] * drag + GRAVITY * dt;
      this.velocities[k + 2] *= drag;
      this.positions[k] += this.velocities[k] * dt;
      this.positions[k + 1] += this.velocities[k + 1] * dt;
      this.positions[k + 2] += this.velocities[k + 2] * dt;
      const fade = Math.max(0, this.life[i] / this.maxLife[i]);
      this.colors[k] = this.base[k] * fade;
      this.colors[k + 1] = this.base[k + 1] * fade;
      this.colors[k + 2] = this.base[k + 2] * fade;
      if (this.life[i] <= 0) this.positions[k + 1] = 9999;
    }
    this.points.geometry.attributes.position.needsUpdate = true;
    this.points.geometry.attributes.color.needsUpdate = true;
  }
}
