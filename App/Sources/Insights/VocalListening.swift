import Foundation

/// A word recognised in Music's audio and when it played (systemUptime seconds).
struct HeardAudioWord: Sendable {
    let text: String
    let hostTime: TimeInterval
}

/// Listens to Music's audio for the singer's words (on-device speech recognition).
protocol VocalListening: Sendable {
    /// Starts listening in the lyrics' language. Returns recognised words, or nil if unavailable.
    func start(languageSample: String) async -> AsyncStream<HeardAudioWord>?
    func feed(_ chunk: AudioChunk) async
    func stop() async
}

enum VocalListeners {
    /// The best listener this Mac supports, or nil (audio capture needs macOS 14.2+).
    static func make() -> (any VocalListening)? {
        if #available(macOS 26.0, *) { return VocalAligner() }
        if #available(macOS 14.2, *) { return LegacyVocalAligner() }
        return nil
    }
}
