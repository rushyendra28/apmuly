// Run: node --test 'App/RendererTests/*.test.mjs'
import { test } from 'node:test';
import assert from 'node:assert/strict';

import { chooseComposition, composeLine, COMPOSITIONS } from '../Resources/Renderer/js/core/grungeLayout.js';

// Word widths at font size 1 (placeholder words of different lengths).
const widths = [2.1, 1.4, 3.2, 1.0, 2.6, 1.8];
const views = [{ width: 21, height: 13 }, { width: 8, height: 13 }, { width: 34, height: 9 }];
const half = (n) => n / 2;

function wordBox(layout, i) {
  const w = layout.words[i];
  return { left: w.x - (widths[i] * layout.size) / 2, right: w.x + (widths[i] * layout.size) / 2, top: w.y + layout.size / 2, bottom: w.y - layout.size / 2 };
}

test('chooseComposition varies by line and suits the line length', () => {
  const kinds = new Set(Array.from({ length: 12 }, (_, i) => chooseComposition(5, i)));
  assert.ok(kinds.size >= 3, 'several layouts across a song');
  assert.ok([...kinds].every((k) => COMPOSITIONS.includes(k)));
  assert.ok(Array.from({ length: 12 }, (_, i) => chooseComposition(1, i)).includes('stack'), 'short lines can stack');
  assert.ok(!Array.from({ length: 12 }, (_, i) => chooseComposition(9, i)).includes('banner'), 'long lines never banner');
});

for (const kind of ['center', 'left', 'right', 'redacted', 'stack']) {
  test(`composeLine(${kind}) keeps every word inside the window, for any window shape`, () => {
    views.forEach((view) => {
      const layout = composeLine(widths, view, kind);
      assert.equal(layout.words.length, widths.length);
      widths.forEach((_, i) => {
        const box = wordBox(layout, i);
        assert.ok(box.left >= -half(view.width) - 1e-6 && box.right <= half(view.width) + 1e-6, `${kind} x in ${view.width}x${view.height}`);
        assert.ok(box.top <= half(view.height) + 1e-6 && box.bottom >= -half(view.height) - 1e-6, `${kind} y in ${view.width}x${view.height}`);
      });
    });
  });
}

test('left / right compositions align rows to the margins', () => {
  const view = views[0];
  const left = composeLine(widths, view, 'left');
  const firstRow = widths.map((_, i) => i).filter((i) => left.words[i].row === 0);
  const leftEdge = Math.min(...firstRow.map((i) => wordBox(left, i).left));
  assert.ok(Math.abs(leftEdge - (-half(view.width) + left.margin)) < 1e-6);

  const right = composeLine(widths, view, 'right');
  const lastRow = Math.max(...right.words.map((w) => w.row));
  const rowIdx = widths.map((_, i) => i).filter((i) => right.words[i].row === lastRow);
  const rightEdge = Math.max(...rowIdx.map((i) => wordBox(right, i).right));
  assert.ok(Math.abs(rightEdge - (half(view.width) - right.margin)) < 1e-6);
});

test('redacted rows get a bar each; banner runs edge to edge along the bottom; stack repeats', () => {
  const view = views[0];
  const redacted = composeLine(widths, view, 'redacted');
  assert.equal(redacted.bars.length, Math.max(...redacted.words.map((w) => w.row)) + 1);

  const banner = composeLine([2.1, 1.4], view, 'banner');
  const span = (2.1 + 1.4) * banner.size + banner.gap;
  assert.ok(span > view.width * 0.98, 'fills the width');
  assert.ok(banner.words[0].y < 0, 'sits low');

  const stack = composeLine([2.1], view, 'stack');
  assert.ok(stack.repeats >= 2 && stack.repeats <= 4);
  assert.equal(stack.copies.length, stack.repeats);
});

test('besideWord keeps emoji off the other rows', async () => {
  const { besideWord } = await import('../Resources/Renderer/js/core/grungeLayout.js');
  const view = { width: 21, height: 13 };
  const layout = composeLine(widths, view, 'left');
  const sticker = layout.size * 0.7;
  widths.forEach((_, i) => {
    const spot = besideWord(layout, widths, i, sticker);
    const row = layout.words[i].row;
    if (row > 0) {
      const rowAbove = layout.words.find((w) => w.row === row - 1);
      assert.ok(spot.y < rowAbove.y - layout.size * 0.3, `word ${i} emoji stays below the row above`);
    }
  });
});
