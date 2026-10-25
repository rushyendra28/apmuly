import Foundation

/// Picks the best lyrics record for a track from search results.
public enum CandidateRanker {
    /// Records further than this from the track's duration are a different edit.
    public static let durationTolerance: TimeInterval = 3

    public static func best(for track: TrackInfo, among candidates: [LyricsCandidate]) -> LyricsCandidate? {
        let title = TitleNormalizer.key(TitleNormalizer.searchTitle(track.title))
        let artist = TitleNormalizer.key(TitleNormalizer.primaryArtist(track.artist))

        return candidates
            .compactMap { candidate -> (candidate: LyricsCandidate, score: Double)? in
                // Unknown track duration (0): can't filter on it, rely on title/artist.
                let durationDiff = track.duration > 0
                    ? candidate.duration.map { abs($0 - track.duration) } ?? .infinity
                    : 0
                guard durationDiff <= durationTolerance else { return nil }

                let candidateTitle = TitleNormalizer.key(TitleNormalizer.searchTitle(candidate.trackName))
                let titleScore: Double = title.isEmpty || candidateTitle.isEmpty ? 0
                    : candidateTitle == title ? 3
                    : candidateTitle.contains(title) || title.contains(candidateTitle) ? 1
                    : 0
                let artistScore: Double = !artist.isEmpty && TitleNormalizer.key(candidate.artistName).contains(artist) ? 2 : 0
                guard titleScore + artistScore > 0 else { return nil }

                return (candidate, contentScore(candidate) + titleScore + artistScore - durationDiff * 0.5)
            }
            .max { $0.score < $1.score }?
            .candidate
    }

    private static func contentScore(_ candidate: LyricsCandidate) -> Double {
        if candidate.syncedLyrics?.isEmpty == false { return 4 }
        if candidate.plainLyrics?.isEmpty == false { return 2 }
        return candidate.instrumental ? 1 : 0
    }
}
