import CryptoKit
import Foundation

public enum CacheLookup: Sendable, Equatable {
    /// `nil` candidate = a remembered "no lyrics found".
    case hit(LyricsCandidate?)
    case miss
}

/// On-disk cache of provider results, one JSON file per track.
public actor LyricsCache {
    /// "Not found" is retried after this long, in case lyrics were added upstream.
    public static let negativeTTL: TimeInterval = 3 * 24 * 60 * 60

    private struct Entry: Codable {
        let candidate: LyricsCandidate?
        let storedAt: Date
    }

    private let directory: URL
    private let now: @Sendable () -> Date

    public init(directory: URL = LyricsCache.defaultDirectory, now: @escaping @Sendable () -> Date = { Date() }) {
        self.directory = directory
        self.now = now
    }

    public static var defaultDirectory: URL {
        URL.applicationSupportDirectory.appendingPathComponent("apmuly/lyrics", isDirectory: true)
    }

    public func lookup(_ trackID: String) -> CacheLookup {
        guard let data = try? Data(contentsOf: fileURL(for: trackID)),
              let entry = try? JSONDecoder().decode(Entry.self, from: data)
        else { return .miss }
        if entry.candidate == nil && now().timeIntervalSince(entry.storedAt) > Self.negativeTTL {
            return .miss
        }
        return .hit(entry.candidate)
    }

    public func store(_ candidate: LyricsCandidate?, for trackID: String) throws(LyricsError) {
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let data = try JSONEncoder().encode(Entry(candidate: candidate, storedAt: now()))
            try data.write(to: fileURL(for: trackID), options: .atomic)
        } catch {
            throw .storage
        }
    }

    /// Hashed so arbitrary ids can never escape the cache directory.
    private func fileURL(for trackID: String) -> URL {
        let digest = SHA256.hash(data: Data(trackID.utf8)).map { String(format: "%02x", $0) }.joined()
        return directory.appendingPathComponent("\(digest).json")
    }
}
