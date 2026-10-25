@preconcurrency import AVFoundation
import CoreMedia
import Foundation
import NaturalLanguage
import os
import Speech

/// Listens to Music's audio with Apple's on-device speech recognition (SpeechAnalyzer, macOS 26)
/// so recognised sung words can be lined up with the lyric file. Audio and transcripts never leave
/// the Mac and are never logged.
@available(macOS 26.0, *)
actor VocalAligner: VocalListening {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "vocal-aligner")
    private static let timescale: CMTimeScale = 48_000

    private var analyzer: SpeechAnalyzer?
    private var inputs: AsyncStream<AnalyzerInput>.Continuation?
    private var analyzerFormat: AVAudioFormat?
    private var inputFormat: AVAudioFormat?
    private var converter: AVAudioConverter?
    /// Host time that analyzer time 0 corresponds to.
    private var epoch: TimeInterval?
    /// End of the last buffer fed, in analyzer time (inputs must never overlap).
    private var fedUntil: TimeInterval = 0
    private var resultsTask: Task<Void, Never>?

    /// Starts listening in the lyrics' language. Returns recognised words, or nil if unsupported.
    func start(languageSample: String) async -> AsyncStream<HeardAudioWord>? {
        await stop()
        guard SpeechTranscriber.isAvailable, let locale = await Self.locale(for: languageSample) else {
            Self.logger.info("Auto-sync unavailable for this song's language")
            return nil
        }
        // Fast results: words arrive within seconds instead of after long phrases.
        let transcriber = SpeechTranscriber(locale: locale, transcriptionOptions: [], reportingOptions: [.fastResults],
                                            attributeOptions: [.audioTimeRange])
        do {
            if let request = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
                try await request.downloadAndInstall()
            }
            guard let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber]) else { return nil }
            let analyzer = SpeechAnalyzer(modules: [transcriber])
            let (inputStream, inputs) = AsyncStream<AnalyzerInput>.makeStream()
            try await analyzer.start(inputSequence: inputStream)
            self.analyzer = analyzer
            self.inputs = inputs
            analyzerFormat = format
            epoch = nil
            fedUntil = 0
            converter = nil
            inputFormat = nil

            let (words, output) = AsyncStream<HeardAudioWord>.makeStream()
            resultsTask = Task { await self.forward(transcriber, to: output) }
            Self.logger.info("Listening for auto-sync (\(locale.identifier, privacy: .public))")
            return words
        } catch {
            Self.logger.error("Auto-sync could not start: \(String(describing: type(of: error)), privacy: .public)")
            return nil
        }
    }

    /// Lyrics fool language detection easily (slang, repetition, short lines), so try the likely
    /// languages in order and fall back to English. A wrong guess only means no auto-sync.
    private static func locale(for sample: String) async -> Locale? {
        let recognizer = NLLanguageRecognizer()
        recognizer.processString(sample)
        let likely = recognizer.languageHypotheses(withMaximum: 3)
            .filter { $0.value >= minimumLanguageConfidence }
            .sorted { $0.value > $1.value }
            .map(\.key.rawValue)
        for language in likely + ["en"] {
            if let locale = await SpeechTranscriber.supportedLocale(equivalentTo: Locale(identifier: language)) {
                return locale
            }
        }
        return nil
    }

    private static let minimumLanguageConfidence = 0.2

    private func forward(_ transcriber: SpeechTranscriber, to output: AsyncStream<HeardAudioWord>.Continuation) async {
        do {
            for try await result in transcriber.results {
                guard let epoch else { continue }
                for run in result.text.runs {
                    guard let range = run.audioTimeRange else { continue }
                    let text = String(result.text[run.range].characters).trimmingCharacters(in: .whitespacesAndNewlines)
                    if !text.isEmpty { output.yield(HeardAudioWord(text: text, hostTime: epoch + range.start.seconds)) }
                }
            }
        } catch is CancellationError {
            // Normal when the song changes or the app stops listening.
        } catch {
            let nsError = error as NSError
            Self.logger.error("Transcription stopped: \(nsError.domain, privacy: .public) \(nsError.code) \(nsError.localizedDescription, privacy: .public)")
        }
        output.finish()
    }

    func feed(_ chunk: AudioChunk) {
        guard let inputs, let analyzerFormat, let buffer = convert(chunk, to: analyzerFormat) else { return }
        let origin = epoch ?? chunk.hostTime
        epoch = origin
        // Follow real playback time, but the resampler can make a buffer a few samples longer
        // than its source; never let the next one start before the previous one ended.
        let start = max(chunk.hostTime - origin, fedUntil)
        fedUntil = start + Double(buffer.frameLength) / analyzerFormat.sampleRate
        inputs.yield(AnalyzerInput(buffer: buffer, bufferStartTime: CMTime(seconds: start, preferredTimescale: Self.timescale)))
    }

    func stop() async {
        inputs?.finish()
        inputs = nil
        if let analyzer { await analyzer.cancelAndFinishNow() }
        analyzer = nil
        resultsTask?.cancel()
        resultsTask = nil
        converter = nil
        epoch = nil
    }

    /// Resamples mono float audio to the recogniser's format (the converter keeps state across chunks).
    private func convert(_ chunk: AudioChunk, to format: AVAudioFormat) -> AVAudioPCMBuffer? {
        if inputFormat?.sampleRate != chunk.sampleRate {
            inputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: chunk.sampleRate, channels: 1, interleaved: false)
            converter = inputFormat.flatMap { AVAudioConverter(from: $0, to: format) }
        }
        guard let inputFormat, let converter, !chunk.samples.isEmpty,
              let input = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: AVAudioFrameCount(chunk.samples.count)),
              let channel = input.floatChannelData?[0]
        else { return nil }
        input.frameLength = AVAudioFrameCount(chunk.samples.count)
        chunk.samples.withUnsafeBufferPointer { channel.update(from: $0.baseAddress!, count: $0.count) }

        let capacity = AVAudioFrameCount(Double(chunk.samples.count) * format.sampleRate / chunk.sampleRate) + 64
        guard let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else { return nil }
        let supplied = SuppliedFlag()
        var error: NSError?
        converter.convert(to: output, error: &error) { _, status in
            if supplied.value {
                status.pointee = .noDataNow
                return nil
            }
            supplied.value = true
            status.pointee = .haveData
            return input
        }
        return error == nil && output.frameLength > 0 ? output : nil
    }
}

/// Mutable flag for AVAudioConverter's synchronous input block.
private final class SuppliedFlag: @unchecked Sendable {
    var value = false
}
