@preconcurrency import AVFoundation
import Foundation
import NaturalLanguage
import os
import Speech

/// Auto-sync listener for macOS 14.2–15, using SFSpeechRecognizer. On-device recognition only, so
/// audio never leaves the Mac. That API only reports word timestamps when a request finishes, so
/// audio is recognised in short back-to-back requests.
@available(macOS 14.2, *)
actor LegacyVocalAligner: VocalListening {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "vocal-aligner")
    /// Length of each recognition request (shorter = sooner results, more words split at boundaries).
    private static let requestSeconds: TimeInterval = 12
    /// A jump in the audio timeline bigger than this starts a fresh request.
    private static let maxGap: TimeInterval = 0.5

    private struct Segment: Sendable {
        let text: String
        let offset: TimeInterval
    }

    private var recognizer: SFSpeechRecognizer?
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var task: SFSpeechRecognitionTask?
    /// Host time of the current request's first sample, and how much audio it has received.
    private var requestStart: TimeInterval = 0
    private var requestFed: TimeInterval = 0
    private var output: AsyncStream<HeardAudioWord>.Continuation?

    func start(languageSample: String) async -> AsyncStream<HeardAudioWord>? {
        stop()
        guard await Self.authorized(), let recognizer = Self.recognizer(for: languageSample) else {
            Self.logger.info("Auto-sync unavailable (speech recognition not allowed or not on-device for this language)")
            return nil
        }
        self.recognizer = recognizer
        var continuation: AsyncStream<HeardAudioWord>.Continuation?
        let words = AsyncStream<HeardAudioWord> { continuation = $0 }
        output = continuation
        Self.logger.info("Listening for auto-sync (\(recognizer.locale.identifier, privacy: .public), legacy)")
        return words
    }

    func feed(_ chunk: AudioChunk) {
        guard recognizer != nil, !chunk.samples.isEmpty else { return }
        let expected = requestStart + requestFed
        if request == nil || requestFed >= Self.requestSeconds || abs(chunk.hostTime - expected) > Self.maxGap {
            beginRequest(at: chunk.hostTime)
        }
        guard let request, let buffer = Self.buffer(from: chunk) else { return }
        request.append(buffer)
        requestFed += Double(chunk.samples.count) / chunk.sampleRate
    }

    func stop() {
        task?.cancel()
        request?.endAudio()
        task = nil
        request = nil
        recognizer = nil
        output?.finish()
        output = nil
    }

    /// Ends the current request (its words arrive shortly) and starts the next one.
    private func beginRequest(at hostTime: TimeInterval) {
        request?.endAudio()
        guard let recognizer else { return }
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.requiresOnDeviceRecognition = true
        request.shouldReportPartialResults = false
        let start = hostTime
        task = recognizer.recognitionTask(with: request) { [weak self] result, _ in
            guard let result, result.isFinal else { return }
            let segments = result.bestTranscription.segments.map { Segment(text: $0.substring, offset: $0.timestamp) }
            Task { await self?.emit(segments, from: start) }
        }
        self.request = request
        requestStart = start
        requestFed = 0
    }

    private func emit(_ segments: [Segment], from start: TimeInterval) {
        segments.forEach { output?.yield(HeardAudioWord(text: $0.text, hostTime: start + $0.offset)) }
    }

    private static func buffer(from chunk: AudioChunk) -> AVAudioPCMBuffer? {
        guard let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: chunk.sampleRate, channels: 1, interleaved: false),
              let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(chunk.samples.count)),
              let channel = buffer.floatChannelData?[0]
        else { return nil }
        buffer.frameLength = AVAudioFrameCount(chunk.samples.count)
        chunk.samples.withUnsafeBufferPointer { channel.update(from: $0.baseAddress!, count: $0.count) }
        return buffer
    }

    private static func authorized() async -> Bool {
        switch SFSpeechRecognizer.authorizationStatus() {
        case .authorized: return true
        case .notDetermined:
            return await withCheckedContinuation { continuation in
                SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0 == .authorized) }
            }
        default: return false
        }
    }

    /// A recogniser for the lyrics' likely language that can run on-device, else English.
    private static func recognizer(for sample: String) -> SFSpeechRecognizer? {
        let languageRecognizer = NLLanguageRecognizer()
        languageRecognizer.processString(sample)
        let likely = languageRecognizer.languageHypotheses(withMaximum: 3)
            .filter { $0.value >= 0.2 }
            .sorted { $0.value > $1.value }
            .map(\.key.rawValue)
        for language in likely + ["en-US"] {
            if let recognizer = SFSpeechRecognizer(locale: Locale(identifier: language)),
               recognizer.supportsOnDeviceRecognition, recognizer.isAvailable {
                return recognizer
            }
        }
        return nil
    }
}
