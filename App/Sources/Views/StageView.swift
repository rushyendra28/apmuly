import SwiftUI
import WebKit

/// Root view. Visualizer mode: 3D themed lyrics. Lyrics mode: a readable lyrics sheet over the
/// theme's dimmed 3D scene. If the renderer can't start, the lyrics sheet is shown on its own.
struct StageView: View {
    let model: NowPlayingModel
    let renderer: RendererBridge
    @FocusState private var focused: Bool

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
        .overlay(alignment: .bottom) { StatusBar(model: model, mode: renderer.mode) }
        .overlay { PermissionPrompt(error: model.bridgeError) }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(characters: CharacterSet(charactersIn: "[]0rRlL")) { press in
            handleKey(press.characters) ? .handled : .ignored
        }
        .task {
            model.start()
            renderer.start()
            focused = true
        }
        .onDisappear {
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

struct RendererView: NSViewRepresentable {
    let bridge: RendererBridge

    func makeNSView(context: Context) -> WKWebView {
        bridge.makeWebView()
    }

    func updateNSView(_ nsView: WKWebView, context: Context) {}
}
