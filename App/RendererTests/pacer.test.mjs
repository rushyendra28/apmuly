// Run: node --test 'App/RendererTests/*.test.mjs'
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { OnsetDetector, vocalBandRange } from '../Resources/Renderer/js/core/onsets.js';
import { WordPacer } from '../Resources/Renderer/js/core/pacer.js';

const BANDS = 24;
const quiet = () => Array(BANDS).fill(0.2);
const [vocalLow, vocalHigh] = vocalBandRange(BANDS);
const withVocal = (level) => quiet().map((v, i) => (i >= vocalLow && i <= vocalHigh ? level : v));
const withKick = (level) => quiet().map((v, i) => (i < 5 ? level : v));

test('vocalBandRange covers the voice (~280 Hz–3.5 kHz) of the 40 Hz–12 kHz log bands', () => {
  assert.deepEqual(vocalBandRange(24), [8, 18]);
});

test('OnsetDetector fires on a sudden rise in the vocal bands, once', () => {
  const detector = new OnsetDetector();
  let t = 0;
  for (let i = 0; i < 40; i++) assert.equal(detector.push(quiet(), (t += 1 / 30)), false);
  assert.equal(detector.push(withVocal(0.8), (t += 1 / 30)), true);
  assert.equal(detector.push(withVocal(0.85), (t += 1 / 30)), false, 'sustained note is not a new onset');
});

test('OnsetDetector ignores kicks and anything within the refractory time', () => {
  const detector = new OnsetDetector();
  let t = 0;
  for (let i = 0; i < 40; i++) detector.push(quiet(), (t += 1 / 30));
  assert.equal(detector.push(withKick(0.9), (t += 1 / 30)), false, 'bass-only hit');
  detector.push(quiet(), (t += 1 / 30));
  assert.equal(detector.push(withVocal(0.8), (t += 1 / 30)), true);
  detector.push(quiet(), (t += 0.02));
  assert.equal(detector.push(withVocal(0.8), (t += 0.02)), false, 'too soon after the last onset');
});

// Placeholder words only. Line-synced: word times are estimates.
function makeDoc() {
  const line = (s, words) => {
    const w = words.map((t, i) => ({ t, s: s + i * 0.5, e: s + (i + 1) * 0.5 }));
    return { s, e: s + words.length * 0.5 + 1, t: words.join(' '), w };
  };
  const lines = [];
  for (let n = 0; n < 12; n++) lines.push(line(n * 4, ['alpha', 'beta', 'gamma', 'delta']));
  return { trackID: 'x', timing: 'lineSynced', lines };
}

/** Feeds onsets for every word of lines [from, to) at estimate + offset (+ per-word jitter). */
function sing(pacer, doc, from, to, offset, jitter = () => 0) {
  for (let n = from; n < to; n++) {
    doc.lines[n].w.forEach((w, i) => {
      const at = w.s + offset + jitter(n, i);
      pacer.observe(at);
      pacer.apply(doc, at + 0.03);
    });
  }
}

test('WordPacer changes nothing until it has calibrated on enough sung words', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  pacer.observe(4.2);
  assert.equal(pacer.apply(doc, 4.25), doc);
  assert.equal(pacer.latency, null);
});

test('WordPacer learns a constant delay (e.g. speakers) and does not shift words for it', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0.18);
  assert.ok(Math.abs(pacer.latency - 0.18) < 0.02);
  // A singer exactly on the estimate (after the constant delay) leaves the next line untouched.
  const paced = pacer.apply(doc, 16.0);
  assert.deepEqual(paced.lines[4].w.map((w) => w.s), doc.lines[4].w.map((w) => w.s));
});

test('WordPacer: an early onset starts the word right away (when it is actually sung)', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0.1);
  // Line 4 word 1 is estimated at 16.5; the singer starts it 0.2 s early (heard at 16.3 + 0.1 delay).
  pacer.observe(16.0 + 0.1);
  pacer.apply(doc, 16.13);
  pacer.observe(16.3 + 0.1);
  const paced = pacer.apply(doc, 16.43);
  const word = paced.lines[4].w[1];
  assert.ok(Math.abs(word.s - 16.3) < 0.01, `started at ${word.s}`);
  assert.ok(paced.lines[4].w[0].e <= word.s + 1e-9, 'previous word ends when this one starts');
});

test('WordPacer: a late singer pushes the rest of the line later, never re-starting a shown word', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0);
  // Word 1 of line 4 (estimate 16.5) is already showing at 16.55 when the singer starts it at 16.7.
  let paced = pacer.apply(doc, 16.55);
  pacer.observe(16.7);
  paced = pacer.apply(doc, 16.72);
  assert.equal(paced.lines[4].w[1].s, 16.5, 'already-shown word keeps its start');
  assert.ok(paced.lines[4].w[2].s > 17.0 + 0.1, 'later words follow the singer');
  const starts = paced.lines[4].w.map((w) => w.s);
  starts.slice(1).forEach((s, i) => assert.ok(s > starts[i]));
});

test('WordPacer ignores onsets far from any word (instruments) and leaves word-synced files alone', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0);
  pacer.observe(19.0); // between lines 4 and 5: no word is sung here
  const paced = pacer.apply(doc, 19.05);
  assert.equal(paced, doc);

  const wordSynced = { ...makeDoc(), timing: 'wordSynced' };
  const other = new WordPacer();
  sing(other, wordSynced, 0, 4, 0.1);
  assert.equal(other.apply(wordSynced, 16.2), wordSynced);
});

test('WordPacer keeps the same document object while nothing changes (no needless redraws)', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0);
  const a = pacer.apply(doc, 20.1);
  const b = pacer.apply(doc, 20.2);
  assert.equal(a, b);
});

/** Dense lines (rap / fast verses): words 0.3–0.4 s apart. */
function denseDoc(gap) {
  const lines = [];
  for (let n = 0; n < 14; n++) {
    const s = n * 4;
    const w = Array.from({ length: 6 }, (_, i) => ({ t: `w${i}`, s: s + i * gap, e: s + (i + 1) * gap }));
    lines.push({ s, e: s + 3.5, t: 'x', w });
  }
  return { trackID: 'd', timing: 'lineSynced', lines };
}

test('WordPacer learns a delay longer than half the word spacing (no aliasing onto the next word)', () => {
  const doc = denseDoc(0.4);
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 8, 0.3);
  assert.ok(Math.abs(pacer.latency - 0.3) < 0.03, `learned ${pacer.latency}`);
});

test('WordPacer never turns a word that is already showing back off (late singer, dense line)', () => {
  const doc = denseDoc(0.3);
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 8, 0.12);
  const line = 8;
  const base = doc.lines[line].w;
  // Words 0 and 1 are showing; the singer is heard late on word 0.
  const position = base[1].s + 0.02;
  pacer.apply(doc, position);
  pacer.observe(base[0].s + 0.12 + 0.13);
  const paced = pacer.apply(doc, position);
  paced.lines[line].w.forEach((w, i) => {
    if (base[i].s <= position) assert.ok(w.s <= position, `word ${i} was showing and must stay showing`);
  });
});

test('WordPacer keeps a line\'s first word after the previous line\'s words', () => {
  const doc = denseDoc(0.4);
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 8, 0);
  const previousLast = doc.lines[8].w[5].s;
  // A very early onset for line 9's first word (inside the early window).
  const first = doc.lines[9].w[0].s;
  pacer.observe(first - 0.29);
  const paced = pacer.apply(doc, first - 0.27);
  assert.ok(paced.lines[9].w[0].s > previousLast);
});

test('WordPacer drops stale onsets while a song has no lyrics yet', () => {
  const pacer = new WordPacer();
  for (let i = 0; i < 500; i++) pacer.observe(i * 0.2);
  assert.ok(pacer.pending.length <= 16);
});

test('WordPacer stays hands-off when most onsets are not the singer (busy instruments)', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  // The singer is on time, but for every sung word there are three instrument hits in between.
  for (let n = 0; n < 4; n++) {
    doc.lines[n].w.forEach((w) => {
      [0, 0.13, 0.26, 0.39].forEach((d) => pacer.observe(w.s + d));
      pacer.apply(doc, w.s + 0.45);
    });
  }
  assert.ok(pacer.confidence < 0.5, `confidence ${pacer.confidence}`);
  const word = doc.lines[4].w[2];
  pacer.observe(word.s - 0.15);
  const paced = pacer.apply(doc, word.s - 0.12);
  assert.equal(paced, doc, 'no re-timing on untrustworthy onsets');
});

test('WordPacer can be switched off', () => {
  const doc = makeDoc();
  const pacer = new WordPacer();
  sing(pacer, doc, 0, 4, 0.1);
  pacer.enabled = false;
  pacer.observe(16.3 + 0.1);
  assert.equal(pacer.apply(doc, 16.43), doc);
});
