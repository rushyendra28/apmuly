import Foundation

/// A track as reported by Music.
public struct TrackInfo: Sendable, Equatable, Hashable, Codable {
    /// Music's persistent ID (hex string).
    public let id: String
    public let title: String
    public let artist: String
    public let album: String
    public let duration: TimeInterval

    public init(id: String, title: String, artist: String, album: String, duration: TimeInterval) {
        self.id = id
        self.title = title
        self.artist = artist
        self.album = album
        self.duration = duration
    }
}

/// A lyrics record from a provider (shape matches LRCLIB's JSON).
public struct LyricsCandidate: Sendable, Equatable, Codable {
    public let trackName: String
    public let artistName: String
    public let duration: TimeInterval?
    public let instrumental: Bool
    public let plainLyrics: String?
    public let syncedLyrics: String?

    public init(trackName: String, artistName: String, duration: TimeInterval?, instrumental: Bool,
                plainLyrics: String?, syncedLyrics: String?) {
        self.trackName = trackName
        self.artistName = artistName
        self.duration = duration
        self.instrumental = instrumental
        self.plainLyrics = plainLyrics
        self.syncedLyrics = syncedLyrics
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        trackName = try c.decodeIfPresent(String.self, forKey: .trackName) ?? ""
        artistName = try c.decodeIfPresent(String.self, forKey: .artistName) ?? ""
        duration = try c.decodeIfPresent(Double.self, forKey: .duration)
        instrumental = try c.decodeIfPresent(Bool.self, forKey: .instrumental) ?? false
        plainLyrics = try c.decodeIfPresent(String.self, forKey: .plainLyrics)
        syncedLyrics = try c.decodeIfPresent(String.self, forKey: .syncedLyrics)
    }
}

public enum LyricsError: Error, Sendable, Equatable {
    case network
    case server(status: Int)
    case decoding
    case storage

    /// Failures that may succeed if tried again later (outages, rate limits, connectivity).
    public var isTransient: Bool {
        switch self {
        case .network: true
        case .server(let status): status >= 500 || status == 429
        case .decoding, .storage: false
        }
    }
}

public enum LyricsResult: Sendable, Equatable {
    case lyrics(LyricsDocument)
    case instrumental
    case notFound
}

public protocol LyricsProvider: Sendable {
    func candidate(for track: TrackInfo) async throws(LyricsError) -> LyricsCandidate?
}
