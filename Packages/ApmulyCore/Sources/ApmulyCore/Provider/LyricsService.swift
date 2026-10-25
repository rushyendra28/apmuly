import Foundation
import os

/// Resolves a track to renderable lyrics: cache → provider → document.
public struct LyricsService: Sendable {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "lyrics")

    private let provider: any LyricsProvider
    private let cache: LyricsCache

    public init(provider: any LyricsProvider = LRCLIBClient(), cache: LyricsCache = LyricsCache()) {
        self.provider = provider
        self.cache = cache
    }

    public func lyrics(for track: TrackInfo) async throws(LyricsError) -> LyricsResult {
        if case .hit(let cached) = await cache.lookup(track.id) {
            return Self.result(from: cached, track: track)
        }
        let candidate = try await provider.candidate(for: track)
        do {
            try await cache.store(candidate, for: track.id)
        } catch {
            // Lyrics are still usable; we'll just refetch next time.
            Self.logger.warning("Could not cache lyrics for \(track.id, privacy: .public): \(String(describing: error))")
        }
        return Self.result(from: candidate, track: track)
    }

    /// Stores a user-supplied LRC that takes precedence over the provider.
    public func setManualLyrics(_ lrc: String, for track: TrackInfo) async throws(LyricsError) {
        let manual = LyricsCandidate(trackName: track.title, artistName: track.artist, duration: track.duration,
                                     instrumental: false, plainLyrics: nil, syncedLyrics: lrc)
        try await cache.store(manual, for: track.id)
    }

    static func result(from candidate: LyricsCandidate?, track: TrackInfo) -> LyricsResult {
        guard let candidate else { return .notFound }
        if candidate.instrumental { return .instrumental }

        if let synced = candidate.syncedLyrics, !synced.isEmpty {
            let doc = LyricsTimeline.build(from: LRCParser.parse(synced), trackDuration: track.duration)
            if doc.lines.contains(where: { !$0.isGap }) { return .lyrics(doc) }
        }
        if let plain = candidate.plainLyrics, !plain.isEmpty {
            let doc = LyricsTimeline.unsynced(plainText: plain, trackDuration: track.duration)
            if !doc.lines.isEmpty { return .lyrics(doc) }
        }
        return .notFound
    }
}
