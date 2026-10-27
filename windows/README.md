# Apmuly for Windows

Synced lyrics for the song playing in **Apple Music for Windows** or the **Spotify** desktop app, drawn on the same 3D stage as the Mac app.
The stage is the shared three.js renderer in `App/Resources/Renderer/`, hosted unchanged in Microsoft Edge
**WebView2**. Everything else is a C# / .NET 10 / WPF port of the Mac app's Swift core.

## Install

1. Download `Apmuly-Windows-<version>-x64.zip` (Intel/AMD) or `-arm64.zip` (Snapdragon/ARM) from the
   [latest release](https://github.com/rushyendra28/apmuly/releases/latest) — the same release as the Mac app — or
   from the **Windows** workflow's artifacts.
2. Unzip anywhere you like (for example `%LOCALAPPDATA%\Programs\Apmuly`) and run `Apmuly.exe`.
   The build is self-contained: no .NET install is needed.
3. Apmuly needs the **Microsoft Edge WebView2 Runtime** (preinstalled on Windows 11 and on up-to-date
   Windows 10). If it's missing, Apmuly shows a link to the
   [Evergreen runtime](https://developer.microsoft.com/microsoft-edge/webview2/).

The build is not code-signed yet, so SmartScreen may warn on first launch ("More info" → "Run anyway").

Requirements: Windows 10 1809 or later (Windows 11 recommended), x64 or ARM64, and Apple Music for Windows
or the Spotify desktop app.

## Use

Start Apple Music or Spotify, play a song, and open Apmuly. Lyrics come from [LRCLIB](https://lrclib.net) (free, no account).

| Key | Action |
| --- | --- |
| `L` (or `Ctrl+L`) | Switch between **Visualizer** (3D lyrics) and **Lyrics** (readable sheet over the scene) |
| `1` / `2` / `3` (or `Ctrl+1/2/3`) | Theme: Kinetic Depth / Grunge / Paper Brush |
| `T` (or `Ctrl+T`) | Next theme (Kinetic → Grunge → Paper → Kinetic) |
| `[` / `]` | Nudge this song's lyrics 0.05 s earlier / later (remembered per song) |
| `0` | Reset this song's nudge |
| `R` | Retry lyrics after a failure |
| `P` | Turn live singer pacing on or off (remembered; on by default) |
| `S` | Player to follow: Auto → Apple Music → Spotify → Auto (remembered) |

Themes:

- **Kinetic Depth** — chrome words flying through warp-speed streaks.
- **Grunge** — black grotesk lyrics spray onto creased paper as they're sung, with grainy album-colour paint.
- **Paper Brush** — bold words pop onto notebook paper, painted colour or a starry night as they're sung, with
  hand-drawn brush doodles, die-cut emoji stickers and torn paper labels.

A saved theme that no longer exists (such as Fisheye from an earlier version) falls back to Kinetic Depth.

**Live singer pacing:** the stage listens for vocal onsets in the music and nudges estimated word timings
onto the singer as the song plays. Press `P` to switch it off or back on.

Click the window first if keys don't respond (the stage needs keyboard focus).

## Where things are stored

Everything lives under `%LOCALAPPDATA%\Apmuly`:

| Path | What |
| --- | --- |
| `settings.json` | Theme, mode, live singer pacing and the followed player |
| `offsets.json` | Per-song lyric nudges |
| `Lyrics\` | LRCLIB results cache (one hashed JSON file per song; "not found" is re-checked after 3 days) |
| `Timing\` | Learned word timings (reserved; nothing writes it on Windows yet) |
| `logs\` | Daily log files, kept 7 days (never contain lyric text) |
| `WebView2\` | The stage's browser profile |

Uninstall: delete the unzipped folder and `%LOCALAPPDATA%\Apmuly`.

## How it works

- **Now playing:** Windows' System Media Transport Controls (the media flyout data). In **Auto**, Apmuly
  follows whichever of Apple Music and Spotify is playing (the one that started or changed track most
  recently if both are), and stays on the last one while both are paused; other apps are only followed when
  neither player is open. Choosing Apple Music or Spotify with `S` pins that player. Each player's songs
  have their own IDs, so nudges never mix. Spotify ads are shown as a plain visualizer. Apmuly listens for
  change events and also polls once a second.
- **Timing:** SMTC reports a position plus the time it was valid; Apmuly extrapolates while playing,
  re-anchors on updates, snaps on seeks and smooths small drift (the Mac's `PlaybackTracker`).
- **Lyrics:** cache → LRCLIB exact lookup → ranked search, with automatic retries (5 s, 20 s, 60 s) when
  LRCLIB is busy (HTTP 5xx / 429) or you're offline. Line-synced lyrics get estimated word timings, which the
  stage then moves onto the singer's vocal onsets as the song plays.
- **Emoji:** keyword lexicons pick emotion emoji and emoji for things a line mentions.
- **Beat:** WASAPI loopback of the default output device feeds a 24-band spectrum and bass level to the stage.

## Differences from the Mac app

| Mac | Windows |
| --- | --- |
| Reads Music.app over Apple Events, with an exact persistent track ID | Reads SMTC; the track ID is a hash of title, artist, album and duration |
| Live word pacing: each word starts on the singer's vocal onset (in the shared stage) | **Same** — fed by the loopback spectrum, and it self-calibrates to the output delay; toggle with `P` |
| Speech recognition learns word timings and auto-syncs early/late lyric files | Not available (no speech pipeline yet); use `[` / `]` to nudge a whole song |
| Measures output-device latency (e.g. Bluetooth) | Not measured; live pacing's calibration absorbs most of it, nudge for the rest |
| On-device language model (macOS 26) or NaturalLanguage sentiment for emoji | Keyword lexicons only (no sentiment model), so fewer emotion emoji |
| Audio tap of Music.app only | Loopback of everything the PC plays (other apps' sound affects the beat) |
| Status captions drawn natively; the page draws the Lyrics-mode sheet | The page draws both (WPF can't draw over WebView2) |
| AutoMix crossfades shown as two decks | Supported by the same tracker, but Apple Music for Windows may not crossfade |
| Plain (unsynced) lyrics shown as a static sheet | Shown as "No synced lyrics for this song" |

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test windows/tests/Apmuly.Core.Tests          # cross-platform core, runs on Windows, macOS, Linux
dotnet build windows/src/Apmuly.Windows               # the app (runs on Windows; compiles anywhere)
dotnet publish windows/src/Apmuly.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

The renderer files are linked from `App/Resources/Renderer/` into the output's `Renderer\` folder at build
time; there is no copy of them under `windows/`.

Layout:

```
windows/
  Apmuly.Windows.sln
  Directory.Build.props        shared settings (nullable, warnings as errors in src/, version)
  src/Apmuly.Core/             net10.0, no Windows APIs: lyrics, matching, LRCLIB, sync, insights,
                               palettes, audio analysis, renderer payloads, the page conversation
  src/Apmuly.Windows/          WPF + WebView2 host, SMTC reader, WASAPI loopback
  tests/Apmuly.Core.Tests/     xUnit tests (ported from the Swift suite, plus Windows-specific logic)
```

CI: `.github/workflows/windows.yml` runs the tests and publishes x64 and ARM64 zips on `windows-latest`;
`v*` tags attach them to the GitHub release.

## Known limitations

- Spotify's web player (in a browser) and Spotify Connect (playing on another device from your phone)
  may not be detected: Apmuly needs the Spotify desktop app playing on this PC.
- Spotify ads are recognised by their title ("Advertisement" / "Spotify") or by being short with no artist;
  a short track without an artist credit may be treated as an ad.
