import Foundation

public struct RGB: Sendable, Equatable, Hashable, Codable {
    public let r: Double
    public let g: Double
    public let b: Double

    public init(r: Double, g: Double, b: Double) {
        self.r = r
        self.g = g
        self.b = b
    }

    /// From "#rrggbb" (invalid input reads as black).
    public init(hex: String) {
        let value = Int(hex.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0
        self.init(r: Double((value >> 16) & 0xFF) / 255, g: Double((value >> 8) & 0xFF) / 255, b: Double(value & 0xFF) / 255)
    }

    public var hex: String {
        let channel = { (v: Double) in Int((min(1, max(0, v)) * 255).rounded()) }
        return String(format: "#%02x%02x%02x", channel(r), channel(g), channel(b))
    }

    var value: Double { max(r, g, b) }

    public var saturation: Double {
        let maxC = value
        return maxC == 0 ? 0 : (maxC - min(r, g, b)) / maxC
    }

    /// 0…1 hue.
    var hue: Double {
        let maxC = value
        let delta = maxC - min(r, g, b)
        guard delta > 0 else { return 0 }
        let sector: Double = switch maxC {
        case r: ((g - b) / delta).truncatingRemainder(dividingBy: 6)
        case g: (b - r) / delta + 2
        default: (r - g) / delta + 4
        }
        let h = sector / 6
        return h < 0 ? h + 1 : h
    }

    /// WCAG relative luminance.
    var luminance: Double {
        let linear = { (c: Double) in c <= 0.03928 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4) }
        return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)
    }

    /// WCAG contrast ratio with another colour (1…21).
    func contrast(with other: RGB) -> Double {
        let (a, b) = (luminance, other.luminance)
        return (max(a, b) + 0.05) / (min(a, b) + 0.05)
    }

    /// From hue / saturation / lightness (all 0…1).
    static func hsl(_ h: Double, _ s: Double, _ l: Double) -> RGB {
        let c = (1 - abs(2 * l - 1)) * s
        let x = c * (1 - abs((h * 6).truncatingRemainder(dividingBy: 2) - 1))
        let m = l - c / 2
        let (r, g, b): (Double, Double, Double) = switch Int(h * 6) % 6 {
        case 0: (c, x, 0)
        case 1: (x, c, 0)
        case 2: (0, c, x)
        case 3: (0, x, c)
        case 4: (x, 0, c)
        default: (c, 0, x)
        }
        return RGB(r: r + m, g: g + m, b: b + m)
    }
}

/// Picks a few vivid, distinct colors from album artwork pixels.
public enum PaletteExtractor {
    private static let minValue = 0.15
    private static let minSaturation = 0.25
    private static let minHueDistance = 0.06
    private static let levels = 8.0

    /// Most prominent vivid colours first. Returns [] when fewer than `minimumColors` are found.
    public static func dominantColors(in pixels: [RGB], maxColors: Int = 4, minimumColors: Int = 2) -> [RGB] {
        let buckets = Dictionary(grouping: pixels.filter(isVivid), by: bucketKey)
        let swatches = buckets.values
            .map { members -> (color: RGB, score: Double) in
                let n = Double(members.count)
                let mean = RGB(r: members.map(\.r).reduce(0, +) / n,
                               g: members.map(\.g).reduce(0, +) / n,
                               b: members.map(\.b).reduce(0, +) / n)
                return (mean, n * (0.3 + mean.saturation))
            }
            .sorted { $0.score > $1.score }
            .map(\.color)

        let distinct = swatches.reduce(into: [RGB]()) { picked, color in
            guard picked.count < maxColors,
                  picked.allSatisfy({ hueDistance($0.hue, color.hue) >= minHueDistance })
            else { return }
            picked.append(color)
        }
        return distinct.count >= minimumColors ? distinct : []
    }

    private static func isVivid(_ c: RGB) -> Bool {
        c.value >= minValue && c.saturation >= minSaturation
    }

    private static func bucketKey(_ c: RGB) -> Int {
        let q = { (v: Double) in Int(min(levels - 1, (v * levels).rounded(.down))) }
        return q(c.r) * 64 + q(c.g) * 8 + q(c.b)
    }

    private static func hueDistance(_ a: Double, _ b: Double) -> Double {
        let d = abs(a - b)
        return min(d, 1 - d)
    }
}
