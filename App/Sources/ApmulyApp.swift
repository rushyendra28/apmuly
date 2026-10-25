import SwiftUI

@main
struct ApmulyApp: App {
    @State private var model: NowPlayingModel
    @State private var renderer: RendererBridge

    init() {
        let model = NowPlayingModel()
        _model = State(initialValue: model)
        _renderer = State(initialValue: RendererBridge(model: model))
    }

    var body: some Scene {
        WindowGroup("Apmuly") {
            StageView(model: model, renderer: renderer)
                .frame(minWidth: 640, minHeight: 400)
                .preferredColorScheme(.dark)
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 1100, height: 680)
        .commands {
            CommandMenu("Mode") {
                ForEach(StageMode.allCases) { mode in
                    Button(mode.title) { renderer.select(mode) }
                }
                Divider()
                Button("Toggle Lyrics / Visualizer") { renderer.select(renderer.mode.toggled) }
                    .keyboardShortcut("l", modifiers: .command)
            }
        }
    }
}
