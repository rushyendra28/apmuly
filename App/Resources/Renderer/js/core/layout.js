// Wraps word widths into centered rows that fit `maxWidth`.

/**
 * @returns {{rowCount: number, scale: number, items: {row: number, x: number}[]}}
 *   `x` is each word's center, relative to the row's center, after `scale`.
 */
export function layoutRows(widths, maxWidth, gap) {
  const widest = widths.reduce((m, w) => Math.max(m, w), 0);
  const scale = widest > maxWidth ? maxWidth / widest : 1;
  const sized = widths.map((w) => w * scale);
  const space = gap * scale;

  const rows = sized.reduce((acc, w, i) => {
    const current = acc[acc.length - 1];
    const occupied = current.indices.length > 0;
    const needed = occupied ? current.width + space + w : w;
    if (occupied && needed > maxWidth) {
      return [...acc, { indices: [i], width: w }];
    }
    const updated = { indices: [...current.indices, i], width: needed };
    return [...acc.slice(0, -1), updated];
  }, [{ indices: [], width: 0 }]);

  const items = new Array(widths.length);
  rows.forEach((row, r) => {
    let cursor = -row.width / 2;
    row.indices.forEach((i) => {
      items[i] = { row: r, x: cursor + sized[i] / 2 };
      cursor += sized[i] + space;
    });
  });
  return { rowCount: rows.length, scale, items };
}
