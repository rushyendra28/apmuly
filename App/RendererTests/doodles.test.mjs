// Run: node --test 'App/RendererTests/*.test.mjs'
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { doodle, DOODLES, chooseDoodle, rng } from '../Resources/Renderer/js/core/doodles.js';

const box = { x: 1, y: -0.5, width: 4, height: 1.2 };

const all = (strokes) => strokes.flat();
const bounds = (points) => ({
  left: Math.min(...points.map((p) => p[0])), right: Math.max(...points.map((p) => p[0])),
  bottom: Math.min(...points.map((p) => p[1])), top: Math.max(...points.map((p) => p[1])),
});

test('every doodle is a list of strokes with enough points to look drawn', () => {
  DOODLES.forEach((kind) => {
    const strokes = doodle(kind, box, rng(7));
    assert.ok(Array.isArray(strokes) && strokes.length >= 1, kind);
    strokes.forEach((stroke) => {
      assert.ok(stroke.length >= 6, `${kind} stroke has ${stroke.length} points`);
      stroke.forEach(([x, y]) => assert.ok(Number.isFinite(x) && Number.isFinite(y)));
    });
  });
});

test('circle and box go around the word, underline sits below it, scribble crosses it', () => {
  const circle = bounds(all(doodle('circle', box, rng(1))));
  assert.ok(circle.left < box.x - box.width / 2 && circle.right > box.x + box.width / 2);
  assert.ok(circle.bottom < box.y - box.height / 2 && circle.top > box.y + box.height / 2);

  const frame = bounds(all(doodle('box', box, rng(2))));
  assert.ok(frame.left < box.x - box.width / 2 && frame.top > box.y + box.height / 2);

  const under = bounds(all(doodle('underline', box, rng(3))));
  assert.ok(under.top < box.y - box.height * 0.3, 'below the word');
  assert.ok(under.right - under.left > box.width * 0.6, 'spans most of it');

  const scribble = bounds(all(doodle('scribble', box, rng(4))));
  assert.ok(scribble.bottom < box.y && scribble.top > box.y, 'crosses the middle');
});

test('the arrow points at the word: its tip ends near the box edge', () => {
  const [shaft] = doodle('arrow', box, rng(5));
  const tip = shaft[shaft.length - 1];
  const dx = Math.max(0, Math.abs(tip[0] - box.x) - box.width / 2);
  const dy = Math.max(0, Math.abs(tip[1] - box.y) - box.height / 2);
  assert.ok(Math.hypot(dx, dy) < box.height, 'tip close to the word');
});

test('doodles are deterministic for a seed and vary between seeds', () => {
  assert.deepEqual(doodle('circle', box, rng(9)), doodle('circle', box, rng(9)));
  assert.notDeepEqual(doodle('circle', box, rng(9)), doodle('circle', box, rng(10)));
});

test('chooseDoodle picks varied doodles across lines', () => {
  const kinds = new Set(Array.from({ length: 20 }, (_, i) => chooseDoodle(i, 3)));
  assert.ok(kinds.size >= 4);
  assert.ok([...kinds].every((k) => DOODLES.includes(k)));
});
