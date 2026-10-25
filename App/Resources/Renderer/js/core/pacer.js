// Live singer pacing: moves estimated word times onto the moments the singer is heard starting them.
//
// Lyric files usually time whole lines, so word times start as estimates. Vocal onsets (core/onsets.js)
// arrive in song time. The pacer first learns the constant delay between onsets and the lyrics
// (speakers, Bluetooth, file offset), then for each onset near an upcoming word:
//   • singer early → that word starts right now (exactly when it's sung) and the rest of the line follows;
//   • singer late  → the word already showing stays, and the rest of the line moves later.
// Only line-synced lyrics are paced; word-synced files are already exact.

const MIN_MATCHES = 8;
const CALIBRATION_SIZE = 40;
const MAX_LATENCY = 0.45;
/** Delay candidates are scored with this tolerance (s) and step. */
const KERNEL = 0.035;
const CANDIDATE_STEP = 0.01;
/**
 * A line's first word comes after a pause, so its onset can't be mistaken for a neighbouring word's:
 * it counts this much more when learning the delay (stops dense lyrics aliasing onto the next word).
 */
const LINE_START_WEIGHT = 3;
const MAX_PENDING = 16;
/** How far before / after its expected time an onset may still belong to a word. */
const EARLY = 0.2;
const LATE = 0.2;
/**
 * Share of recent onsets that must line up with sung words before any word is moved. Busy mixes
 * (snares, strummed guitars) also make onsets in the voice range; then the pacer stays hands-off.
 */
const MIN_CONFIDENCE = 0.5;
const MIN_SHIFT = 0.02;
const MIN_GAP = 0.06;
const MIN_LAST_WORD = 0.1;

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
};

export class WordPacer {
  constructor() {
    /** Recent onsets as their possible delays: [{r, weight}] per onset. */
    this.calibration = [];
    /** Seconds from a word's lyric time to its heard onset; null until calibrated. */
    this.latency = null;
    /** Share of recent onsets explained by sung words at that delay (0…1). */
    this.confidence = 0;
    /** Live pacing can be switched off (the words then keep their estimated / learned times). */
    this.enabled = true;
    this.pending = [];
    this.base = null;
    this.words = [];
    /** Adjusted word starts, by "line:word". */
    this.starts = new Map();
    this.version = 0;
    this.cache = null;
    this.cacheVersion = -1;
  }

  /** A vocal onset, in song time (lyrics clock). */
  observe(time) {
    if (!Number.isFinite(time)) return;
    this.pending.push(time);
    if (this.pending.length > MAX_PENDING) this.pending.shift(); // no lyrics yet: keep only recent onsets
  }

  /** The document with paced word times (the same object when nothing changed). */
  apply(doc, position) {
    if (!doc || doc.timing !== 'lineSynced' || !Array.isArray(doc.lines) || !this.enabled) {
      this.pending = [];
      return doc;
    }
    if (doc !== this.base) this.#reset(doc);
    const onsets = this.pending;
    this.pending = [];
    onsets.forEach((onset) => this.#take(onset, position));
    if (this.starts.size === 0) return doc;
    if (this.cacheVersion !== this.version) {
      this.cache = this.#build(doc);
      this.cacheVersion = this.version;
    }
    return this.cache;
  }

  #reset(doc) {
    this.base = doc;
    this.words = doc.lines.flatMap((line, li) => (line.t === '' ? [] : line.w.map((w, wi) => ({ line: li, word: wi, s: w.s }))));
    this.starts.clear();
    this.version++;
  }

  #start(k) {
    const w = this.words[k];
    return this.starts.get(`${w.line}:${w.word}`) ?? w.s;
  }

  #take(onset, position) {
    this.#calibrate(onset);
    if (this.latency === null || this.confidence < MIN_CONFIDENCE) return;
    const heard = onset - this.latency;
    let k = this.#nearestWord(heard);
    if (k < 0) return;
    const shown = (i) => position >= this.#start(i);
    // An onset before an earlier unsung word of the same line most likely belongs to that word.
    while (k > 0 && !shown(k - 1) && this.words[k - 1].line === this.words[k].line) k--;

    const current = this.#start(k);
    if (Math.abs(heard - current) < MIN_SHIFT) return;
    if (!shown(k) && heard < current) {
      // Never before the previous word (even across lines), so word starts stay in order.
      const previous = k > 0 ? this.#start(k - 1) + MIN_GAP : -Infinity;
      this.#moveFrom(k, Math.max(heard, previous), true, position);
    } else if (shown(k) && heard > current) {
      this.#moveFrom(k, heard, false, position);
    }
  }

  /**
   * Learns the constant delay between lyric times and heard onsets: every candidate delay is scored by
   * how many recent onsets it explains (each onset may belong to any nearby word).
   */
  #calibrate(onset) {
    const options = [];
    for (let i = Math.max(0, this.#closest(onset, (j) => this.words[j].s) - 3); i < this.words.length; i++) {
      const r = onset - this.words[i].s;
      if (r < -MAX_LATENCY) break;
      if (r <= MAX_LATENCY) options.push({ r, weight: this.words[i].word === 0 ? LINE_START_WEIGHT : 1 });
    }
    if (options.length === 0) return;
    this.calibration.push(options);
    if (this.calibration.length > CALIBRATION_SIZE) this.calibration.shift();
    if (this.calibration.length < MIN_MATCHES) return;

    let best = null;
    let bestScore = 0;
    for (let latency = -MAX_LATENCY; latency <= MAX_LATENCY + 1e-9; latency += CANDIDATE_STEP) {
      const score = this.calibration.reduce((sum, onsetOptions) => sum
        + Math.max(...onsetOptions.map((o) => o.weight * Math.exp(-((o.r - latency) ** 2) / (2 * KERNEL * KERNEL)))), 0);
      if (score > bestScore) {
        bestScore = score;
        best = latency;
      }
    }
    // Refine: the median of the delays that agree with the best candidate.
    const agreeing = this.calibration.flatMap((o) => o.filter((x) => Math.abs(x.r - best) <= KERNEL * 1.5).map((x) => x.r));
    this.latency = agreeing.length >= MIN_MATCHES / 2 ? median(agreeing) : null;
    this.confidence = this.calibration.filter((o) => o.some((x) => Math.abs(x.r - best) <= KERNEL * 1.5)).length / this.calibration.length;
  }

  /** The word whose expected start is nearest `heard`, if `heard` is within its window. */
  #nearestWord(heard) {
    const k = this.#closest(heard, (i) => this.#start(i));
    if (k < 0) return -1;
    const delta = heard - this.#start(k);
    return delta >= -EARLY && delta <= LATE ? k : -1;
  }

  #closest(time, startOf) {
    if (this.words.length === 0) return -1;
    let low = 0;
    let high = this.words.length - 1;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (startOf(mid) < time) low = mid + 1;
      else high = mid;
    }
    if (low > 0 && Math.abs(startOf(low - 1) - time) <= Math.abs(startOf(low) - time)) return low - 1;
    return low;
  }

  /**
   * Re-paces word `k`'s line from `time`: with `includeWord`, word k starts at `time`; otherwise word k
   * stays and only the words after it shift. Later words are spread so the line still ends on time.
   * Words already showing at `position` are never moved.
   */
  #moveFrom(k, time, includeWord, position) {
    const { line } = this.words[k];
    const lineWords = this.base.lines[line].w;
    const end = lineWords[lineWords.length - 1].e;
    const from = this.#start(k);
    if (end - from <= MIN_GAP || end - time <= MIN_GAP) return;
    if (includeWord) this.starts.set(`${line}:${this.words[k].word}`, time);
    const delta = time - from;
    for (let j = k + 1; j < this.words.length && this.words[j].line === line; j++) {
      const s = this.#start(j);
      if (s <= position) continue;
      const moved = includeWord
        ? time + (s - from) * ((end - time) / (end - from))
        : s + delta * ((end - s) / (end - from));
      this.starts.set(`${line}:${this.words[j].word}`, moved);
    }
    this.version++;
  }

  #build(doc) {
    const lines = doc.lines.map((line, li) => {
      if (!line.w.some((_, wi) => this.starts.has(`${li}:${wi}`))) return line;
      const starts = [];
      line.w.forEach((w, wi) => {
        const s = this.starts.get(`${li}:${wi}`) ?? w.s;
        starts.push(wi > 0 ? Math.max(s, starts[wi - 1] + MIN_GAP) : s);
      });
      const w = line.w.map((word, wi) => ({
        ...word,
        s: starts[wi],
        e: wi < line.w.length - 1 ? starts[wi + 1] : Math.max(word.e, starts[wi] + MIN_LAST_WORD),
      }));
      return { ...line, w };
    });
    return { ...doc, lines };
  }
}
