using Apmuly.Core.Provider;

namespace Apmuly.Core.Matching;

/// <summary>Picks the best lyrics record for a track from search results.</summary>
public static class CandidateRanker
{
    /// <summary>Records further than this from the track's duration are a different edit.</summary>
    public const double DurationTolerance = 3;

    public static LyricsCandidate? Best(TrackInfo track, IEnumerable<LyricsCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(candidates);
        var title = TitleNormalizer.Key(TitleNormalizer.SearchTitle(track.Title));
        var artist = TitleNormalizer.Key(TitleNormalizer.PrimaryArtist(track.Artist));

        // First of the highest-scoring candidates (like Swift's max(by:)).
        LyricsCandidate? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            if (Score(track, title, artist, candidate) is { } score && score > bestScore)
            {
                (best, bestScore) = (candidate, score);
            }
        }
        return best;
    }

    /// <summary>Score of one candidate, or null when it can't be this track.</summary>
    private static double? Score(TrackInfo track, string title, string artist, LyricsCandidate candidate)
    {
        // Unknown track duration (0): can't filter on it, rely on title/artist.
        var durationDiff = track.Duration > 0
            ? candidate.Duration is { } d ? Math.Abs(d - track.Duration) : double.PositiveInfinity
            : 0;
        if (durationDiff > DurationTolerance)
        {
            return null;
        }

        var candidateTitle = TitleNormalizer.Key(TitleNormalizer.SearchTitle(candidate.TrackName));
        double titleScore = title.Length == 0 || candidateTitle.Length == 0 ? 0
            : candidateTitle == title ? 3
            : candidateTitle.Contains(title, StringComparison.Ordinal) || title.Contains(candidateTitle, StringComparison.Ordinal) ? 1
            : 0;
        double artistScore = artist.Length > 0
            && TitleNormalizer.Key(candidate.ArtistName).Contains(artist, StringComparison.Ordinal) ? 2 : 0;
        if (titleScore + artistScore <= 0)
        {
            return null;
        }
        return ContentScore(candidate) + titleScore + artistScore - durationDiff * 0.5;
    }

    private static double ContentScore(LyricsCandidate candidate)
    {
        if (!string.IsNullOrEmpty(candidate.SyncedLyrics))
        {
            return 4;
        }
        if (!string.IsNullOrEmpty(candidate.PlainLyrics))
        {
            return 2;
        }
        return candidate.Instrumental ? 1 : 0;
    }
}
