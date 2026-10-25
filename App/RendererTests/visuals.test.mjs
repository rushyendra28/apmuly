// Run: node --test 'App/RendererTests/*.test.mjs'
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { stickerCues } from '../Resources/Renderer/js/core/stickers.js';

// Placeholder words only.
const words = [
  { t: 'one', s: 10, e: 11 },
  { t: 'two', s: 12, e: 13 },
  { t: 'three', s: 14, e: 15 },
];
const line = { s: 9.5, e: 16, t: 'one two three', w: words };

test('mood emoji pops in with the first sung word and is short-lived', () => {
  const [mood] = stickerCues({ emoji: '😄', burst: false, objects: [] }, line);
  assert.equal(mood.role, 'emotion');
  assert.equal(mood.appearAt, 10);
  assert.ok(mood.lifetime > 1 && mood.lifetime < 4);
});

test('one emoji cue per object, on the word it is sung', () => {
  const cues = stickerCues({ emoji: null, burst: false, objects: [
    { word: 'two', emoji: '🌙', wordIndex: 1 },
    { word: 'lighthouse', emoji: null, wordIndex: null },
    { word: 'three', emoji: '💃', wordIndex: 2 },
    { word: 'sun', emoji: '☀️', wordIndex: null },
  ] }, line);
  assert.deepEqual(cues.map((c) => c.key), ['object:two', 'object:three', 'object:sun'], 'objects without an emoji are skipped');
  assert.deepEqual(cues.map((c) => c.emoji), ['🌙', '💃', '☀️']);
  assert.equal(cues[0].appearAt, 12);
  assert.equal(cues[0].wordIndex, 1);
  assert.equal(cues[1].appearAt, 14);
  assert.ok(cues[2].appearAt > 9.5 && cues[2].appearAt < 10.5);
  assert.ok(cues.every((c) => c.lifetime === undefined), 'objects stay with their line');
});

test('a mood change adds an emoji shower', () => {
  const cues = stickerCues({ emoji: '🥳', burst: true, objects: [] }, line);
  const confetti = cues.filter((c) => c.role === 'confetti');
  assert.equal(confetti.length, 3);
  assert.ok(confetti.every((c) => c.emoji === '🥳'));
  assert.equal(new Set(confetti.map((c) => c.key)).size, 3);
});

test('stickerCues tolerates missing data', () => {
  assert.deepEqual(stickerCues(null, { s: 0, e: 1, t: 'x', w: [] }), []);
  const cues = stickerCues({ emoji: null, objects: [{ word: 'sun', emoji: '☀️', wordIndex: 7 }] }, { s: 3, e: 5, t: 'x', w: [] });
  assert.equal(cues.length, 1);
  assert.ok(cues[0].appearAt >= 3);
});
