// Layouts for the Grunge theme: each lyric line becomes a poster-like composition that fits the window.
import { layoutRows } from './layout.js';

export const COMPOSITIONS = ['center', 'left', 'right', 'redacted', 'stack', 'banner'];

/** Space between words and line spacing, in ems. */
const GAP_EM = 0.26;
const LEADING = 0.9;
/** Leaves room for the frame's captions and marks. */
const MARGIN_SHARE = 0.11;
/** Text block never taller than this share of the space inside the margins. */
const MAX_BLOCK = 0.86;
const BANNER_OVERFLOW = 1.04;
const MAX_REPEATS = 4;

/** A different composition from line to line; short lines may stack or run as a banner. */
export function chooseComposition(wordCount, index) {
  if (wordCount <= 2 && index % 3 === 1) return 'stack';
  if (wordCount <= 3 && index % 5 === 3) return 'banner';
  return ['center', 'left', 'redacted', 'right'][index % 4];
}

/**
 * @param {number[]} widths each word's width at font size 1
 * @param {{width: number, height: number}} view visible area (world units, centred on 0)
 * @param {string} kind one of COMPOSITIONS
 * @param {{maxSize?: number}} [options] cap on the type size (world units)
 * @returns {{size: number, margin: number, gap: number, words: {x: number, y: number, row: number}[],
 *            bars: {y: number, left: number, right: number, height: number}[], repeats: number, copies: number[]}}
 *   Word x / y are centres. `copies` are y offsets of repeated copies (stack), the first is the live one.
 */
export function composeLine(widths, view, kind, { maxSize = Infinity } = {}) {
  const margin = MARGIN_SHARE * Math.min(view.width, view.height);
  if (kind === 'banner') return banner(widths, view, margin);
  if (kind === 'stack') return stack(widths, view, margin);
  return rows(widths, view, margin, kind, maxSize);
}

function rows(widths, view, margin, kind, maxSize) {
  const innerW = view.width - margin * 2;
  const innerH = view.height - margin * 2;
  const share = kind === 'center' ? 0.92 : 0.86;
  // Portrait windows wrap into more rows, so words can be bigger relative to the width.
  let size = Math.min(maxSize, view.height * 0.2, view.width * (view.width < view.height ? 0.2 : 0.13));
  let laid = null;
  for (let tries = 0; tries < 8; tries++) {
    laid = layoutRows(widths.map((w) => w * size), innerW * share, GAP_EM * size);
    const height = blockHeight(laid.rowCount, size * laid.scale);
    if (height <= innerH * MAX_BLOCK) break;
    size *= (innerH * MAX_BLOCK) / height;
  }
  const em = size * laid.scale;
  const rowCount = laid.rowCount;
  const extents = Array.from({ length: rowCount }, (_, r) => {
    const members = laid.items.map((item, i) => ({ item, i })).filter(({ item }) => item.row === r);
    return {
      left: Math.min(...members.map(({ item, i }) => item.x - (widths[i] * em) / 2)),
      right: Math.max(...members.map(({ item, i }) => item.x + (widths[i] * em) / 2)),
    };
  });
  const height = blockHeight(rowCount, em);
  const top = kind === 'left' ? view.height / 2 - margin - em / 2
    : kind === 'right' ? -view.height / 2 + margin + height - em / 2
      : height / 2 - em / 2;
  const shift = (r) => (kind === 'left' || kind === 'redacted' ? -view.width / 2 + margin - extents[r].left
    : kind === 'right' ? view.width / 2 - margin - extents[r].right : 0);
  const rowY = (r) => top - r * em * LEADING;
  const words = laid.items.map((item) => ({ x: item.x + shift(item.row), y: rowY(item.row), row: item.row }));
  const bars = kind !== 'redacted' ? [] : extents.map((extent, r) => ({
    y: rowY(r) - em * 0.02,
    left: extent.left + shift(r) - em * 0.1,
    right: Math.min(view.width / 2 - margin / 2, extent.right + shift(r) + em * (0.3 + 0.35 * ((r * 7) % 3))),
    height: em * 0.3,
  }));
  return { size: em, margin, gap: GAP_EM * em, words, bars, repeats: 1, copies: [0] };
}

/** One row across the whole window, partly cropped by its bottom edge. */
function banner(widths, view, margin) {
  const total = widths.reduce((a, b) => a + b, 0) + GAP_EM * (widths.length - 1);
  const size = Math.min(view.height * 0.5, (view.width * BANNER_OVERFLOW) / total);
  let cursor = (-total * size) / 2;
  const y = -view.height / 2 + size * 0.38;
  const words = widths.map((w) => {
    const x = cursor + (w * size) / 2;
    cursor += (w + GAP_EM) * size;
    return { x, y, row: 0 };
  });
  return { size, margin, gap: GAP_EM * size, words, bars: [], repeats: 1, copies: [0] };
}

/** A short line repeated down the window; the top copy is sung, the others echo it. */
function stack(widths, view, margin) {
  const innerW = view.width - margin * 2;
  const innerH = view.height - margin * 2;
  const total = widths.reduce((a, b) => a + b, 0) + GAP_EM * (widths.length - 1);
  let size = Math.min(view.height * 0.24, (innerW * 0.92) / total);
  const repeats = Math.max(2, Math.min(MAX_REPEATS, Math.floor(innerH / (size * LEADING))));
  size = Math.min(size, innerH / (repeats * LEADING + (1 - LEADING)));
  const height = blockHeight(repeats, size);
  const top = height / 2 - size / 2;
  const copies = Array.from({ length: repeats }, (_, k) => top - k * size * LEADING);
  let cursor = (-total * size) / 2;
  const words = widths.map((w) => {
    const x = cursor + (w * size) / 2;
    cursor += (w + GAP_EM) * size;
    return { x, y: copies[0], row: 0 };
  });
  return { size, margin, gap: GAP_EM * size, words, bars: [], repeats, copies };
}

/**
 * Where an emoji should sit for word `i`, in free space and never on another row: after the word when it
 * ends its row and there's room, else before the row when it starts it, else above it on the top row,
 * otherwise tucked low after its last letter.
 * @param {{width: number}} [view] the window, to check there's room at the sides
 * @returns {{x: number, y: number}} centre
 */
export function besideWord(layout, widths, i, stickerSize, view = { width: Infinity }) {
  const word = layout.words[i];
  const half = (widths[i] * layout.size) / 2;
  const rowMates = layout.words.map((w, j) => ({ w, j })).filter(({ w }) => w.row === word.row);
  const edge = view.width / 2 - stickerSize * 0.6;
  const after = word.x + half + stickerSize * 0.6;
  if (rowMates.every(({ j }) => j <= i) && after <= edge) return { x: after, y: word.y + layout.size * 0.05 };
  const first = rowMates[0].j;
  const rowStart = layout.words[first].x - (widths[first] * layout.size) / 2 - stickerSize * 0.6;
  if (rowStart >= -edge) return { x: rowStart, y: word.y + layout.size * 0.05 };
  if (word.row === 0) return { x: word.x + half - stickerSize * 0.2, y: word.y + layout.size * 0.55 + stickerSize * 0.45 };
  return { x: word.x + half - stickerSize * 0.1, y: word.y - layout.size * 0.1 };
}

function blockHeight(rowCount, em) {
  return (rowCount - 1) * em * LEADING + em;
}
