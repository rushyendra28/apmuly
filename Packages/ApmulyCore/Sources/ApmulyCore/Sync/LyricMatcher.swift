import Foundation

/// A word recognised in the song's audio, placed on the track timeline.
public struct HeardWord: Sendable, Equatable {
    public let text: String
    /// Track position (seconds) at which the word was heard.
    public let position: TimeInterval

    public init(text: String, position: TimeInterval) {
        self.text = text
        self.position = position
    }
}

/// A lyric word whose sung time was observed in the audio.
public struct SyncMatch: Sendable, Equatable {
    public let lyricTime: TimeInterval
    public let heardTime: TimeInterval

    public init(lyricTime: TimeInterval, heardTime: TimeInterval) {
        self.lyricTime = lyricTime
        self.heardTime = heardTime
    }
}

/// When one specific lyric word was actually sung, on the lyric file's timeline.
public struct WordObservation: Sendable, Equatable, Codable {
    public let line: Int
    public let word: Int
    public let time: TimeInterval

    public init(line: Int, word: Int, time: TimeInterval) {
        self.line = line
        self.word = word
        self.time = time
    }
}

/// Matches words recognised from the singer's voice to the lyric file's words.
/// Filler words never anchor a match. A match counts when a nearby heard word agrees with a nearby
/// lyric word (allowing one missed word, since sung vocals are recognised patchily), or when the word
/// is long and distinctive and appears only once in the search window. Stray guesses that slip
/// through are rejected later by `SyncCalibrator`'s outlier filtering.
public enum LyricMatcher {
    /// How far (seconds) a heard word may be from its lyric timestamp to be considered.
    public static let searchWindow: TimeInterval = 3
    private static let minimumLength = 3
    private static let distinctiveLength = 6
    /// How many words apart a confirming neighbour may be (1 = adjacent, 2 = one missed word).
    private static let neighbourReach = 2
    private static let fillerWords: Set<String> = [
        "the", "and", "you", "a", "to", "in", "it", "of", "me", "my", "is", "on", "be", "we", "so", "oh",
        "yeah", "im", "its", "your", "that", "for", "but", "all", "no", "do", "just", "up", "now", "like",
        "got", "get", "with", "what", "this", "at", "can", "dont", "know", "baby", "la", "na", "ooh", "ah",
        "hey", "uh", "wanna", "gonna", "are", "was", "were", "they", "she", "her", "him", "his", "our",
        "out", "how", "when", "not", "one", "say", "said", "too", "yes", "ohh", "whoa", "mmm",
    ]

    private struct FlatWord {
        let key: String
        let start: TimeInterval
        let line: Int
        let word: Int
    }

    /// Matches as (lyric time, heard time) pairs, for calibrating the song's overall offset.
    public static func match(heard: [HeardWord], lyrics: LyricsDocument) -> [SyncMatch] {
        let flat = flatten(lyrics)
        return pairs(heard: heard, flat: flat).map { SyncMatch(lyricTime: flat[$0.lyric].start, heardTime: heard[$0.heard].position) }
    }

    /// Matches as word observations on the lyric file's timeline (heard time minus the sync offset),
    /// for re-timing words to the singer's pace.
    public static func observe(heard: [HeardWord], lyrics: LyricsDocument,
                               offset: (TimeInterval) -> TimeInterval) -> [WordObservation] {
        let flat = flatten(lyrics)
        return pairs(heard: heard, flat: flat).map { pair in
            let word = flat[pair.lyric]
            return WordObservation(line: word.line, word: word.word, time: heard[pair.heard].position - offset(word.start))
        }
    }

    private static func flatten(_ lyrics: LyricsDocument) -> [FlatWord] {
        lyrics.lines.enumerated().flatMap { lineIndex, line in
            line.words.enumerated().map { wordIndex, word in
                FlatWord(key: key(word.text), start: word.start, line: lineIndex, word: wordIndex)
            }
        }
    }

    private static func pairs(heard: [HeardWord], flat: [FlatWord]) -> [(heard: Int, lyric: Int)] {
        let keys = heard.map { key($0.text) }
        return heard.indices.compactMap { i -> (heard: Int, lyric: Int)? in
            let k = keys[i]
            guard k.count >= minimumLength, !fillerWords.contains(k) else { return nil }
            let candidates = flat.indices.filter { flat[$0].key == k && abs(flat[$0].start - heard[i].position) <= searchWindow }
            guard let index = candidates.min(by: { abs(flat[$0].start - heard[i].position) < abs(flat[$1].start - heard[i].position) })
            else { return nil }
            let distinctive = k.count >= distinctiveLength && candidates.count == 1
            guard distinctive || neighbourAgrees(i, index, keys: keys, flat: flat) else { return nil }
            return (i, index)
        }
    }

    /// Whether a heard word near `i` equals a lyric word near `index` on the same side.
    private static func neighbourAgrees(_ i: Int, _ index: Int, keys: [String], flat: [FlatWord]) -> Bool {
        for direction in [-1, 1] {
            for heardStep in 1...neighbourReach {
                let h = i + direction * heardStep
                guard keys.indices.contains(h), !keys[h].isEmpty else { continue }
                for lyricStep in 1...neighbourReach {
                    let l = index + direction * lyricStep
                    if flat.indices.contains(l), flat[l].key == keys[h] { return true }
                }
            }
        }
        return false
    }

    /// Lowercase letters and digits only ("Don't," → "dont").
    static func key(_ text: String) -> String {
        String(text.lowercased().filter { $0.isLetter || $0.isNumber })
    }
}
