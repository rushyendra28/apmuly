// Pure lyric-timing helpers. A line is {s, e, t, w: [{t, s, e}]}; an empty `t` is an instrumental gap.

/** Index of the last line that has started at `pos`, or -1 before the first line. */
export function lineIndexAt(lines, pos) {
  let lo = 0;
  let hi = lines.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (lines[mid].s <= pos) lo = mid + 1;
    else hi = mid;
  }
  return lo - 1;
}

/** Where `pos` sits relative to one word: ahead of it, singing it (with 0…1 progress), or done. */
export function wordPhase(word, pos) {
  const since = pos - word.s;
  if (pos < word.s) return { state: 'ahead', t: 0, since };
  if (pos >= word.e) return { state: 'done', t: 1, since };
  const duration = word.e - word.s;
  return { state: 'singing', t: duration > 0 ? since / duration : 1, since };
}

const clamp01 = (v) => Math.min(1, Math.max(0, v));

/** Whether we're in an intro / instrumental break, and how far until the next sung line. */
export function gapState(lines, index, pos) {
  if (index < 0) {
    const next = lines[0];
    if (!next) return { inGap: true, progress: 0, untilNext: Infinity };
    return { inGap: true, progress: next.s > 0 ? clamp01(pos / next.s) : 1, untilNext: next.s - pos };
  }
  const line = lines[index];
  if (line.t !== '') return { inGap: false, progress: 0, untilNext: 0 };
  const next = lines[index + 1];
  const end = next ? next.s : line.e;
  const span = end - line.s;
  return { inGap: true, progress: span > 0 ? clamp01((pos - line.s) / span) : 1, untilNext: end - pos };
}
