import CryptoKit
import Foundation

/// Remembers learned word timings per track, so the next play is on the singer's pace from the
/// first line. Stored as small JSON files (word positions and times only, no lyric text).
public actor TimingStore {
    private struct Key: Hashable {
        let line: Int
        let word: Int
    }

    private let directory: URL

    public init(directory: URL = ApmulyPaths.applicationSupport.appendingPathComponent("timing", isDirectory: true)) {
        self.directory = directory
    }

    public func load(_ trackID: String) -> [WordObservation] {
        guard let data = try? Data(contentsOf: fileURL(trackID)),
              let observations = try? JSONDecoder().decode([WordObservation].self, from: data)
        else { return [] }
        return observations
    }

    /// Merges with what's stored; a newer observation of the same word replaces the older one.
    public func save(_ observations: [WordObservation], for trackID: String) throws(LyricsError) {
        let merged = (load(trackID) + observations).reduce(into: [Key: WordObservation]()) { result, observation in
            result[Key(line: observation.line, word: observation.word)] = observation
        }
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let sorted = merged.values.sorted { ($0.line, $0.word) < ($1.line, $1.word) }
            try JSONEncoder().encode(sorted).write(to: fileURL(trackID), options: .atomic)
        } catch {
            throw .storage
        }
    }

    private func fileURL(_ trackID: String) -> URL {
        let digest = SHA256.hash(data: Data(trackID.utf8)).map { String(format: "%02x", $0) }.joined()
        return directory.appendingPathComponent("\(digest).json")
    }
}
