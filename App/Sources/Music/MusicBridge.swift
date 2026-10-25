import AppKit
import ApmulyCore
import Foundation
import os

/// Raw poll result from Music, before track duration is attached.
struct PlayerPoll: Sendable {
    let trackID: String
    let position: TimeInterval
    let state: PlayerState
    let hostTime: TimeInterval
    let latency: TimeInterval
}

enum MusicBridgeError: Error, Sendable, Equatable {
    /// The user hasn't allowed (or denied) Automation access to Music.
    case notAuthorized
    case scriptFailed(code: Int)
}

/// Talks to Music.app over Apple Events. All scripts run on one serial queue:
/// NSAppleScript must not be used concurrently.
final class MusicBridge: @unchecked Sendable {
    static let bundleID = "com.apple.Music"
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "music-bridge")
    private static let notAuthorizedCode = -1743

    // Lean on purpose: each property is a separate Apple Event (~20ms each).
    private static let pollSource = """
    tell application "Music"
      set s to player state as string
      if s is "stopped" then return {s, "", 0}
      return {s, persistent ID of current track, player position}
    end tell
    """
    private static let trackSource = """
    tell application "Music"
      set t to current track
      return {persistent ID of t, name of t, artist of t, album of t, duration of t}
    end tell
    """

    private static let artworkSource = """
    tell application "Music"
      set t to current track
      if (count of artworks of t) is 0 then return {persistent ID of t, ""}
      return {persistent ID of t, raw data of artwork 1 of t}
    end tell
    """

    private let queue = DispatchQueue(label: "io.github.rushyendra28.apmuly.music-bridge", qos: .userInteractive)
    private lazy var pollScript = Self.compile(Self.pollSource)
    private lazy var trackScript = Self.compile(Self.trackSource)
    private lazy var artworkScript = Self.compile(Self.artworkSource)

    var isMusicRunning: Bool {
        !NSRunningApplication.runningApplications(withBundleIdentifier: Self.bundleID).isEmpty
    }

    func poll() async -> Result<PlayerPoll, MusicBridgeError> {
        // Never send Apple Events to a closed Music app: that would launch it.
        guard isMusicRunning else {
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
        guard isMusicRunning else { return .failure(.scriptFailed(code: -600)) }
        return await run { [self] in
            execute(trackScript).map { d in
                TrackInfo(id: d.atIndex(1)?.stringValue ?? "",
                          title: d.atIndex(2)?.stringValue ?? "",
                          artist: d.atIndex(3)?.stringValue ?? "",
                          album: d.atIndex(4)?.stringValue ?? "",
                          duration: d.atIndex(5)?.doubleValue ?? 0)
            }
        }
    }

    /// Raw artwork bytes (JPEG/PNG) of the current track, tagged with its persistent ID; nil data if none.
    func currentArtwork() async -> Result<(trackID: String, data: Data?), MusicBridgeError> {
        guard isMusicRunning else { return .failure(.scriptFailed(code: -600)) }
        return await run { [self] in
            execute(artworkScript).map { d in
                let bytes = d.atIndex(2)?.data ?? Data()
                return (trackID: d.atIndex(1)?.stringValue ?? "", data: bytes.count > 64 ? bytes : nil)
            }
        }
    }

    // MARK: - Private

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
