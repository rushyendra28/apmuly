using Apmuly.Core.Matching;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Tests.Matching;

public class TitleNormalizerTests
{
    [Theory]
    [InlineData("Song Name (feat. Somebody)", "Song Name")]
    [InlineData("Song Name [ft. A & B]", "Song Name")]
    [InlineData("Song Name (with Someone)", "Song Name")]
    [InlineData("Song Name - Remastered 2011", "Song Name")]
    [InlineData("Song Name - 2011 Remaster", "Song Name")]
    [InlineData("Song Name (Remastered)", "Song Name")]
    [InlineData("Song Name (2019 Remaster)", "Song Name")]
    [InlineData("Song Name (Imanbek Remix)", "Song Name (Imanbek Remix)")]
    [InlineData("Plain", "Plain")]
    public void CleansTitlesForSearching(string input, string expected) =>
        Assert.Equal(expected, TitleNormalizer.SearchTitle(input));

    [Theory]
    [InlineData("Drake & Majid Jordan", "Drake")]
    [InlineData("21 Savage, Rich Brian", "21 Savage")]
    [InlineData("Artist feat. Other", "Artist")]
    [InlineData("Artist ft. Other", "Artist")]
    [InlineData("Artist x Other", "Artist")]
    [InlineData("Solo", "Solo")]
    public void ExtractsThePrimaryArtist(string input, string expected) =>
        Assert.Equal(expected, TitleNormalizer.PrimaryArtist(input));

    [Fact]
    public void ComparisonKeyFoldsCaseAccentsAndPunctuation() =>
        Assert.Equal(TitleNormalizer.Key("cafe deja vu"), TitleNormalizer.Key("Café — Déjà Vu!"));
}

public class CandidateRankerTests
{
    private static readonly TrackInfo Track = new("1", "Night Drive (feat. Guest)", "Main Artist & Guest", "Album", 200);

    private static LyricsCandidate Candidate(string title, string artist = "Main Artist", double duration = 200,
                                             string? synced = "[00:01.00]x", string? plain = "x", bool instrumental = false) =>
        new(title, artist, duration, instrumental, plain, synced);

    [Fact]
    public void RejectsCandidatesWhoseDurationIsTooFarOff() =>
        Assert.Null(CandidateRanker.Best(Track, [Candidate("Night Drive", duration: 210)]));

    [Fact]
    public void PrefersSyncedOverPlain()
    {
        var plain = Candidate("Night Drive", synced: null);
        var synced = Candidate("Night Drive", duration: 201.5);
        Assert.Equal(synced, CandidateRanker.Best(Track, [plain, synced]));
    }

    [Fact]
    public void PrefersMatchingTitleAndArtist()
    {
        var right = Candidate("Night Drive");
        Assert.Equal(right, CandidateRanker.Best(Track, [Candidate("Something Else", artist: "Nobody"), right]));
    }

    [Fact]
    public void UnknownTrackDurationSkipsTheDurationFilter()
    {
        var unknown = new TrackInfo("1", "Night Drive", "Main Artist", "", 0);
        Assert.NotNull(CandidateRanker.Best(unknown, [Candidate("Night Drive")]));
    }

    [Fact]
    public void AnEmptyTitleDoesNotMatchEveryCandidate()
    {
        var untitled = new TrackInfo("1", "", "Nobody Known", "", 200);
        Assert.Null(CandidateRanker.Best(untitled, [Candidate("Anything", artist: "Someone")]));
    }

    [Fact]
    public void RejectsCandidatesMatchingNeitherTitleNorArtist() =>
        Assert.Null(CandidateRanker.Best(Track, [Candidate("Other", artist: "Nobody")]));

    [Fact]
    public void TiesKeepTheFirstCandidate()
    {
        var first = Candidate("Night Drive");
        var second = Candidate("Night Drive") with { PlainLyrics = "y" };
        Assert.Same(first, CandidateRanker.Best(Track, [first, second]));
    }
}
