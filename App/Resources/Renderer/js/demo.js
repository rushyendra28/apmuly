// Self-running demo with made-up placeholder words (no real lyrics), for previewing the stage in a
// browser: index.html?demo&t=<startSeconds>        lyrics + emoji
//          index.html?demo&nolyrics                visualizer (a song without lyrics)
//          …&theme=grunge|paper                    another theme (default: kinetic)
//          …&mode=lyrics                           Lyrics mode (the page's own lyrics sheet)
// A simulated singer starts each word a little early or late, and the spectrum carries their vocal
// onsets, so live singer pacing can be seen working.

const WORDS = ['neon', 'candy', 'bounce', 'glow', 'pop', 'wave', 'spark', 'drift',
  'jelly', 'orbit', 'fizz', 'zoom', 'shine', 'twist', 'burst', 'float'];
const OBJECT_EMOJI = { candy: '🍬', wave: '🌊', orbit: '🪐', spark: '✨', jelly: '🐙' };
const MOODS = ['😄', '🥳', '😎', '🥺'];
const PALETTE = { lyric: '#f6dfe6', highlight: '#c2335e', secondary: '#5a3440' };
const LINE_SECONDS = 3.2;
const BEAT_SECONDS = 0.5;
const BAND_COUNT = 24;
/** The simulated singer is heard this long after the words' lyric times (like speaker delay)… */
const SINGER_DELAY = 0.08;
/** …and starts each word up to this much early or late. */
const SINGER_SWING = 0.12;
const VOCAL_BANDS = [8, 18];

function demoLyrics(trackID) {
  const lines = [];
  for (let n = 0; n < 40; n++) {
    const start = 1 + n * LINE_SECONDS;
    if (n % 6 === 5) {
      lines.push({ s: start, e: start + LINE_SECONDS, t: '', w: [] });
      continue;
    }
    const count = 3 + (n % 3);
    const texts = Array.from({ length: count }, (_, i) => WORDS[(n * 5 + i * 3) % WORDS.length]);
    // Uneven pace, like a singer: longer words are held longer.
    const weights = texts.map((t, i) => t.length + ((n + i) % 3));
    const unit = (LINE_SECONDS - 0.6) / weights.reduce((a, b) => a + b, 0);
    let cursor = start;
    const words = texts.map((t, i) => {
      const s = cursor;
      cursor += weights[i] * unit;
      return { t, s, e: cursor };
    });
    lines.push({ s: start, e: start + LINE_SECONDS, t: words.map((w) => w.t).join(' '), w: words });
  }
  return { trackID, timing: 'lineSynced', lines };
}

function demoInsights(trackID, doc) {
  const lines = doc.lines.flatMap((line, index) => {
    if (!line.t) return [];
    const objects = line.w.flatMap((w, wordIndex) => (OBJECT_EMOJI[w.t] ? [{ word: w.t, emoji: OBJECT_EMOJI[w.t], wordIndex }] : [])).slice(0, 2);
    return [{ index, emoji: MOODS[index % MOODS.length], burst: index % 3 === 0, objects }];
  });
  return { trackID, lines };
}

/** When the simulated singer is heard starting each word. */
function singerOnsets(doc) {
  return doc.lines.flatMap((line, n) => line.w.map((w, i) => w.s + SINGER_DELAY + (((n * 7 + i * 3) % 5) - 2) * (SINGER_SWING / 2)));
}

/** Synthetic spectrum: a kick on the beat in the lows, gentle shimmer, and the singer's onsets in the voice range. */
function demoBands(position, onBeat, singing) {
  return Array.from({ length: BAND_COUNT }, (_, i) => {
    const low = i < 5 ? (onBeat ? 0.9 : 0.35) : 0;
    const shimmer = 0.2 + 0.05 * Math.sin(position * (1 + i * 0.1) + i);
    const voice = i >= VOCAL_BANDS[0] && i <= VOCAL_BANDS[1] ? (singing ? 0.75 : 0.3) : 0;
    return Math.min(1, low + shimmer + voice);
  });
}

export function startDemo(api, { startAt = 0, lyrics = true, mode = 'visualizer' } = {}) {
  api.setOptions(JSON.stringify({ sheet: true }));
  api.setMode(mode);
  let onsets = [];
  const trackID = 'demo-track';
  api.setTrack(JSON.stringify({ trackID, title: 'Demo Track', artist: 'Apmuly', lyrics: lyrics ? 'synced' : 'none' }));
  api.setPalette(JSON.stringify({ trackID, ...PALETTE }));
  if (lyrics) {
    const doc = demoLyrics(trackID);
    api.setLyrics(JSON.stringify(doc));
    api.setInsights(JSON.stringify(demoInsights(trackID, doc)));
    onsets = singerOnsets(doc);
  }
  const origin = performance.now() / 1000 - startAt;
  let seq = 0;
  setInterval(() => {
    const position = performance.now() / 1000 - origin;
    const onBeat = (position % BEAT_SECONDS) < 0.05;
    const singing = onsets.some((o) => position >= o && position < o + 0.06);
    api.setFrame(JSON.stringify({
      decks: [{ trackID, role: 'current', position, isRunning: true, opacity: 1 }],
      energy: { seq: seq++, bass: onBeat ? 0.3 : 0.04, level: 0.2, bands: demoBands(position, onBeat, singing) },
    }));
  }, 33);
}
