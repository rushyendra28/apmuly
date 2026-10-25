// Simple onset detector over bass energy samples (~30 Hz) from Music's audio.

const AVERAGE_RATE = 0.08;   // EMA weight per sample
const SPIKE_RATIO = 1.45;    // bass must exceed the running average by this factor
const MIN_BASS = 0.015;      // ignore near-silence
const REFRACTORY = 0.25;     // seconds between beats (caps at 240 BPM)
const PULSE_DECAY = 6;       // 1/s

export const initialBeat = () => ({ avg: 0, lastBeat: -Infinity });

/** Returns the next detector state and whether this sample is a beat. */
export function detectBeat(state, bass, now) {
  const beat = bass > MIN_BASS
    && bass > state.avg * SPIKE_RATIO
    && now - state.lastBeat > REFRACTORY;
  return {
    beat,
    state: {
      avg: state.avg + (bass - state.avg) * AVERAGE_RATE,
      lastBeat: beat ? now : state.lastBeat,
    },
  };
}

export const decayPulse = (pulse, dt) => pulse * Math.exp(-dt * PULSE_DECAY);
