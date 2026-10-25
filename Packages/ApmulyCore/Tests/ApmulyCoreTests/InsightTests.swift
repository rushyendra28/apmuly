import Testing
@testable import ApmulyCore

// All line text below is made-up placeholder text.

@Suite("Emoji lexicon")
struct EmojiLexiconTests {
    @Test("Maps objects to emoji, including simple plurals", arguments: [
        ("moon", "🌙"), ("Moon", "🌙"), ("roses", "🌹"), ("cars", "🚗"), ("cities", "🏙️"), ("glasses", "👓"),
        ("pizza", "🍕"), ("whale", "🐳"), ("headphones", "🎧"), ("castle", "🏰"),
    ])
    func objectEmoji(word: String, expected: String) {
        #expect(EmojiLexicon.emoji(forObject: word) == expected)
    }

    @Test("Maps action words to emoji", arguments: [
        ("dance", "💃"), ("dancing", "💃"), ("cry", "😭"), ("crying", "😭"), ("fly", "🕊️"), ("sing", "🎤"),
    ])
    func actionEmoji(word: String, expected: String) {
        #expect(EmojiLexicon.emoji(forAction: word) == expected)
    }

    @Test("Has a large vocabulary")
    func vocabularySize() {
        #expect(EmojiLexicon.objectCount >= 250)
        #expect(EmojiLexicon.actionCount >= 30)
    }

    @Test("Unknown and abstract words have no object emoji", arguments: ["quantum", "time", "way", ""])
    func noEmoji(word: String) {
        #expect(EmojiLexicon.emoji(forObject: word) == nil)
    }

    @Test("Every emotion except neutral has emoji")
    func emotionEmoji() {
        for emotion in Emotion.allCases where emotion != .neutral {
            #expect(EmojiLexicon.emoji(for: emotion, variant: 0) != nil)
        }
        #expect(EmojiLexicon.emoji(for: .neutral, variant: 0) == nil)
    }

    @Test("Abstract words are recognised", arguments: ["time", "life", "feeling", "way"])
    func abstractWords(word: String) {
        #expect(EmojiLexicon.isAbstract(word))
    }
}

@Suite("Emotion lexicon")
struct EmotionLexiconTests {
    @Test("Keyword cues win over sentiment", arguments: [
        (["tears", "fall"], 0.5, Emotion.sadness),
        (["dance", "all", "night"], -0.2, Emotion.excitement),
        (["broken", "heart"], 0.0, Emotion.heartbreak),
        (["kiss", "me"], 0.0, Emotion.love),
    ])
    func keywords(words: [String], sentiment: Double, expected: Emotion) {
        #expect(EmotionLexicon.emotion(words: words, sentiment: sentiment) == expected)
    }

    @Test("Falls back to sentiment", arguments: [
        (0.8, Emotion.joy), (-0.7, Emotion.sadness), (0.1, Emotion.neutral),
    ])
    func sentiment(score: Double, expected: Emotion) {
        #expect(EmotionLexicon.emotion(words: ["plain", "words"], sentiment: score) == expected)
    }
}

@Suite("Insight planner")
struct InsightPlannerTests {
    private func line(_ start: Double, _ words: [String]) -> LyricLine {
        let ws = words.enumerated().map { i, w in LyricWord(text: w, start: start + Double(i), end: start + Double(i) + 1) }
        return LyricLine(start: start, end: start + Double(words.count), text: words.joined(separator: " "), words: ws)
    }

    @Test("Every expressive line gets an emotion emoji; a mood change also triggers a burst")
    func emotionEveryLine() {
        let doc = [line(0, ["a"]), line(2, ["b"]), line(4, ["c"]), line(6, ["d"])]
        let readings: [Int: LineReading] = [
            0: LineReading(emotion: .joy, objects: []),
            1: LineReading(emotion: .joy, objects: []),
            2: LineReading(emotion: .sadness, objects: []),
            3: LineReading(emotion: .neutral, objects: []),
        ]
        let insights = InsightPlanner.plan(lines: doc, readings: readings)
        #expect(insights[0]?.emoji != nil)
        #expect(insights[0]?.burst == true)
        #expect(insights[1]?.emoji != nil)
        #expect(insights[1]?.burst == false)
        #expect(insights[2]?.burst == true)
        #expect(insights[3]?.emoji == nil)
    }

    @Test("Objects get emoji and know the word they're sung on")
    func objectCues() {
        let doc = [line(10, ["under", "the", "moons", "and", "lighthouse"])]
        let insights = InsightPlanner.plan(lines: doc, readings: [0: LineReading(emotion: .neutral, objects: ["Moon", "lighthouse"])])
        let objects = insights[0]?.objects ?? []
        #expect(objects.map(\.word) == ["moon", "lighthouse"])
        #expect(objects[0].emoji == "🌙")
        #expect(objects[0].wordIndex == 2)
        #expect(objects[1].emoji == nil)
        #expect(objects[1].wordIndex == 4)
    }

    @Test("Words in the line add emoji even when the analyzer named nothing")
    func lineWordsAddEmoji() {
        let doc = [line(0, ["we", "dance", "under", "stars"])]
        let objects = InsightPlanner.plan(lines: doc, readings: [0: LineReading(emotion: .neutral, objects: [])])[0]?.objects ?? []
        #expect(objects.map(\.word) == ["dance", "star"])
        #expect(objects[0].emoji == "💃")
        #expect(objects[0].wordIndex == 1)
        #expect(objects[1].emoji == "⭐")
    }

    @Test("Drops abstract, invalid and duplicate objects, caps per line")
    func filtersObjects() {
        let doc = [line(0, ["x"])]
        let readings = [0: LineReading(emotion: .neutral, objects: ["time", "car", "car", "rose", "sun", "moon", "<script>", ""])]
        let objects = InsightPlanner.plan(lines: doc, readings: readings)[0]?.objects ?? []
        #expect(objects.map(\.word) == ["car", "rose", "sun"])
    }

    @Test("The same object skips only the very next line")
    func objectCooldown() {
        let doc = [line(0, ["a"]), line(2, ["b"]), line(4, ["c"])]
        let readings = Dictionary(uniqueKeysWithValues: (0..<3).map { ($0, LineReading(emotion: .neutral, objects: ["star"])) })
        let insights = InsightPlanner.plan(lines: doc, readings: readings)
        #expect(insights[0]?.objects.first?.word == "star")
        #expect(insights[1]?.objects.isEmpty ?? true)
        #expect(insights[2]?.objects.first?.word == "star")
    }

    @Test("Gap lines and missing readings produce nothing")
    func gapsIgnored() {
        let gaps = [LyricLine(start: 0, end: 4, text: "", words: []), LyricLine(start: 4, end: 8, text: "", words: [])]
        #expect(InsightPlanner.plan(lines: gaps, readings: [0: LineReading(emotion: .joy, objects: ["sun"])]).isEmpty)
    }
}
