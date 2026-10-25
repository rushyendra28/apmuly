import Foundation

/// A local playback clock, anchored to Music's reported position and
/// extrapolated between (relatively expensive) Apple Event polls.
public struct PlaybackClock: Sendable, Equatable {
    /// Errors larger than this are treated as seeks and applied immediately.
    public static let snapThreshold: TimeInterval = 0.75
    /// Fraction of a small error corrected per sample — hides jitter in Music's position.
    public static let correctionGain = 0.3
    /// Samples whose Apple Event round-trip took longer than this are too imprecise to use.
    public static let maxUsableLatency: TimeInterval = 0.12

    public let anchorPosition: TimeInterval
    public let anchorHostTime: TimeInterval
    public let isRunning: Bool

    public init(position: TimeInterval, hostTime: TimeInterval, isRunning: Bool) {
        anchorPosition = position
        anchorHostTime = hostTime
        self.isRunning = isRunning
    }

    public func position(at hostTime: TimeInterval) -> TimeInterval {
        isRunning ? anchorPosition + (hostTime - anchorHostTime) : anchorPosition
    }

    /// Returns a clock corrected towards a new observation.
    public func correcting(position observed: TimeInterval, at hostTime: TimeInterval,
                           latency: TimeInterval, isRunning running: Bool) -> PlaybackClock {
        let snapped = PlaybackClock(position: observed, hostTime: hostTime, isRunning: running)
        guard running == isRunning, running else { return snapped }

        let predicted = position(at: hostTime)
        let error = observed - predicted
        // Seeks always apply; only fine corrections need a precise (fast) sample.
        guard abs(error) <= Self.snapThreshold else { return snapped }
        guard latency <= Self.maxUsableLatency else { return self }
        return PlaybackClock(position: predicted + error * Self.correctionGain, hostTime: hostTime, isRunning: true)
    }

    /// Same position at `hostTime`, but with the given running state.
    public func with(isRunning running: Bool, at hostTime: TimeInterval) -> PlaybackClock {
        PlaybackClock(position: position(at: hostTime), hostTime: hostTime, isRunning: running)
    }
}
