import ApmulyCore
import AppKit
import SwiftUI

/// Native text over the 3D stage for anything the renderer doesn't draw:
/// loading / not found / instrumental states, and untimed lyrics.
struct CurrentStatusOverlay: View {
    @ObservedObject var model: NowPlayingModel
    /// False while the 3D stage is running: songs without lyrics become a titled visualizer instead.
    var showsNoLyricsCaption = true

    var body: some View {
        if model.tracker.current == nil {
            StatusText(text: playPrompt)
        } else {
            switch model.currentLyrics {
            case .loaded(.lyrics(let doc)) where doc.timing == .unsynced:
                UnsyncedView(doc: doc)
            case .loaded(.lyrics):
                EmptyView()
            case .loaded(.instrumental), .loaded(.notFound):
                if showsNoLyricsCaption { Caption(text: noLyricsText) }
            case .failed:
                Caption(text: "Couldn't load lyrics — press R to retry")
            case .loading, nil:
                Caption(text: model.currentTrack.map { "Finding lyrics for \($0.title)…" } ?? "")
            }
        }
    }
}

extension CurrentStatusOverlay {
    private var playPrompt: String {
        switch model.sourcePreference {
        case .auto: "Play something in Apple Music or Spotify"
        case .appleMusic: "Play something in Apple Music"
        case .spotify: "Play something in Spotify"
        }
    }

    private var noLyricsText: String {
        if case .loaded(.instrumental) = model.currentLyrics { return "♪ Instrumental" }
        return "No lyrics found for \(model.currentTrack?.title ?? "this track")"
    }
}

/// A small pill near the top, so the visuals stay the star.
private struct Caption: View {
    let text: String

    var body: some View {
        VStack {
            Text(text)
                .font(.system(size: 14, weight: .semibold, design: .rounded))
                .foregroundStyle(.white.opacity(0.9))
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .background(.ultraThinMaterial, in: Capsule())
                .environment(\.colorScheme, .dark)
                .padding(.top, 36)
            Spacer()
        }
    }
}

struct StatusText: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 20, weight: .medium, design: .rounded))
            .foregroundStyle(.white.opacity(0.6))
            .multilineTextAlignment(.center)
            .padding()
    }
}

/// Lyrics without timestamps: shown as a static, scrollable sheet — never faked as in-sync.
struct UnsyncedView: View {
    let doc: LyricsDocument

    var body: some View {
        VStack(spacing: 12) {
            Text("Lyrics aren't time-synced for this song")
                .font(.system(size: 12, weight: .semibold, design: .rounded))
                .foregroundStyle(.white.opacity(0.6))
                .padding(.top, 24)
            ScrollView {
                VStack(spacing: 10) {
                    ForEach(doc.lines.indices, id: \.self) { i in
                        Text(doc.lines[i].text)
                            .font(.system(size: 20, weight: .medium, design: .rounded))
                            .foregroundStyle(.white.opacity(0.85))
                    }
                }
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)
                .padding(.vertical, 24)
            }
        }
        .padding(.horizontal, 48)
        .background(.black.opacity(0.45))
    }
}

struct StatusBar: View {
    @ObservedObject var model: NowPlayingModel
    let mode: StageMode
    let theme: StageTheme
    let livePacing: Bool

    /// Below this, output latency isn't worth mentioning (built-in speakers).
    private static let notableLatency: TimeInterval = 0.04

    var body: some View {
        HStack(spacing: 12) {
            if let track = model.currentTrack {
                Text("\(track.title) — \(track.artist)").lineLimit(1)
            }
            Spacer()
            Text(mode == .lyrics ? mode.title : theme.title)
            Text(syncLabel).monospacedDigit()
            Text("L lyrics · 1–\(StageTheme.allCases.count) or T theme · P live pacing · S source · [ ] nudge · 0 reset").foregroundStyle(.white.opacity(0.5))
        }
        .font(.system(size: 12, weight: .medium, design: .rounded))
        .foregroundStyle(.white.opacity(0.8))
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
        .background(.ultraThinMaterial, in: Capsule())
        .environment(\.colorScheme, .dark)
        .padding(.horizontal, 16)
        .padding(.bottom, 14)
    }

    /// How timing is being corrected for the current song.
    private var syncLabel: String {
        guard let id = model.tracker.current?.trackID else { return "" }
        var parts: [String] = []
        if let estimate = model.autoSync[id] {
            parts.append(String(format: "auto-synced %+.2fs", estimate.globalOffset))
        } else if model.aligningTrackID == id {
            parts.append("listening to sync…")
        }
        if model.outputLatency >= Self.notableLatency {
            parts.append(String(format: "output +%.2fs", model.outputLatency))
        }
        let nudge = model.currentOffset
        if nudge != 0 {
            parts.append(String(format: "nudge %+.2fs", nudge))
        }
        if !livePacing { parts.append("live pacing off") }
        if let source = model.source { parts.insert("via \(source.displayName)\(model.sourcePreference == .auto ? "" : " (fixed)")", at: 0) }
        return parts.isEmpty ? "in sync" : parts.joined(separator: " · ")
    }
}

struct PermissionPrompt: View {
    let error: MusicBridgeError?
    var appName = "Music"

    var body: some View {
        if error == .notAuthorized {
            VStack(spacing: 16) {
                Text("Apmuly needs permission to read what's playing in \(appName).")
                    .font(.title3.weight(.semibold))
                Button("Open Automation Settings") {
                    let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Automation")
                    url.map { _ = NSWorkspace.shared.open($0) }
                }
            }
            .padding(32)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
        }
    }
}
