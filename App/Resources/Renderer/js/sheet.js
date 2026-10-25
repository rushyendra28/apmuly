// Lyrics mode as an HTML sheet over the (dimmed) stage: the whole song, the current line filling word
// by word on the singer's timing, in the song's colours. Also draws host status text (Windows), since
// native views can't overlay the web view there.

const LIGHT_THEMES = new Set(['grunge', 'paper']);
const MAX_FILL_SCALE = 1;

const css = `
  #sheet { position: fixed; inset: 0; pointer-events: none; opacity: 0; transition: opacity 0.4s ease;
    -webkit-mask-image: linear-gradient(transparent 0, #000 18%, #000 82%, transparent 100%);
    mask-image: linear-gradient(transparent 0, #000 18%, #000 82%, transparent 100%); }
  #sheet.visible { opacity: 1; }
  #sheet-scrim { position: fixed; inset: 0; pointer-events: none; opacity: 0; transition: opacity 0.4s ease;
    background: rgba(0, 0, 0, 0.55); -webkit-backdrop-filter: blur(18px); backdrop-filter: blur(18px); }
  #sheet-scrim.visible { opacity: 1; }
  body.light #sheet-scrim { background: rgba(232, 232, 228, 0.72); }
  #sheet-lines { position: absolute; left: 0; right: 0; margin: 0 auto; max-width: min(1000px, 86vw);
    padding: 0 7vw; box-sizing: border-box; transition: transform 0.55s cubic-bezier(0.22, 1, 0.36, 1); }
  .sheet-line { font: 800 clamp(22px, 3.6vw, 46px)/1.18 -apple-system, BlinkMacSystemFont, "SF Pro Rounded",
      "Segoe UI Variable Display", "Segoe UI", sans-serif;
    letter-spacing: -0.01em; margin: 0 0 0.62em; color: var(--dim); transform-origin: left center;
    transform: scale(0.86); transition: transform 0.45s cubic-bezier(0.22, 1, 0.36, 1), color 0.3s, filter 0.3s;
    filter: blur(0.6px); }
  .sheet-line.current { transform: scale(1); filter: none; }
  .sheet-line.past { color: var(--past); }
  .sheet-line.current .sheet-word { background-image: linear-gradient(90deg, var(--hi) var(--p, 0%), var(--dim) var(--p, 0%));
    -webkit-background-clip: text; background-clip: text; color: transparent; }
  .sheet-emoji { margin-left: 0.4em; }
  #status { position: fixed; left: 0; right: 0; top: 42%; text-align: center; pointer-events: none;
    font: 600 clamp(16px, 2vw, 22px)/1.4 -apple-system, "Segoe UI Variable Text", "Segoe UI", sans-serif;
    color: var(--status, rgba(255,255,255,0.7)); opacity: 0; transition: opacity 0.4s; }
  #status.visible { opacity: 1; }
  #status small { display: block; font-size: 0.72em; opacity: 0.7; margin-top: 0.3em; }
  body.light #status { --status: rgba(20, 20, 20, 0.75); }
`;

const percent = (value) => `${(Math.min(MAX_FILL_SCALE, Math.max(0, value)) * 100).toFixed(1)}%`;

export class LyricsSheet {
  constructor() {
    const style = document.createElement('style');
    style.textContent = css;
    document.head.append(style);
    this.scrim = Object.assign(document.createElement('div'), { id: 'sheet-scrim' });
    this.root = Object.assign(document.createElement('div'), { id: 'sheet' });
    this.list = Object.assign(document.createElement('div'), { id: 'sheet-lines' });
    this.root.append(this.list);
    this.status = Object.assign(document.createElement('div'), { id: 'status' });
    document.body.append(this.scrim, this.root, this.status);
    this.trackID = null;
    this.lineCount = -1;
    this.lines = [];
    this.current = null;
    this.visible = false;
    window.addEventListener('resize', () => { this.current = null; });
  }

  setStatus(title, detail) {
    this.status.replaceChildren();
    if (title) {
      this.status.append(title);
      if (detail) this.status.append(Object.assign(document.createElement('small'), { textContent: detail }));
    }
    this.status.classList.toggle('visible', Boolean(title));
  }

  setTheme(name) {
    document.body.classList.toggle('light', LIGHT_THEMES.has(name));
  }

  /**
   * @param {boolean} visible lyrics mode with the HTML sheet enabled
   * @param {object|null} deck current deck (paced doc, position, lineIndex)
   * @param {Map<number, {emoji?: string}>|null} insights
   * @param {{lyric: string, highlight: string}} palette
   */
  update(visible, deck, insights, palette) {
    const show = visible && Boolean(deck?.doc);
    if (show !== this.visible) {
      this.visible = show;
      this.root.classList.toggle('visible', show);
      this.scrim.classList.toggle('visible', visible);
    }
    if (!show) return;
    this.#colors(palette);
    // Rebuild for a new song, new lyrics or late-arriving emoji (the paced doc changes identity often, so
    // compare its words instead).
    if (deck.doc !== this.lastDoc) {
      this.lastDoc = deck.doc;
      this.docSignature = deck.doc.lines.map((l) => `${l.t}|${l.w.length}`).join('\n');
    }
    const signature = this.docSignature;
    if (deck.trackID !== this.trackID || signature !== this.signature || insights !== this.insights) {
      this.signature = signature;
      this.insights = insights;
      this.#build(deck, insights);
    }
    this.#follow(deck);
  }

  #colors(palette) {
    const light = document.body.classList.contains('light');
    this.root.style.setProperty('--hi', light ? '#151515' : palette.lyric);
    this.root.style.setProperty('--dim', light ? 'rgba(21,21,21,0.38)' : 'rgba(255,255,255,0.4)');
    this.root.style.setProperty('--past', light ? 'rgba(21,21,21,0.28)' : 'rgba(255,255,255,0.28)');
  }

  #build(deck, insights) {
    this.trackID = deck.trackID;
    this.lineCount = deck.doc.lines.length;
    this.current = null;
    this.lines = deck.doc.lines.map((line, index) => {
      const el = document.createElement('div');
      el.className = 'sheet-line';
      if (line.t === '') {
        el.textContent = '♪';
        this.list.append(el);
        return { el, words: [] };
      }
      const words = line.w.map((w, i) => {
        const span = Object.assign(document.createElement('span'), { className: 'sheet-word', textContent: w.t });
        el.append(span);
        if (i < line.w.length - 1) el.append(' ');
        return span;
      });
      const emoji = insights?.get(index)?.emoji;
      if (emoji) el.append(Object.assign(document.createElement('span'), { className: 'sheet-emoji', textContent: emoji }));
      return { el, words };
    });
    this.list.replaceChildren(...this.lines.map((l) => l.el));
  }

  #follow(deck) {
    const index = deck.lineIndex;
    if (index !== this.current) {
      this.lines.forEach((line, i) => {
        line.el.classList.toggle('current', i === index);
        line.el.classList.toggle('past', i < index);
      });
      if (this.current !== null && this.lines[this.current]) this.lines[this.current].words.forEach((s) => s.style.setProperty('--p', '100%'));
      this.current = index;
      const target = this.lines[Math.max(0, index)]?.el;
      if (target) {
        const y = window.innerHeight / 2 - target.offsetTop - target.offsetHeight / 2;
        this.list.style.transform = `translateY(${y}px)`;
      }
    }
    // The current line fills word by word on the (singer-paced) timing.
    const line = deck.doc.lines[index];
    const spans = this.lines[index]?.words ?? [];
    line?.w.forEach((w, i) => {
      const fill = w.e > w.s ? (deck.position - w.s) / (w.e - w.s) : Number(deck.position >= w.s);
      spans[i]?.style.setProperty('--p', percent(fill));
    });
  }
}
