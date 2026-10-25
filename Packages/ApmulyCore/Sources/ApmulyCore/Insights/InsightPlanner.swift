import Foundation

/// Decides which emoji and pictures to show per line: an emotion emoji on every expressive
/// line (with a "burst" when the mood changes), plus objects and actions from the analyzer
/// and from the line's own words.
public enum InsightPlanner {
    /// The same object/action isn't shown again on the very next line.
    public static let objectCooldown = 1
    public static let maxObjectsPerLine = 3
    private static let maxObjectLength = 24

    /// - Returns: insights keyed by line index; lines with nothing to show are omitted.
    public static func plan(lines: [LyricLine], readings: [Int: LineReading]) -> [Int: LineInsight] {
        var lastEmotion: Emotion?
        var lastShown: [String: Int] = [:]
        var result: [Int: LineInsight] = [:]

        for (index, line) in lines.enumerated() where !line.isGap {
            guard let reading = readings[index] else { continue }

            let expressive = reading.emotion != .neutral
            let emoji = expressive ? EmojiLexicon.emoji(for: reading.emotion, variant: index) : nil
            let burst = expressive && reading.emotion != lastEmotion
            if expressive { lastEmotion = reading.emotion }

            var seen = Set<String>()
            let cues = (reading.objects.compactMap { modelCue($0, line: line) } + lineCues(line))
                .filter { seen.insert($0.word).inserted }
                .filter { lastShown[$0.word].map { index - $0 > objectCooldown } ?? true }
                .prefix(maxObjectsPerLine)
            cues.forEach { lastShown[$0.word] = index }

            if emoji != nil || !cues.isEmpty {
                result[index] = LineInsight(emoji: emoji, burst: burst, objects: Array(cues))
            }
        }
        return result
    }

    /// An object or action named by the analyzer, with its emoji if one fits.
    private static func modelCue(_ raw: String, line: LyricLine) -> ObjectCue? {
        guard let word = validObject(raw) else { return nil }
        let action = EmojiLexicon.emoji(forAction: word)
        return ObjectCue(word: word, emoji: action ?? EmojiLexicon.emoji(forObject: word),
                         wordIndex: wordIndex(of: word, in: line))
    }

    /// Emoji-able words straight from the line (actions first, then objects).
    private static func lineCues(_ line: LyricLine) -> [ObjectCue] {
        line.words.enumerated().compactMap { i, w in
            if let action = EmojiLexicon.emoji(forAction: w.text) {
                return ObjectCue(word: EmojiLexicon.normalize(w.text), emoji: action, wordIndex: i)
            }
            guard let emoji = EmojiLexicon.emoji(forObject: w.text) else { return nil }
            return ObjectCue(word: EmojiLexicon.canonical(w.text), emoji: emoji, wordIndex: i)
        }
    }

    /// Canonical object word, or nil if it's empty, abstract, too long, or not made of letters.
    private static func validObject(_ raw: String) -> String? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, trimmed.count <= maxObjectLength,
              trimmed.allSatisfy({ $0.isLetter || $0 == " " || $0 == "-" })
        else { return nil }
        let word = EmojiLexicon.canonical(trimmed)
        return word.isEmpty || EmojiLexicon.isAbstract(word) ? nil : word
    }

    private static func wordIndex(of object: String, in line: LyricLine) -> Int? {
        line.words.firstIndex { EmojiLexicon.canonical($0.text) == object || EmojiLexicon.normalize($0.text) == object }
    }
}
