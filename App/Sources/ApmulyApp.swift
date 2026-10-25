import SwiftUI

@main
struct ApmulyApp: App {
    @StateObject private var model: NowPlayingModel
    @StateObject private var renderer: RendererBridge

    init() {
        let model = NowPlayingModel()
        _model = StateObject(wrappedValue: model)
        _renderer = StateObject(wrappedValue: RendererBridge(model: model))
    }

    var body: some Scene {
        WindowGroup("Apmuly") {
            StageView(model: model, renderer: renderer)
                .frame(minWidth: 640, idealWidth: 1100, minHeight: 400, idealHeight: 680)
                .preferredColorScheme(.dark)
        }
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandMenu("Mode") {
                ForEach(StageMode.allCases) { mode in
                    Button(mode.title) { renderer.select(mode) }
                }
                Divider()
                Button("Toggle Lyrics / Visualizer") { renderer.select(renderer.mode.toggled) }
                    .keyboardShortcut("l", modifiers: .command)
            }
            CommandMenu("Theme") {
                ForEach(StageTheme.allCases) { theme in
                    Button(theme.title) { renderer.select(theme) }
                        .keyboardShortcut(KeyEquivalent(theme.shortcut), modifiers: .command)
                }
                Divider()
                Button("Next Theme") { renderer.select(renderer.theme.next) }
                    .keyboardShortcut("t", modifiers: .command)
                Divider()
                Button(renderer.livePacing ? "Turn Off Live Singer Pacing" : "Turn On Live Singer Pacing") { renderer.toggleLivePacing() }
            }
        }
    }
}
