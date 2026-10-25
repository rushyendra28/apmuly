import Testing
@testable import ApmulyCore

@Suite("LRC parser")
struct LRCParserTests {
    @Test("Parses basic timestamped lines in order")
    func parsesBasicLines() {
        let lrc = """
        [00:01.50]first placeholder line
        [00:04.25]second placeholder line
        """
        let parsed = LRCParser.parse(lrc)
        #expect(parsed.entries.map(\.start) == [1.5, 4.25])
        #expect(parsed.entries.map(\.text) == ["first placeholder line", "second placeholder line"])
    }

    @Test("Supports mm:ss, two- and three-digit fractions", arguments: [
        ("[01:02]x", 62.0),
        ("[01:02.5]x", 62.5),
        ("[01:02.05]x", 62.05),
        ("[01:02.123]x", 62.123),
        ("[10:00.00]x", 600.0),
    ])
    func parsesTimestampFormats(line: String, expected: Double) {
        let entry = LRCParser.parse(line).entries.first
        #expect(entry.map { abs($0.start - expected) < 0.0001 } == true)
    }

    @Test("Expands repeated timestamps on one line and sorts the result")
    func expandsRepeatedTimestamps() {
        let lrc = """
        [00:10.00][00:30.00]repeated hook
        [00:20.00]verse
        """
        let parsed = LRCParser.parse(lrc)
        #expect(parsed.entries.map(\.start) == [10, 20, 30])
        #expect(parsed.entries.map(\.text) == ["repeated hook", "verse", "repeated hook"])
    }

    @Test("Ignores metadata tags but applies [offset:] (positive offset = lyrics earlier)")
    func appliesOffsetTag() {
        let lrc = """
        [ar:Someone]
        [ti:Something]
        [offset:+500]
        [00:02.00]line
        """
        let parsed = LRCParser.parse(lrc)
        #expect(parsed.entries.count == 1)
        #expect(parsed.entries[0].start == 1.5)
    }

    @Test("Keeps empty timestamped lines as gaps")
    func keepsGapLines() {
        let parsed = LRCParser.parse("[00:01.00]a\n[00:05.00]\n[00:09.00]b")
        #expect(parsed.entries.map(\.text) == ["a", "", "b"])
    }

    @Test("Skips garbage and untimed lines, handles CRLF and whitespace")
    func skipsGarbage() {
        let parsed = LRCParser.parse("hello\r\n[xx:yy]bad\r\n  [00:03.00]  spaced text  \r\n")
        #expect(parsed.entries.count == 1)
        #expect(parsed.entries[0].text == "spaced text")
    }

    @Test("Parses enhanced word timestamps")
    func parsesEnhancedWords() {
        let parsed = LRCParser.parse("[00:01.00]<00:01.00>alpha <00:01.60>beta <00:02.40>gamma")
        let entry = parsed.entries[0]
        #expect(entry.text == "alpha beta gamma")
        #expect(entry.wordStarts?.map(\.text) == ["alpha", "beta", "gamma"])
        #expect(entry.wordStarts?.map(\.start) == [1.0, 1.6, 2.4])
    }

    @Test("Never produces negative times after offset")
    func clampsNegativeTimes() {
        let parsed = LRCParser.parse("[offset:+3000]\n[00:01.00]early")
        #expect(parsed.entries[0].start == 0)
    }
}
