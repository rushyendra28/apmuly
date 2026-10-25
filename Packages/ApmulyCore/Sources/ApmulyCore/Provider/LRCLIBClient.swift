import Foundation

public protocol HTTPClient: Sendable {
    /// Returns the body and HTTP status code.
    func get(_ request: URLRequest) async throws -> (Data, Int)
}

public struct URLSessionHTTPClient: HTTPClient {
    private let session: URLSession

    public init(session: URLSession = .shared) {
        self.session = session
    }

    public func get(_ request: URLRequest) async throws -> (Data, Int) {
        let (data, response) = try await session.data(for: request)
        return (data, (response as? HTTPURLResponse)?.statusCode ?? 0)
    }
}

/// Fetches lyrics from https://lrclib.net (free, no API key).
/// Strategy: exact lookup by full metadata, then a ranked search with cleaned title/artist.
public struct LRCLIBClient: LyricsProvider {
    public static let defaultBaseURL = URL(string: "https://lrclib.net")!
    public static let defaultUserAgent = "Apmuly/1.1 (+https://github.com/rushyendra28/apmuly)"
    private static let timeout: TimeInterval = 15

    private let http: any HTTPClient
    private let baseURL: URL
    private let userAgent: String

    public init(http: any HTTPClient = URLSessionHTTPClient(),
                baseURL: URL = defaultBaseURL,
                userAgent: String = defaultUserAgent) {
        self.http = http
        self.baseURL = baseURL
        self.userAgent = userAgent
    }

    public func candidate(for track: TrackInfo) async throws(LyricsError) -> LyricsCandidate? {
        let knownDuration = track.duration > 0 ? [("duration", String(Int(track.duration.rounded())))] : []
        let exact: LyricsCandidate? = try await fetch("/api/get", [
            ("track_name", track.title),
            ("artist_name", track.artist),
            ("album_name", track.album),
        ] + knownDuration)
        if let exact, Self.hasSynced(exact) { return exact }

        // No exact match, or the exact record is plain-only: a synced copy may exist under other metadata.
        let results: [LyricsCandidate]
        do {
            results = try await fetch("/api/search", [
                ("track_name", TitleNormalizer.searchTitle(track.title)),
                ("artist_name", TitleNormalizer.primaryArtist(track.artist)),
            ]) ?? []
        } catch {
            if let exact { return exact }
            throw error
        }
        let best = CandidateRanker.best(for: track, among: results)
        if let best, Self.hasSynced(best) { return best }
        return exact ?? best
    }

    private static func hasSynced(_ candidate: LyricsCandidate) -> Bool {
        candidate.syncedLyrics?.isEmpty == false
    }

    /// Decodes a 2xx body, returns nil on 404, throws otherwise.
    private func fetch<T: Decodable>(_ path: String, _ query: [(String, String)]) async throws(LyricsError) -> T? {
        guard var components = URLComponents(url: baseURL.appendingPathComponent(path), resolvingAgainstBaseURL: false)
        else { throw .network }
        components.queryItems = query.map { URLQueryItem(name: $0.0, value: $0.1) }
        guard let url = components.url else { throw .network }

        var request = URLRequest(url: url, timeoutInterval: Self.timeout)
        request.setValue(userAgent, forHTTPHeaderField: "User-Agent")

        let data: Data
        let status: Int
        do {
            (data, status) = try await http.get(request)
        } catch {
            throw .network
        }
        if status == 404 { return nil }
        guard (200..<300).contains(status) else { throw .server(status: status) }
        do {
            return try JSONDecoder().decode(T.self, from: data)
        } catch {
            throw .decoding
        }
    }
}
