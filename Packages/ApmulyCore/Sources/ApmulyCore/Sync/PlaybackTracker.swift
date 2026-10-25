import Foundation

public enum PlayerState: String, Sendable, Equatable {
    case playing, paused, stopped
}

/// One poll of Music's player.
public struct PlaybackSample: Sendable, Equatable {
    public let trackID: String
    public let position: TimeInterval
    /// 0 when unknown.
    public let duration: TimeInterval
    public let state: PlayerState
    /// Monotonic seconds at the midpoint of the Apple Event round-trip.
    public let hostTime: TimeInterval
    public let latency: TimeInterval

    public init(trackID: String, position: TimeInterval, duration: TimeInterval,
                state: PlayerState, hostTime: TimeInterval, latency: TimeInterval) {
        self.trackID = trackID
        self.position = position
        self.duration = duration
        self.state = state
        self.hostTime = hostTime
        self.latency = latency
    }
}

public struct Deck: Sendable, Equatable {
    public let trackID: String
    /// 0 when unknown.
    public let duration: TimeInterval
    public let clock: PlaybackClock
    /// Host time this deck became current (drives the fade-in).
    public let since: TimeInterval

    var hasKnownDuration: Bool { duration > 0 }
}

public struct OutgoingDeck: Sendable, Equatable {
    public let deck: Deck
    public let fadeStart: TimeInterval
    public let fadeEnd: TimeInterval
}

/// What the renderer needs for one deck on one frame.
public struct DeckFrame: Sendable, Equatable {
    public let trackID: String
    public let position: TimeInterval
    /// Whether the position is advancing (lets renderers extrapolate between updates).
    public let isRunning: Bool
    /// 0…1, linear; renderers apply their own easing.
    public let opacity: Double
}

public struct PlaybackFrame: Sendable, Equatable {
    public let current: DeckFrame?
    public let outgoing: DeckFrame?
}

/// A backwards jump near a track's end that may be an in-progress transition.
struct PendingJump: Sendable, Equatable {
    /// When the first suspicious sample arrived.
    let since: TimeInterval
    /// The most recent suspicious sample.
    let position: TimeInterval
    let hostTime: TimeInterval
}

/// Follows Music's player across seeks, pauses and AutoMix / crossfade transitions.
///
/// Measured behaviour this handles: during AutoMix, Music reports the switch mid-blend
/// (old track ~1.6s from its end, new track already ~1.7s in), and for one poll the
/// position flips to the new track while the track id is still the old one.
public struct PlaybackTracker: Sendable, Equatable {
    /// A backwards jump this close to the end of a track may be a transition in progress.
    public static let transitionEdge: TimeInterval = 20
    /// Longest a suspected-transition jump is held before being accepted as a seek.
    public static let jumpHoldTime: TimeInterval = 1.5
    /// Longest the outgoing track keeps running after a switch.
    public static let maxOverlap: TimeInterval = 12
    /// An incoming track already this far in means Music is blending; a manual skip starts near 0.
    public static let blendEvidence: TimeInterval = 1.0
    /// Fade for a manual skip (old track has stopped).
    public static let quickFade: TimeInterval = 0.6
    public static let fadeIn: TimeInterval = 0.8
    private static let minimumOverlap: TimeInterval = 0.25
    /// Two held samples this far apart that advance in step confirm a real seek.
    private static let confirmGap: TimeInterval = 0.3
    private static let linearTolerance: TimeInterval = 0.3

    public let current: Deck?
    public let outgoing: OutgoingDeck?
    let pendingJump: PendingJump?
    /// Host time of the last sample applied; older samples are stale.
    let lastHostTime: TimeInterval?

    public init() {
        self.init(current: nil, outgoing: nil, pendingJump: nil, lastHostTime: nil)
    }

    init(current: Deck?, outgoing: OutgoingDeck?, pendingJump: PendingJump?, lastHostTime: TimeInterval?) {
        self.current = current
        self.outgoing = outgoing
        self.pendingJump = pendingJump
        self.lastHostTime = lastHostTime
    }

    // MARK: - Ingest

    public func ingesting(_ sample: PlaybackSample) -> PlaybackTracker {
        // Polls can finish out of order (a new track's metadata fetch takes extra time).
        if let lastHostTime, sample.hostTime < lastHostTime { return self }

        let t = sample.hostTime
        let liveOutgoing = outgoing.flatMap { $0.fadeEnd > t ? $0 : nil }
        guard sample.state != .stopped, !sample.trackID.isEmpty else {
            return PlaybackTracker(current: nil, outgoing: liveOutgoing, pendingJump: nil, lastHostTime: t)
        }
        guard let current else {
            return PlaybackTracker(current: Self.deck(from: sample), outgoing: liveOutgoing,
                                   pendingJump: nil, lastHostTime: t)
        }
        return current.trackID == sample.trackID
            ? continuing(current, with: sample, outgoing: liveOutgoing)
            : switching(from: current, to: sample)
    }

    private func continuing(_ deck: Deck, with sample: PlaybackSample, outgoing: OutgoingDeck?) -> PlaybackTracker {
        let t = sample.hostTime
        let isRunning = sample.state == .playing
        let predicted = deck.clock.position(at: t)
        let jumpedBack = deck.clock.isRunning && isRunning
            && deck.hasKnownDuration
            && deck.duration - predicted < Self.transitionEdge
            && sample.position < predicted - PlaybackClock.snapThreshold

        if jumpedBack, let hold = holding(sample) {
            // Probably a transition whose track id hasn't caught up yet: keep the old clock.
            return PlaybackTracker(current: deck, outgoing: outgoing, pendingJump: hold, lastHostTime: t)
        }
        let clock = deck.clock.correcting(position: sample.position, at: t,
                                          latency: sample.latency, isRunning: isRunning)
        let updated = Deck(trackID: deck.trackID, duration: sample.duration, clock: clock, since: deck.since)
        return PlaybackTracker(current: updated, outgoing: outgoing, pendingJump: nil, lastHostTime: t)
    }

    /// The updated hold for a suspicious jump, or nil once it's confirmed as a real seek.
    private func holding(_ sample: PlaybackSample) -> PendingJump? {
        let t = sample.hostTime
        guard let pending = pendingJump else {
            return PendingJump(since: t, position: sample.position, hostTime: t)
        }
        let elapsed = t - pending.hostTime
        let advancesInStep = elapsed >= Self.confirmGap
            && abs(sample.position - (pending.position + elapsed)) < Self.linearTolerance
        let expired = t - pending.since >= Self.jumpHoldTime
        return advancesInStep || expired
            ? nil
            : PendingJump(since: pending.since, position: sample.position, hostTime: t)
    }

    private func switching(from old: Deck, to sample: PlaybackSample) -> PlaybackTracker {
        let t = sample.hostTime
        let incoming = Self.deck(from: sample)
        let remaining = old.hasKnownDuration ? old.duration - old.clock.position(at: t) : .infinity

        guard remaining > Self.minimumOverlap else {
            return PlaybackTracker(current: incoming, outgoing: nil, pendingJump: nil, lastHostTime: t)
        }
        let blending = old.clock.isRunning
            && remaining <= Self.maxOverlap
            && sample.position >= Self.blendEvidence
        let leaving = blending
            ? OutgoingDeck(deck: old, fadeStart: t, fadeEnd: t + remaining)
            : OutgoingDeck(deck: Deck(trackID: old.trackID, duration: old.duration,
                                      clock: old.clock.with(isRunning: false, at: t), since: old.since),
                           fadeStart: t, fadeEnd: t + Self.quickFade)
        return PlaybackTracker(current: incoming, outgoing: leaving, pendingJump: nil, lastHostTime: t)
    }

    private static func deck(from sample: PlaybackSample) -> Deck {
        Deck(trackID: sample.trackID, duration: sample.duration,
             clock: PlaybackClock(position: sample.position, hostTime: sample.hostTime,
                                  isRunning: sample.state == .playing),
             since: sample.hostTime)
    }

    // MARK: - Frame

    public func frame(at hostTime: TimeInterval) -> PlaybackFrame {
        let currentFrame = current.map { deck in
            DeckFrame(trackID: deck.trackID, position: Self.clamped(deck, at: hostTime),
                      isRunning: deck.clock.isRunning,
                      opacity: min(1, max(0, (hostTime - deck.since) / Self.fadeIn)))
        }
        let outgoingFrame = outgoing.flatMap { out -> DeckFrame? in
            guard hostTime < out.fadeEnd else { return nil }
            let progress = (hostTime - out.fadeStart) / (out.fadeEnd - out.fadeStart)
            return DeckFrame(trackID: out.deck.trackID, position: Self.clamped(out.deck, at: hostTime),
                             isRunning: out.deck.clock.isRunning,
                             opacity: min(1, max(0, 1 - progress)))
        }
        return PlaybackFrame(current: currentFrame, outgoing: outgoingFrame)
    }

    private static func clamped(_ deck: Deck, at hostTime: TimeInterval) -> TimeInterval {
        let position = max(0, deck.clock.position(at: hostTime))
        return deck.hasKnownDuration ? min(deck.duration, position) : position
    }
}
