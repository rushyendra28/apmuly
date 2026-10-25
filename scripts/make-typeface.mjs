#!/usr/bin/env node
// Converts a TrueType/OpenType font into a three.js typeface JSON (the format FontLoader reads),
// keeping only the characters lyrics need (Latin, Latin-1, Latin Extended-A and common punctuation).
//
// Usage: node scripts/make-typeface.mjs <font.ttf> <out.json> <path to an opentype.js@1.x install>
// e.g.   npm install --prefix /tmp/ot opentype.js@1.3.4
//        node scripts/make-typeface.mjs Inter-ExtraBold.ttf App/Resources/Renderer/fonts/inter_extrabold.typeface.json /tmp/ot
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join, resolve } from 'node:path';

const [input, output, opentypeDir] = process.argv.slice(2);
if (!input || !output || !opentypeDir) {
  console.error('Usage: node scripts/make-typeface.mjs <font.ttf> <out.json> <opentype.js install dir>');
  process.exit(1);
}
const opentype = createRequire(join(resolve(opentypeDir), 'noop.js'))('opentype.js');

const RANGES = [[0x20, 0x7e], [0xa0, 0xff], [0x100, 0x17f], [0x2010, 0x2027], [0x20ac, 0x20ac]];
const round = (n) => Math.round(n);

/** three.js typeface outline: "m x y", "l x y", "q x y cx cy", "b x y c1x c1y c2x c2y" (end point first). */
function outline(path) {
  return path.commands.map((c) => {
    switch (c.type) {
      case 'M': return `m ${round(c.x)} ${round(c.y)}`;
      case 'L': return `l ${round(c.x)} ${round(c.y)}`;
      case 'Q': return `q ${round(c.x)} ${round(c.y)} ${round(c.x1)} ${round(c.y1)}`;
      case 'C': return `b ${round(c.x)} ${round(c.y)} ${round(c.x1)} ${round(c.y1)} ${round(c.x2)} ${round(c.y2)}`;
      default: return '';
    }
  }).filter(Boolean).join(' ');
}

const font = opentype.parse(readFileSync(input).buffer);
const glyphs = {};
for (const [from, to] of RANGES) {
  for (let code = from; code <= to; code++) {
    const ch = String.fromCodePoint(code);
    const glyph = font.charToGlyph(ch);
    if (!glyph || glyph.index === 0) continue;
    // Font units, y up (opentype's getPath flips y, so build from the raw outline).
    const path = glyph.getPath(0, 0, font.unitsPerEm);
    path.commands.forEach((c) => {
      ['y', 'y1', 'y2'].forEach((k) => { if (k in c) c[k] = -c[k]; });
    });
    const box = glyph.getBoundingBox();
    glyphs[ch] = { ha: round(glyph.advanceWidth), x_min: round(box.x1), x_max: round(box.x2), o: outline(path) };
  }
}

const head = font.tables.head;
const names = font.names;
const json = {
  glyphs,
  familyName: names.fontFamily?.en ?? 'Font',
  ascender: font.ascender,
  descender: font.descender,
  underlinePosition: font.tables.post.underlinePosition,
  underlineThickness: font.tables.post.underlineThickness,
  boundingBox: { xMin: head.xMin, yMin: head.yMin, xMax: head.xMax, yMax: head.yMax },
  resolution: font.unitsPerEm,
  original_font_information: { format: 0, copyright: names.copyright?.en ?? '', license: names.license?.en ?? '' },
  cssFontWeight: 'bold',
  cssFontStyle: 'normal',
};
writeFileSync(output, JSON.stringify(json));
console.log(`${Object.keys(glyphs).length} glyphs → ${output}`);
