import Testing
@testable import ApmulyCore

@Suite("Playback tracker (transitions)")
struct PlaybackTrackerTests {
    private func sample(_ id: String, _ position: Double, at host: Double, duration: Double = 200,
                        state: PlayerState = .playing, latency: Double = 0.02) -> PlaybackSample {
        PlaybackSample(trackID: id, position: position, duration: duration, state: state, hostTime: host, latency: latency)
    }

    private func feed(_ samples: [PlaybackSample], into tracker: PlaybackTracker = PlaybackTracker()) -> PlaybackTracker {
        samples.reduce(tracker) { $0.ingesting($1) }
    }

    @Test("First sample becomes the current deck and fades in")
    func firstSample() {
        let tracker = feed([sample("A", 30, at: 0)])
        let frame = tracker.frame(at: 0.2)
        #expect(frame.current?.trackID == "A")
        #expect(abs((frame.current?.position ?? 0) - 30.2) < 0.0001)
        #expect((frame.current?.opacity ?? 0) < 1)
        #expect(tracker.frame(at: 5).current?.opacity == 1)
        #expect(frame.outgoing == nil)
    }

    /// Replays what Music reported during a real AutoMix transition: one sample
    /// arrives with the OLD track id but the NEW track's position, then the id flips.
    @Test("AutoMix: non-atomic position/id update does not flash the old song's start")
    func autoMixRace() {
        let a = 178.24
        let tracker = feed([
            sample("A", 175.0, at: 0, duration: a),
            sample("A", 175.6, at: 0.6, duration: a),
            sample("A", 176.2, at: 1.2, duration: a),
            sample("A", 1.70, at: 1.37, duration: a), // stale id, new position
        ])
        // Must still be following A near its end, not jumped back to 1.7.
        let held = tracker.frame(at: 1.37)
        #expect(held.current?.trackID == "A")
        #expect((held.current?.position ?? 0) > 176)

        let switched = tracker.ingesting(sample("B", 1.88, at: 1.54, duration: 253.59))
        let frame = switched.frame(at: 2.0)
        #expect(frame.current?.trackID == "B")
        #expect(abs((frame.current?.position ?? 0) - 2.34) < 0.0001)
        // Old song keeps running out its remaining ~1.7s, fading out.
        #expect(frame.outgoing?.trackID == "A")
        #expect((frame.outgoing?.position ?? 0) > 176.9)
        #expect((frame.outgoing?.opacity ?? 0) > 0 && (frame.outgoing?.opacity ?? 1) < 1)
        // …and is gone once A would have ended.
        #expect(switched.frame(at: 3.5).outgoing == nil)
    }

    @Test("Backwards jump near the end is confirmed by a second, linearly-continuing sample (restart / repeat-one)")
    func nearEndSeekConfirmedByLinearSample() {
        let held = feed([sample("A", 195, at: 0), sample("A", 0.2, at: 0.5)])
        #expect((held.frame(at: 0.5).current?.position ?? 0) > 190) // first stale-looking sample is held
        let confirmed = held.ingesting(sample("A", 0.7, at: 1.0))
        #expect(abs((confirmed.frame(at: 1.0).current?.position ?? 0) - 0.7) < 0.0001)
    }

    @Test("Inconsistent backwards jumps near the end are accepted when the hold expires")
    func nearEndJumpAcceptedAfterHold() {
        let tracker = feed([
            sample("A", 195, at: 0),
            sample("A", 0.2, at: 0.5),
            sample("A", 5.0, at: 1.0), // not linear with the previous jump
        ])
        #expect((tracker.frame(at: 1.0).current?.position ?? 0) > 190)
        let host = 0.5 + PlaybackTracker.jumpHoldTime + 0.1
        let accepted = tracker.ingesting(sample("A", 9.0, at: host))
        #expect(abs((accepted.frame(at: host).current?.position ?? 0) - 9.0) < 0.0001)
    }

    @Test("Manual skip near the end (incoming starts at ~0) fades quickly instead of blending")
    func skipNearEndDoesNotBlend() {
        let tracker = feed([sample("A", 190, at: 0), sample("B", 0.1, at: 0.5)])
        let frame = tracker.frame(at: 0.6)
        #expect(frame.outgoing?.position == tracker.frame(at: 0.9).outgoing?.position) // frozen
        #expect(frame.outgoing?.isRunning == false)
        #expect(tracker.frame(at: 0.5 + PlaybackTracker.quickFade + 0.01).outgoing == nil)
    }

    @Test("Samples older than the last ingested one are ignored")
    func dropsOutOfOrderSamples() {
        let tracker = feed([
            sample("A", 50, at: 0),
            sample("", 0, at: 2, state: .stopped),
            sample("X", 10, at: 1), // finished its metadata fetch late
        ])
        #expect(tracker.frame(at: 2.5).current == nil)
    }

    @Test("A playing sample with an empty track id is treated as stopped")
    func emptyIDIsStopped() {
        let tracker = feed([sample("A", 50, at: 0), sample("", 0, at: 1)])
        #expect(tracker.frame(at: 1).current == nil)
    }

    @Test("Unknown duration (0) does not clamp, hold jumps, or blend")
    func unknownDuration() {
        let tracker = feed([sample("A", 30, at: 0, duration: 0), sample("A", 5, at: 0.5, duration: 0)])
        #expect(tracker.frame(at: 0.5).current?.position == 5) // seek applied, not held
        #expect(tracker.frame(at: 10.5).current?.position == 15) // not clamped to 0
        let switched = tracker.ingesting(sample("B", 2.0, at: 11, duration: 0))
        #expect(switched.frame(at: 11 + PlaybackTracker.quickFade + 0.01).outgoing == nil)
    }

    @Test("Backwards seek mid-song applies immediately")
    func midSongSeekBack() {
        let tracker = feed([sample("A", 100, at: 0), sample("A", 20, at: 0.5)])
        #expect(tracker.frame(at: 0.5).current?.position == 20)
    }

    @Test("Forward seek applies immediately")
    func forwardSeek() {
        let tracker = feed([sample("A", 10, at: 0), sample("A", 150, at: 0.5)])
        #expect(tracker.frame(at: 0.5).current?.position == 150)
    }

    @Test("Manual skip mid-song: old deck freezes and fades quickly")
    func manualSkip() {
        let tracker = feed([sample("A", 60, at: 0), sample("B", 0.1, at: 1)])
        let early = tracker.frame(at: 1.1)
        #expect(early.current?.trackID == "B")
        #expect(early.outgoing?.trackID == "A")
        #expect(early.outgoing?.position == tracker.frame(at: 1.3).outgoing?.position) // frozen
        #expect(tracker.frame(at: 1 + PlaybackTracker.quickFade + 0.01).outgoing == nil)
    }

    @Test("Track that ends normally (no overlap) leaves no outgoing deck")
    func naturalEndWithoutOverlap() {
        let tracker = feed([sample("A", 199.9, at: 0), sample("B", 0.0, at: 0.3)])
        #expect(tracker.frame(at: 0.3).outgoing == nil)
    }

    @Test("Pause freezes position; resume continues")
    func pauseResume() {
        let paused = feed([sample("A", 50, at: 0), sample("A", 51, at: 1, state: .paused)])
        #expect(paused.frame(at: 10).current?.position == 51)
        #expect(paused.frame(at: 10).current?.isRunning == false)
        let resumed = paused.ingesting(sample("A", 51, at: 10))
        #expect(resumed.frame(at: 11).current?.position == 52)
        #expect(resumed.frame(at: 11).current?.isRunning == true)
    }

    @Test("Stopped clears the current deck")
    func stopped() {
        let tracker = feed([sample("A", 50, at: 0), sample("", 0, at: 1, state: .stopped)])
        #expect(tracker.frame(at: 1).current == nil)
    }

    @Test("Position is clamped to the track duration")
    func clampsToDuration() {
        let tracker = feed([sample("A", 199, at: 0)])
        #expect(tracker.frame(at: 10).current?.position == 200)
    }
}
