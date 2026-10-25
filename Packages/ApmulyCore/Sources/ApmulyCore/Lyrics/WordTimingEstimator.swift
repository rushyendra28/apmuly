import Foundation

/// Estimates per-word timing inside a line from syllable counts,
/// for sources that only provide line-level timestamps.
public enum WordTimingEstimator {
    /// Upper bound on how long one syllable is sung; keeps short lines from
    /// stretching across long instrumental gaps.
    static let maxSecondsPerSyllable: TimeInterval = 0.45
    static let heldNoteAllowance: TimeInterval = 0.5
    private static let vowels = Set("aeiouy")

    public static func estimate(text: String, start: TimeInterval, end: TimeInterval) -> [LyricWord] {
        let tokens = text.split(whereSeparator: \.isWhitespace).map(String.init)
        guard !tokens.isEmpty, end > start else { return [] }

        let weights = tokens.map { Double(syllables(in: $0)) }
        let totalWeight = weights.reduce(0, +)
        let span = min(end - start, totalWeight * maxSecondsPerSyllable + heldNoteAllowance)

        let boundaries = weights.reduce(into: [start]) { acc, weight in
            acc.append(acc[acc.count - 1] + span * weight / totalWeight)
        }
        return tokens.indices.map { i in
            LyricWord(text: tokens[i], start: boundaries[i], end: boundaries[i + 1])
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
