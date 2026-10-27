using Apmuly.Core.Media;
using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Media;

// Titles below are made-up placeholder words.
public class SourceSelectorTests
{
    private const string Apple = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";
    private const string Spotify = "Spotify.exe";
    private const string Browser = "MSEdge";

    private static SessionState S(string app, bool playing, double active) => new(app, MediaSources.Classify(app), playing, active);

    [Theory]
    [InlineData(Apple, MediaSource.AppleMusic)]
    [InlineData(Spotify, MediaSource.Spotify)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", MediaSource.Spotify)]
    [InlineData(Browser, MediaSource.Other)]
    [InlineData(null, MediaSource.Other)]
    public void ClassifiesPlayers(string? app, MediaSource expected) => Assert.Equal(expected, MediaSources.Classify(app));

    [Fact]
    public void AutoPicksThePlayingPlayer() =>
        Assert.Equal(1, SourceSelector.Pick([S(Apple, false, 50), S(Spotify, true, 10)], SourcePreference.Auto, MediaSource.AppleMusic, null));

    [Fact]
    public void AutoPrefersTheMostRecentlyStartedWhenBothPlay() =>
        Assert.Equal(0, SourceSelector.Pick([S(Apple, true, 30), S(Spotify, true, 20)], SourcePreference.Auto, MediaSource.Spotify, null));

    [Fact]
    public void AutoKeepsThePreviousPlayerWhenNothingPlays() =>
        Assert.Equal(0, SourceSelector.Pick([S(Apple, false, 5), S(Spotify, false, 90)], SourcePreference.Auto, MediaSource.AppleMusic, null));

    [Fact]
    public void AutoFallsBackToTheMostRecentlyActive() =>
        Assert.Equal(1, SourceSelector.Pick([S(Apple, false, 5), S(Spotify, false, 90)], SourcePreference.Auto, null, null));

    [Fact]
    public void OtherSessionsAreIgnoredUnlessNoPlayerRuns()
    {
        Assert.Equal(1, SourceSelector.Pick([S(Browser, true, 99), S(Spotify, false, 1)], SourcePreference.Auto, null, Browser));
        Assert.Equal(0, SourceSelector.Pick([S(Browser, true, 99)], SourcePreference.Auto, null, Browser));
        Assert.Null(SourceSelector.Pick([S(Browser, true, 99)], SourcePreference.Auto, null, null));
    }

    [Fact]
    public void ManualPreferenceAlwaysFollowsThatPlayer()
    {
        SessionState[] both = [S(Apple, true, 99), S(Spotify, false, 1)];
        Assert.Equal(1, SourceSelector.Pick(both, SourcePreference.Spotify, MediaSource.AppleMusic, null));
        Assert.Equal(0, SourceSelector.Pick(both, SourcePreference.AppleMusic, null, null));
        Assert.Null(SourceSelector.Pick([S(Apple, true, 99)], SourcePreference.Spotify, null, Apple));
    }

    [Fact]
    public void ActivityRecordsStartsAndTrackChanges()
    {
        var activity = new SessionActivity();
        Assert.Equal(double.NegativeInfinity, activity.Observe(Spotify, false, null, 1).LastActive);
        Assert.Equal(2, activity.Observe(Spotify, true, "glimmer", 2).LastActive);
        Assert.Equal(2, activity.Observe(Spotify, true, null, 3).LastActive);
        Assert.Equal(2, activity.Observe(Spotify, true, "glimmer", 4).LastActive);
        Assert.Equal(5, activity.Observe(Spotify, true, "falcon", 5).LastActive);
        activity.Retain([]);
        Assert.Equal(6, activity.Observe(Spotify, true, "falcon", 6).LastActive);
    }

    [Fact]
    public void PreferenceCyclesAndParses()
    {
        Assert.Equal(SourcePreference.AppleMusic, SourcePreference.Auto.Next());
        Assert.Equal(SourcePreference.Spotify, SourcePreference.AppleMusic.Next());
        Assert.Equal(SourcePreference.Auto, SourcePreference.Spotify.Next());
        Assert.Equal(SourcePreference.Spotify, MediaSources.ParsePreference("spotify"));
        Assert.Null(MediaSources.ParsePreference("tidal"));
    }
}

public class SpotifySamplingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static MediaReading Reading(string app, string title, string artist, double seconds) => new(app, title, artist, "",
        new TimelineSnapshot(TimeSpan.FromSeconds(5), Now, TimeSpan.Zero, TimeSpan.FromSeconds(seconds), PlayerState.Playing));

    [Fact]
    public void TrackIdsArePrefixedBySource()
    {
        var spotify = MediaSampler.From(Reading("Spotify.exe", "Glimmer", "Falcon", 200), 1, Now).Track!;
        var apple = MediaSampler.From(Reading("AppleInc.AppleMusicWin!App", "Glimmer", "Falcon", 200), 1, Now).Track!;
        Assert.StartsWith("spotify|", spotify.Id);
        Assert.StartsWith("applemusic|", apple.Id);
        Assert.NotEqual(spotify.Id, apple.Id);
        Assert.Equal("Spotify", spotify.Source);
        Assert.Equal("Apple Music", apple.Source);
    }

    [Fact]
    public void SpotifyArtistIsNotSplit()
    {
        var track = MediaSampler.From(Reading("Spotify.exe", "Glimmer", "Velvet — Harbor", 200), 1, Now).Track!;
        Assert.Equal("Velvet — Harbor", track.Artist);
        Assert.Equal("", track.Album);
    }

    [Theory]
    [InlineData("Advertisement", "Someone", 200, true)]
    [InlineData("Spotify", "", 200, true)]
    [InlineData("Glimmer", "", 30, true)]
    [InlineData("Glimmer", "Falcon", 30, false)]
    [InlineData("Glimmer", "", 200, false)]
    public void DetectsSpotifyAds(string title, string artist, double seconds, bool ad) =>
        Assert.Equal(ad, MediaSampler.From(Reading("Spotify.exe", title, artist, seconds), 1, Now).Track!.SkipLyrics);

    [Fact]
    public void AppleMusicIsNeverTreatedAsAnAd() =>
        Assert.False(MediaSampler.From(Reading("AppleInc.AppleMusicWin!App", "Spotify", "", 20), 1, Now).Track!.SkipLyrics);
}
