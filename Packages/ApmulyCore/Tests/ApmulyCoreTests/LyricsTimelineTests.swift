import Testing
@testable import ApmulyCore

@Suite("Lyrics timeline")
struct LyricsTimelineTests {
    private func document(_ lrc: String, duration: Double = 200) -> LyricsDocument {
        LyricsTimeline.build(from: LRCParser.parse(lrc), trackDuration: duration)
    }

    @Test("Line end is the next line's start")
    func lineEndsAtNextStart() {
        let doc = document("[00:01.00]one two\n[00:04.00]three\n[00:09.00]four")
        #expect(doc.lines.map(\.end).prefix(2) == [4, 9])
    }

    @Test("Last line ends within a cap or at track end")
    func lastLineIsCapped() {
        let long = document("[00:10.00]last", duration: 200)
        #expect(long.lines.last?.end == 10 + LyricsTimeline.maxLastLineDuration)
        let short = document("[00:10.00]last", duration: 12)
        #expect(short.lines.last?.end == 12)
    }

    @Test("Estimated words are contiguous, ordered and stay within the line")
    func estimatedWordsStayInLine() {
        let doc = document("[00:02.00]tiny extraordinary word\n[00:06.00]next")
        let words = doc.lines[0].words
        #expect(words.map(\.text) == ["tiny", "extraordinary", "word"])
        #expect(words.first?.start == 2)
        #expect(words.allSatisfy { $0.end > $0.start && $0.start >= 2 && $0.end <= 6 })
        for (a, b) in zip(words, words.dropFirst()) { #expect(abs(a.end - b.start) < 0.0001) }
        // Longer words get more time
        #expect(words[1].end - words[1].start > words[0].end - words[0].start)
        #expect(doc.timing == .lineSynced)
    }

    @Test("Long gaps do not stretch the sung span of a short line")
    func longGapDoesNotStretchWords() {
        let doc = document("[00:00.00]go\n[00:30.00]next")
        let lastWordEnd = doc.lines[0].words.last?.end ?? 0
        #expect(lastWordEnd < 3)
    }

    @Test("Enhanced word timings are used verbatim")
    func usesEnhancedWords() {
        let doc = document("[00:01.00]<00:01.00>alpha <00:01.60>beta\n[00:03.00]next")
        #expect(doc.lines[0].words.map(\.start) == [1.0, 1.6])
        #expect(doc.lines[0].words.map(\.end) == [1.6, 3.0])
        #expect(doc.timing == .wordSynced)
    }

    @Test("The last enhanced word does not stretch across a long gap")
    func enhancedLastWordCapped() {
        let doc = document("[00:01.00]<00:01.00>alpha <00:01.60>go\n[00:40.00]next")
        #expect((doc.lines[0].words.last?.end ?? 99) < 3)
    }

    @Test("Finds the active line by position", arguments: [
        (0.5, nil as Int?),
        (1.0, 0),
        (3.99, 0),
        (4.0, 1),
        (100.0, 2),
    ])
    func findsActiveLine(position: Double, expected: Int?) {
        let doc = document("[00:01.00]a\n[00:04.00]b\n[00:09.00]c")
        #expect(doc.lineIndex(at: position) == expected)
    }

    @Test("Line progress and active word")
    func progressAndWord() {
        let doc = document("[00:02.00]alpha beta\n[00:06.00]next")
        let line = doc.lines[0]
        #expect(line.progress(at: 0) == 0)
        #expect(line.progress(at: 4) == 0.5)
        #expect(line.progress(at: 99) == 1)
        #expect(line.wordIndex(at: 1.9) == nil)
        #expect(line.wordIndex(at: 2.0) == 0)
        #expect(line.wordIndex(at: line.words[1].start) == 1)
    }

    @Test("Gap lines have no words")
    func gapLinesHaveNoWords() {
        let doc = document("[00:01.00]a\n[00:05.00]\n[00:09.00]b")
        #expect(doc.lines[1].isGap)
        #expect(doc.lines[1].words.isEmpty)
    }

    @Test("Plain lyrics become an unsynced document")
    func plainLyrics() {
        let doc = LyricsTimeline.unsynced(plainText: "one\n\ntwo\n", trackDuration: 100)
        #expect(doc.timing == .unsynced)
        #expect(doc.lines.map(\.text) == ["one", "two"])
    }
}

@Suite("Syllable estimate")
struct SyllableTests {
    @Test("Approximates syllables", arguments: [
        ("go", 1), ("hello", 2), ("extraordinary", 5), ("time", 1), ("rhythm", 1), ("", 1),
    ])
    func syllables(word: String, expected: Int) {
        #expect(WordTimingEstimator.syllables(in: word) == expected)
    }

    @Test("Non-Latin text gets a positive weight")
    func nonLatinWeight() {
        #expect(WordTimingEstimator.syllables(in: "こんにちは") >= 2)
    }
}
