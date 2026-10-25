import Testing
@testable import ApmulyCore

@Suite("Title normalizer")
struct TitleNormalizerTests {
    @Test("Cleans titles for searching", arguments: [
        ("Song Name (feat. Somebody)", "Song Name"),
        ("Song Name [ft. A & B]", "Song Name"),
        ("Song Name (with Someone)", "Song Name"),
        ("Song Name - Remastered 2011", "Song Name"),
        ("Song Name - 2011 Remaster", "Song Name"),
        ("Song Name (Remastered)", "Song Name"),
        ("Song Name (2019 Remaster)", "Song Name"),
        ("Song Name (Imanbek Remix)", "Song Name (Imanbek Remix)"),
        ("Plain", "Plain"),
    ])
    func searchTitle(input: String, expected: String) {
        #expect(TitleNormalizer.searchTitle(input) == expected)
    }

    @Test("Extracts the primary artist", arguments: [
        ("Drake & Majid Jordan", "Drake"),
        ("21 Savage, Rich Brian", "21 Savage"),
        ("Artist feat. Other", "Artist"),
        ("Artist ft. Other", "Artist"),
        ("Artist x Other", "Artist"),
        ("Solo", "Solo"),
    ])
    func primaryArtist(input: String, expected: String) {
        #expect(TitleNormalizer.primaryArtist(input) == expected)
    }

    @Test("Comparison key folds case, accents and punctuation")
    func comparisonKey() {
        #expect(TitleNormalizer.key("Café — Déjà Vu!") == TitleNormalizer.key("cafe deja vu"))
    }
}

@Suite("Candidate ranker")
struct CandidateRankerTests {
    private let track = TrackInfo(id: "1", title: "Night Drive (feat. Guest)", artist: "Main Artist & Guest",
                                  album: "Album", duration: 200)

    private func candidate(_ title: String, artist: String = "Main Artist", duration: Double = 200,
                           synced: String? = "[00:01.00]x", plain: String? = "x",
                           instrumental: Bool = false) -> LyricsCandidate {
        LyricsCandidate(trackName: title, artistName: artist, duration: duration,
                        instrumental: instrumental, plainLyrics: plain, syncedLyrics: synced)
    }

    @Test("Rejects candidates whose duration is too far off")
    func rejectsDuration() {
        #expect(CandidateRanker.best(for: track, among: [candidate("Night Drive", duration: 210)]) == nil)
    }

    @Test("Prefers synced over plain")
    func prefersSynced() {
        let plain = candidate("Night Drive", synced: nil)
        let synced = candidate("Night Drive", duration: 201.5)
        #expect(CandidateRanker.best(for: track, among: [plain, synced]) == synced)
    }

    @Test("Prefers matching title and artist")
    func prefersTitleMatch() {
        let wrong = candidate("Something Else", artist: "Nobody")
        let right = candidate("Night Drive")
        #expect(CandidateRanker.best(for: track, among: [wrong, right]) == right)
    }

    @Test("Unknown track duration skips the duration filter")
    func unknownDurationAccepted() {
        let unknown = TrackInfo(id: "1", title: "Night Drive", artist: "Main Artist", album: "", duration: 0)
        #expect(CandidateRanker.best(for: unknown, among: [candidate("Night Drive")]) != nil)
    }

    @Test("An empty title does not match every candidate")
    func emptyTitleDoesNotMatchAll() {
        let untitled = TrackInfo(id: "1", title: "", artist: "Nobody Known", album: "", duration: 200)
        #expect(CandidateRanker.best(for: untitled, among: [candidate("Anything", artist: "Someone")]) == nil)
    }

    @Test("Rejects candidates matching neither title nor artist")
    func rejectsUnrelated() {
        #expect(CandidateRanker.best(for: track, among: [candidate("Other", artist: "Nobody")]) == nil)
    }
}
