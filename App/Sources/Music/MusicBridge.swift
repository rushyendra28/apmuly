import AppKit
import ApmulyCore
import Foundation
import os

/// Raw poll result from a player app, before track duration is attached.
struct PlayerPoll: Sendable {
    let trackID: String
    let position: TimeInterval
    let state: PlayerState
    let hostTime: TimeInterval
    let latency: TimeInterval
}

enum MusicBridgeError: Error, Sendable, Equatable {
    /// The user hasn't allowed (or denied) Automation access to the player app.
    case notAuthorized
    case scriptFailed(code: Int)
}

/// How to talk to one desktop player over Apple Events.
struct PlayerApp: Sendable {
    let source: MediaSource
    let bundleID: String
    let pollSource: String
    let trackSource: String
    let artworkSource: String
    /// Durations come in seconds (Music) or milliseconds (Spotify).
    let durationScale: Double
    /// Artwork arrives as image bytes (Music) or as a web link to fetch (Spotify).
    let artworkIsURL: Bool
    /// Posted by the app on play / pause / skip.
    let notification: String

    // Lean on purpose: each property is a separate Apple Event (~20ms each).
    static let music = PlayerApp(
        source: .appleMusic, bundleID: "com.apple.Music",
        pollSource: """
        tell application "Music"
          set s to player state as string
          if s is "stopped" then return {s, "", 0}
          return {s, persistent ID of current track, player position}
        end tell
        """,
        trackSource: """
        tell application "Music"
          set t to current track
          return {persistent ID of t, name of t, artist of t, album of t, duration of t}
        end tell
        """,
        artworkSource: """
        tell application "Music"
          set t to current track
          if (count of artworks of t) is 0 then return {persistent ID of t, ""}
          return {persistent ID of t, raw data of artwork 1 of t}
        end tell
        """,
        durationScale: 1, artworkIsURL: false, notification: "com.apple.Music.playerInfo")

    static let spotify = PlayerApp(
        source: .spotify, bundleID: "com.spotify.client",
        pollSource: """
        tell application "Spotify"
          set s to player state as string
          if s is "stopped" then return {s, "", 0}
          return {s, id of current track, player position}
        end tell
        """,
        trackSource: """
        tell application "Spotify"
          set t to current track
          return {id of t, name of t, artist of t, album of t, duration of t}
        end tell
        """,
        artworkSource: """
        tell application "Spotify"
          set t to current track
          return {id of t, artwork url of t}
        end tell
        """,
        durationScale: 0.001, artworkIsURL: true, notification: "com.spotify.client.PlaybackStateChanged")
}

/// Talks to one player app over Apple Events. All scripts run on one serial queue:
/// NSAppleScript must not be used concurrently.
final class PlayerBridge: @unchecked Sendable {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "music-bridge")
    private static let notAuthorizedCode = -1743
    /// Spotify artwork is only ever fetched from its image CDN.
    private static let artworkHosts: Set<String> = ["i.scdn.co", "mosaic.scdn.co", "image-cdn-ak.spotifycdn.com", "image-cdn-fa.spotifycdn.com"]
    private static let maxArtworkBytes = 4_000_000

    let app: PlayerApp
    private let queue: DispatchQueue
    private lazy var pollScript = Self.compile(app.pollSource)
    private lazy var trackScript = Self.compile(app.trackSource)
    private lazy var artworkScript = Self.compile(app.artworkSource)

    init(app: PlayerApp) {
        self.app = app
        queue = DispatchQueue(label: "io.github.rushyendra28.apmuly.bridge.\(app.source.rawValue)", qos: .userInteractive)
    }

    var isRunning: Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: app.bundleID).isEmpty
    }

    func poll() async -> Result<PlayerPoll, MusicBridgeError> {
        // Never send Apple Events to a closed app: that would launch it.
        guard isRunning else {
            let now = ProcessInfo.processInfo.systemUptime
            return .success(PlayerPoll(trackID: "", position: 0, state: .stopped, hostTime: now, latency: 0))
        }
        return await run { [self] in
            let start = ProcessInfo.processInfo.systemUptime
            let result = execute(pollScript)
            let end = ProcessInfo.processInfo.systemUptime
            return result.map { d in
                PlayerPoll(trackID: d.atIndex(2)?.stringValue ?? "",
                           position: d.atIndex(3)?.doubleValue ?? 0,
                           state: Self.state(d.atIndex(1)?.stringValue),
                           hostTime: (start + end) / 2,
                           latency: end - start)
            }
        }
    }

    func currentTrack() async -> Result<TrackInfo, MusicBridgeError> {
        guard isRunning else { return .failure(.scriptFailed(code: -600)) }
        let scale = app.durationScale
        return await run { [self] in
            execute(trackScript).map { d in
                TrackInfo(id: d.atIndex(1)?.stringValue ?? "",
                          title: d.atIndex(2)?.stringValue ?? "",
                          artist: d.atIndex(3)?.stringValue ?? "",
                          album: d.atIndex(4)?.stringValue ?? "",
                          duration: (d.atIndex(5)?.doubleValue ?? 0) * scale)
            }
        }
    }

    /// Raw artwork bytes (JPEG/PNG) of the current track, tagged with its ID; nil data if none.
    func currentArtwork() async -> Result<(trackID: String, data: Data?), MusicBridgeError> {
        guard isRunning else { return .failure(.scriptFailed(code: -600)) }
        let isURL = app.artworkIsURL
        let reply: Result<(String, Data?, String?), MusicBridgeError> = await run { [self] in
            execute(artworkScript).map { d in
                let id = d.atIndex(1)?.stringValue ?? ""
                if isURL { return (id, nil, d.atIndex(2)?.stringValue) }
                let bytes = d.atIndex(2)?.data ?? Data()
                return (id, bytes.count > 64 ? bytes : nil, nil)
            }
        }
        switch reply {
        case .failure(let error): return .failure(error)
        case .success(let (id, data, link)):
            guard let link else { return .success((trackID: id, data: data)) }
            return .success((trackID: id, data: await Self.download(link)))
        }
    }

    // MARK: - Private

    /// Fetches artwork only over HTTPS from Spotify's image hosts, size-capped.
    private static func download(_ link: String) async -> Data? {
        guard let url = URL(string: link), url.scheme == "https", let host = url.host, artworkHosts.contains(host) else { return nil }
        do {
            let (data, response) = try await URLSession.shared.data(from: url)
            guard (response as? HTTPURLResponse)?.statusCode == 200, data.count > 64, data.count <= maxArtworkBytes else { return nil }
            return data
        } catch {
            logger.info("Artwork download failed: \(error.localizedDescription, privacy: .public)")
            return nil
        }
    }

    private func run<T: Sendable>(_ work: @escaping @Sendable () -> T) async -> T {
        await withCheckedContinuation { continuation in
            queue.async { continuation.resume(returning: work()) }
        }
    }

    private func execute(_ script: NSAppleScript?) -> Result<NSAppleEventDescriptor, MusicBridgeError> {
        guard let script else { return .failure(.scriptFailed(code: 0)) }
        var error: NSDictionary?
        let descriptor = script.executeAndReturnError(&error)
        guard let error else { return .success(descriptor) }
        let code = (error[NSAppleScript.errorNumber] as? Int) ?? 0
        return .failure(code == Self.notAuthorizedCode ? .notAuthorized : .scriptFailed(code: code))
    }

    private static func compile(_ source: String) -> NSAppleScript? {
        let script = NSAppleScript(source: source)
        var error: NSDictionary?
        script?.compileAndReturnError(&error)
        if let error {
            logger.fault("AppleScript failed to compile: \(String(describing: error), privacy: .public)")
            return nil
        }
        return script
    }

    private static func state(_ raw: String?) -> PlayerState {
        switch raw {
        case "playing", "fast forwarding", "rewinding": .playing
        case "paused": .paused
        default: .stopped
        }
    }
}
