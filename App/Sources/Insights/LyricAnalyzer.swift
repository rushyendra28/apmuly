import ApmulyCore
import Foundation
import FoundationModels
import NaturalLanguage
import os

@available(macOS 26.0, *)
@Generable
enum LineEmotion: String {
    case joy, love, sadness, anger, excitement, calm, fear, confidence, heartbreak, longing, neutral
}

@available(macOS 26.0, *)
@Generable
struct LineLabel {
    @Guide(description: "The number of the line, exactly as given")
    var index: Int
    @Guide(description: "The main emotion or expression of the line")
    var emotion: LineEmotion
    @Guide(description: "Up to three concrete, physical, picturable things named in the line, e.g. moon, car, rose. Singular lowercase nouns. Empty if none.")
    var objects: [String]
}

@available(macOS 26.0, *)
@Generable
struct LineLabels {
    @Guide(description: "One entry per numbered line")
    var lines: [LineLabel]
}

/// Reads each lyric line's emotion and the concrete things it mentions — entirely on device.
/// Uses Apple's on-device language model when available (macOS 26 + Apple Intelligence), else
/// NaturalLanguage sentiment + keywords (macOS 12+).
/// Lyric text never leaves the Mac and is never logged.
actor LyricAnalyzer {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "lyric-analyzer")
    private static let chunkSize = 10
    private static let instructions = """
        You annotate song lyric lines for a music visualizer. For each numbered line, give its dominant \
        emotion and up to three concrete, physical objects it names that could be shown as a picture. \
        Never quote, continue or rewrite the lines.
        """

    /// Streams readings chunk by chunk (in song order) so visuals appear before the whole song is done.
    nonisolated func readings(for document: LyricsDocument) -> AsyncStream<[Int: LineReading]> {
        AsyncStream { continuation in
            let task = Task {
                await self.analyze(document) { continuation.yield($0) }
                continuation.finish()
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    private func analyze(_ document: LyricsDocument, emit: ([Int: LineReading]) -> Void) async {
        let sung = document.lines.enumerated().filter { !$0.element.isGap }.map { (index: $0.offset, line: $0.element) }
        let useModel = Self.modelAvailable
        var modelFailures = 0

        for start in stride(from: 0, to: sung.count, by: Self.chunkSize) {
            if Task.isCancelled { return }
            let chunk = Array(sung[start..<min(start + Self.chunkSize, sung.count)])
            var readings = [Int: LineReading]()
            if useModel, #available(macOS 26.0, *) {
                do {
                    readings = try await modelReadings(chunk)
                } catch {
                    modelFailures += 1
                    Self.logger.info("On-device model skipped a chunk (\(String(describing: type(of: error)), privacy: .public)); using fallback")
                }
            }
            for item in chunk where readings[item.index] == nil {
                readings[item.index] = Self.fallbackReading(item.line)
            }
            emit(readings)
        }
        Self.logger.info("Analyzed \(sung.count) lines (model: \(useModel), fallback chunks: \(modelFailures))")
    }

    private static var modelAvailable: Bool {
        guard #available(macOS 26.0, *) else { return false }
        return SystemLanguageModel.default.isAvailable
    }

    @available(macOS 26.0, *)
    private func modelReadings(_ chunk: [(index: Int, line: LyricLine)]) async throws -> [Int: LineReading] {
        let session = LanguageModelSession(instructions: Self.instructions)
        let prompt = "Lines:\n" + chunk.map { "\($0.index): \($0.line.text)" }.joined(separator: "\n")
        let response = try await session.respond(to: prompt, generating: LineLabels.self,
                                                 options: GenerationOptions(temperature: 0))
        let byIndex = Dictionary(chunk.map { ($0.index, $0.line) }, uniquingKeysWith: { first, _ in first })
        return response.content.lines.reduce(into: [Int: LineReading]()) { result, label in
            guard let line = byIndex[label.index] else { return }
            let emotion = Emotion(rawValue: label.emotion.rawValue) ?? .neutral
            // Keep only objects actually named in the line (guards against invented ones).
            let words = Set(line.words.map { EmojiLexicon.canonical($0.text) })
            let objects = label.objects.filter { words.contains(EmojiLexicon.canonical($0)) }
            result[label.index] = LineReading(emotion: emotion, objects: objects)
        }
    }

    /// Sentiment + keyword emotion, and only nouns that have an emoji (no web photos from guesses).
    static func fallbackReading(_ line: LyricLine) -> LineReading {
        let text = line.text
        let tagger = NLTagger(tagSchemes: [.sentimentScore, .lexicalClass])
        tagger.string = text
        let (score, _) = tagger.tag(at: text.startIndex, unit: .paragraph, scheme: .sentimentScore)
        var nouns: [String] = []
        tagger.enumerateTags(in: text.startIndex..<text.endIndex, unit: .word, scheme: .lexicalClass,
                             options: [.omitWhitespace, .omitPunctuation]) { tag, range in
            if tag == .noun, EmojiLexicon.emoji(forObject: String(text[range])) != nil { nouns.append(String(text[range])) }
            return true
        }
        let emotion = EmotionLexicon.emotion(words: line.words.map(\.text), sentiment: Double(score?.rawValue ?? "0") ?? 0)
        return LineReading(emotion: emotion, objects: nouns)
    }
}
