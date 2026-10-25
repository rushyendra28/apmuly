import * as THREE from '../../vendor/three.module.min.js';
import { EffectComposer } from '../../vendor/addons/postprocessing/EffectComposer.js';
import { RenderPass } from '../../vendor/addons/postprocessing/RenderPass.js';
import { OutputPass } from '../../vendor/addons/postprocessing/OutputPass.js';

export const CAMERA_Z = 16;

/**
 * Renderer, camera and resize handling shared by all themes.
 * No bloom: text stays crisp; emphasis comes from colour, brightness and motion.
 */
export class Stage {
  constructor(canvas) {
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;

    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 500);
    this.camera.position.set(0, 0, CAMERA_Z);

    this.composer = new EffectComposer(this.renderer);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.composer.addPass(new OutputPass());

    this.resize();
    window.addEventListener('resize', () => this.resize());
  }

  resize() {
    const width = window.innerWidth;
    const height = window.innerHeight;
    this.renderer.setSize(width, height, false);
    this.composer.setSize(width, height);
    this.camera.aspect = width / Math.max(1, height);
    this.camera.updateProjectionMatrix();
  }

  /** World-space size of the view at `distance` in front of the camera. */
  viewSize(distance = CAMERA_Z) {
    const height = 2 * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2)) * distance;
    return { width: height * this.camera.aspect, height };
  }

  /** Resets per-theme global state (fog, background, camera, exposure). */
  reset() {
    this.scene.fog = null;
    this.scene.background = null;
    this.scene.environment = null;
    this.camera.position.set(0, 0, CAMERA_Z);
    this.camera.rotation.set(0, 0, 0);
    this.camera.fov = 45;
    this.camera.updateProjectionMatrix();
    this.camera.lookAt(0, 0, 0);
    this.renderer.toneMappingExposure = 1;
  }

  render() {
    this.composer.render();
  }
}
