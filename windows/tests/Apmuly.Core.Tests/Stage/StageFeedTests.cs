using System.Text.Json;
using Apmuly.Core.Audio;
using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Apmuly.Core.Tests.Support;
using Apmuly.Core.Visuals;

namespace Apmuly.Core.Tests.Stage;

// Lyrics are made-up placeholder words.
public class StageFeedTests
{
    private const string Lrc = "[00:01.00]glimmer moon harbor\n[00:05.00]velvet tears\n[00:09.00]";
    private static LyricsCandidate Synced => new("Glimmer Falcon", "Velvet Harbor", 200, false, null, Lrc);

    private static string[] Fns(IEnumerable<HostCall> calls) => calls.Select(c => c.Fn).ToArray();

    private static JsonElement Arg(IEnumerable<HostCall> calls, string fn) =>
        JsonDocument.Parse(calls.Single(c => c.Fn == fn).Arg).RootElement;

    [Fact]
    public async Task SendsEachAssetOnceThenTheFrame()
    {
        // The provider holds its answer until released, so the first tick sees "loading".
        using var release = new ManualResetEventSlim();
        using var h = new ModelHarness(FakeProvider.Gated(Synced, release));
        var feed = new StageFeed();
        var track = ModelHarness.Track();
        h.Play(track, 10, 0);

        var first = feed.Tick(h.Model, 0.1, null);
        Assert.Equal(["setTrack", "setStatus", "setFrame"], Fns(first));
        Assert.Equal("loading", Arg(first, "setTrack").GetProperty("lyrics").GetString());
        release.Set();
        await h.SettleAsync();
        h.Model.SetArtwork(track.Id, "data:image/jpeg;base64,AAAA", ThemePalette.Fallback);

        var calls = feed.Tick(h.Model, 0.2, null);
        Assert.Equal(["setTrack", "setLyrics", "setPalette", "setInsights", "setArtwork", "setStatus", "setFrame"], Fns(calls));
        Assert.Equal("synced", Arg(calls, "setTrack").GetProperty("lyrics").GetString());
        Assert.False(Arg(calls, "setTrack").TryGetProperty("source", out _)); // harness track has no source
        Assert.Equal("", Arg(calls, "setStatus").GetProperty("title").GetString());

        Assert.Equal(["setFrame"], Fns(feed.Tick(h.Model, 0.3, null)));
    }

    [Fact]
    public async Task ResetResendsEverything()
    {
        using var h = new ModelHarness(FakeProvider.Returning(Synced));
        var feed = new StageFeed();
        h.Play(ModelHarness.Track(), 10, 0);
        await h.SettleAsync();
        feed.Tick(h.Model, 0.1, null);
        feed.Reset();
        Assert.Equal(["setTrack", "setLyrics", "setInsights", "setStatus", "setFrame"], Fns(feed.Tick(h.Model, 0.2, null)));
    }

    [Fact]
    public async Task LyricsPayloadMatchesTheRendererContract()
    {
        using var h = new ModelHarness(FakeProvider.Returning(Synced));
        h.Play(ModelHarness.Track(), 10, 0);
        await h.SettleAsync();
        var lyrics = Arg(new StageFeed().Tick(h.Model, 0.1, null), "setLyrics");

        Assert.Equal("T1", lyrics.GetProperty("trackID").GetString());
        Assert.Equal("lineSynced", lyrics.GetProperty("timing").GetString());
        var lines = lyrics.GetProperty("lines");
        Assert.Equal(3, lines.GetArrayLength());
        var word = lines[0].GetProperty("w")[1];
        Assert.Equal("moon", word.GetProperty("t").GetString());
        Assert.Equal(["t", "s", "e"], word.EnumerateObject().Select(p => p.Name)); // words carry no icon field
        var gap = lines[2];
        Assert.Equal("", gap.GetProperty("t").GetString());
        Assert.Equal(0, gap.GetProperty("w").GetArrayLength());
        Assert.True(lines[0].GetProperty("s").GetDouble() < lines[0].GetProperty("e").GetDouble());
    }

    [Fact]
    public async Task InsightsPayloadMatchesTheRendererContract()
    {
        using var h = new ModelHarness(FakeProvider.Returning(Synced));
        h.Play(ModelHarness.Track(), 10, 0);
        await h.SettleAsync();
        var insights = Arg(new StageFeed().Tick(h.Model, 0.1, null), "setInsights");
        var lines = insights.GetProperty("lines");
        var first = lines[0];
        Assert.Equal(0, first.GetProperty("index").GetInt32());
        Assert.False(first.GetProperty("burst").GetBoolean());
        var moon = first.GetProperty("objects")[0];
        Assert.Equal("moon", moon.GetProperty("word").GetString());
        Assert.Equal(1, moon.GetProperty("wordIndex").GetInt32());
        Assert.True(lines[1].GetProperty("burst").GetBoolean()); // "tears": sadness
    }

    [Fact]
    public void FramePayloadMatchesTheRendererContract()
    {
        using var h = new ModelHarness(FakeProvider.Returning(null));
        h.Play(ModelHarness.Track(), 10, 0);
        var energy = new EnergySnapshot(7, 0.25, 0.5, Enumerable.Repeat(0.5, 24).ToArray());
        var frame = Arg(new StageFeed().Tick(h.Model, 2, energy), "setFrame");
        var deck = frame.GetProperty("decks")[0];
        Assert.Equal("T1", deck.GetProperty("trackID").GetString());
        Assert.Equal("current", deck.GetProperty("role").GetString());
        Assert.Equal(12, deck.GetProperty("position").GetDouble(), 6);
        Assert.True(deck.GetProperty("isRunning").GetBoolean());
        Assert.Equal(1, deck.GetProperty("opacity").GetDouble());
        Assert.Equal(7, frame.GetProperty("energy").GetProperty("seq").GetInt32());
        Assert.Equal(24, frame.GetProperty("energy").GetProperty("bands").GetArrayLength());

        var silent = Arg(new StageFeed().Tick(h.Model, 2, null), "setFrame");
        Assert.False(silent.TryGetProperty("energy", out _));
    }

    [Fact]
    public void NothingPlayingSendsStatusAndAnEmptyFrame()
    {
        using var h = new ModelHarness(FakeProvider.Returning(null));
        var calls = new StageFeed().Tick(h.Model, 0, null);
        Assert.Equal(["setStatus", "setFrame"], Fns(calls));
        Assert.Equal(StageStatus.NothingPlaying, Arg(calls, "setStatus").GetProperty("title").GetString());
        Assert.Equal(0, Arg(calls, "setFrame").GetProperty("decks").GetArrayLength());
    }

    [Fact]
    public void HostCallsWrapTheArgumentAsAJsonString()
    {
        var json = HostCall.SetStatus(new StatusPayload("Looking for lyrics…", "a \"quoted\" title")).ToJson();
        var root = JsonDocument.Parse(json).RootElement;
        Assert.Equal("setStatus", root.GetProperty("fn").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("arg").ValueKind);
        var arg = JsonDocument.Parse(root.GetProperty("arg").GetString()!).RootElement;
        Assert.Equal("a \"quoted\" title", arg.GetProperty("detail").GetString());

        var theme = JsonDocument.Parse(HostCall.SetTheme(StageTheme.Paper).ToJson()).RootElement;
        Assert.Equal("paper", theme.GetProperty("arg").GetString());
        var options = HostCall.SetOptions(StageSession.OptionsFor(StageSettings.Default with { Pacing = false })).Arg;
        Assert.Equal("""{"forwardKeys":true,"sheet":true,"pacing":false}""", options);
    }
}
