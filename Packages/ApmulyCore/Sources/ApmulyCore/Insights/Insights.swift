import Foundation

/// The dominant feeling or expression of a lyric line.
public enum Emotion: String, Sendable, Equatable, Codable, CaseIterable {
    case joy, love, sadness, anger, excitement, calm, fear, confidence, heartbreak, longing, neutral
}

/// What an analyzer (on-device model or fallback) read from one line.
public struct LineReading: Sendable, Equatable, Codable {
    public let emotion: Emotion
    /// Concrete things the line mentions, as the analyzer named them (unvalidated).
    public let objects: [String]

    public init(emotion: Emotion, objects: [String]) {
        self.emotion = emotion
        self.objects = objects
    }
}

/// Something mentioned in a line that the stage can picture.
public struct ObjectCue: Sendable, Equatable, Codable {
    /// Normalised word (lowercase, singular).
    public let word: String
    /// Emoji for the object, if one fits.
    public let emoji: String?
    /// Index of the word in the line that mentions it (so it appears as it's sung).
    public let wordIndex: Int?
}

/// What to show alongside one line.
public struct LineInsight: Sendable, Equatable, Codable {
    /// Emotion emoji for the line (every expressive line has one).
    public let emoji: String?
    /// True when the mood just changed — the stage celebrates with an emoji shower.
    public let burst: Bool
    public let objects: [ObjectCue]
}
