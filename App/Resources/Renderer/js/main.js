import { FontLoader } from 'three/addons/loaders/FontLoader.js';
import { Stage } from './engine/stage.js';
import { WordFactory } from './engine/words.js';
import { Sparkles } from './engine/particles.js';
import { StickerFactory } from './engine/stickers.js';
import { Decks } from './engine/decks.js';
import { initialBeat, detectBeat, decayPulse } from './core/beat.js';
import { damp } from './core/easing.js';
import { KineticDepth } from './themes/kinetic.js';
import { startDemo } from './demo.js';

const FONTS = {
  bold: 'fonts/helvetiker_bold.typeface.json',
};
const MAX_DT = 0.1;
/** How fast the visible pulse rises toward a beat (1/s): ~50 ms attack instead of a one-frame jump. */
const PULSE_ATTACK = 30;
/** Spectrum smoothing: bands rise fast and fall gently, like a VU meter. */
const BAND_ATTACK = 30;
const BAND_RELEASE = 6;
const MAX_TRACKED = 8;

/** Messages to Swift. Outside the app (demo mode) status is mirrored onto <html data-*> for headless checks. */
const post = (message) => {
  document.documentElement.dataset[message.type] = message.message ?? 'true';
  try {
    window.webkit?.messageHandlers?.apmuly?.postMessage(message);
  } catch {
    // Not hosted in WKWebView.
  }
};

window.addEventListener('error', (e) => post({ type: 'error', message: `${e.message} @ ${e.filename}:${e.lineno}` }));
window.addEventListener('unhandledrejection', (e) => post({ type: 'error', message: String(e.reason?.stack ?? e.reason) }));

const parse = (json) => {
  try {
    return JSON.parse(json);
  } catch (error) {
    post({ type: 'error', message: `Bad payload: ${error}` });
    return null;
  }
};

/** Keeps a Map bounded (oldest entries dropped first). */
const remember = (map, key, value) => {
  map.delete(key);
  map.set(key, value);
  while (map.size > MAX_TRACKED) map.delete(map.keys().next().value);
};

async function loadFonts() {
  const loader = new FontLoader();
  const entries = await Promise.all(Object.entries(FONTS).map(async ([name, url]) => [name, new WordFactory(await loader.loadAsync(url))]));
  return Object.fromEntries(entries);
}

async function boot() {
  const fonts = await loadFonts();
  const stage = new Stage(document.getElementById('stage'));
  const sparkles = new Sparkles();
  stage.scene.add(sparkles.points);

  const decks = new Decks();
  let pulse = 0;
  let pulseTarget = 0;
  let beatPending = false;
  let beatState = initialBeat();
  let lastEnergySeq = -1;
  let bandTargets = [];
  let bands = [];
  let level = 0;
  /** 'visualizer' draws lyrics in 3D; 'lyrics' keeps only the scene (the app shows a native sheet). */
  let mode = 'visualizer';

  const engine = {
    stage,
    fonts,
    sparkles,
    stickers: new StickerFactory(),
    insights: new Map(),
    tracks: new Map(),
    artwork: new Map(),
    kick: (amount) => { pulseTarget = Math.max(pulseTarget, amount); },
  };
  const theme = new KineticDepth(engine);
  theme.activate();

  const now = () => performance.now() / 1000;

  window.apmuly = {
    setTheme() {},
    setMode(name) {
      if (name === 'visualizer' || name === 'lyrics') mode = name;
    },
    setLyrics(json) {
      const doc = parse(json);
      if (doc?.trackID && Array.isArray(doc.lines)) decks.setLyrics(doc);
    },
    setInsights(json) {
      const payload = parse(json);
      if (!payload?.trackID || !Array.isArray(payload.lines)) return;
      const lines = payload.lines.filter((l) => Number.isInteger(l.index) && Array.isArray(l.objects));
      remember(engine.insights, payload.trackID, new Map(lines.map((l) => [l.index, l])));
    },
    setTrack(json) {
      const t = parse(json);
      if (!t?.trackID || typeof t.title !== 'string') return;
      remember(engine.tracks, t.trackID, { title: t.title, artist: String(t.artist ?? ''), lyrics: String(t.lyrics ?? 'loading') });
    },
    setArtwork(json) {
      const a = parse(json);
      if (!a?.trackID || typeof a.url !== 'string' || !a.url.startsWith('data:image/')) return;
      remember(engine.artwork, a.trackID, a.url);
    },
    setFrame(json) {
      const f = parse(json);
      if (!f || !Array.isArray(f.decks)) return;
      decks.setFrame(f.decks, now());
      const energy = f.energy;
      if (energy && energy.seq !== lastEnergySeq) {
        lastEnergySeq = energy.seq;
        const result = detectBeat(beatState, energy.bass, now());
        beatState = result.state;
        if (result.beat) beatPending = true;
        if (Array.isArray(energy.bands)) bandTargets = energy.bands.map((b) => Math.min(1, Math.max(0, Number(b) || 0)));
        level = Number(energy.level) || 0;
      }
    },
  };

  let last = now();
  function tick() {
    const t = now();
    const dt = Math.min(MAX_DT, t - last);
    last = t;
    const beat = beatPending;
    beatPending = false;
    pulseTarget = beat ? 1 : decayPulse(pulseTarget, dt);
    pulse = damp(pulse, pulseTarget, PULSE_ATTACK, dt);
    bands = bandTargets.map((target, i) => damp(bands[i] ?? 0, target, target > (bands[i] ?? 0) ? BAND_ATTACK : BAND_RELEASE, dt));

    // In lyrics mode the theme gets no lyric documents: its text animates out, the scene keeps going.
    const frameDecks = decks.frame(t, dt).map((d) => (mode === 'lyrics' ? { ...d, doc: null } : d));
    theme.update({ now: t, dt, pulse, beat, bands, level, mode, decks: frameDecks });
    sparkles.update(dt);
    stage.render();
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);

  const params = new URLSearchParams(location.search);
  if (params.has('demo')) startDemo(window.apmuly, { startAt: Number(params.get('t') ?? 0), lyrics: !params.has('nolyrics') });
  post({ type: 'ready' });
}

boot().catch((error) => post({ type: 'error', message: String(error?.stack ?? error) }));
