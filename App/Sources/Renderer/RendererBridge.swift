import AppKit
import ApmulyCore
import Foundation
import Observation
import os
import WebKit

/// Owns the 3D renderer web view and streams playback state into it at ~30 Hz.
@MainActor
@Observable
final class RendererBridge: NSObject {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "renderer")
    private static let modeKey = "stageMode"
    private static let tickInterval: TimeInterval = 1.0 / 30
    private static let handlerName = "apmuly"

    private(set) var mode: StageMode
    /// Set when the renderer can't start; the UI falls back to the native lyrics sheet.
    private(set) var failure: String?

    @ObservationIgnored private let model: NowPlayingModel
    @ObservationIgnored private let audio: MusicAudioTap
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private weak var webView: WKWebView?
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var isReady = false
    @ObservationIgnored private var sentLyrics: Set<String> = []
    @ObservationIgnored private var sentInsightVersions: [String: Int] = [:]
    @ObservationIgnored private var sentTrackStates: [String: String] = [:]
    @ObservationIgnored private var sentArtwork: Set<String> = []
    @ObservationIgnored private let encoder = JSONEncoder()

    init(model: NowPlayingModel, defaults: UserDefaults = .standard) {
        self.model = model
        audio = model.audioTap
        self.defaults = defaults
        mode = defaults.string(forKey: Self.modeKey).flatMap(StageMode.init(rawValue:)) ?? .visualizer
        super.init()
    }

    // MARK: - Web view

    func makeWebView() -> WKWebView {
        isReady = false
        let configuration = WKWebViewConfiguration()
        let root = Bundle.main.resourceURL?.appendingPathComponent("Renderer", isDirectory: true)
        if let root {
            configuration.setURLSchemeHandler(RendererSchemeHandler(root: root), forURLScheme: RendererSchemeHandler.scheme)
        }
        configuration.userContentController.add(WeakMessageHandler(self), name: Self.handlerName)

        let webView = PassiveWebView(frame: .zero, configuration: configuration)
        webView.navigationDelegate = self
        webView.setValue(false, forKey: "drawsBackground")
        #if DEBUG
        webView.isInspectable = true
        #endif
        self.webView = webView
        if root == nil {
            fail("Renderer resources are missing from the app bundle")
        } else {
            webView.load(URLRequest(url: RendererSchemeHandler.entryURL))
        }
        return webView
    }

    func start() {
        guard timer == nil else { return }
        let timer = Timer(timeInterval: Self.tickInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        audio.stop()
    }

    func select(_ mode: StageMode) {
        self.mode = mode
        defaults.set(mode.rawValue, forKey: Self.modeKey)
        call("setMode", argument: mode.rawValue)
    }

    // MARK: - Streaming

    private func tick() {
        audio.ensureRunning()
        // Skip work while the window is hidden; rendering is paused anyway and polling continues in the model.
        guard isReady, let window = webView?.window, window.occlusionState.contains(.visible) else { return }

        let now = ProcessInfo.processInfo.systemUptime
        let decks = model.render(at: now)
        let present = [("current", decks.current), ("outgoing", decks.outgoing)]
            .compactMap { role, deck in deck.map { (role, $0) } }
        present.forEach { sendTrackAssets($0.1) }

        let frame = RendererPayload.Frame(
            decks: present.map { role, deck in
                RendererPayload.Deck(trackID: deck.trackID, role: role, position: deck.lyricsPosition,
                                     isRunning: deck.isRunning, opacity: deck.opacity)
            },
            energy: audio.snapshot().map { RendererPayload.Energy(seq: $0.sequence, bass: $0.bass, level: $0.level, bands: $0.bands) })
        send("setFrame", frame)
    }

    private func sendTrackAssets(_ deck: DeckRender) {
        let id = deck.trackID
        let lyricsState: String = switch deck.lyrics {
        case .loaded(.lyrics(let doc)) where doc.timing != .unsynced: "synced"
        case .loading, nil: "loading"
        default: "none"
        }
        if let track = deck.track, sentTrackStates[id] != lyricsState {
            sentTrackStates[id] = lyricsState
            send("setTrack", RendererPayload.Track(trackID: id, title: track.title, artist: track.artist, lyrics: lyricsState))
        }
        if !sentLyrics.contains(id), case .loaded(.lyrics(let doc)) = deck.lyrics, doc.timing != .unsynced {
            sentLyrics.insert(id)
            send("setLyrics", RendererPayload.Lyrics(trackID: id, document: doc))
        }
        if let version = model.insightVersions[id], sentInsightVersions[id] != version, let insights = model.insights[id] {
            sentInsightVersions[id] = version
            send("setInsights", RendererPayload.Insights(trackID: id, insights: insights))
        }
        if !sentArtwork.contains(id), let url = model.artworkURLs[id] {
            sentArtwork.insert(id)
            send("setArtwork", RendererPayload.Artwork(trackID: id, url: url))
        }
    }

    private func send(_ function: String, _ payload: some Encodable) {
        do {
            let json = String(decoding: try encoder.encode(payload), as: UTF8.self)
            call(function, argument: json)
        } catch {
            Self.logger.error("Encoding \(function, privacy: .public) failed: \(String(describing: error), privacy: .public)")
        }
    }

    /// Passes data as a JS argument (never string-interpolated into code).
    private func call(_ function: String, argument: String) {
        guard isReady, let webView else { return }
        webView.callAsyncJavaScript("window.apmuly && window.apmuly.\(function)(arg)", arguments: ["arg": argument],
                                    in: nil, in: .page) { result in
            if case .failure(let error) = result {
                Self.logger.error("\(function, privacy: .public) failed: \(error.localizedDescription, privacy: .public)")
            }
        }
    }

    // MARK: - Messages from the page

    fileprivate func received(_ body: Any) {
        guard let message = body as? [String: Any], let type = message["type"] as? String else { return }
        switch type {
        case "ready":
            isReady = true
            failure = nil
            sentLyrics = []
            sentInsightVersions = [:]
            sentTrackStates = [:]
            sentArtwork = []
            call("setMode", argument: mode.rawValue)
            Self.logger.info("Renderer ready")
        case "error":
            let text = (message["message"] as? String) ?? "unknown"
            Self.logger.error("Renderer error: \(text, privacy: .public)")
            if !isReady { fail(text) }
        default:
            break
        }
    }

    private func fail(_ reason: String) {
        failure = reason
        Self.logger.fault("Renderer unavailable: \(reason, privacy: .public)")
    }
}

extension RendererBridge: WKNavigationDelegate {
    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction) async -> WKNavigationActionPolicy {
        // The renderer is self-contained: never navigate anywhere else.
        action.request.url?.scheme == RendererSchemeHandler.scheme ? .allow : .cancel
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        Self.logger.error("Renderer process terminated; reloading")
        isReady = false
        webView.load(URLRequest(url: RendererSchemeHandler.entryURL))
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: any Error) {
        fail(error.localizedDescription)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: any Error) {
        fail(error.localizedDescription)
    }
}

/// Avoids the retain cycle WKUserContentController would create with the bridge.
private final class WeakMessageHandler: NSObject, WKScriptMessageHandler {
    private weak var bridge: RendererBridge?

    init(_ bridge: RendererBridge) {
        self.bridge = bridge
    }

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        MainActor.assumeIsolated { bridge?.received(message.body) }
    }
}

/// The renderer is display-only: it never takes keyboard focus or mouse events,
/// so SwiftUI keeps handling shortcuts.
final class PassiveWebView: WKWebView {
    override var acceptsFirstResponder: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
