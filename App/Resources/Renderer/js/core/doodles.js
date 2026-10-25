// Hand-drawn doodles for the Paper Brush theme, as brush paths (lists of [x, y] points) fitted to a
// word's box {x, y (centre), width, height}. A little wobble makes them look drawn, not computed.

export const DOODLES = ['circle', 'box', 'underline', 'scribble', 'arrow', 'sparkle', 'swirl', 'strokes'];

/** Deterministic pseudo-random sequence for a seed (so a song always gets the same doodles). */
export function rng(seed) {
  let state = (Math.abs(Math.floor(seed)) % 2147483646) + 1;
  return () => {
    state = (state * 16807) % 2147483647;
    return (state - 1) / 2147483646;
  };
}

/** Varied from line to line; long lines lean to underlines and arrows, short ones to circles. */
export function chooseDoodle(index, wordCount) {
  const short = ['circle', 'sparkle', 'box', 'swirl', 'strokes', 'arrow'];
  const long = ['underline', 'arrow', 'circle', 'scribble', 'box', 'sparkle'];
  const list = wordCount <= 2 ? short : long;
  return list[(index * 7 + wordCount) % list.length];
}

const wobble = (random, amount) => (random() - 0.5) * 2 * amount;

function circle(b, random) {
  const rx = b.width / 2 + b.height * 0.45;
  const ry = b.height * 0.95;
  const start = random() * Math.PI * 2;
  const turns = 1.12 + random() * 0.1;
  const steps = 64;
  return [Array.from({ length: steps + 1 }, (_, i) => {
    const t = i / steps;
    const a = start + t * turns * Math.PI * 2;
    const grow = 1 + t * 0.08 + wobble(random, 0.02);
    return [b.x + Math.cos(a) * rx * grow, b.y + Math.sin(a) * ry * grow];
  })];
}

function rectangle(b, random) {
  const pad = b.height * 0.3;
  const l = b.x - b.width / 2 - pad;
  const r = b.x + b.width / 2 + pad;
  const t = b.y + b.height / 2 + pad * 0.6;
  const bo = b.y - b.height / 2 - pad * 0.6;
  const corners = [[l, t], [r + pad * 0.3, t], [r, bo], [l - pad * 0.2, bo], [l + pad * 0.2, t + pad * 0.4]];
  const path = [];
  corners.slice(1).forEach(([x, y], i) => {
    const [px, py] = corners[i];
    for (let k = 0; k < 10; k++) {
      const f = k / 10;
      path.push([px + (x - px) * f + wobble(random, pad * 0.06), py + (y - py) * f + wobble(random, pad * 0.06)]);
    }
  });
  path.push(corners[corners.length - 1]);
  return [path];
}

function underline(b, random) {
  const y = b.y - b.height * 0.68;
  const left = b.x - b.width * 0.48;
  const span = b.width * (0.85 + random() * 0.15);
  const waves = 3 + Math.floor(random() * 3);
  const steps = 48;
  return [Array.from({ length: steps + 1 }, (_, i) => {
    const t = i / steps;
    return [left + t * span, y + Math.sin(t * waves * Math.PI * 2) * b.height * 0.12 + wobble(random, b.height * 0.015)];
  })];
}

function scribble(b, random) {
  const passes = 7 + Math.floor(random() * 4);
  const path = [];
  for (let i = 0; i <= passes; i++) {
    const x = b.x - b.width / 2 + (i / passes) * b.width;
    const up = i % 2 === 0;
    path.push([x + wobble(random, b.height * 0.1), b.y + (up ? 1 : -1) * b.height * (0.3 + random() * 0.15)]);
    path.push([x + b.width / passes / 2, b.y + (up ? -1 : 1) * b.height * (0.25 + random() * 0.15)]);
  }
  return [path];
}

function arrow(b, random) {
  const fromLeft = random() > 0.5;
  const side = fromLeft ? -1 : 1;
  const tip = [b.x + side * (b.width / 2) * 0.6, b.y - b.height * 0.62];
  const tail = [tip[0] + side * b.height * 1.6, tip[1] - b.height * 1.5];
  const bend = [tail[0] - side * b.height * 0.2, tip[1] - b.height * 0.2];
  const steps = 24;
  const shaft = Array.from({ length: steps + 1 }, (_, i) => {
    const t = i / steps;
    const u = 1 - t;
    return [u * u * tail[0] + 2 * u * t * bend[0] + t * t * tip[0], u * u * tail[1] + 2 * u * t * bend[1] + t * t * tip[1]];
  });
  const [px, py] = shaft[steps - 3];
  const angle = Math.atan2(tip[1] - py, tip[0] - px);
  const head = (turn) => Array.from({ length: 7 }, (_, i) => {
    const len = (i / 6) * b.height * 0.45;
    return [tip[0] - Math.cos(angle + turn) * len, tip[1] - Math.sin(angle + turn) * len];
  });
  return [shaft, head(0.55), head(-0.55)];
}

function sparkle(b, random) {
  const cx = b.x + b.width / 2 + b.height * 0.5;
  const cy = b.y + b.height * 0.7;
  const size = b.height * (0.35 + random() * 0.15);
  return [0, Math.PI / 2, Math.PI / 4, -Math.PI / 4].map((a, k) => {
    const len = k < 2 ? size : size * 0.55;
    return Array.from({ length: 7 }, (_, i) => {
      const t = i / 6 - 0.5;
      return [cx + Math.cos(a) * len * t * 2, cy + Math.sin(a) * len * t * 2];
    });
  });
}

function swirl(b, random) {
  const steps = 90;
  const rx = b.width * 0.62;
  const ry = b.height * 0.85;
  const phase = random() * Math.PI;
  return [Array.from({ length: steps + 1 }, (_, i) => {
    const t = (i / steps) * Math.PI * 2;
    return [b.x + Math.sin(t + phase) * rx, b.y + Math.sin(2 * t + phase) * ry * 0.6 + wobble(random, b.height * 0.02)];
  })];
}

/** Vertical brush strokes rising behind the word, like paint daubs. */
function strokes(b, random) {
  const count = 7 + Math.floor(random() * 4);
  return Array.from({ length: count }, (_, k) => {
    const x = b.x - b.width * 0.6 + (k / (count - 1)) * b.width * 1.2 + wobble(random, b.height * 0.1);
    const bottom = b.y - b.height * 1.8;
    const top = b.y - b.height * (0.2 - random() * 0.9);
    return Array.from({ length: 8 }, (_, i) => [x + wobble(random, b.height * 0.03), bottom + ((top - bottom) * i) / 7]);
  });
}

const BUILDERS = { circle, box: rectangle, underline, scribble, arrow, sparkle, swirl, strokes };

/** @returns {[number, number][][]} strokes in drawing order */
export function doodle(kind, box, random) {
  const build = BUILDERS[kind] ?? circle;
  return build(box, random);
}
