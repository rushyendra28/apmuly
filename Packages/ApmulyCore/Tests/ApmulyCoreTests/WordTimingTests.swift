import Foundation
import Testing
@testable import ApmulyCore

// All words below are made-up placeholder text.

private func line(_ start: Double, _ end: Double, _ words: [String]) -> LyricLine {
    let text = words.joined(separator: " ")
    return LyricLine(start: start, end: end, text: text,
                     words: WordTimingEstimator.estimate(text: text, start: start, end: end))
}

@Suite("Word observations")
struct WordObservationTests {
    @Test("Heard words map to their line and word, corrected by the song's sync offset")
    func observe() {
        let doc = LyricsDocument(lines: [line(10, 14, ["glimmer", "falcon", "harbor", "velvet"])], timing: .lineSynced)
        let heard = [
            HeardWord(text: "glimmer", position: 10.6),
            HeardWord(text: "falcon", position: 11.3),
            HeardWord(text: "harbor", position: 12.0),
        ]
        let observations = LyricMatcher.observe(heard: heard, lyrics: doc) { _ in 0.2 }
        #expect(observations.map(\.word) == [0, 1, 2])
        #expect(observations.allSatisfy { $0.line == 0 })
        let times: [Double] = observations.map { ($0.time * 10).rounded() / 10 }
        #expect(times == [10.4, 11.1, 11.8])
    }
}

@Suite("Word retimer")
struct WordRetimerTests {
    private let doc = LyricsDocument(lines: [
        line(10, 20, ["glow", "pop", "zoom", "spark", "drift"]),
        line(20, 30, ["mint", "plume"]),
    ], timing: .lineSynced)

    private func starts(_ d: LyricsDocument, _ i: Int) -> [Double] { d.lines[i].words.map(\.start) }

    @Test("Pins heard words and spreads the others between them")
    func pinsAndSpreads() {
        let retimed = WordRetimer.retime(doc, observations: [
            WordObservation(line: 0, word: 1, time: 11.0),
            WordObservation(line: 0, word: 3, time: 15.0),
        ])
        let s = starts(retimed, 0)
        #expect(s[1] == 11.0)
        #expect(s[3] == 15.0)
        #expect(s[0] >= 10 && s[0] < 11)
        #expect(s[2] > 11 && s[2] < 15)
        #expect(s[4] > 15 && s[4] < 20)
        let words = retimed.lines[0].words
        for (a, b) in zip(words, words.dropFirst()) { #expect(abs(a.end - b.start) < 0.0001) }
        #expect(words.allSatisfy { $0.end > $0.start })
    }

    @Test("Continues at the singer's measured pace after the last heard word")
    func learnedPace() {
        let retimed = WordRetimer.retime(doc, observations: [
            WordObservation(line: 0, word: 0, time: 10.0),
            WordObservation(line: 0, word: 1, time: 11.0),
        ])
        let s = starts(retimed, 0)
        #expect(abs(s[2] - 12.0) < 0.01)
        #expect(abs(s[3] - 13.0) < 0.01)
    }

    @Test("Rejects observations that are out of order or outside the line")
    func rejectsBadObservations() {
        let retimed = WordRetimer.retime(doc, observations: [
            WordObservation(line: 0, word: 1, time: 13.0),
            WordObservation(line: 0, word: 2, time: 12.0), // earlier than the word before it
            WordObservation(line: 0, word: 4, time: 25.0), // after the line ends
        ])
        let s = starts(retimed, 0)
        #expect(s[1] == 13.0)
        #expect(s[2] > 13.0)
        #expect(s[4] < 20.0)
    }

    @Test("Observations before the line starts are clamped to the line start")
    func clampsEarly() {
        let retimed = WordRetimer.retime(doc, observations: [WordObservation(line: 0, word: 0, time: 9.4)])
        #expect(starts(retimed, 0)[0] == 10.0)
    }

    @Test("A repeated line inherits the timing learned on another occurrence")
    func repeatedLines() {
        let chorus = LyricsDocument(lines: [
            line(10, 14, ["glow", "pop", "zoom"]),
            line(14, 20, ["mint", "plume"]),
            line(40, 44, ["glow", "pop", "zoom"]),
        ], timing: .lineSynced)
        let retimed = WordRetimer.retime(chorus, observations: [
            WordObservation(line: 0, word: 1, time: 11.5),
            WordObservation(line: 0, word: 2, time: 12.5),
        ])
        let s = starts(retimed, 2)
        #expect(abs(s[1] - 41.5) < 0.0001)
        #expect(abs(s[2] - 42.5) < 0.0001)
    }

    @Test("Lines without observations are untouched")
    func untouched() {
        #expect(WordRetimer.retime(doc, observations: []) == doc)
        let retimed = WordRetimer.retime(doc, observations: [WordObservation(line: 0, word: 1, time: 11.0)])
        #expect(retimed.lines[1] == doc.lines[1])
    }
}

@Suite("Timing store")
struct TimingStoreTests {
    private func directory() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("apmuly-timing-\(UUID().uuidString)")
    }

    @Test("Saves, loads and merges observations per track (newest wins)")
    func roundTrip() async throws {
        let store = TimingStore(directory: directory())
        try await store.save([WordObservation(line: 0, word: 1, time: 11.0), WordObservation(line: 2, word: 0, time: 30.0)], for: "T1")
        try await store.save([WordObservation(line: 0, word: 1, time: 11.2)], for: "T1")
        let loaded = await store.load("T1").sorted { ($0.line, $0.word) < ($1.line, $1.word) }
        #expect(loaded == [WordObservation(line: 0, word: 1, time: 11.2), WordObservation(line: 2, word: 0, time: 30.0)])
        #expect(await store.load("other").isEmpty)
    }
}

@Suite("Emoji compatibility")
struct EmojiCompatibilityTests {
    @Test("Every emoji displays on macOS 12: Unicode 13 or earlier, no joined (ZWJ) sequences")
    func compatible() {
        for emoji in EmojiLexicon.allEmoji {
            #expect(!emoji.unicodeScalars.contains("\u{200D}"), "\(emoji) is a ZWJ sequence")
            for scalar in emoji.unicodeScalars {
                let age = scalar.properties.age
                #expect(age.map { $0.major < 13 || ($0.major == 13 && $0.minor == 0) } == true,
                        "\(emoji) needs Unicode \(age.map { "\($0.major).\($0.minor)" } ?? "?")")
            }
        }
    }
}

@Suite("Singing weights")
struct SingingWeightTests {
    private func durations(_ text: String, _ start: Double = 0, _ end: Double = 10) -> [Double] {
        WordTimingEstimator.estimate(text: text, start: start, end: end).map { $0.end - $0.start }
    }

    @Test("The last word of a line is held longer than the same word mid-line")
    func phraseFinalHold() {
        let d = durations("glow glow glow")
        #expect(d[2] > d[0] * 1.3)
        #expect(abs(d[0] - d[1]) < 0.0001)
    }

    @Test("Short filler words go by quicker than content words")
    func fillerWordsAreQuick() {
        let d = durations("the spark and glow")
        #expect(d[0] < d[1])
        #expect(d[2] < d[3])
    }

    @Test("A comma inside a line lengthens the word before it")
    func commaPause() {
        let d = durations("spark, glow spark")
        let plain = durations("spark glow spark")
        #expect(d[0] > plain[0])
    }

    @Test("Weights stay positive and the line is still fully covered")
    func coverage() {
        let words = WordTimingEstimator.estimate(text: "a the of to", start: 5, end: 6)
        #expect(words.allSatisfy { $0.end > $0.start })
        #expect(abs((words.last?.end ?? 0) - 6) < 0.0001)
        #expect(WordTimingEstimator.weights(for: ["a", "glow"]).allSatisfy { $0 > 0 })
    }
}
