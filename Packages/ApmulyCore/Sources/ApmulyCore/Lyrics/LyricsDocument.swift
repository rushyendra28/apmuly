import Foundation

public enum LyricsTiming: String, Sendable, Equatable, Codable {
    /// Word-level timestamps from the source.
    case wordSynced
    /// Line-level timestamps; word timings are estimated.
    case lineSynced
    /// No timestamps; lines spread evenly across the track.
    case unsynced
}

public struct LyricWord: Sendable, Equatable, Codable {
    public let text: String
    public let start: TimeInterval
    public let end: TimeInterval
}

public struct LyricLine: Sendable, Equatable, Codable {
    public let start: TimeInterval
    public let end: TimeInterval
    public let text: String
    public let words: [LyricWord]

    /// An empty timestamped line: an instrumental break.
    public var isGap: Bool { text.isEmpty }

    /// 0…1 progress through the line at `position`.
    public func progress(at position: TimeInterval) -> Double {
        guard end > start else { return 1 }
        return min(1, max(0, (position - start) / (end - start)))
    }

    /// Index of the word being sung at `position`, if any.
    public func wordIndex(at position: TimeInterval) -> Int? {
        words.lastIndex { $0.start <= position }
    }
}

public struct LyricsDocument: Sendable, Equatable, Codable {
    public let lines: [LyricLine]
    public let timing: LyricsTiming

    public init(lines: [LyricLine], timing: LyricsTiming) {
        self.lines = lines
        self.timing = timing
    }

    /// The last line that has started at `position` (binary search), or nil before the first line.
    public func lineIndex(at position: TimeInterval) -> Int? {
        var low = 0
        var high = lines.count
        while low < high {
            let mid = (low + high) / 2
            if lines[mid].start <= position { low = mid + 1 } else { high = mid }
        }
        return low == 0 ? nil : low - 1
    }
}
