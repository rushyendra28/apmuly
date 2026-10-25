import Foundation

/// Fallback emotion reading (when the on-device model is unavailable): keyword cues first, then sentiment.
public enum EmotionLexicon {
    private static let cues: [(Emotion, Set<String>)] = [
        (.heartbreak, ["heartbreak", "heartbroken", "heartache"]),
        (.sadness, ["tears", "tear", "cry", "crying", "cried", "sad", "lonely", "alone", "sorrow", "weep"]),
        (.love, ["love", "kiss", "kisses", "darling", "lover", "romance", "adore"]),
        (.excitement, ["dance", "dancing", "party", "jump", "wild", "celebrate", "hype", "fun"]),
        (.anger, ["hate", "mad", "angry", "rage", "fight", "furious"]),
        (.fear, ["scared", "afraid", "fear", "terrified", "nightmare", "panic"]),
        (.longing, ["miss", "missing", "wish", "yearn", "someday", "waiting"]),
        (.calm, ["calm", "peace", "peaceful", "slow", "breathe", "quiet", "gentle"]),
        (.confidence, ["boss", "king", "queen", "crown", "win", "winning", "champion", "unstoppable"]),
        (.joy, ["happy", "smile", "smiling", "laugh", "laughing", "joy", "sunshine"]),
    ]
    private static let sentimentThreshold = 0.4

    /// - Parameters:
    ///   - words: the line's words (any case/punctuation).
    ///   - sentiment: -1…1 sentiment score for the line.
    public static func emotion(words: [String], sentiment: Double) -> Emotion {
        let normalized = Set(words.map { EmojiLexicon.normalize($0) })
        let brokenHeart = normalized.contains("heart") && !normalized.isDisjoint(with: ["broken", "break", "breaking", "broke"])
        if brokenHeart { return .heartbreak }
        if let hit = cues.first(where: { !$0.1.isDisjoint(with: normalized) }) { return hit.0 }
        if sentiment > sentimentThreshold { return .joy }
        if sentiment < -sentimentThreshold { return .sadness }
        return .neutral
    }
}
