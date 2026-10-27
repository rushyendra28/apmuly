import ApmulyCore
import Foundation

/// Follows Apple Music or Spotify: whichever is playing (Automatic), or the app the user picked.
/// Offers the same calls as a single bridge, so the model doesn't care which app it's reading.
@MainActor
final class PlayerRouter {
    private static let preferenceKey = "sourcePreference"

    let bridges: [MediaSource: PlayerBridge]
    private let defaults: UserDefaults
    private(set) var preference: SourcePreference
    /// The app being followed (nil until one is running).
    private(set) var current: MediaSource?
    private var playingSince: [MediaSource: TimeInterval] = [:]

    init(apps: [PlayerApp] = [.music, .spotify], defaults: UserDefaults = .standard) {
        bridges = Dictionary(uniqueKeysWithValues: apps.map { ($0.source, PlayerBridge(app: $0)) })
        self.defaults = defaults
        preference = defaults.string(forKey: Self.preferenceKey).flatMap(SourcePreference.init(rawValue:)) ?? .auto
    }

    var currentApp: PlayerApp? { current.flatMap { bridges[$0]?.app } }

    func setPreference(_ preference: SourcePreference) {
        self.preference = preference
        defaults.set(preference.rawValue, forKey: Self.preferenceKey)
    }

    /// Polls the running apps, picks which to follow, and returns its poll (ads / episodes read as stopped).
    func poll() async -> Result<PlayerPoll, MusicBridgeError> {
        var polls: [MediaSource: Result<PlayerPoll, MusicBridgeError>] = [:]
        for (source, bridge) in bridges where bridge.isRunning || preference == SourcePreference(source) {
            polls[source] = await bridge.poll()
        }
        let snapshots = MediaSource.allCases.compactMap { source -> SourceSnapshot? in
            guard let bridge = bridges[source] else { return nil }
            let state = (try? polls[source]?.get())?.state ?? .stopped
            updatePlayingSince(source, state: state)
            return SourceSnapshot(source: source, isRunning: bridge.isRunning, state: state, playingSince: playingSince[source])
        }
        current = SourceSelector.choose(preference: preference, current: current, snapshots: snapshots)
        guard let current, let result = polls[current] else {
            let now = ProcessInfo.processInfo.systemUptime
            return .success(PlayerPoll(trackID: "", position: 0, state: .stopped, hostTime: now, latency: 0))
        }
        return result.map { poll in
            current.isSong(trackID: poll.trackID) ? poll
                : PlayerPoll(trackID: "", position: 0, state: .stopped, hostTime: poll.hostTime, latency: poll.latency)
        }
    }

    func currentTrack() async -> Result<TrackInfo, MusicBridgeError> {
        guard let bridge = current.flatMap({ bridges[$0] }) else { return .failure(.scriptFailed(code: -600)) }
        return await bridge.currentTrack()
    }

    func currentArtwork() async -> Result<(trackID: String, data: Data?), MusicBridgeError> {
        guard let bridge = current.flatMap({ bridges[$0] }) else { return .failure(.scriptFailed(code: -600)) }
        return await bridge.currentArtwork()
    }

    private func updatePlayingSince(_ source: MediaSource, state: PlayerState) {
        if state == .playing {
            if playingSince[source] == nil { playingSince[source] = ProcessInfo.processInfo.systemUptime }
        } else {
            playingSince[source] = nil
        }
    }
}

private extension SourcePreference {
    init(_ source: MediaSource) {
        switch source {
        case .appleMusic: self = .appleMusic
        case .spotify: self = .spotify
        }
    }
}
