// Per-song colours from the album artwork (computed by the app), with a fallback until they arrive.

/** Same as ThemePalette.fallback in the app. */
export const FALLBACK_PALETTE = Object.freeze({ lyric: '#e9e4f2', highlight: '#6a58e6', secondary: '#4d4863' });

const HEX = /^#[0-9a-f]{6}$/i;
const ROLES = ['lyric', 'highlight', 'secondary'];

/** @returns {{lyric: string, highlight: string, secondary: string}|null} only when every role is a #rrggbb colour. */
export function readPalette(payload) {
  if (!payload || !ROLES.every((role) => typeof payload[role] === 'string' && HEX.test(payload[role]))) return null;
  return { lyric: payload.lyric, highlight: payload.highlight, secondary: payload.secondary };
}

/** Palettes by track ID, bounded (oldest dropped first). */
export class PaletteStore {
  constructor(limit = 8) {
    this.limit = limit;
    this.map = new Map();
  }

  set(trackID, palette) {
    this.map.delete(trackID);
    this.map.set(trackID, palette);
    while (this.map.size > this.limit) this.map.delete(this.map.keys().next().value);
  }

  has(trackID) {
    return this.map.has(trackID);
  }

  get(trackID) {
    return this.map.get(trackID) ?? FALLBACK_PALETTE;
  }
}
