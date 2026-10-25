import { lineIndexAt } from '../core/timeline.js';
import { positionAt, reanchor } from '../core/clock.js';

const MAX_LYRICS = 8;
const OPACITY_RATE = 10;

/**
 * Holds lyrics per track and the latest deck snapshot from Swift, and produces
 * per-frame deck state with locally extrapolated positions.
 */
export class Decks {
  constructor() {
    this.lyrics = new Map();
    this.anchors = new Map();
    this.opacities = new Map();
    this.snapshot = [];
  }

  setLyrics(doc) {
    this.lyrics.delete(doc.trackID);
    this.lyrics.set(doc.trackID, doc);
    while (this.lyrics.size > MAX_LYRICS) this.lyrics.delete(this.lyrics.keys().next().value);
  }

  /** @param {{trackID: string, role: string, position: number, isRunning: boolean, opacity: number}[]} decks */
  setFrame(decks, now) {
    const anchors = new Map(decks.map((d) => [d.trackID, reanchor(this.anchors.get(d.trackID), d, now)]));
    this.anchors = anchors;
    this.snapshot = decks;
  }

  /** Song position of a running deck at `now`, or null. */
  positionOf(trackID, now) {
    const deck = this.snapshot.find((d) => d.trackID === trackID);
    return deck?.isRunning ? positionAt(this.anchors.get(trackID), now) : null;
  }

  frame(now, dt) {
    const blend = 1 - Math.exp(-dt * OPACITY_RATE);
    const opacities = new Map();
    const decks = this.snapshot.map((d) => {
      const previous = this.opacities.get(d.trackID) ?? 0;
      const opacity = previous + (d.opacity - previous) * blend;
      opacities.set(d.trackID, opacity);
      const position = positionAt(this.anchors.get(d.trackID), now);
      const doc = this.lyrics.get(d.trackID) ?? null;
      return {
        trackID: d.trackID,
        role: d.role,
        position,
        opacity,
        doc,
        lineIndex: doc ? lineIndexAt(doc.lines, position) : -1,
      };
    });
    this.opacities = opacities;
    return decks;
  }
}
