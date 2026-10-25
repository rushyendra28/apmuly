// Local extrapolation of a deck's playback position between updates from Swift.
// Anchors are plain immutable objects: {pos, at, running}.

/** Differences larger than this (seconds) are real seeks and applied at once. */
const SNAP = 0.5;
/** Fraction of a smaller difference absorbed per update (~30 Hz → settles in ~0.3 s), so words never jump. */
const GAIN = 0.1;

export const anchorFrom = (snapshot, now) => ({
  pos: snapshot.position,
  at: now,
  running: Boolean(snapshot.isRunning),
});

export const positionAt = (anchor, now) => {
  if (!anchor) return 0;
  return anchor.running ? anchor.pos + (now - anchor.at) : anchor.pos;
};

export function reanchor(previous, snapshot, now) {
  if (!previous || !previous.running || !snapshot.isRunning) return anchorFrom(snapshot, now);
  const predicted = positionAt(previous, now);
  const error = snapshot.position - predicted;
  if (Math.abs(error) > SNAP) return anchorFrom(snapshot, now);
  return { pos: predicted + error * GAIN, at: now, running: true };
}
