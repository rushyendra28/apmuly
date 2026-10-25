import AppKit
import SwiftUI
import WebKit

/// Root view. Visualizer mode: 3D themed lyrics. Lyrics mode: a readable lyrics sheet over the
/// theme's dimmed 3D scene. If the renderer can't start, the lyrics sheet is shown on its own.
struct StageView: View {
    @ObservedObject var model: NowPlayingModel
    @ObservedObject var renderer: RendererBridge
    @State private var keys = KeyMonitor()

    private var showsSheet: Bool { renderer.mode == .lyrics || renderer.failure != nil }

    /// Dim the 3D scene behind the sheet only when there are lyrics to read.
    private var sheetDim: Double {
        guard renderer.failure == nil else { return 0 }
        if case .loaded(.lyrics) = model.currentLyrics { return 0.5 }
        return 0.1
    }

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()
            if renderer.failure == nil {
                RendererView(bridge: renderer).ignoresSafeArea()
            }
            if showsSheet {
                Color.black.opacity(sheetDim).ignoresSafeArea()
                LyricsMachineView(model: model, showsNoLyricsCaption: renderer.failure != nil)
            } else {
                CurrentStatusOverlay(model: model, showsNoLyricsCaption: false)
            }
        }
        .animation(.easeInOut(duration: 0.35), value: showsSheet)
        .overlay(StatusBar(model: model, mode: renderer.mode), alignment: .bottom)
        .overlay(PermissionPrompt(error: model.bridgeError))
        .task {
            model.start()
            renderer.start()
            keys.start { handleKey($0) }
        }
        .onDisappear {
            keys.stop()
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
        default:
            return false
        }
        return true
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
