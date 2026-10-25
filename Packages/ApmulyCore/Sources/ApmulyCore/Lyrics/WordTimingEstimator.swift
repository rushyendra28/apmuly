import Foundation

/// Estimates per-word timing inside a line from how words are usually sung,
/// for sources that only provide line-level timestamps: syllable counts, quick filler words,
/// a longer hold on the line's last word and a short breath at commas.
public enum WordTimingEstimator {
    /// Singers stretch the last word of a phrase.
    static let phraseFinalHold = 1.6
    /// …and pause briefly at a comma.
    static let commaHold = 1.3
    /// One-syllable filler words are sung quicker than content words.
    static let fillerWeight = 0.6
    private static let fillerWords: Set<String> = [
        "a", "an", "the", "i", "im", "to", "of", "in", "on", "at", "and", "or", "but", "my", "me", "you", "your",
        "it", "its", "is", "be", "we", "he", "she", "so", "for", "with", "as", "by", "if", "oh", "da", "la", "na",
        "de", "el", "y", "que", "en", "le", "les", "et", "un", "une", "der", "die", "das", "und",
    ]
    /// Upper bound on how long one syllable is sung; keeps short lines from
    /// stretching across long instrumental gaps.
    static let maxSecondsPerSyllable: TimeInterval = 0.45
    static let heldNoteAllowance: TimeInterval = 0.5
    private static let vowels = Set("aeiouy")

    public static func estimate(text: String, start: TimeInterval, end: TimeInterval) -> [LyricWord] {
        let tokens = text.split(whereSeparator: \.isWhitespace).map(String.init)
        guard !tokens.isEmpty, end > start else { return [] }

        let weights = weights(for: tokens)
        let totalWeight = weights.reduce(0, +)
        let syllableCount = tokens.map { Double(syllables(in: $0)) }.reduce(0, +)
        let span = min(end - start, syllableCount * maxSecondsPerSyllable + heldNoteAllowance)

        var boundaries = weights.reduce(into: [start]) { acc, weight in
            acc.append(acc[acc.count - 1] + span * weight / totalWeight)
        }
        boundaries[boundaries.count - 1] = start + span // no floating-point drift past the line end
        return tokens.indices.map { i in
            LyricWord(text: tokens[i], start: boundaries[i], end: boundaries[i + 1])
        }
    }

    /// Relative sung length of each word of a line (all > 0).
    public static func weights(for tokens: [String]) -> [Double] {
        tokens.indices.map { i in
            let token = tokens[i]
            let syllableCount = syllables(in: token)
            let bare = token.lowercased().filter(\.isLetter)
            var weight = Double(syllableCount)
            if syllableCount == 1, fillerWords.contains(bare) { weight *= fillerWeight }
            if i == tokens.count - 1, tokens.count > 1 { weight *= phraseFinalHold }
            else if token.hasSuffix(",") || token.hasSuffix(";") || token.hasSuffix("—") { weight *= commaHold }
            return weight
        }
    }

    /// Longest plausible sung duration for `text`, held notes included.
    static func maxSungDuration(of text: String) -> TimeInterval {
        let syllableCount = text.split(whereSeparator: \.isWhitespace).map { syllables(in: String($0)) }.reduce(0, +)
        return Double(max(1, syllableCount)) * maxSecondsPerSyllable + heldNoteAllowance
    }

    /// Rough syllable count; always ≥ 1 so every word gets some time.
    public static func syllables(in word: String) -> Int {
        let letters = word.folding(options: [.diacriticInsensitive, .caseInsensitive], locale: nil)
            .filter(\.isLetter)
        guard !letters.isEmpty else { return 1 }
        guard letters.allSatisfy(\.isASCII) else {
            // Non-Latin scripts: approximate by character count.
            return max(1, (letters.count + 1) / 2)
        }

        let groups = letters.reduce(into: (count: 0, inVowel: false)) { state, ch in
            let isVowel = vowels.contains(ch)
            if isVowel && !state.inVowel { state.count += 1 }
            state.inVowel = isVowel
        }.count
        let silentE = letters.hasSuffix("e") && !letters.hasSuffix("le") && groups > 1
        return max(1, groups - (silentE ? 1 : 0))
    }
}
