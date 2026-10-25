using Apmuly.Core.Lyrics;
using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Apmuly.Core.Sync;
using Apmuly.Core.Tests.Support;
using Apmuly.Core.Visuals;

namespace Apmuly.Core.Tests.Stage;

// Lyrics are made-up placeholder words.
public class NowPlayingModelTests
{
    private const string Lrc = "[00:01.00]glimmer falcon harbor\n[00:05.00]velvet moon tears\n[00:09.00]";
    private static LyricsCandidate Synced => new("Glimmer Falcon", "Velvet Harbor", 200, false, null, Lrc);

    [Fact]
    public async Task NewTrackLoadsLyricsInsightsAndVersions()
    {
        using var release = new ManualResetEventSlim();
        using var h = new ModelHarness(FakeProvider.Gated(Synced, release));
        var track = ModelHarness.Track();
        h.Play(track, 10, 0);
        Assert.IsType<LyricsLoad.Loading>(h.Model.Render(0).Current!.Lyrics);
        release.Set();
        await h.SettleAsync();

        var deck = h.Model.Render(1).Current!;
        var found = Assert.IsType<LyricsResult.Found>(Assert.IsType<LyricsLoad.Loaded>(deck.Lyrics).Result);
        Assert.Equal(LyricsTiming.LineSynced, found.Document.Timing);
        var assets = h.Model.Assets(track.Id);
        Assert.Equal(1, assets.LyricsVersion);
        Assert.Equal(1, assets.InsightVersion);
        Assert.NotNull(assets.Insights);
        Assert.Equal(11, deck.LyricsPosition, 6);
    }

    [Fact]
    public async Task LyricsAreLoadedOncePerTrack()
    {
        var provider = FakeProvider.Returning(Synced);
        using var h = new ModelHarness(provider);
        var track = ModelHarness.Track();
        h.Play(track, 10, 0);
        h.Play(track, 11, 1);
        await h.SettleAsync();
        h.Play(track, 12, 2);
        await h.SettleAsync();
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task TransientFailuresAreRetriedWithBackoffThenSucceed()
    {
        var provider = new FakeProvider(() => throw LyricsException.Server(503), () => throw LyricsException.Server(503), () => Synced);
        using var h = new ModelHarness(provider);
        h.Play(ModelHarness.Track(), 10, 0);
        for (var i = 0; i < 10 && h.Model.Render(0).Current!.Lyrics is not LyricsLoad.Loaded; i++)
        {
            await h.SettleAsync();
        }
        Assert.IsType<LyricsLoad.Loaded>(h.Model.Render(0).Current!.Lyrics);
        Assert.Equal(3, provider.Calls);
    }

    [Fact]
    public async Task RetriesStopAfterThePolicyIsExhausted()
    {
        var provider = FakeProvider.Failing(LyricsException.Server(503));
        using var h = new ModelHarness(provider);
        h.Play(ModelHarness.Track(), 10, 0);
        for (var i = 0; i < 10; i++)
        {
            await h.SettleAsync();
        }
        Assert.Equal(1 + LyricRetryPolicy.Default.MaxRetries, provider.Calls);
        Assert.Equal(new LyricsLoad.Failed(LyricsErrorKind.Server, 503), h.Model.Render(0).Current!.Lyrics);
    }

    [Fact]
    public async Task PermanentFailuresAreNotRetriedButRCanRetry()
    {
        var provider = new FakeProvider(() => throw new LyricsException(LyricsErrorKind.Decoding), () => Synced);
        using var h = new ModelHarness(provider);
        h.Play(ModelHarness.Track(), 10, 0);
        await h.SettleAsync();
        Assert.IsType<LyricsLoad.Failed>(h.Model.Render(0).Current!.Lyrics);
        Assert.Equal(1, provider.Calls);

        h.Model.RetryLyrics();
        await h.SettleAsync();
        Assert.IsType<LyricsLoad.Loaded>(h.Model.Render(0).Current!.Lyrics);
    }

    [Fact]
    public async Task NudgesShiftTheLyricsPositionAndPersistPerSong()
    {
        using var h = new ModelHarness(FakeProvider.Returning(Synced));
        var track = ModelHarness.Track();
        h.Play(track, 10, 0, PlayerState.Paused);
        await h.SettleAsync();
        h.Model.AdjustOffset(-NowPlayingModel.OffsetStep);
        h.Model.AdjustOffset(-NowPlayingModel.OffsetStep);
        Assert.Equal(10.1, h.Model.Render(0).Current!.LyricsPosition, 6);
        Assert.Equal(-0.1, h.Offsets.Get(track.Id), 6);
        Assert.Equal(-0.1, h.Model.CurrentOffset, 6);
        h.Model.ResetOffset();
        Assert.Equal(10, h.Model.Render(0).Current!.LyricsPosition, 6);
    }

    [Fact]
    public async Task UnsyncedAndMissingLyricsAreLoadedWithoutInsights()
    {
        using var h = new ModelHarness(FakeProvider.Returning(new LyricsCandidate("T", "A", 200, false, "plain words", null)));
        var track = ModelHarness.Track();
        h.Play(track, 10, 0);
        await h.SettleAsync();
        var result = Assert.IsType<LyricsLoad.Loaded>(h.Model.Render(0).Current!.Lyrics).Result;
        Assert.Equal(LyricsTiming.Unsynced, Assert.IsType<LyricsResult.Found>(result).Document.Timing);
        Assert.Equal(0, h.Model.Assets(track.Id).InsightVersion);
    }

    [Fact]
    public void ArtworkIsAcceptedOnceAndAttemptsAreCapped()
    {
        using var h = new ModelHarness(FakeProvider.Returning(null));
        var track = ModelHarness.Track();
        Assert.False(h.Model.NeedsArtwork(track.Id));
        h.Play(track, 10, 0);
        Assert.True(h.Model.NeedsArtwork(track.Id));
        for (var i = 0; i < NowPlayingModel.MaxArtworkAttempts; i++)
        {
            h.Model.NoteArtworkMissing(track.Id);
        }
        Assert.False(h.Model.NeedsArtwork(track.Id));
        Assert.Throws<ArgumentException>(() => h.Model.SetArtwork(track.Id, "https://example.invalid/x.png", ThemePalette.Fallback));
        h.Model.SetArtwork(track.Id, "data:image/jpeg;base64,AAAA", ThemePalette.Fallback);
        Assert.Equal("data:image/jpeg;base64,AAAA", h.Model.Assets(track.Id).ArtworkUrl);
    }

    [Fact]
    public void StoppingClearsTheCurrentDeck()
    {
        using var h = new ModelHarness(FakeProvider.Returning(null));
        h.Play(ModelHarness.Track(), 10, 0);
        h.Model.Ingest(null, new PlaybackSample("", 0, 0, PlayerState.Stopped, 1, 0));
        Assert.Null(h.Model.Render(1).Current);
        Assert.Null(h.Model.CurrentTrackId);
    }
}
