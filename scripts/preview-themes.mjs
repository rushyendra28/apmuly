#!/usr/bin/env node
// Renders each theme's placeholder demo in headless Chrome (real time, via the DevTools
// protocol) and saves screenshots. Reports any renderer errors.
//
// Usage: node scripts/preview-themes.mjs <outDir> [seconds=4] [scenes=lyrics,nolyrics,grunge,…] [sizes=1280x720]
// Several window sizes (e.g. 1280x720,700x900,1920x800) each get their own screenshots: <scene>-<size>.png.
// 'lyrics' = Kinetic Depth with placeholder lyrics + emoji; 'nolyrics' = its visualizer for songs without
// lyrics; 'grunge' / 'paper' (+ '-nolyrics') = the same scenes in the other themes; 'sheet' = Lyrics mode.
// Serves App/Resources/Renderer itself on 127.0.0.1:8765 while it runs.
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdirSync, writeFileSync, mkdtempSync, readFile } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, extname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const CHROME = process.env.CHROME_PATH ?? '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome';
const PORT = 9333;
const SERVE_PORT = 8765;
const ROOT = resolve(fileURLToPath(new URL('../App/Resources/Renderer', import.meta.url)));
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.json': 'application/json', '.woff2': 'font/woff2' };
const SCENES = {
  lyrics: 'demo&t=0.5',
  nolyrics: 'demo&nolyrics',
  grunge: 'demo&t=0.5&theme=grunge',
  'grunge-nolyrics': 'demo&nolyrics&theme=grunge',
  paper: 'demo&t=0.5&theme=paper',
  'paper-nolyrics': 'demo&nolyrics&theme=paper',
  sheet: 'demo&t=0.5&mode=lyrics',
  'sheet-grunge': 'demo&t=0.5&mode=lyrics&theme=grunge',
};
const [outDir = 'theme-previews', seconds = '4', list = Object.keys(SCENES).join(','), sizeList = '1280x720'] = process.argv.slice(2);
const sizes = sizeList.split(',').map((s) => s.split('x').map(Number));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// Minimal static server (localhost only, confined to the renderer folder).
const server = createServer((req, res) => {
  const path = resolve(ROOT, `.${decodeURIComponent(new URL(req.url, 'http://x').pathname)}`);
  if (!path.startsWith(ROOT + sep)) {
    res.writeHead(403).end();
    return;
  }
  readFile(path, (error, data) => {
    if (error) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, { 'Content-Type': TYPES[extname(path)] ?? 'application/octet-stream' }).end(data);
  });
});
await new Promise((r) => server.listen(SERVE_PORT, '127.0.0.1', r));

mkdirSync(outDir, { recursive: true });
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${mkdtempSync(join(tmpdir(), 'apmuly-chrome-'))}`,
  '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--hide-scrollbars', '--window-size=1280,720', 'about:blank',
], { stdio: 'ignore' });

async function pageSocket() {
  for (let i = 0; i < 50; i++) {
    try {
      const pages = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
      const page = pages.find((p) => p.type === 'page');
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* not up yet */ }
    await sleep(200);
  }
  throw new Error('Chrome did not start');
}

const ws = new WebSocket(await pageSocket());
await new Promise((r) => ws.addEventListener('open', r, { once: true }));
let nextId = 1;
const pending = new Map();
let pageErrors = [];
ws.addEventListener('message', (event) => {
  const msg = JSON.parse(event.data);
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  } else if (msg.method === 'Runtime.exceptionThrown') {
    const details = msg.params.exceptionDetails;
    pageErrors.push(details.exception?.description ?? details.text);
  } else if (msg.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(msg.params.type)) {
    pageErrors.push(msg.params.args.map((a) => a.value ?? a.description).join(' '));
  } else if (msg.method === 'Log.entryAdded' && msg.params.entry.level === 'error') {
    pageErrors.push(`${msg.params.entry.text} ${msg.params.entry.url ?? ''}`);
  }
});
const send = (method, params = {}) => new Promise((resolve) => {
  const id = nextId++;
  pending.set(id, resolve);
  ws.send(JSON.stringify({ id, method, params }));
});

await send('Runtime.enable');
await send('Log.enable');
let failures = 0;
for (const [width, height] of sizes) {
await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
const suffix = sizes.length > 1 ? `-${width}x${height}` : '';
for (const theme of list.split(',')) {
  pageErrors = [];
  const query = SCENES[theme];
  if (!query) {
    console.log(`${theme}: unknown scene (${Object.keys(SCENES).join(', ')})`);
    failures++;
    continue;
  }
  await send('Page.navigate', { url: `http://127.0.0.1:8765/index.html?${query}` });
  await sleep(Number(seconds) * 1000);
  const status = await send('Runtime.evaluate', { expression: 'JSON.stringify(document.documentElement.dataset)', returnByValue: true });
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join(outDir, `${theme}${suffix}.png`), Buffer.from(shot.result.data, 'base64'));
  const dataset = JSON.parse(status.result.result.value);
  if (dataset.error || !dataset.ready) failures++;
  const pacer = dataset.pacer ? ` (singer pacing ${dataset.pacer})` : '';
  console.log(`${theme}${suffix}: ${dataset.error ? `ERROR ${dataset.error}` : dataset.ready ? `ok${pacer}` : 'not ready'}`);
  [...new Set(pageErrors)].filter((e) => !String(e).includes('favicon')).slice(0, 5)
    .forEach((e) => console.log(`  page: ${String(e).split('\n').slice(0, 3).join(' | ')}`));
}
}
ws.close();
chrome.kill();
server.close();
process.exit(failures ? 1 : 0);
