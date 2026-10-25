import Foundation

/// Turns parsed LRC entries into a `LyricsDocument` with line ends and word timings.
public enum LyricsTimeline {
    /// How long the final line stays up when nothing follows it.
    public static let maxLastLineDuration: TimeInterval = 6
    private static let minimumLineDuration: TimeInterval = 0.5

    public static func build(from parsed: ParsedLRC, trackDuration: TimeInterval) -> LyricsDocument {
        let entries = parsed.entries
        let lines = entries.indices.map { i -> LyricLine in
            let entry = entries[i]
            let end = i + 1 < entries.count
                ? entries[i + 1].start
                : max(entry.start + minimumLineDuration, min(entry.start + maxLastLineDuration, trackDuration))
            return LyricLine(start: entry.start, end: end, text: entry.text,
                             words: words(for: entry, lineEnd: end))
        }
        let hasWordTiming = entries.contains { $0.wordStarts != nil }
        return LyricsDocument(lines: lines, timing: hasWordTiming ? .wordSynced : .lineSynced)
    }

    /// Plain lyrics: spread non-empty lines evenly over the track.
    public static func unsynced(plainText: String, trackDuration: TimeInterval) -> LyricsDocument {
        let texts = plainText.split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        guard !texts.isEmpty else { return LyricsDocument(lines: [], timing: .unsynced) }
        let slot = max(trackDuration, Double(texts.count)) / Double(texts.count)
        let lines = texts.enumerated().map { i, text in
            let start = Double(i) * slot
            return LyricLine(start: start, end: start + slot, text: text,
                             words: WordTimingEstimator.estimate(text: text, start: start, end: start + slot))
        }
        return LyricsDocument(lines: lines, timing: .unsynced)
    }

    private static func words(for entry: LRCEntry, lineEnd: TimeInterval) -> [LyricWord] {
        guard !entry.text.isEmpty else { return [] }
        guard let timed = entry.wordStarts else {
            return WordTimingEstimator.estimate(text: entry.text, start: entry.start, end: lineEnd)
        }
        return timed.indices.map { i in
            let word = timed[i]
            // The last word has no following timestamp: cap it so it isn't "sung" across a long gap.
            let end = i + 1 < timed.count
                ? timed[i + 1].start
                : min(lineEnd, word.start + WordTimingEstimator.maxSungDuration(of: word.text))
            return LyricWord(text: word.text, start: word.start, end: max(end, word.start))
        }
    }
}
