import Testing
@testable import ApmulyCore

@Suite("Media source selection")
struct MediaSourceTests {
    private func snap(_ source: MediaSource, _ state: PlayerState, running: Bool = true, since: Double = 0) -> SourceSnapshot {
        SourceSnapshot(source: source, isRunning: running, state: state, playingSince: state == .playing ? since : nil)
    }

    @Test("Auto follows whichever app is playing")
    func followsPlaying() {
        let pick = SourceSelector.choose(preference: .auto, current: .appleMusic,
                                         snapshots: [snap(.appleMusic, .paused), snap(.spotify, .playing, since: 5)])
        #expect(pick == .spotify)
    }

    @Test("Auto picks the one that started most recently when both play")
    func mostRecent() {
        let pick = SourceSelector.choose(preference: .auto, current: .spotify,
                                         snapshots: [snap(.appleMusic, .playing, since: 20), snap(.spotify, .playing, since: 10)])
        #expect(pick == .appleMusic)
    }

    @Test("Auto stays on the current app while nothing plays")
    func staysWhenPaused() {
        let pick = SourceSelector.choose(preference: .auto, current: .spotify,
                                         snapshots: [snap(.appleMusic, .paused), snap(.spotify, .paused)])
        #expect(pick == .spotify)
    }

    @Test("Auto falls back to a running app when the current one quits")
    func fallsBack() {
        let pick = SourceSelector.choose(preference: .auto, current: .spotify,
                                         snapshots: [snap(.appleMusic, .paused), snap(.spotify, .stopped, running: false)])
        #expect(pick == .appleMusic)
    }

    @Test("A manual choice always wins, even when the other app is playing")
    func manual() {
        let pick = SourceSelector.choose(preference: .spotify, current: .appleMusic,
                                         snapshots: [snap(.appleMusic, .playing, since: 3), snap(.spotify, .paused)])
        #expect(pick == .spotify)
    }

    @Test("Preferences cycle Auto → Apple Music → Spotify → Auto")
    func cycle() {
        #expect(SourcePreference.auto.next == .appleMusic)
        #expect(SourcePreference.appleMusic.next == .spotify)
        #expect(SourcePreference.spotify.next == .auto)
    }

    @Test("Spotify ads and episodes aren't songs")
    func notSongs() {
        #expect(MediaSource.spotify.isSong(trackID: "spotify:ad:123") == false)
        #expect(MediaSource.spotify.isSong(trackID: "spotify:episode:9") == false)
        #expect(MediaSource.spotify.isSong(trackID: "spotify:track:4uLU6hMCjMI75M1A2tKUQC"))
        #expect(MediaSource.appleMusic.isSong(trackID: "A1B2C3D4E5F60718"))
    }
}
