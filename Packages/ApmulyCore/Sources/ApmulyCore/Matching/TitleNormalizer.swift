import Foundation

/// Cleans up Music's track metadata for lyric searches and fuzzy comparison.
public enum TitleNormalizer {
    /// Drops featured-artist and remaster suffixes; keeps remix/version info (different timing).
    public static func searchTitle(_ title: String) -> String {
        let featuring = /\s*[\(\[](?:feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]/.ignoresCase()
        let bracketedRemaster = /\s*[\(\[][^\)\]]*remaster[^\)\]]*[\)\]]/.ignoresCase()
        let dashRemaster = /\s+-\s+.*remaster.*$/.ignoresCase()
        return title
            .replacing(featuring, with: "")
            .replacing(bracketedRemaster, with: "")
            .replacing(dashRemaster, with: "")
            .trimmingCharacters(in: .whitespaces)
    }

    /// First credited artist: "A & B", "A, B", "A feat. B", "A x B" → "A".
    public static func primaryArtist(_ artist: String) -> String {
        let separator = /\s+(?:&|feat\.?|ft\.?|x|with)\s+|\s*,\s*/.ignoresCase()
        guard let match = artist.firstMatch(of: separator) else { return artist }
        let primary = artist[..<match.range.lowerBound].trimmingCharacters(in: .whitespaces)
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
