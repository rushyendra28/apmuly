using Apmuly.Core.Media;
using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Media;

public class MetadataNormalizerTests
{
    [Fact]
    public void SplitsAppleMusicArtistAlbumWhenAlbumIsEmpty()
    {
        var m = MetadataNormalizer.Normalize("Glimmer Falcon", "Velvet Harbor — Copper Meadow", "");
        Assert.Equal(new MediaMetadata("Glimmer Falcon", "Velvet Harbor", "Copper Meadow"), m);
    }

    [Fact]
    public void KeepsArtistWhenAlbumIsPresent()
    {
        var m = MetadataNormalizer.Normalize("T", "A — B", "Album");
        Assert.Equal("A — B", m.Artist);
        Assert.Equal("Album", m.Album);
    }

    [Fact]
    public void StripsControlCharactersTrimsAndCaps()
    {
        var m = MetadataNormalizer.Normalize("  line\u0000break\n ", null, new string('x', 5000));
        Assert.Equal("line break", m.Title);
        Assert.Equal("", m.Artist);
        Assert.Equal(MetadataNormalizer.MaxFieldLength, m.Album.Length);
    }
}

public class TrackIdentityTests
{
    [Fact]
    public void IsStableAndRoundsDuration()
    {
        var a = TrackIdentity.Compute("Glimmer", "Falcon", "Harbor", 199.6);
        Assert.Equal(a, TrackIdentity.Compute("Glimmer", "Falcon", "Harbor", 200.2));
        Assert.Matches("^[0-9a-f]{16}$", a);
    }

    [Fact]
    public void DiffersWhenAnyFieldDiffers()
    {
        var a = TrackIdentity.Compute("Glimmer", "Falcon", "Harbor", 200);
        Assert.NotEqual(a, TrackIdentity.Compute("Glimmer", "Falcon", "Harbor", 230));
        Assert.NotEqual(a, TrackIdentity.Compute("Glimmer", "Falcon", "Velvet", 200));
        Assert.NotEqual(TrackIdentity.Compute("a|b", "c", "", 0), TrackIdentity.Compute("a", "b|c", "", 0));
    }
}

public class SessionPickerTests
{
    [Fact]
    public void PrefersAppleMusic() =>
        Assert.Equal(1, SessionPicker.Pick(["Spotify.exe", "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"], "Spotify.exe"));

    [Fact]
    public void FallsBackToTheCurrentSession() => Assert.Equal(1, SessionPicker.Pick(["a", "b"], "b"));

    [Fact]
    public void NothingToFollow()
    {
        Assert.Null(SessionPicker.Pick([], null));
        Assert.Null(SessionPicker.Pick(["a"], "zzz"));
        Assert.Null(SessionPicker.Pick([null, "a"], null));
    }
}

public class MediaSamplerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static MediaReading Reading(string title, PlayerState state = PlayerState.Playing) => new(
        "AppleInc.AppleMusicWin!App", title, "Velvet Harbor — Copper Meadow", "",
        new TimelineSnapshot(TimeSpan.FromSeconds(30), Now, TimeSpan.Zero, TimeSpan.FromSeconds(200), state));

    [Fact]
    public void BuildsTrackAndSampleWithTheSameId()
    {
        var result = MediaSampler.From(Reading("Glimmer Falcon"), 50, Now);
        Assert.NotNull(result.Track);
        Assert.Equal(result.Track!.Id, result.Sample.TrackId);
        Assert.Equal("Velvet Harbor", result.Track.Artist);
        Assert.Equal("Copper Meadow", result.Track.Album);
        Assert.Equal(200, result.Track.Duration);
        Assert.Equal(30, result.Sample.Position);
    }

    [Fact]
    public void NoReadingEmptyTitleOrStoppedIsStopped()
    {
        Assert.Equal(PlayerState.Stopped, MediaSampler.From(null, 1, Now).Sample.State);
        Assert.Null(MediaSampler.From(Reading("   "), 1, Now).Track);
        Assert.Equal("", MediaSampler.From(Reading("T", PlayerState.Stopped), 1, Now).Sample.TrackId);
    }
}
