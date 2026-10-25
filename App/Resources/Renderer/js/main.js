import * as THREE from '../vendor/three.module.min.js';
import { FontLoader } from '../vendor/addons/loaders/FontLoader.js';
import { Stage } from './engine/stage.js';
import { WordFactory } from './engine/words.js';
import { Sparkles } from './engine/particles.js';
import { StickerFactory } from './engine/stickers.js';
import { Decks } from './engine/decks.js';
import { initialBeat, detectBeat, decayPulse } from './core/beat.js';
import { damp } from './core/easing.js';
import { readPalette, PaletteStore, FALLBACK_PALETTE } from './core/palette.js';
import { OnsetDetector } from './core/onsets.js';
import { WordPacer } from './core/pacer.js';
import { KineticDepth } from './themes/kinetic.js';
import { GrungeTheme } from './themes/grunge.js';
import { PaperBrushTheme } from './themes/paper.js';
import { startDemo } from './demo.js';
import { LyricsSheet } from './sheet.js';

const FONTS = {
  bold: 'fonts/inter_extrabold.typeface.json',
};
const MAX_DT = 0.1;
/** How fast the visible pulse rises toward a beat (1/s): ~50 ms attack instead of a one-frame jump. */
const PULSE_ATTACK = 30;
/** Spectrum smoothing: bands rise fast and fall gently, like a VU meter. */
const BAND_ATTACK = 30;
const BAND_RELEASE = 6;
const MAX_TRACKED = 8;
const THEMES = { kinetic: KineticDepth, grunge: GrungeTheme, paper: PaperBrushTheme };
const DEFAULT_THEME = 'kinetic';
const isTheme = (name) => typeof name === 'string' && Object.prototype.hasOwnProperty.call(THEMES, name);

/**
 * Messages to the host app: WKWebView on macOS, WebView2 on Windows. Status is also mirrored onto
 * <html data-*> for headless checks (demo mode).
 */
const post = (message) => {
  document.documentElement.dataset[message.type] = message.message ?? 'true';
  try {
    window.webkit?.messageHandlers?.apmuly?.postMessage(message);
    window.chrome?.webview?.postMessage(message);
  } catch {
    // Not hosted in an app.
  }
};

/** Functions a host may call (Windows sends {fn, arg} web messages; macOS calls window.apmuly directly). */
const HOST_CALLS = new Set(['setTheme', 'setMode', 'setOptions', 'setStatus', 'setLyrics', 'setInsights', 'setTrack', 'setArtwork',
  'setPalette', 'setFrame']);

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
  let forwardKeys = false;
  /** Lyrics mode drawn by the page (the host may draw its own instead). */
  let sheetEnabled = false;
  /** Live singer pacing (the host can switch it off). */
  let livePacing = true;
  const sheet = new LyricsSheet();
  // Live singer pacing: vocal onsets in the music move estimated word times onto the singer.
  const onsets = new OnsetDetector();
  const pacers = new Map();
  const pacerFor = (trackID) => {
    if (!pacers.has(trackID)) {
      const pacer = new WordPacer();
      pacer.enabled = livePacing;
      remember(pacers, trackID, pacer);
    }
    return pacers.get(trackID);
  };
  window.addEventListener('keydown', (e) => {
    if (!forwardKeys || e.repeat || e.metaKey || e.altKey) return;
    post({ type: 'key', message: e.ctrlKey ? `ctrl+${e.key.toLowerCase()}` : e.key });
    e.preventDefault();
  });

  const engine = {
    stage,
    fonts,
    sparkles,
    stickers: new StickerFactory(),
    insights: new Map(),
    tracks: new Map(),
    artwork: new Map(),
    palettes: new PaletteStore(MAX_TRACKED),
    kick: (amount) => { pulseTarget = Math.max(pulseTarget, amount); },
  };

  let themeName = null;
  let theme = null;
  /** The palette last handed to the theme (re-sent when the song or its colours change). */
  let shownPalette = null;
  /** Switches themes; if the new one fails to start, the previous one is restored. */
  const useTheme = (name) => {
    if (!isTheme(name) || name === themeName) return;
    const previous = themeName;
    theme?.deactivate();
    try {
      const next = new THEMES[name](engine);
      next.activate();
      theme = next;
      themeName = name;
    } catch (error) {
      post({ type: 'error', message: `Theme ${name} failed: ${error?.stack ?? error}` });
      theme = null;
      themeName = null;
      if (previous && previous !== name) useTheme(previous);
      return;
    }
    sheet.setTheme(name);
    shownPalette = null;
  };
  useTheme(new URLSearchParams(location.search).get('theme') ?? DEFAULT_THEME);
  if (!theme) useTheme(DEFAULT_THEME);

  const now = () => performance.now() / 1000;

  window.apmuly = {
    /**
     * Host options: `forwardKeys` sends key presses to the host as {type: 'key'} messages (Windows);
     * `sheet` draws Lyrics mode in the page.
     */
    setOptions(json) {
      const options = parse(json);
      if (options && typeof options.forwardKeys === 'boolean') forwardKeys = options.forwardKeys;
      if (options && typeof options.sheet === 'boolean') sheetEnabled = options.sheet;
      if (options && typeof options.pacing === 'boolean') {
        livePacing = options.pacing;
        pacers.forEach((pacer) => { pacer.enabled = livePacing; });
      }
    },
    /** Status text when there's nothing else to show (hosts that can't overlay native views). */
    setStatus(json) {
      const status = parse(json);
      if (!status) return;
      sheet.setStatus(typeof status.title === 'string' ? status.title : '', typeof status.detail === 'string' ? status.detail : '');
    },
    setTheme(name) {
      useTheme(name);
    },
    setPalette(json) {
      const payload = parse(json);
      const palette = readPalette(payload);
      if (payload?.trackID && palette) engine.palettes.set(payload.trackID, palette);
    },
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
        const result = detectBeat(beatState, Number(energy.bass) || 0, now());
        beatState = result.state;
        if (result.beat) beatPending = true;
        if (Array.isArray(energy.bands)) {
          const raw = energy.bands.map((b) => Math.min(1, Math.max(0, Number(b) || 0)));
          bandTargets = raw;
          const current = f.decks.find((d) => d.role === 'current');
          if (onsets.push(raw, now()) && current && decks.lyrics.has(current.trackID)) {
            const at = decks.positionOf(current.trackID, now());
            if (at !== null) pacerFor(current.trackID).observe(at);
          }
        }
        level = Number(energy.level) || 0;
      }
    },
  };

  let last = now();
  /** One bad frame must never stop the stage: report it (once per message) and keep rendering. */
  let lastError = '';
  function tick() {
    try {
      frame();
    } catch (error) {
      const message = String(error?.stack ?? error);
      if (message !== lastError) post({ type: 'error', message });
      lastError = message;
    } finally {
      requestAnimationFrame(tick);
    }
  }

  function frame() {
    const t = now();
    const dt = Math.min(MAX_DT, t - last);
    last = t;
    const beat = beatPending;
    beatPending = false;
    pulseTarget = beat ? 1 : decayPulse(pulseTarget, dt);
    pulse = damp(pulse, pulseTarget, PULSE_ATTACK, dt);
    bands = bandTargets.map((target, i) => damp(bands[i] ?? 0, target, target > (bands[i] ?? 0) ? BAND_ATTACK : BAND_RELEASE, dt));

    const paced = decks.frame(t, dt).map((d) => (d.doc ? { ...d, doc: pacerFor(d.trackID).apply(d.doc, d.position) } : d));
    // In lyrics mode the theme gets no lyric documents: its text animates out, the scene keeps going.
    const frameDecks = mode === 'lyrics' ? paced.map((d) => ({ ...d, doc: null })) : paced;
    const current = paced.find((d) => d.role === 'current');
    // Until a new song's colours are ready, keep the previous song's (no flash of the fallback).
    const palette = engine.palettes.has(current?.trackID) ? engine.palettes.get(current.trackID) : shownPalette ?? FALLBACK_PALETTE;
    if (palette !== shownPalette) {
      theme?.setPalette(palette, shownPalette === null);
      shownPalette = palette;
    }
    theme?.update({ now: t, dt, pulse, beat, bands, level, mode, decks: frameDecks });
    sheet.update(sheetEnabled && mode === 'lyrics', current ?? null, current ? engine.insights.get(current.trackID) ?? null : null, palette);
    sparkles.update(dt);
    stage.render();
  }
  requestAnimationFrame(tick);

  window.chrome?.webview?.addEventListener('message', (event) => {
    const { fn, arg } = event.data ?? {};
    if (HOST_CALLS.has(fn)) window.apmuly[fn](arg);
  });

  const params = new URLSearchParams(location.search);
  if (params.has('demo')) {
    startDemo(window.apmuly, { startAt: Number(params.get('t') ?? 0), lyrics: !params.has('nolyrics'), mode: params.get('mode') ?? 'visualizer' });
    // Headless checks read the pacer's state from <html data-pacer>.
    setInterval(() => {
      const pacer = pacers.get('demo-track');
      if (pacer) document.documentElement.dataset.pacer = JSON.stringify({ latency: pacer.latency, paced: pacer.starts.size });
    }, 500);
  }
  post({ type: 'ready' });
}

boot().catch((error) => post({ type: 'error', message: String(error?.stack ?? error) }));
