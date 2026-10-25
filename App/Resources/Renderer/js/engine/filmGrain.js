import * as THREE from '../../vendor/three.module.min.js';
import { NOISE } from './ink.js';

// A finishing pass for the dark 3D theme: a soft vignette and fine moving film grain, riding with the
// camera like the frame. Cheap: one hash per pixel.

const DISTANCE = 4.5;

const VERTEX = `
  varying vec2 vUv;
  void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`;

const VIGNETTE = `
  uniform float strength;
  varying vec2 vUv;
  void main() {
    vec2 p = vUv - 0.5;
    float v = smoothstep(0.35, 0.85, length(p * vec2(1.0, 0.8)) * 1.25);
    gl_FragColor = vec4(0.0, 0.0, 0.0, v * strength);
  }`;

const GRAIN = `
  uniform float time;
  uniform float amount;
  ${NOISE}
  void main() {
    float g = hash(gl_FragCoord.xy + tick(time, 24.0) * 29.0);
    gl_FragColor = vec4(vec3(g * amount), 1.0);
  }`;

export class FilmGrain {
  constructor(stage, { vignette = 0.55, grain = 0.01 } = {}) {
    this.stage = stage;
    if (!stage.camera.parent) stage.scene.add(stage.camera);
    this.group = new THREE.Group();
    this.group.position.z = -DISTANCE;
    stage.camera.add(this.group);
    const plane = new THREE.PlaneGeometry(1, 1);
    this.vignette = new THREE.Mesh(plane, new THREE.ShaderMaterial({
      vertexShader: VERTEX, fragmentShader: VIGNETTE, uniforms: { strength: { value: vignette } },
      transparent: true, depthTest: false, depthWrite: false, toneMapped: false,
    }));
    this.grain = new THREE.Mesh(plane, new THREE.ShaderMaterial({
      vertexShader: VERTEX, fragmentShader: GRAIN, uniforms: { time: { value: 0 }, amount: { value: grain } },
      transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false,
    }));
    this.vignette.renderOrder = 55;
    this.grain.renderOrder = 56;
    this.group.add(this.vignette, this.grain);
  }

  update(now) {
    const view = this.stage.viewSize(DISTANCE);
    this.group.scale.set(view.width * 1.05, view.height * 1.05, 1);
    this.grain.material.uniforms.time.value = now;
  }

  dispose() {
    this.group.removeFromParent();
    this.vignette.geometry.dispose();
    this.vignette.material.dispose();
    this.grain.material.dispose();
  }
}
