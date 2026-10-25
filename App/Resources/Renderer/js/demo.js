// Self-running demo with made-up placeholder words (no real lyrics), for previewing the stage in a
// browser: index.html?demo&t=<startSeconds>        lyrics + emoji
//          index.html?demo&nolyrics                visualizer (a song without lyrics)

const WORDS = ['neon', 'candy', 'bounce', 'glow', 'pop', 'wave', 'spark', 'drift',
  'jelly', 'orbit', 'fizz', 'zoom', 'shine', 'twist', 'burst', 'float'];
const OBJECT_EMOJI = { candy: '🍬', wave: '🌊', orbit: '🪐', spark: '✨', jelly: '🪼' };
const MOODS = ['😄', '🥳', '😎', '🥺'];
const LINE_SECONDS = 3.2;
const BEAT_SECONDS = 0.5;
const BAND_COUNT = 24;

function demoLyrics(trackID) {
  const lines = [];
  for (let n = 0; n < 40; n++) {
    const start = 1 + n * LINE_SECONDS;
    if (n % 6 === 5) {
      lines.push({ s: start, e: start + LINE_SECONDS, t: '', w: [] });
      continue;
    }
    const count = 3 + (n % 3);
    const words = Array.from({ length: count }, (_, i) => {
      const s = start + (i * (LINE_SECONDS - 0.6)) / count;
      return { t: WORDS[(n * 5 + i * 3) % WORDS.length], s, e: s + (LINE_SECONDS - 0.6) / count };
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

/** Synthetic spectrum: a kick on the beat in the lows, moving shimmer in the highs. */
function demoBands(position, onBeat) {
  return Array.from({ length: BAND_COUNT }, (_, i) => {
    const low = i < 5 ? (onBeat ? 0.9 : 0.35) : 0;
    const shimmer = 0.25 + 0.25 * Math.sin(position * (2 + i * 0.3) + i);
    return Math.min(1, low + shimmer * (1 - i / BAND_COUNT) + 0.1);
  });
}

export function startDemo(api, { startAt = 0, lyrics = true } = {}) {
  const trackID = 'demo-track';
  api.setTrack(JSON.stringify({ trackID, title: 'Demo Track', artist: 'Apmuly', lyrics: lyrics ? 'synced' : 'none' }));
  if (lyrics) {
    const doc = demoLyrics(trackID);
    api.setLyrics(JSON.stringify(doc));
    api.setInsights(JSON.stringify(demoInsights(trackID, doc)));
  }
  const origin = performance.now() / 1000 - startAt;
  let seq = 0;
  setInterval(() => {
    const position = performance.now() / 1000 - origin;
    const onBeat = (position % BEAT_SECONDS) < 0.05;
    api.setFrame(JSON.stringify({
      decks: [{ trackID, role: 'current', position, isRunning: true, opacity: 1 }],
      energy: { seq: seq++, bass: onBeat ? 0.3 : 0.04, level: 0.2, bands: demoBands(position, onBeat) },
    }));
  }, 33);
}
