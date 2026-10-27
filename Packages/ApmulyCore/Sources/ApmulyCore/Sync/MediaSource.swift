import Foundation

/// A desktop music app Apmuly can follow.
public enum MediaSource: String, Sendable, CaseIterable, Codable {
    case appleMusic
    case spotify

    public var displayName: String {
        switch self {
        case .appleMusic: "Apple Music"
        case .spotify: "Spotify"
        }
    }

    /// Spotify also plays ads and podcast episodes; only tracks have lyrics.
    public func isSong(trackID: String) -> Bool {
        switch self {
        case .appleMusic: !trackID.isEmpty
        case .spotify: trackID.hasPrefix("spotify:track:") || trackID.hasPrefix("spotify:local:")
        }
    }
}

/// Which app to follow: whichever is playing (auto), or one the user picked.
public enum SourcePreference: String, Sendable, CaseIterable, Codable {
    case auto
    case appleMusic
    case spotify

    public var title: String {
        switch self {
        case .auto: "Automatic"
        case .appleMusic: "Apple Music"
        case .spotify: "Spotify"
        }
    }

    public var next: SourcePreference {
        let all = Self.allCases
        return all[((all.firstIndex(of: self) ?? 0) + 1) % all.count]
    }

    var fixed: MediaSource? {
        switch self {
        case .auto: nil
        case .appleMusic: .appleMusic
        case .spotify: .spotify
        }
    }
}

/// One app's state at a poll.
public struct SourceSnapshot: Sendable, Equatable {
    public let source: MediaSource
    public let isRunning: Bool
    public let state: PlayerState
    /// When it last started playing (any clock), if it's playing now.
    public let playingSince: TimeInterval?

    public init(source: MediaSource, isRunning: Bool, state: PlayerState, playingSince: TimeInterval?) {
        self.source = source
        self.isRunning = isRunning
        self.state = state
        self.playingSince = playingSince
    }
}

public enum SourceSelector {
    /// Auto: the playing app (the most recent to start if several); while nothing plays, stay on the current
    /// app if it's still running, else any running app. A manual preference always wins.
    public static func choose(preference: SourcePreference, current: MediaSource?, snapshots: [SourceSnapshot]) -> MediaSource? {
        if let fixed = preference.fixed { return fixed }
        let playing = snapshots.filter { $0.isRunning && $0.state == .playing }
        if let latest = playing.max(by: { ($0.playingSince ?? 0) < ($1.playingSince ?? 0) }) { return latest.source }
        if let current, snapshots.contains(where: { $0.source == current && $0.isRunning }) { return current }
        return snapshots.first(where: \.isRunning)?.source ?? current
    }
}
