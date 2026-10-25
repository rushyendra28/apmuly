import * as THREE from '../../vendor/three.module.min.js';
import { FALLBACK_PALETTE } from '../core/palette.js';

const ROLES = ['lyric', 'highlight', 'secondary'];
/** Colours cross-fade over roughly a second when the song changes. */
const BLEND_RATE = 3;

/** A theme's live colours, easing toward the current song's palette. */
export class PaletteBlend {
  constructor(palette = FALLBACK_PALETTE) {
    this.colors = Object.fromEntries(ROLES.map((role) => [role, new THREE.Color(palette[role])]));
    this.targets = Object.fromEntries(ROLES.map((role) => [role, new THREE.Color(palette[role])]));
  }

  get lyric() { return this.colors.lyric; }
  get highlight() { return this.colors.highlight; }
  get secondary() { return this.colors.secondary; }

  /** `instant` skips the cross-fade (e.g. the first palette after switching themes). */
  setTarget(palette, instant = false) {
    ROLES.forEach((role) => {
      this.targets[role].set(palette[role]);
      if (instant) this.colors[role].copy(this.targets[role]);
    });
  }

  update(dt) {
    const k = 1 - Math.exp(-dt * BLEND_RATE);
    ROLES.forEach((role) => this.colors[role].lerp(this.targets[role], k));
  }
}
