import Foundation

public struct TimedWord: Sendable, Equatable, Codable {
    public let text: String
    public let start: TimeInterval
}

public struct LRCEntry: Sendable, Equatable {
    public let start: TimeInterval
    public let text: String
    /// Present only for enhanced LRC (`<mm:ss.xx>word`).
    public let wordStarts: [TimedWord]?
}

public struct ParsedLRC: Sendable, Equatable {
    public let entries: [LRCEntry]
}

/// Parses LRC / enhanced LRC text. Tolerant: malformed lines are skipped, never fatal.
public enum LRCParser {
    public static func parse(_ text: String) -> ParsedLRC {
        let rawLines = text.split(whereSeparator: \.isNewline).map { $0.trimmingCharacters(in: .whitespaces) }
        let offset = rawLines.lazy.compactMap(offsetSeconds(in:)).first ?? 0

        let ordered = rawLines
            .flatMap { entries(fromLine: $0, offset: offset) }
            .enumerated()
            .sorted { ($0.element.start, $0.offset) < ($1.element.start, $1.offset) }
            .map(\.element)
        return ParsedLRC(entries: ordered)
    }

    // MARK: - Line parsing

    private static func entries(fromLine line: String, offset: TimeInterval) -> [LRCEntry] {
        let (stamps, body) = leadingTimestamps(of: line)
        guard !stamps.isEmpty else { return [] }

        let words = enhancedWords(in: body, offset: offset)
        let text = words.map { $0.map(\.text).joined(separator: " ") } ?? body.trimmingCharacters(in: .whitespaces)
        return stamps.map { LRCEntry(start: max(0, $0 - offset), text: text, wordStarts: words) }
    }

    /// Splits `[t1][t2]body` into timestamps and the remaining body.
    private static func leadingTimestamps(of line: String) -> ([TimeInterval], String) {
        var stamps: [TimeInterval] = []
        var rest = Substring(line)
        while rest.hasPrefix("["), let close = rest.firstIndex(of: "]") {
            let tag = rest[rest.index(after: rest.startIndex)..<close]
            guard let seconds = timestamp(String(tag)) else { break }
            stamps.append(seconds)
            rest = rest[rest.index(after: close)...]
        }
        return (stamps, String(rest))
    }

    private static func offsetSeconds(in line: String) -> TimeInterval? {
        guard line.lowercased().hasPrefix("[offset:"), line.hasSuffix("]") else { return nil }
        let value = line.dropFirst("[offset:".count).dropLast().trimmingCharacters(in: .whitespaces)
        return Double(value).map { $0 / 1000 }
    }

    private static func enhancedWords(in body: String, offset: TimeInterval) -> [TimedWord]? {
        let pattern = Patterns.regex(#"<(\d+:\d+(?:\.\d+)?)>\s*([^<]*)"#)
        let words = Patterns.captures(pattern, in: body).compactMap { groups -> TimedWord? in
            guard let start = timestamp(groups[1]) else { return nil }
            let text = groups[2].trimmingCharacters(in: .whitespaces)
            return text.isEmpty ? nil : TimedWord(text: text, start: max(0, start - offset))
        }
        return words.isEmpty ? nil : words
    }

    /// `mm:ss`, `mm:ss.f`, `mm:ss.ff`, `mm:ss.fff` → seconds.
    static func timestamp(_ tag: String) -> TimeInterval? {
        let parts = tag.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 2,
              parts[0].allSatisfy(\.isASCII), parts[1].allSatisfy(\.isASCII),
              let minutes = Int(parts[0]), minutes >= 0,
              parts[1].allSatisfy({ $0.isNumber || $0 == "." }),
              let seconds = Double(parts[1]), seconds >= 0
        else { return nil }
        return Double(minutes) * 60 + seconds
    }
}
