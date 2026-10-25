import Testing
@testable import ApmulyCore

// All words below are made-up placeholder text.

private func doc(_ lines: [(Double, [String])], wordGap: Double = 0.5) -> LyricsDocument {
    let built = lines.map { start, words in
        LyricLine(start: start, end: start + Double(words.count) * wordGap, text: words.joined(separator: " "),
                  words: words.enumerated().map { i, w in
                      LyricWord(text: w, start: start + Double(i) * wordGap, end: start + Double(i + 1) * wordGap)
                  })
    }
    return LyricsDocument(lines: built, timing: .lineSynced)
}

@Suite("Lyric matcher")
struct LyricMatcherTests {
    private let lyrics = doc([
        (10, ["glimmer", "falcon", "harbor", "velvet"]),
        (20, ["copper", "meadow", "lantern", "falcon"]),
    ])

    @Test("Matches a heard run of words to the lyric words, giving the offset per word")
    func matchesSequence() {
        let heard = [
            HeardWord(text: "Glimmer", position: 10.4),
            HeardWord(text: "falcon", position: 10.9),
            HeardWord(text: "harbor,", position: 11.4),
        ]
        let matches = LyricMatcher.match(heard: heard, lyrics: lyrics)
        #expect(matches.count == 3)
        #expect(matches.allSatisfy { abs(($0.heardTime - $0.lyricTime) - 0.4) < 0.0001 })
    }

    @Test("An isolated short word with no neighbouring agreement is ignored")
    func isolatedShortWordIgnored() {
        let short = doc([(30, ["mint", "plume", "dusk"])])
        let heard = [HeardWord(text: "plume", position: 30.7), HeardWord(text: "zzz", position: 31.0)]
        #expect(LyricMatcher.match(heard: heard, lyrics: short).isEmpty)
    }

    @Test("A long, distinctive word matches on its own when it's the only one nearby")
    func distinctiveWordMatchesAlone() {
        let heard = [HeardWord(text: "lantern", position: 21.3)]
        #expect(LyricMatcher.match(heard: heard, lyrics: lyrics).map(\.lyricTime) == [21.0])
    }

    @Test("Neighbour agreement tolerates one missed word in between")
    func toleratesGap() {
        let short = doc([(30, ["mint", "plume", "dusk", "fern"])])
        // "dusk" wasn't recognised; "mint" and "fern"... are two apart in both sequences.
        let heard = [HeardWord(text: "plume", position: 30.6), HeardWord(text: "fern", position: 31.6)]
        #expect(LyricMatcher.match(heard: heard, lyrics: short).count == 2)
    }

    @Test("Filler words never anchor a match")
    func stopWordsIgnored() {
        let filler = doc([(5, ["the", "and", "you", "the", "and"])])
        let heard = [HeardWord(text: "the", position: 5.1), HeardWord(text: "and", position: 5.6)]
        #expect(LyricMatcher.match(heard: heard, lyrics: filler).isEmpty)
    }

    @Test("Words far outside the search window don't match")
    func outsideWindow() {
        let heard = [HeardWord(text: "copper", position: 60), HeardWord(text: "meadow", position: 60.5)]
        #expect(LyricMatcher.match(heard: heard, lyrics: lyrics).isEmpty)
    }

    @Test("Chooses the nearest occurrence of a repeated word")
    func nearestOccurrence() {
        let heard = [HeardWord(text: "lantern", position: 21.2), HeardWord(text: "falcon", position: 21.7)]
        let matches = LyricMatcher.match(heard: heard, lyrics: lyrics)
        #expect(matches.map(\.lyricTime) == [21.0, 21.5])
    }
}

@Suite("Sync calibrator")
struct SyncCalibratorTests {
    private func matches(offset: Double, at times: [Double], jitter: [Double] = []) -> [SyncMatch] {
        times.enumerated().map { i, t in
            SyncMatch(lyricTime: t, heardTime: t + offset + (jitter.isEmpty ? 0 : jitter[i % jitter.count]))
        }
    }

    @Test("Finds a constant offset despite jitter")
    func constantOffset() throws {
        let estimate = try #require(SyncCalibrator.estimate(matches(offset: 0.4, at: Array(stride(from: 10.0, to: 40, by: 3)),
                                                                     jitter: [0.03, -0.04, 0.01, -0.02])))
        #expect(abs(estimate.offset(at: 20) - 0.4) < 0.05)
        #expect(estimate.matchCount == 10)
    }

    @Test("Wrong matches (outliers) don't drag the estimate")
    func outliers() throws {
        let good = matches(offset: -0.3, at: Array(stride(from: 10.0, to: 40, by: 3)))
        let bad = [SyncMatch(lyricTime: 15, heardTime: 17.5), SyncMatch(lyricTime: 25, heardTime: 22)]
        let estimate = try #require(SyncCalibrator.estimate(good + bad))
        #expect(abs(estimate.offset(at: 20) + 0.3) < 0.05)
    }

    @Test("Finds the true offset even when over half the matches are misheard words")
    func manyOutliers() throws {
        let good = matches(offset: -0.27, at: Array(stride(from: 10.0, to: 130, by: 10)), jitter: [0.03, -0.02, 0.0, -0.03])
        let scattered = [2.1, -1.8, 0.9, -2.4, 1.5, -0.9, 2.6, -1.3, 1.1, -2.0, 0.6, 1.9, -2.7, 2.3]
        let bad = scattered.enumerated().map { i, o in SyncMatch(lyricTime: 12 + Double(i) * 8.5, heardTime: 12 + Double(i) * 8.5 + o) }
        let estimate = try #require(SyncCalibrator.estimate(good + bad))
        #expect(abs(estimate.globalOffset + 0.27) < 0.05)
        #expect(abs(estimate.offset(at: 70) + 0.27) < 0.06)
        #expect(estimate.matchCount == 12)
    }

    @Test("Too few or too scattered matches give no estimate")
    func insufficient() {
        #expect(SyncCalibrator.estimate(matches(offset: 0.4, at: [10, 13])) == nil)
        let scattered = [0.0, 1.2, -0.9, 0.8, -1.1, 0.3].enumerated().map { i, o in
            SyncMatch(lyricTime: Double(i * 5), heardTime: Double(i * 5) + o)
        }
        #expect(SyncCalibrator.estimate(scattered) == nil)
    }

    @Test("Follows drift between sections of the song")
    func localDrift() throws {
        let early = matches(offset: 0.2, at: Array(stride(from: 10.0, to: 60, by: 4)))
        let late = matches(offset: 0.7, at: Array(stride(from: 120.0, to: 170, by: 4)))
        let estimate = try #require(SyncCalibrator.estimate(early + late))
        #expect(abs(estimate.offset(at: 30) - 0.2) < 0.05)
        #expect(abs(estimate.offset(at: 150) - 0.7) < 0.05)
    }

    @Test("Absurd offsets are rejected")
    func absurd() {
        #expect(SyncCalibrator.estimate(matches(offset: 9, at: Array(stride(from: 10.0, to: 40, by: 3)))) == nil)
    }
}

@Suite("Line decoration")
struct LineDecorationTests {
    @Test("Emotion emoji first, then unique object emoji, capped")
    func decoration() {
        let insight = LineInsight(emoji: "😍", burst: false, objects: [
            ObjectCue(word: "moon", emoji: "🌙", wordIndex: 0),
            ObjectCue(word: "lighthouse", emoji: nil, wordIndex: 1),
            ObjectCue(word: "star", emoji: "⭐", wordIndex: 2),
            ObjectCue(word: "stars", emoji: "⭐", wordIndex: 3),
        ])
        #expect(LineDecoration.emoji(for: insight) == "😍 🌙 ⭐")
        #expect(LineDecoration.emoji(for: nil) == "")
    }
}
