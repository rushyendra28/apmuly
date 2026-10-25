import Foundation

/// A song's colours, taken from its album artwork, for the stage themes.
public struct ThemePalette: Sendable, Equatable, Codable {
    /// Lyric words (readable on black).
    public let lyric: String
    /// The current word's highlight (white text stays readable on it).
    public let highlight: String
    /// Accents and background detail (dimmer than the lyric colour).
    public let secondary: String

    public init(lyric: String, highlight: String, secondary: String) {
        self.lyric = lyric
        self.highlight = highlight
        self.secondary = secondary
    }

    /// Used when artwork is missing or has no vivid colour.
    public static let fallback = ThemePalette(lyric: "#e9e4f2", highlight: "#6a58e6", secondary: "#4d4863")

    private static let lyricContrastOnBlack = 7.5
    private static let whiteContrastOnHighlight = 3.2
    private static let black = RGB(r: 0, g: 0, b: 0)
    private static let white = RGB(r: 1, g: 1, b: 1)

    public static func from(pixels: [RGB]) -> ThemePalette {
        guard let base = PaletteExtractor.dominantColors(in: pixels, maxColors: 1, minimumColors: 1).first else {
            return fallback
        }
        let hue = base.hue
        let saturation = base.saturation

        // Highlight: vivid, and dark enough for white text on it.
        let highlightSaturation = min(0.85, max(0.45, saturation))
        let highlight = stride(from: 0.56, through: 0.2, by: -0.02).lazy
            .map { RGB.hsl(hue, highlightSaturation, $0) }
            .first { $0.contrast(with: white) >= whiteContrastOnHighlight } ?? RGB.hsl(hue, highlightSaturation, 0.3)

        // Lyric: a soft tint of the same hue, light enough to read on black.
        let lyricSaturation = min(0.6, saturation) * 0.8
        let lyric = stride(from: 0.74, through: 0.96, by: 0.02).lazy
            .map { RGB.hsl(hue, lyricSaturation, $0) }
            .first { $0.contrast(with: black) >= lyricContrastOnBlack } ?? RGB.hsl(hue, lyricSaturation, 0.95)

        let secondary = RGB.hsl(hue, min(0.5, saturation) * 0.6, 0.32)
        return ThemePalette(lyric: lyric.hex, highlight: highlight.hex, secondary: secondary.hex)
    }
}
