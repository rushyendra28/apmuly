// Small, pure easing helpers (t in 0…1).

export const clamp01 = (t) => Math.min(1, Math.max(0, t));
export const lerp = (a, b, t) => a + (b - a) * t;
export const easeOutCubic = (t) => 1 - (1 - t) ** 3;
export const easeInCubic = (t) => t ** 3;

/** Overshoots past 1 then settles — the "springy landing". */
export const easeOutBack = (t, overshoot = 1.70158) => {
  const c3 = overshoot + 1;
  return 1 + c3 * (t - 1) ** 3 + overshoot * (t - 1) ** 2;
};

export const easeOutElastic = (t) => {
  if (t <= 0) return 0;
  if (t >= 1) return 1;
  return 2 ** (-10 * t) * Math.sin((t * 10 - 0.75) * ((2 * Math.PI) / 3)) + 1;
};

export const smoothstep = (edge0, edge1, x) => {
  const t = clamp01((x - edge0) / (edge1 - edge0));
  return t * t * (3 - 2 * t);
};

/**
 * Exponential approach of `current` toward `target` at `rate` (1/s), independent of frame rate.
 * Used to ease every visual parameter so nothing changes in a single frame.
 */
export const damp = (current, target, rate, dt) => target + (current - target) * Math.exp(-rate * dt);

/** Deterministic pseudo-random in [0, 1) from an integer seed — stable per word/letter. */
export const seeded = (seed) => {
  const x = Math.sin(seed * 12.9898 + 78.233) * 43758.5453;
  return x - Math.floor(x);
};
