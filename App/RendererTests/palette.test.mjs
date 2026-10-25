// Run: node --test 'App/RendererTests/*.test.mjs'
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { FALLBACK_PALETTE, readPalette, PaletteStore } from '../Resources/Renderer/js/core/palette.js';

test('readPalette accepts three hex colours and rejects anything else', () => {
  const good = { trackID: 'a', lyric: '#e0d0ff', highlight: '#5533cc', secondary: '#403050' };
  assert.deepEqual(readPalette(good), { lyric: '#e0d0ff', highlight: '#5533cc', secondary: '#403050' });
  assert.equal(readPalette(null), null);
  assert.equal(readPalette({ ...good, lyric: 'red' }), null);
  assert.equal(readPalette({ ...good, highlight: 'url(x)' }), null);
  assert.equal(readPalette({ ...good, secondary: undefined }), null);
});

test('PaletteStore returns the track palette, else the fallback, and stays bounded', () => {
  const store = new PaletteStore(2);
  assert.deepEqual(store.get('missing'), FALLBACK_PALETTE);
  assert.deepEqual(store.get(null), FALLBACK_PALETTE);
  const p = { lyric: '#111111', highlight: '#222222', secondary: '#333333' };
  store.set('a', p);
  store.set('b', p);
  store.set('c', p);
  assert.deepEqual(store.get('c'), p);
  assert.ok(store.has('c'));
  assert.ok(!store.has('a'));
  assert.deepEqual(store.get('a'), FALLBACK_PALETTE, 'oldest dropped');
});

test('fallback matches the app (ThemePalette.fallback)', () => {
  assert.deepEqual(FALLBACK_PALETTE, { lyric: '#e9e4f2', highlight: '#6a58e6', secondary: '#4d4863' });
});
