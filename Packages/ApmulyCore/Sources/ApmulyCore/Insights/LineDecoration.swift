import Foundation

/// The emoji shown at the end of a line in Lyrics mode: mood first, then the things it mentions.
public enum LineDecoration {
    public static let maxEmoji = 3

    public static func emoji(for insight: LineInsight?) -> String {
        guard let insight else { return "" }
        var seen = Set<String>()
        return ([insight.emoji] + insight.objects.map(\.emoji))
            .compactMap { $0 }
            .filter { seen.insert($0).inserted }
            .prefix(maxEmoji)
            .joined(separator: " ")
    }
}
