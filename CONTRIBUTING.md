# Contributing to Apmuly

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Development setup

You need macOS 26 or later, Xcode 26 or later, [Homebrew](https://brew.sh) and Node 22+.

```sh
git clone https://github.com/rushyendra28/apmuly.git
cd apmuly
make setup     # installs XcodeGen
make run       # generates the Xcode project, builds and launches the app
make test      # Swift + JavaScript tests
```

`Apmuly.xcodeproj` is generated from [`project.yml`](project.yml) — edit that file, not the
project, and run `make project` afterwards.

**Tired of permission prompts after every build?** Run `scripts/create-signing-identity.sh` once,
then copy `Config/Local.xcconfig.example` to `Config/Local.xcconfig` and uncomment the
`CODE_SIGN_IDENTITY` line. Your builds get a stable signature, so macOS remembers the permissions.

## Where things live

| Path | What |
|---|---|
| `Packages/ApmulyCore` | Pure Swift logic with tests: LRC parsing, LRCLIB client and cache, playback tracking (AutoMix-aware), lyric↔singer sync, emoji/emotion planning |
| `App/Sources` | The macOS app: Music bridge, audio tap, on-device analysis, SwiftUI views, renderer bridge |
| `App/Resources/Renderer` | The 3D stage (three.js in a web view): `js/core` is pure logic (tested), `js/engine` and `js/themes/kinetic.js` draw it |
| `App/RendererTests` | Node tests for the renderer's pure logic |
| `scripts` | Release, icon, signing-identity and preview tools |

## Guidelines

- **Tests first.** Logic in `ApmulyCore` and `js/core` should come with tests (Swift Testing / `node:test`).
- **Never commit real lyrics** — not in tests, fixtures, screenshots or issues. Use made-up
  placeholder words (see `js/demo.js` and the test files).
- Keep privacy promises: lyric text and audio stay on the Mac; only track metadata goes to LRCLIB.
  Don't log lyric text or speech transcripts.
- Match the surrounding code style; keep files focused and functions small.
- `make previews` renders the stage with placeholder words in headless Chrome — handy for
  checking visual changes (needs Google Chrome, or set `CHROME_PATH`).

## Pull requests

1. Fork and create a branch.
2. Make your change with tests; run `make test` and `make build`.
3. Describe what changed and why; include a preview screenshot for visual changes.
