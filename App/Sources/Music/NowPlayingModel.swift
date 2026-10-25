import AppKit
import ApmulyCore
import Combine
import Foundation
import os

enum LyricsLoad: Sendable, Equatable {
    case loading
    case loaded(LyricsResult)
    case failed(LyricsError)
}

/// Everything a view needs to draw one deck (current or outgoing track).
struct DeckRender: Equatable {
    let trackID: String
    let track: TrackInfo?
    let lyrics: LyricsLoad?
    /// Playback position with the user's lyric offset applied.
    let lyricsPosition: TimeInterval
    let isRunning: Bool
    let opacity: Double
}

/// Polls Music, follows playback across transitions, and loads lyrics and artwork per track.
@MainActor
final class NowPlayingModel: ObservableObject {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "now-playing")
    static let offsetStep: TimeInterval = 0.05
    private static let playingInterval: UInt64 = 500_000_000
    private static let idleInterval: UInt64 = 1_000_000_000
    /// After this many failed polls in a row, stop trusting the free-running clock.
    private static let maxConsecutiveFailures = 3
    /// Backoff (seconds) for automatic lyric retries after temporary failures (outage, rate limit, offline).
    private static let lyricRetryDelays: [UInt64] = [5, 20, 60]

    @Published private(set) var tracker = PlaybackTracker()
    @Published private(set) var tracks: [String: TrackInfo] = [:]
    @Published private(set) var lyrics: [String: LyricsLoad] = [:]
    @Published private(set) var bridgeError: MusicBridgeError?
    @Published private(set) var offsets: [String: TimeInterval] = [:]
    /// Small JPEG data URLs of each track's artwork.
    private(set) var artworkURLs: [String: String] = [:]
    /// Emotion emoji and object emoji per line, per track (grows as analysis progresses).
    private(set) var insights: [String: [Int: LineInsight]] = [:]
    /// Bumped whenever a track's insights change, so the renderer knows to resend.
    private(set) var insightVersions: [String: Int] = [:]
    /// Bumped whenever a track's lyric timing changes (learned singer pace), so the renderer resends.
    private(set) var lyricsVersions: [String: Int] = [:]
    private let analyzer = LyricAnalyzer()
    private static let artworkMaxPixel = 256

    // Sync: auto-alignment to the singer, learned word timing, output-device latency.
    /// Music's audio (shared with the renderer for beat detection).
    let audioTap = MusicAudioTap()
    /// Listens for the singer; nil where the OS can't (needs audio capture: macOS 14.2+).
    private let aligner: (any VocalListening)? = VocalListeners.make()
    private let timingStore = TimingStore()
    /// Measured per-track offset between the lyric file and the singer.
    @Published private(set) var autoSync: [String: SyncEstimate] = [:]
    /// Track currently being listened to for auto-sync.
    @Published private(set) var aligningTrackID: String?
    /// Delay from Music to your ears on the current output device.
    @Published private(set) var outputLatency: TimeInterval = 0
    /// Lyrics as fetched (before re-timing to the singer's pace).
    private var baseDocs: [String: LyricsDocument] = [:]
    /// Word timings learned from the singer (this play and earlier ones).
    private var learned: [String: [WordObservation]] = [:]
    private var heardWords: [String: [HeardWord]] = [:]
    private var heardSinceCalibration = 0
    private var alignTask: Task<Void, Never>?
    private var feedTask: Task<Void, Never>?
    private var lastLatencyCheck: TimeInterval = -.infinity
    private static let maxHeardWords = 800
    private static let recalibrateEvery = 6
    private static let latencyCheckInterval: TimeInterval = 5
    private static let feedInterval: UInt64 = 250_000_000

    private let bridge: MusicBridge
    private let lyricsService: LyricsService
    private let defaults: UserDefaults
    private var pollTask: Task<Void, Never>?
    private var observer: MusicNotificationObserver?
    /// Keeps App Nap from throttling polls while Apmuly sits behind Music.
    private var activity: (any NSObjectProtocol)?
    private var consecutiveFailures = 0
    private var health = PollHealth()
    private var lyricAttempts: [String: Int] = [:]

    init(bridge: MusicBridge = MusicBridge(), lyricsService: LyricsService = LyricsService(),
         defaults: UserDefaults = .standard) {
        self.bridge = bridge
        self.lyricsService = lyricsService
        self.defaults = defaults
    }

    // MARK: - Lifecycle

    func start() {
        guard pollTask == nil else { return }
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                await self.pollNow()
                let playing = self.tracker.current?.clock.isRunning == true
                try? await Task.sleep(nanoseconds: playing ? Self.playingInterval : Self.idleInterval)
            }
        }
        // Music posts this on play/pause/skip and at the switch point of an AutoMix transition.
        observer = MusicNotificationObserver { [weak self] in
            self?.health.notifications += 1
            Task { await self?.pollNow() }
        }
        activity = ProcessInfo.processInfo.beginActivity(
            options: .userInitiatedAllowingIdleSystemSleep,
            reason: "Following Music playback to keep lyrics in sync")
        if let aligner {
            feedTask = Task { [weak self] in
                while !Task.isCancelled {
                    guard let self else { return }
                    if let chunk = self.audioTap.drainSamples() { await aligner.feed(chunk) }
                    try? await Task.sleep(nanoseconds: Self.feedInterval)
                }
            }
        }
    }

    func stop() {
        pollTask?.cancel()
        pollTask = nil
        feedTask?.cancel()
        feedTask = nil
        stopAlignment()
        observer?.invalidate()
        observer = nil
        activity.map(ProcessInfo.processInfo.endActivity)
        activity = nil
    }

    // MARK: - Polling

    func pollNow() async {
        switch await bridge.poll() {
        case .failure(let error):
            handleFailure(error)
        case .success(let poll):
            bridgeError = nil
            consecutiveFailures = 0
            recordHealth(at: poll.hostTime)
            guard poll.state != .stopped, !poll.trackID.isEmpty else {
                tracker = tracker.ingesting(sample(from: poll, duration: 0))
                return
            }
            guard let duration = await duration(for: poll.trackID) else { return }
            tracker = tracker.ingesting(sample(from: poll, duration: duration))
            refreshOutputLatency(at: poll.hostTime)
            updateAlignment()
        }
    }

    // MARK: - Sync (auto-alignment, singer pace, output latency)

    private func refreshOutputLatency(at hostTime: TimeInterval) {
        guard hostTime - lastLatencyCheck >= Self.latencyCheckInterval else { return }
        lastLatencyCheck = hostTime
        let latency = OutputLatency.current() ?? 0
        if abs(latency - outputLatency) > 0.005 {
            Self.logger.info("Output latency \(Int(latency * 1000)) ms")
            outputLatency = latency
        }
    }

    /// Starts listening for the current song (once per track) when it has timed lyrics.
    private func updateAlignment() {
        guard let aligner, let deck = tracker.current, deck.clock.isRunning, deck.trackID != aligningTrackID,
              let doc = baseDocs[deck.trackID]
        else { return }
        let trackID = deck.trackID
        aligningTrackID = trackID
        heardSinceCalibration = 0
        alignTask?.cancel()
        audioTap.setCollecting(true)
        let languageSample = doc.lines.map(\.text).joined(separator: " ")
        alignTask = Task { [weak self] in
            guard let words = await aligner.start(languageSample: languageSample) else { return }
            for await word in words {
                if Task.isCancelled { break }
                self?.record(word, trackID: trackID)
            }
        }
    }

    private func stopAlignment() {
        alignTask?.cancel()
        alignTask = nil
        aligningTrackID = nil
        audioTap.setCollecting(false)
        if let aligner { Task { await aligner.stop() } }
    }

    private func record(_ word: HeardAudioWord, trackID: String) {
        guard let deck = tracker.current, deck.trackID == trackID, word.hostTime >= deck.since,
              let doc = baseDocs[trackID]
        else { return }
        let heard = HeardWord(text: word.text, position: deck.clock.position(at: word.hostTime))
        heardWords[trackID, default: []].append(heard)
        if heardWords[trackID, default: []].count > Self.maxHeardWords { heardWords[trackID]?.removeFirst() }
        heardSinceCalibration += 1
        guard heardSinceCalibration >= Self.recalibrateEvery else { return }
        heardSinceCalibration = 0

        let allHeard = heardWords[trackID] ?? []
        let matches = LyricMatcher.match(heard: allHeard, lyrics: doc)
        guard let estimate = SyncCalibrator.estimate(matches) else {
            Self.logger.info("Auto-sync \(trackID, privacy: .public): heard \(allHeard.count) words, \(matches.count) matched — not confident yet")
            return
        }
        let previous = autoSync[trackID]?.globalOffset
        autoSync[trackID] = estimate
        if previous.map({ abs($0 - estimate.globalOffset) > 0.02 }) ?? true {
            Self.logger.info("Auto-sync \(trackID, privacy: .public): \(Int(estimate.globalOffset * 1000)) ms from \(estimate.matchCount) matched words")
        }
        learnWordTiming(trackID, heard: allHeard, doc: doc, estimate: estimate)
    }

    /// Re-times words to the singer's pace from what was heard, and remembers it for next time.
    private func learnWordTiming(_ trackID: String, heard: [HeardWord], doc: LyricsDocument, estimate: SyncEstimate) {
        let observed = LyricMatcher.observe(heard: heard, lyrics: doc) { estimate.offset(at: $0) }
        guard !observed.isEmpty else { return }
        learned[trackID] = Self.merge(learned[trackID] ?? [], observed)
        applyLearnedTiming(trackID)
        Self.logger.info("Singer pace \(trackID, privacy: .public): \(self.learned[trackID]?.count ?? 0) words timed")
        let store = timingStore
        Task {
            do throws(LyricsError) {
                try await store.save(observed, for: trackID)
            } catch {
                Self.logger.error("Could not save word timing for \(trackID, privacy: .public)")
            }
        }
    }

    private func applyLearnedTiming(_ trackID: String) {
        guard let base = baseDocs[trackID] else { return }
        lyrics[trackID] = .loaded(.lyrics(WordRetimer.retime(base, observations: learned[trackID] ?? [])))
        lyricsVersions[trackID, default: 0] += 1
    }

    /// Newest observation of each word wins.
    private static func merge(_ old: [WordObservation], _ new: [WordObservation]) -> [WordObservation] {
        var byWord: [String: WordObservation] = [:]
        for observation in old + new { byWord["\(observation.line):\(observation.word)"] = observation }
        return Array(byWord.values)
    }

    /// Total shift applied to lyric timing at `position` (user nudge + auto-sync + output delay).
    func syncOffset(for trackID: String, at position: TimeInterval) -> TimeInterval {
        (offsets[trackID] ?? 0) + (autoSync[trackID]?.offset(at: position) ?? 0) + outputLatency
    }

    private func handleFailure(_ error: MusicBridgeError) {
        bridgeError = error
        consecutiveFailures += 1
        Self.logger.error("Poll failed (\(self.consecutiveFailures)x): \(String(describing: error), privacy: .public)")
        guard consecutiveFailures >= Self.maxConsecutiveFailures else { return }
        // Can't see Music any more: clear the stage rather than let lyrics run blind.
        let stopped = PlaybackSample(trackID: "", position: 0, duration: 0, state: .stopped,
                                     hostTime: ProcessInfo.processInfo.systemUptime, latency: 0)
        tracker = tracker.ingesting(stopped)
    }

    private func sample(from poll: PlayerPoll, duration: TimeInterval) -> PlaybackSample {
        PlaybackSample(trackID: poll.trackID, position: poll.position, duration: duration,
                       state: poll.state, hostTime: poll.hostTime, latency: poll.latency)
    }

    /// Known duration, or fetches track metadata (and kicks off lyrics + artwork loads) for a new track.
    private func duration(for trackID: String) async -> TimeInterval? {
        if let known = tracks[trackID] { return known.duration }
        switch await bridge.currentTrack() {
        case .failure(let error):
            handleFailure(error)
            return nil
        case .success(let info):
            // The track may have changed again between the two Apple Events.
            guard info.id == trackID else { return nil }
            tracks[info.id] = info
            offsets[info.id] = defaults.double(forKey: Self.offsetKey(info.id))
            loadLyrics(for: info)
            loadArtwork(for: info.id)
            return info.duration
        }
    }

    private func loadLyrics(for track: TrackInfo) {
        guard lyrics[track.id] == nil else { return }
        lyrics[track.id] = .loading
        Task {
            do throws(LyricsError) {
                let result = try await lyricsService.lyrics(for: track)
                guard case .lyrics(let doc) = result, doc.timing != .unsynced else {
                    lyrics[track.id] = .loaded(result)
                    lyricsVersions[track.id, default: 0] += 1
                    return
                }
                baseDocs[track.id] = doc
                // Timing learned on earlier plays puts words on the singer's pace from the first line.
                learned[track.id] = await timingStore.load(track.id)
                applyLearnedTiming(track.id)
                if insights[track.id] == nil { analyzeInsights(track.id, doc) }
            } catch {
                Self.logger.error("Lyrics failed for \(track.id, privacy: .public): \(String(describing: error), privacy: .public)")
                lyrics[track.id] = .failed(error)
                scheduleRetry(for: track, after: error)
            }
        }
    }

    private func scheduleRetry(for track: TrackInfo, after error: LyricsError) {
        let attempt = lyricAttempts[track.id, default: 0]
        guard error.isTransient, attempt < Self.lyricRetryDelays.count else { return }
        lyricAttempts[track.id] = attempt + 1
        let seconds = Self.lyricRetryDelays[attempt]
        Self.logger.info("Retrying lyrics for \(track.id, privacy: .public) in \(seconds) s")
        Task {
            try? await Task.sleep(nanoseconds: seconds * 1_000_000_000)
            guard case .failed = lyrics[track.id] else { return }
            lyrics[track.id] = nil
            loadLyrics(for: track)
        }
    }

    private func loadArtwork(for trackID: String) {
        Task {
            guard case .success(let artwork) = await bridge.currentArtwork(), artwork.trackID == trackID,
                  let data = artwork.data
            else { return }
            let maxPixel = Self.artworkMaxPixel
            let url = await Task.detached(priority: .utility) { ImageProcessor.dataURL(from: data, maxPixel: maxPixel) }.value
            guard let url else {
                Self.logger.error("Could not decode artwork for \(trackID, privacy: .public)")
                return
            }
            artworkURLs[trackID] = url
        }
    }

    // MARK: - Insights (emotion emoji + emoji for things mentioned)

    private func analyzeInsights(_ trackID: String, _ doc: LyricsDocument) {
        insights[trackID] = [:]
        Task {
            var readings: [Int: LineReading] = [:]
            for await chunk in analyzer.readings(for: doc) {
                readings.merge(chunk) { _, new in new }
                let planned = InsightPlanner.plan(lines: doc.lines, readings: readings)
                insights[trackID] = planned
                insightVersions[trackID, default: 0] += 1
            }
        }
    }

    func retryLyrics() {
        guard let id = tracker.current?.trackID, let track = tracks[id], case .failed = lyrics[id] else { return }
        lyrics[id] = nil
        loadLyrics(for: track)
    }

    // MARK: - Background health (verifies polling keeps pace while the app is hidden)

    private struct PollHealth {
        static let reportInterval: TimeInterval = 10
        var polls = 0
        var notifications = 0
        var maxGap: TimeInterval = 0
        var lastPoll: TimeInterval?
        var windowStart: TimeInterval?
    }

    private func recordHealth(at hostTime: TimeInterval) {
        health.polls += 1
        if let last = health.lastPoll { health.maxGap = max(health.maxGap, hostTime - last) }
        health.lastPoll = hostTime
        let start = health.windowStart ?? hostTime
        health.windowStart = start
        guard hostTime - start >= PollHealth.reportInterval else { return }
        let window = health
        let active = NSApp?.isActive ?? false
        let visible = NSApp?.windows.contains { $0.occlusionState.contains(.visible) } ?? false
        Self.logger.info("""
            health polls=\(window.polls) notifications=\(window.notifications) \
            maxGapMs=\(Int(window.maxGap * 1000)) appActive=\(active) windowVisible=\(visible)
            """)
        health = PollHealth(lastPoll: hostTime, windowStart: hostTime)
    }

    // MARK: - Offset ("lyrics later" is positive)

    func adjustOffset(by delta: TimeInterval) {
        guard let id = tracker.current?.trackID else { return }
        setOffset((offsets[id] ?? 0) + delta, for: id)
    }

    func resetOffset() {
        guard let id = tracker.current?.trackID else { return }
        setOffset(0, for: id)
    }

    var currentOffset: TimeInterval {
        tracker.current.flatMap { offsets[$0.trackID] } ?? 0
    }

    private func setOffset(_ value: TimeInterval, for id: String) {
        let rounded = (value * 1000).rounded() / 1000
        offsets[id] = rounded
        defaults.set(rounded, forKey: Self.offsetKey(id))
    }

    private static func offsetKey(_ id: String) -> String { "lyricsOffset.\(id)" }

    // MARK: - Rendering

    var currentTrack: TrackInfo? {
        tracker.current.flatMap { tracks[$0.trackID] }
    }

    var currentLyrics: LyricsLoad? {
        tracker.current.flatMap { lyrics[$0.trackID] }
    }

    func render(at hostTime: TimeInterval) -> (current: DeckRender?, outgoing: DeckRender?) {
        let frame = tracker.frame(at: hostTime)
        return (frame.current.map(render), frame.outgoing.map(render))
    }

    private func render(_ deck: DeckFrame) -> DeckRender {
        DeckRender(trackID: deck.trackID, track: tracks[deck.trackID], lyrics: lyrics[deck.trackID],
                   lyricsPosition: deck.position - syncOffset(for: deck.trackID, at: deck.position),
                   isRunning: deck.isRunning, opacity: deck.opacity)
    }
}
