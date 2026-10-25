import Foundation
import Testing
@testable import ApmulyCore

/// Returns canned responses keyed by URL path and records every request.
final class FakeHTTPClient: HTTPClient, @unchecked Sendable {
    private let lock = NSLock()
    private let responses: [String: (Int, String)]
    private let failure: (any Error)?
    private var recorded: [URLRequest] = []

    init(responses: [String: (Int, String)] = [:], failure: (any Error)? = nil) {
        self.responses = responses
        self.failure = failure
    }

    var requests: [URLRequest] { lock.withLock { recorded } }

    func get(_ request: URLRequest) async throws -> (Data, Int) {
        lock.withLock { recorded.append(request) }
        if let failure { throw failure }
        let (status, body) = responses[request.url?.path ?? ""] ?? (404, "{}")
        return (Data(body.utf8), status)
    }
}

private func query(_ request: URLRequest) -> [String: String] {
    let items = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)?.queryItems ?? []
    return Dictionary(uniqueKeysWithValues: items.map { ($0.name, $0.value ?? "") })
}

@Suite("LRCLIB client")
struct LRCLIBClientTests {
    private let track = TrackInfo(id: "ABC", title: "Night Drive (feat. Guest)", artist: "Main Artist & Guest",
                                  album: "Album", duration: 199.6)
    private let record = #"{"trackName":"Night Drive","artistName":"Main Artist","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":"[00:01.00]x"}"#

    @Test("Exact lookup sends full metadata, rounded duration and a User-Agent")
    func exactLookup() async throws {
        let http = FakeHTTPClient(responses: ["/api/get": (200, record)])
        let client = LRCLIBClient(http: http, userAgent: "apmuly-test")
        let result = try await client.candidate(for: track)

        #expect(result?.syncedLyrics == "[00:01.00]x")
        let request = try #require(http.requests.first)
        #expect(query(request) == ["track_name": track.title, "artist_name": track.artist,
                                   "album_name": "Album", "duration": "200"])
        #expect(request.value(forHTTPHeaderField: "User-Agent") == "apmuly-test")
    }

    @Test("Falls back to search with cleaned title and primary artist, then ranks")
    func searchFallback() async throws {
        let http = FakeHTTPClient(responses: ["/api/search": (200, "[\(record)]")])
        let result = try await LRCLIBClient(http: http).candidate(for: track)

        #expect(result?.trackName == "Night Drive")
        #expect(http.requests.count == 2)
        #expect(query(http.requests[1]) == ["track_name": "Night Drive", "artist_name": "Main Artist"])
    }

    @Test("A plain-only exact match falls through to search, which wins if it has synced lyrics")
    func plainExactPrefersSyncedSearch() async throws {
        let plainOnly = #"{"trackName":"Night Drive","artistName":"Main Artist","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":null}"#
        let http = FakeHTTPClient(responses: ["/api/get": (200, plainOnly), "/api/search": (200, "[\(record)]")])
        let result = try await LRCLIBClient(http: http).candidate(for: track)
        #expect(result?.syncedLyrics == "[00:01.00]x")
    }

    @Test("A plain-only exact match is kept when search has nothing better")
    func plainExactKeptWhenSearchEmpty() async throws {
        let plainOnly = #"{"trackName":"Night Drive","artistName":"Main Artist","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":null}"#
        let http = FakeHTTPClient(responses: ["/api/get": (200, plainOnly), "/api/search": (200, "[]")])
        let result = try await LRCLIBClient(http: http).candidate(for: track)
        #expect(result?.plainLyrics == "x")
    }

    @Test("Unknown duration is not sent to the exact lookup")
    func omitsUnknownDuration() async throws {
        let http = FakeHTTPClient()
        let unknown = TrackInfo(id: "X", title: "T", artist: "A", album: "L", duration: 0)
        _ = try await LRCLIBClient(http: http).candidate(for: unknown)
        #expect(query(try #require(http.requests.first))["duration"] == nil)
    }

    @Test("Only temporary failures are worth retrying", arguments: [
        (LyricsError.network, true),
        (LyricsError.server(status: 503), true),
        (LyricsError.server(status: 500), true),
        (LyricsError.server(status: 429), true),
        (LyricsError.server(status: 400), false),
        (LyricsError.decoding, false),
        (LyricsError.storage, false),
    ])
    func transientErrors(error: LyricsError, expected: Bool) {
        #expect(error.isTransient == expected)
    }

    @Test("Returns nil when nothing matches")
    func nothingFound() async throws {
        let http = FakeHTTPClient(responses: ["/api/search": (200, "[]")])
        #expect(try await LRCLIBClient(http: http).candidate(for: track) == nil)
    }

    @Test("Server errors are reported, not treated as 'not found'")
    func serverError() async {
        let http = FakeHTTPClient(responses: ["/api/get": (503, "")])
        await #expect(throws: LyricsError.server(status: 503)) {
            try await LRCLIBClient(http: http).candidate(for: track)
        }
    }

    @Test("Network failures map to .network")
    func networkError() async {
        let http = FakeHTTPClient(failure: URLError(.notConnectedToInternet))
        await #expect(throws: LyricsError.network) {
            try await LRCLIBClient(http: http).candidate(for: track)
        }
    }

    @Test("Malformed JSON maps to .decoding")
    func decodingError() async {
        let http = FakeHTTPClient(responses: ["/api/get": (200, "not json")])
        await #expect(throws: LyricsError.decoding) {
            try await LRCLIBClient(http: http).candidate(for: track)
        }
    }
}
