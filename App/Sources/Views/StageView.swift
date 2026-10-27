import AppKit
import SwiftUI
import WebKit

/// Root view. Visualizer mode: 3D themed lyrics. Lyrics mode: the stage's own lyrics sheet over its
/// dimmed scene (singer-paced, in the song's colours). If the renderer can't start, a native lyrics
/// sheet is shown on its own.
struct StageView: View {
    @ObservedObject var model: NowPlayingModel
    @ObservedObject var renderer: RendererBridge
    @StateObject private var activity = ActivityMonitor()
    @State private var keys = KeyMonitor()

    private var rendererFailed: Bool { renderer.failure != nil }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            if !rendererFailed {
                RendererView(bridge: renderer).ignoresSafeArea()
            }
            if rendererFailed {
                LyricsMachineView(model: model, showsNoLyricsCaption: true)
            } else {
                // Status text and untimed lyrics; songs without lyrics become a visualizer instead.
                CurrentStatusOverlay(model: model, showsNoLyricsCaption: renderer.mode == .lyrics)
            }
        }
        .overlay(StatusBar(model: model, mode: renderer.mode, theme: renderer.theme, livePacing: renderer.livePacing)
            .opacity(activity.isActive ? 1 : 0)
            .animation(.easeInOut(duration: 0.4), value: activity.isActive), alignment: .bottom)
        .overlay(PermissionPrompt(error: model.bridgeError, appName: model.source == .spotify ? "Spotify" : "Music"))
        .task {
            model.start()
            renderer.start()
            activity.start()
            keys.start { key in
                activity.touch()
                return handleKey(key)
            }
        }
        .onDisappear {
            keys.stop()
            activity.stop()
            model.stop()
            renderer.stop()
        }
    }

    private func handleKey(_ characters: String) -> Bool {
        switch characters {
        case "[": model.adjustOffset(by: -NowPlayingModel.offsetStep)
        case "]": model.adjustOffset(by: NowPlayingModel.offsetStep)
        case "0": model.resetOffset()
        case "r", "R": model.retryLyrics()
        case "l", "L": renderer.select(renderer.mode.toggled)
        case "t", "T": renderer.select(renderer.theme.next)
        case "p", "P": renderer.toggleLivePacing()
        case "s", "S": model.cycleSource()
        case "e", "E": renderer.toggleEmoji()
        default:
            guard let theme = StageTheme.allCases.first(where: { String($0.shortcut) == characters }) else { return false }
            renderer.select(theme)
        }
        return true
    }
}

/// Shows the status bar while the mouse moves or keys are pressed, then fades it away.
@MainActor
final class ActivityMonitor: ObservableObject {
    @Published private(set) var isActive = true
    private static let idleDelay: TimeInterval = 3
    private var token: Any?
    private var hideTask: Task<Void, Never>?

    func start() {
        guard token == nil else { return }
        NSApp.windows.forEach { $0.acceptsMouseMovedEvents = true }
        token = NSEvent.addLocalMonitorForEvents(matching: [.mouseMoved, .leftMouseDown, .scrollWheel]) { [weak self] event in
            MainActor.assumeIsolated { self?.touch() }
            return event
        }
        touch()
    }

    func touch() {
        if !isActive { isActive = true }
        hideTask?.cancel()
        hideTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(Self.idleDelay * 1_000_000_000))
            guard !Task.isCancelled else { return }
            self?.isActive = false
        }
    }

    func stop() {
        if let token { NSEvent.removeMonitor(token) }
        token = nil
        hideTask?.cancel()
    }
}

/// Window-wide single-key shortcuts (works on macOS 12; SwiftUI's `onKeyPress` needs macOS 14).
@MainActor
final class KeyMonitor {
    private var token: Any?

    /// `handler` returns true when it handled the key (the key press is then swallowed).
    func start(_ handler: @escaping @MainActor (String) -> Bool) {
        guard token == nil else { return }
        token = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            let modifiers = event.modifierFlags.intersection([.command, .control, .option])
            guard modifiers.isEmpty, let characters = event.charactersIgnoringModifiers else { return event }
            let handled = MainActor.assumeIsolated { handler(characters) }
            return handled ? nil : event
        }
    }

    func stop() {
        if let token { NSEvent.removeMonitor(token) }
        token = nil
    }
}

struct RendererView: NSViewRepresentable {
    let bridge: RendererBridge

    func makeNSView(context: Context) -> WKWebView {
        bridge.makeWebView()
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}
}
