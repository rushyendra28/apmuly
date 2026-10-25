// Detects when a singer starts a new syllable, from the music's spectrum bands (sent ~30×/s).
// A vocal onset is a sudden rise concentrated in the voice range; kicks and cymbals rise elsewhere.

/** The host's bands are log-spaced over this range (MusicAudioTap on macOS, loopback on Windows). */
const LOWEST_HZ = 40;
const HIGHEST_HZ = 12_000;
const VOCAL_LOW_HZ = 280;
const VOCAL_HIGH_HZ = 3_500;

/** Smallest mean rise per vocal band (bands are 0…1) that can count as an onset. */
const MIN_RISE = 0.06;
/** Share of the frame's total rise that must be in the vocal bands. */
const MIN_VOCAL_SHARE = 0.55;
/** Must also stand out from recent activity: median + K × median absolute deviation. */
const K = 3;
const HISTORY = 45;
/** Syllables are rarely closer than this. */
const REFRACTORY = 0.1;

/** Indices of the first and last band inside the voice range. */
export function vocalBandRange(count) {
  const step = Math.log(HIGHEST_HZ / LOWEST_HZ) / count;
  const index = (hz) => Math.min(count - 1, Math.max(0, Math.floor(Math.log(hz / LOWEST_HZ) / step)));
  return [index(VOCAL_LOW_HZ), index(VOCAL_HIGH_HZ)];
}

const median = (values) => {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
};

export class OnsetDetector {
  constructor() {
    this.previous = null;
    this.history = [];
    this.last = -Infinity;
  }

  /**
   * @param {number[]} bands spectrum levels 0…1, low to high
   * @param {number} time seconds (any clock)
   * @returns {boolean} whether a vocal onset happened in this frame
   */
  push(bands, time) {
    const previous = this.previous;
    this.previous = bands.slice();
    if (!previous || previous.length !== bands.length) return false;

    const [low, high] = vocalBandRange(bands.length);
    let vocal = 0;
    let all = 0;
    bands.forEach((level, i) => {
      const rise = Math.max(0, level - previous[i]);
      all += rise;
      if (i >= low && i <= high) vocal += rise;
    });
    const rise = vocal / (high - low + 1);
    const typical = median(this.history);
    const spread = median(this.history.map((v) => Math.abs(v - typical)));
    this.history.push(rise);
    if (this.history.length > HISTORY) this.history.shift();

    const isOnset = rise >= MIN_RISE && rise > typical + K * spread && all > 0 && vocal / all >= MIN_VOCAL_SHARE
      && time - this.last >= REFRACTORY;
    if (isOnset) this.last = time;
    return isOnset;
  }
}
