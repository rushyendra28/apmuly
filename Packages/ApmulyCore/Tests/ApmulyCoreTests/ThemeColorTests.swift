import Foundation
import Testing
@testable import ApmulyCore

/// WCAG relative luminance / contrast, to check palettes stay readable.
private func luminance(_ hex: String) -> Double {
    let value = Int(hex.dropFirst(), radix: 16) ?? 0
    let channel = { (shift: Int) -> Double in
        let c = Double((value >> shift) & 0xFF) / 255
        return c <= 0.03928 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)
    }
    return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
}

private func contrast(_ a: String, _ b: String) -> Double {
    let (la, lb) = (luminance(a), luminance(b))
    return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)
}

private func pixels(_ spec: [(RGB, Int)]) -> [RGB] {
    spec.flatMap { Array(repeating: $0.0, count: $0.1) }
}

private let red = RGB(r: 0.85, g: 0.12, b: 0.2)
private let teal = RGB(r: 0.05, g: 0.7, b: 0.65)
private let black = RGB(r: 0.02, g: 0.02, b: 0.02)
private let grey = RGB(r: 0.5, g: 0.5, b: 0.52)

@Suite("Palette extractor")
struct PaletteExtractorTests {
    @Test("Finds the dominant vivid colours, most common first, ignoring greys")
    func dominant() {
        let colors = PaletteExtractor.dominantColors(in: pixels([(black, 900), (grey, 500), (red, 300), (teal, 120)]))
        #expect(colors.count == 2)
        #expect(colors.first.map { $0.r > 0.7 && $0.g < 0.3 } == true)
    }

    @Test("Greyscale artwork has no vivid colours")
    func greyscale() {
        #expect(PaletteExtractor.dominantColors(in: pixels([(black, 10), (grey, 10)])).isEmpty)
    }
}

@Suite("Theme palette")
struct ThemePaletteTests {
    @Test("Takes its hue from the artwork's dominant colour")
    func hueFromArtwork() {
        let palette = ThemePalette.from(pixels: pixels([(black, 500), (red, 300), (teal, 100)]))
        let highlight = RGB(hex: palette.highlight)
        #expect(highlight.r > highlight.g && highlight.r > highlight.b)
        #expect(palette != ThemePalette.fallback)
    }

    @Test("Stays readable: words on black, white text on the highlight", arguments: [
        RGB(r: 0.85, g: 0.12, b: 0.2), RGB(r: 0.95, g: 0.9, b: 0.1), RGB(r: 0.1, g: 0.2, b: 0.9),
        RGB(r: 0.6, g: 0.95, b: 0.6), RGB(r: 0.3, g: 0.05, b: 0.1),
    ])
    func readable(color: RGB) {
        let palette = ThemePalette.from(pixels: pixels([(color, 400)]))
        #expect(contrast(palette.lyric, "#000000") >= 7, "lyric \(palette.lyric)")
        #expect(contrast("#ffffff", palette.highlight) >= 3, "highlight \(palette.highlight)")
        #expect(luminance(palette.secondary) < luminance(palette.lyric))
    }

    @Test("Greyscale artwork gets the neutral fallback palette")
    func fallback() {
        #expect(ThemePalette.from(pixels: pixels([(black, 100), (grey, 100)])) == ThemePalette.fallback)
        #expect(ThemePalette.from(pixels: []) == ThemePalette.fallback)
    }

    @Test("Hex round-trips")
    func hex() {
        #expect(RGB(hex: "#ff8000").hex == "#ff8000")
        #expect(RGB(r: 1, g: 0.5, b: 0).hex == "#ff8000")
    }
}
