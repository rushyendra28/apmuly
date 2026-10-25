using Apmuly.Core.Lyrics;
using Apmuly.Core.Provider;
using Apmuly.Core.Stage;

namespace Apmuly.Core.Tests.Stage;

public class StageStatusTests
{
    private static readonly TrackInfo Track = new("T1", "Glimmer Falcon", "Velvet Harbor", "", 200);

    private static DeckRender Deck(LyricsLoad? lyrics) => new("T1", Track, lyrics, 0, true, 1);

    private static LyricsLoad Loaded(LyricsResult result) => new LyricsLoad.Loaded(result);

    [Fact]
    public void DescribesEveryState()
    {
        Assert.Equal(StageStatus.NothingPlaying, StageStatus.For(null).Title);
        Assert.Equal(new StatusPayload(StageStatus.Looking, "Glimmer Falcon — Velvet Harbor"), StageStatus.For(Deck(null)));
        Assert.Equal(StageStatus.Looking, StageStatus.For(Deck(new LyricsLoad.Loading())).Title);
        Assert.Equal(StageStatus.Failed, StageStatus.For(Deck(new LyricsLoad.Failed(LyricsErrorKind.Network, null))).Title);
        Assert.Equal(StageStatus.Instrumental, StageStatus.For(Deck(Loaded(new LyricsResult.Instrumental()))).Title);
        Assert.Equal(StageStatus.NotFound, StageStatus.For(Deck(Loaded(new LyricsResult.NotFound()))).Title);
        var unsynced = LyricsTimeline.Unsynced("plain", 100);
        Assert.Equal(StageStatus.Unsynced, StageStatus.For(Deck(Loaded(new LyricsResult.Found(unsynced)))).Title);
        var synced = LyricsTimeline.Build(LrcParser.Parse("[00:01.00]glimmer"), 100);
        Assert.Equal(StatusPayload.None, StageStatus.For(Deck(Loaded(new LyricsResult.Found(synced)))));
    }

    [Fact]
    public void LyricsStateForSetTrack()
    {
        Assert.Equal("loading", StageStatus.LyricsState(null));
        Assert.Equal("loading", StageStatus.LyricsState(new LyricsLoad.Loading()));
        Assert.Equal("none", StageStatus.LyricsState(new LyricsLoad.Failed(LyricsErrorKind.Network, null)));
        Assert.Equal("none", StageStatus.LyricsState(Loaded(new LyricsResult.Found(LyricsTimeline.Unsynced("p", 10)))));
        Assert.Equal("synced", StageStatus.LyricsState(Loaded(new LyricsResult.Found(LyricsTimeline.Build(LrcParser.Parse("[00:01.00]x"), 10)))));
    }
}
