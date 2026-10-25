import Foundation
import Testing
@testable import ApmulyCore

actor FakeProvider: LyricsProvider {
    private let result: Result<LyricsCandidate?, LyricsError>
    private(set) var calls = 0

    init(_ result: Result<LyricsCandidate?, LyricsError>) { self.result = result }

    func candidate(for track: TrackInfo) async throws(LyricsError) -> LyricsCandidate? {
        calls += 1
        return try result.get()
    }
}

private func tempDirectory() -> URL {
    FileManager.default.temporaryDirectory.appendingPathComponent("apmuly-tests-\(UUID().uuidString)")
}

private let track = TrackInfo(id: "A1B2", title: "T", artist: "A", album: "L", duration: 100)

private func candidate(synced: String? = nil, plain: String? = nil, instrumental: Bool = false) -> LyricsCandidate {
    LyricsCandidate(trackName: "T", artistName: "A", duration: 100, instrumental: instrumental,
                    plainLyrics: plain, syncedLyrics: synced)
}

@Suite("Lyrics cache")
struct LyricsCacheTests {
    @Test("Round-trips a candidate through disk")
    func roundTrip() async throws {
        let dir = tempDirectory()
        let stored = candidate(synced: "[00:01.00]x")
        try await LyricsCache(directory: dir).store(stored, for: track.id)
        let loaded = await LyricsCache(directory: dir).lookup(track.id)
        #expect(loaded == .hit(stored))
    }

    @Test("Negative entries expire")
    func negativeExpiry() async throws {
        let dir = tempDirectory()
        let start = Date(timeIntervalSince1970: 1_000_000)
        try await LyricsCache(directory: dir, now: { start }).store(nil, for: track.id)
        #expect(await LyricsCache(directory: dir, now: { start }).lookup(track.id) == .hit(nil))
        let later = start.addingTimeInterval(LyricsCache.negativeTTL + 1)
        #expect(await LyricsCache(directory: dir, now: { later }).lookup(track.id) == .miss)
    }

    @Test("Unsafe ids cannot escape the cache directory")
    func sanitizesIDs() async throws {
        let dir = tempDirectory()
        try await LyricsCache(directory: dir).store(candidate(plain: "x"), for: "../../evil")
        let files = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        #expect(files.count == 1)
        #expect(files.allSatisfy { !$0.contains("/") && !$0.hasPrefix(".") })
    }

    @Test("Corrupt cache files are treated as a miss")
    func corruptFile() async throws {
        let dir = tempDirectory()
        let cache = LyricsCache(directory: dir)
        try await cache.store(candidate(plain: "x"), for: track.id)
        let file = try #require(FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil).first)
        try Data("garbage".utf8).write(to: file)
        #expect(await cache.lookup(track.id) == .miss)
    }
}

@Suite("Lyrics service")
struct LyricsServiceTests {
    private func service(_ provider: FakeProvider) -> LyricsService {
        LyricsService(provider: provider, cache: LyricsCache(directory: tempDirectory()))
    }

    @Test("Synced lyrics become a line-synced document")
    func synced() async throws {
        let result = try await service(FakeProvider(.success(candidate(synced: "[00:01.00]a\n[00:03.00]b")))).lyrics(for: track)
        guard case .lyrics(let doc) = result else { Issue.record("expected lyrics"); return }
        #expect(doc.timing == .lineSynced)
        #expect(doc.lines.count == 2)
    }

    @Test("Falls back to plain lyrics when synced is missing or unparsable")
    func plainFallback() async throws {
        let result = try await service(FakeProvider(.success(candidate(synced: "garbage", plain: "a\nb")))).lyrics(for: track)
        guard case .lyrics(let doc) = result else { Issue.record("expected lyrics"); return }
        #expect(doc.timing == .unsynced)
    }

    @Test("Instrumental and not-found results", arguments: [
        (candidate(instrumental: true) as LyricsCandidate?, LyricsResult.instrumental),
        (nil, LyricsResult.notFound),
        (candidate(), LyricsResult.notFound),
    ])
    func emptyResults(input: LyricsCandidate?, expected: LyricsResult) async throws {
        #expect(try await service(FakeProvider(.success(input))).lyrics(for: track) == expected)
    }

    @Test("Second request is served from cache")
    func usesCache() async throws {
        let provider = FakeProvider(.success(candidate(synced: "[00:01.00]a")))
        let svc = service(provider)
        _ = try await svc.lyrics(for: track)
        _ = try await svc.lyrics(for: track)
        #expect(await provider.calls == 1)
    }

    @Test("Errors propagate and are not cached")
    func errorsNotCached() async throws {
        let provider = FakeProvider(.failure(.network))
        let svc = service(provider)
        await #expect(throws: LyricsError.network) { try await svc.lyrics(for: track) }
        await #expect(throws: LyricsError.network) { try await svc.lyrics(for: track) }
        #expect(await provider.calls == 2)
    }

    @Test("A user-supplied LRC overrides the provider")
    func manualOverride() async throws {
        let provider = FakeProvider(.success(nil))
        let svc = service(provider)
        try await svc.setManualLyrics("[00:02.00]mine", for: track)
        guard case .lyrics(let doc) = try await svc.lyrics(for: track) else { Issue.record("expected lyrics"); return }
        #expect(doc.lines.first?.start == 2)
        #expect(await provider.calls == 0)
    }
}
