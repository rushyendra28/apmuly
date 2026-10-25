import Testing
@testable import ApmulyCore

@Suite("Playback clock")
struct PlaybackClockTests {
    private let running = PlaybackClock(position: 10, hostTime: 100, isRunning: true)

    @Test("Extrapolates while running, holds while paused")
    func extrapolates() {
        #expect(running.position(at: 102.5) == 12.5)
        let paused = PlaybackClock(position: 10, hostTime: 100, isRunning: false)
        #expect(paused.position(at: 150) == 10)
    }

    @Test("Small error is smoothed, not snapped")
    func smoothsSmallError() {
        // Predicted 11.0 at host 101, Music says 11.2
        let corrected = running.correcting(position: 11.2, at: 101, latency: 0.02, isRunning: true)
        let now = corrected.position(at: 101)
        #expect(now > 11.0 && now < 11.2)
    }

    @Test("Persistent error converges within a few samples")
    func converges() {
        let final = (1...10).reduce(running) { clock, i in
            let host = 100 + Double(i) * 0.5
            return clock.correcting(position: 10 + Double(i) * 0.5 + 0.2, at: host, latency: 0.02, isRunning: true)
        }
        #expect(abs(final.position(at: 105) - 15.2) < 0.01)
    }

    @Test("Large error snaps (seek)")
    func snapsOnSeek() {
        let corrected = running.correcting(position: 60, at: 101, latency: 0.02, isRunning: true)
        #expect(corrected.position(at: 101) == 60)
    }

    @Test("Slow samples are ignored")
    func ignoresSlowSamples() {
        let corrected = running.correcting(position: 11.3, at: 101, latency: 0.5, isRunning: true)
        #expect(corrected == running)
    }

    @Test("Seeks apply even when the sample was slow")
    func slowSeekStillSnaps() {
        let corrected = running.correcting(position: 90, at: 101, latency: 0.5, isRunning: true)
        #expect(corrected.position(at: 101) == 90)
    }

    @Test("State change snaps to the reported position")
    func snapsOnStateChange() {
        let paused = running.correcting(position: 10.9, at: 101, latency: 0.02, isRunning: false)
        #expect(paused.isRunning == false)
        #expect(paused.position(at: 120) == 10.9)
    }

    @Test("Paused clock follows seeks while paused, even on slow samples")
    func pausedFollowsSeeks() {
        let paused = PlaybackClock(position: 10, hostTime: 100, isRunning: false)
        #expect(paused.correcting(position: 42, at: 101, latency: 0.5, isRunning: false).position(at: 102) == 42)
    }
}
