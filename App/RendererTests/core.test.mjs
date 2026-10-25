// Run: node --test App/RendererTests
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { lineIndexAt, wordPhase, gapState } from '../Resources/Renderer/js/core/timeline.js';
import { anchorFrom, positionAt, reanchor } from '../Resources/Renderer/js/core/clock.js';
import { initialBeat, detectBeat, decayPulse } from '../Resources/Renderer/js/core/beat.js';
import { layoutRows } from '../Resources/Renderer/js/core/layout.js';
import { damp } from '../Resources/Renderer/js/core/easing.js';

// Placeholder lyrics only.
const line = (s, e, words) => ({ s, e, t: words.map(w => w[0]).join(' '), w: words.map(([t, ws, we]) => ({ t, s: ws, e: we })) });
const lines = [
  line(1, 4, [['alpha', 1, 2], ['beta', 2, 3]]),
  { s: 4, e: 9, t: '', w: [] },
  line(9, 12, [['gamma', 9, 10]]),
];

test('lineIndexAt finds the last started line', () => {
  assert.equal(lineIndexAt(lines, 0.5), -1);
  assert.equal(lineIndexAt(lines, 1), 0);
  assert.equal(lineIndexAt(lines, 5), 1);
  assert.equal(lineIndexAt(lines, 100), 2);
  assert.equal(lineIndexAt([], 3), -1);
});

test('wordPhase classifies ahead / singing / done with progress', () => {
  const w = lines[0].w[0];
  assert.deepEqual(wordPhase(w, 0.5), { state: 'ahead', t: 0, since: -0.5 });
  assert.equal(wordPhase(w, 1.5).state, 'singing');
  assert.equal(wordPhase(w, 1.5).t, 0.5);
  assert.equal(wordPhase(w, 2.5).state, 'done');
  assert.equal(wordPhase(w, 2.5).t, 1);
});

test('gapState reports instrumental breaks and progress to the next line', () => {
  assert.deepEqual(gapState(lines, 1, 6.5), { inGap: true, progress: 0.5, untilNext: 2.5 });
  assert.equal(gapState(lines, 0, 2).inGap, false);
  const intro = gapState(lines, -1, 0.5);
  assert.equal(intro.inGap, true);
  assert.equal(intro.untilNext, 0.5);
});

test('clock extrapolates while running and holds while paused', () => {
  const a = anchorFrom({ position: 10, isRunning: true }, 100);
  assert.equal(positionAt(a, 102), 12);
  const p = anchorFrom({ position: 10, isRunning: false }, 100);
  assert.equal(positionAt(p, 150), 10);
  assert.equal(positionAt(null, 1), 0);
});

test('reanchor smooths tiny jitter and snaps real jumps', () => {
  const a = anchorFrom({ position: 10, isRunning: true }, 100);
  const smoothed = reanchor(a, { position: 11.03, isRunning: true }, 101);
  const at = positionAt(smoothed, 101);
  assert.ok(at > 11 && at < 11.03);
  const snapped = reanchor(a, { position: 50, isRunning: true }, 101);
  assert.equal(positionAt(snapped, 101), 50);
  const paused = reanchor(a, { position: 11, isRunning: false }, 101);
  assert.equal(positionAt(paused, 200), 11);
});

test('reanchor slews moderate corrections instead of jumping', () => {
  const a = anchorFrom({ position: 10, isRunning: true }, 100);
  const slewed = reanchor(a, { position: 11.3, isRunning: true }, 101); // predicted 11.0
  const at = positionAt(slewed, 101);
  assert.ok(at > 11 && at < 11.1, `expected a small step, got ${at}`);
});

test('damp moves toward the target independent of frame rate', () => {
  assert.ok(Math.abs(damp(0, 1, 10, 0.1) - (1 - Math.exp(-1))) < 1e-9);
  const twoSmallSteps = damp(damp(0, 1, 10, 0.05), 1, 10, 0.05);
  assert.ok(Math.abs(twoSmallSteps - damp(0, 1, 10, 0.1)) < 1e-9);
  assert.equal(damp(5, 5, 10, 0.016), 5);
});

test('beat detector fires on bass spikes with a refractory period', () => {
  let s = initialBeat();
  for (let i = 0; i < 30; i++) s = detectBeat(s, 0.05, i * 0.033).state;
  const hit = detectBeat(s, 0.2, 1.0);
  assert.equal(hit.beat, true);
  const tooSoon = detectBeat(hit.state, 0.25, 1.1);
  assert.equal(tooSoon.beat, false);
  const silent = detectBeat(initialBeat(), 0.0, 1);
  assert.equal(silent.beat, false);
});

test('pulse decays toward zero', () => {
  assert.ok(decayPulse(1, 0.5) < 0.1);
  assert.equal(decayPulse(0, 1), 0);
});

test('layoutRows wraps words and centers each row', () => {
  const rows = layoutRows([2, 2, 2, 2], 5, 0.5);
  assert.equal(rows.rowCount, 2);
  assert.deepEqual(rows.items.map(i => i.row), [0, 0, 1, 1]);
  // Row width 4.5 centered → first word center at -1.25
  assert.equal(rows.items[0].x, -1.25);
  assert.equal(rows.items[1].x, 1.25);
  assert.equal(rows.scale, 1);
});

test('layoutRows scales down when a single word is too wide', () => {
  const rows = layoutRows([10], 5, 0.5);
  assert.equal(rows.scale, 0.5);
  assert.equal(rows.items[0].x, 0);
});


