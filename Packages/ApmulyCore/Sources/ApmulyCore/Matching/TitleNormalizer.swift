import Foundation

/// Cleans up Music's track metadata for lyric searches and fuzzy comparison.
public enum TitleNormalizer {
    /// Drops featured-artist and remaster suffixes; keeps remix/version info (different timing).
    public static func searchTitle(_ title: String) -> String {
        let featuring = Patterns.regex(#"\s*[\(\[](?:feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]"#, caseInsensitive: true)
        let bracketedRemaster = Patterns.regex(#"\s*[\(\[][^\)\]]*remaster[^\)\]]*[\)\]]"#, caseInsensitive: true)
        let dashRemaster = Patterns.regex(#"\s+-\s+.*remaster.*$"#, caseInsensitive: true)
        return [featuring, bracketedRemaster, dashRemaster]
            .reduce(title) { Patterns.replacing($1, in: $0) }
            .trimmingCharacters(in: .whitespaces)
    }

    /// First credited artist: "A & B", "A, B", "A feat. B", "A x B" → "A".
    public static func primaryArtist(_ artist: String) -> String {
        let separator = Patterns.regex(#"\s+(?:&|feat\.?|ft\.?|x|with)\s+|\s*,\s*"#, caseInsensitive: true)
        guard let match = Patterns.firstRange(separator, in: artist) else { return artist }
        let primary = artist[..<match.lowerBound].trimmingCharacters(in: .whitespaces)
        return primary.isEmpty ? artist : primary
    }

    /// Case-, accent- and punctuation-insensitive comparison key.
    public static func key(_ text: String) -> String {
        String(text.folding(options: [.caseInsensitive, .diacriticInsensitive], locale: nil)
            .map { $0.isLetter || $0.isNumber ? $0 : " " })
            .split(separator: " ")
            .joined(separator: " ")
    }
}
