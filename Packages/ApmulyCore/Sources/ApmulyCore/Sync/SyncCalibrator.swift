import Foundation

/// How far the lyric file's timestamps are from when the words are actually sung.
/// Positive offset = the singer is later than the file says (lyrics should show later).
public struct SyncEstimate: Sendable, Equatable {
    /// Robust (median) offset across the whole song.
    public let globalOffset: TimeInterval
    /// Number of agreeing matches the estimate is based on.
    public let matchCount: Int
    let times: [TimeInterval]
    let offsets: [TimeInterval]

    /// Smoothly varying local offset, so drift between song sections is followed without jumps.
    public func offset(at position: TimeInterval) -> TimeInterval {
        let sigma = SyncCalibrator.localSigma
        var weightSum = 0.0
        var weighted = 0.0
        for (t, o) in zip(times, offsets) {
            let d = (t - position) / sigma
            let w = exp(-0.5 * d * d)
            weightSum += w
            weighted += w * o
        }
        guard weightSum > 0 else { return globalOffset }
        let blend = weightSum / (weightSum + SyncCalibrator.globalPrior)
        return blend * (weighted / weightSum) + (1 - blend) * globalOffset
    }
}

/// Turns word matches into a timing correction.
///
/// Speech recognition of sung vocals mishears a lot, so many matches are wrong. Wrong matches
/// scatter widely while correct ones agree tightly, so each match is judged against the densest
/// cluster of offsets *near it in the song* (which also follows gradual drift between sections).
public enum SyncCalibrator {
    public static let minimumMatches = 5
    /// At least this share of matches must agree, so a few lucky coincidences can't decide.
    public static let minimumInlierFraction = 0.25
    /// Offsets beyond this are a wrong song/file, not a timing error.
    public static let maxOffset: TimeInterval = 3
    /// Matches within this many seconds of song time are compared with each other.
    static let localWindow: TimeInterval = 20
    /// Offsets this close (±) count as agreeing.
    static let clusterHalfWidth: TimeInterval = 0.12
    /// A match is trusted when it's this close to its neighbourhood's agreed offset.
    static let inlierBand: TimeInterval = 0.15
    static let localSigma: TimeInterval = 15
    static let globalPrior = 0.5

    public static func estimate(_ matches: [SyncMatch]) -> SyncEstimate? {
        guard matches.count >= minimumMatches else { return nil }
        let offsets = matches.map { $0.heardTime - $0.lyricTime }
        let inliers = matches.indices.filter { i in
            let local = matches.indices
                .filter { abs(matches[$0].lyricTime - matches[i].lyricTime) <= localWindow }
                .map { offsets[$0] }
            return abs(offsets[i] - densestCenter(local)) <= inlierBand
        }
        let required = max(minimumMatches, Int((Double(matches.count) * minimumInlierFraction).rounded(.up)))
        guard inliers.count >= required else { return nil }

        let inlierOffsets = inliers.map { offsets[$0] }
        let center = median(inlierOffsets)
        guard abs(center) <= maxOffset else { return nil }
        return SyncEstimate(globalOffset: center, matchCount: inliers.count,
                            times: inliers.map { matches[$0].lyricTime }, offsets: inlierOffsets)
    }

    /// Median of the largest group of values that all lie within ±`clusterHalfWidth` of each other.
    static func densestCenter(_ values: [Double]) -> Double {
        let sorted = values.sorted()
        guard !sorted.isEmpty else { return 0 }
        var best = (count: 0, low: 0, high: 0)
        var low = 0
        for high in sorted.indices {
            while sorted[high] - sorted[low] > 2 * clusterHalfWidth { low += 1 }
            if high - low + 1 > best.count { best = (high - low + 1, low, high) }
        }
        return median(Array(sorted[best.low...best.high]))
    }

    static func median(_ values: [Double]) -> Double {
        let sorted = values.sorted()
        guard !sorted.isEmpty else { return 0 }
        let mid = sorted.count / 2
        return sorted.count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid]
    }
}
