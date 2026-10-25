using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Apmuly.Core.Sync;
using Apmuly.Core.Tests.Support;

namespace Apmuly.Core.Tests.Stage;

public class StageSessionTests
{
    private static (StageSession Session, ModelHarness Harness) Make()
    {
        var h = new ModelHarness(FakeProvider.Returning(null));
        return (new StageSession(new SettingsStore(h.SettingsPath), h.Model, new StageFeed()), h);
    }

    [Fact]
    public void ReadySendsOptionsThemeAndMode()
    {
        var (session, h) = Make();
        using (h)
        {
            Assert.False(session.IsReady);
            var calls = session.Receive("""{"type":"ready"}""");
            Assert.True(session.IsReady);
            Assert.Equal(["setOptions", "setTheme", "setMode"], calls.Select(c => c.Fn));
            Assert.Equal("""{"forwardKeys":true,"sheet":true,"pacing":true}""", calls[0].Arg);
            Assert.Equal(["kinetic", "visualizer"], calls.Skip(1).Select(c => c.Arg));
            session.PageLost();
            Assert.False(session.IsReady);
        }
    }

    [Theory]
    [InlineData("1", "kinetic")]
    [InlineData("2", "grunge")]
    [InlineData("3", "paper")]
    [InlineData("ctrl+3", "paper")]
    [InlineData("ctrl+1", "kinetic")]
    public void NumberKeysSelectThemes(string key, string theme)
    {
        var (session, h) = Make();
        using (h)
        {
            session.HandleKey("2");
            var calls = session.HandleKey(key);
            Assert.Equal(theme, session.Settings.Theme.WireName());
            Assert.Equal(theme == "grunge" ? [] : [theme], calls.Select(c => c.Arg));
        }
    }

    [Fact]
    public void TCyclesThemesAndSettingsPersist()
    {
        var (session, h) = Make();
        using (h)
        {
            Assert.Equal("grunge", session.HandleKey("t").Single().Arg);
            Assert.Equal("paper", session.HandleKey("T").Single().Arg);
            Assert.Equal("kinetic", session.HandleKey("ctrl+t").Single().Arg);
            session.HandleKey("3");
            var toggled = session.Receive("""{"type":"key","message":"l"}""");
            Assert.Equal(("setMode", "lyrics"), (toggled.Single().Fn, toggled.Single().Arg));
            Assert.Equal(new StageSettings(StageTheme.Paper, StageMode.Lyrics), new SettingsStore(h.SettingsPath).Load());
        }
    }

    [Fact]
    public void PTogglesPacingSendsOptionsAndPersists()
    {
        var (session, h) = Make();
        using (h)
        {
            Assert.True(session.Settings.Pacing);
            var off = session.HandleKey("p").Single();
            Assert.Equal(("setOptions", """{"forwardKeys":true,"sheet":true,"pacing":false}"""), (off.Fn, off.Arg));
            Assert.False(new SettingsStore(h.SettingsPath).Load().Pacing);
            Assert.Equal("""{"forwardKeys":true,"sheet":true,"pacing":false}""", session.Receive("""{"type":"ready"}""")[0].Arg);
            var on = session.HandleKey("P").Single();
            Assert.Equal("""{"forwardKeys":true,"sheet":true,"pacing":true}""", on.Arg);
            Assert.True(new SettingsStore(h.SettingsPath).Load().Pacing);
        }
    }

    [Fact]
    public void NudgeKeysAdjustTheCurrentSong()
    {
        var (session, h) = Make();
        using (h)
        {
            h.Play(ModelHarness.Track(), 10, 0, PlayerState.Paused);
            session.HandleKey("]");
            session.HandleKey("]");
            session.HandleKey("[");
            Assert.Equal(0.05, h.Model.CurrentOffset, 6);
            session.HandleKey("0");
            Assert.Equal(0, h.Model.CurrentOffset);
            Assert.Empty(session.HandleKey("q"));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":"key"}""")]
    [InlineData("""{"type":"key","message":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"}""")]
    [InlineData("""{"type":"navigate","message":"https://example.invalid"}""")]
    public void InvalidPageMessagesAreIgnored(string? json)
    {
        Assert.Null(PageMessage.Parse(json));
        var (session, h) = Make();
        using (h)
        {
            Assert.Empty(session.Receive(json));
        }
    }

    [Fact]
    public void ErrorMessagesAreParsedAndTruncated()
    {
        var message = PageMessage.Parse($$"""{"type":"error","message":"{{new string('e', 5000)}}"}""");
        Assert.Equal(PageMessageType.Error, message!.Type);
        Assert.Equal(2000, message.Message!.Length);
    }
}

public class SettingsAndOffsetStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void SettingsDefaultRoundTripAndRejectUnknownValues()
    {
        var store = new SettingsStore(_dir.File("settings.json"));
        Assert.Equal(StageSettings.Default, store.Load());
        Assert.True(store.Save(new StageSettings(StageTheme.Paper, StageMode.Lyrics, Pacing: false)));
        Assert.Equal(new StageSettings(StageTheme.Paper, StageMode.Lyrics, Pacing: false), store.Load());
        File.WriteAllText(_dir.File("settings.json"), """{"theme":"fisheye","mode":"lyrics"}""");
        Assert.Equal(new StageSettings(StageTheme.Kinetic, StageMode.Lyrics, Pacing: true), store.Load());
        File.WriteAllText(_dir.File("settings.json"), """{"theme":"neon","pacing":"yes"}""");
        Assert.Equal(StageSettings.Default, store.Load());
        File.WriteAllText(_dir.File("settings.json"), "{broken");
        Assert.Equal(StageSettings.Default, store.Load());
    }

    [Fact]
    public void OffsetsRoundToMillisecondsPersistAndValidate()
    {
        var store = new OffsetStore(_dir.File("offsets.json"));
        Assert.Equal(0, store.Get("a"));
        Assert.Equal((0.123, true), store.Set("a", 0.12345));
        Assert.Equal(0.123, new OffsetStore(_dir.File("offsets.json")).Get("a"));
        Assert.Equal(0, store.Set("b", double.NaN).Value);
        store.Set("a", 0);
        Assert.Equal(0, new OffsetStore(_dir.File("offsets.json")).Get("a"));
        File.WriteAllText(_dir.File("offsets.json"), """{"x":1e9,"y":0.5}""");
        var reloaded = new OffsetStore(_dir.File("offsets.json"));
        Assert.Equal(0, reloaded.Get("x"));
        Assert.Equal(0.5, reloaded.Get("y"));
    }
}

public class KeyCommandTests
{
    [Theory]
    [InlineData("l", StageCommandKind.ToggleMode)]
    [InlineData("L", StageCommandKind.ToggleMode)]
    [InlineData("ctrl+l", StageCommandKind.ToggleMode)]
    [InlineData("t", StageCommandKind.NextTheme)]
    [InlineData("0", StageCommandKind.ResetNudge)]
    [InlineData("r", StageCommandKind.RetryLyrics)]
    [InlineData("R", StageCommandKind.RetryLyrics)]
    [InlineData("p", StageCommandKind.TogglePacing)]
    [InlineData("P", StageCommandKind.TogglePacing)]
    public void MapsMacShortcuts(string key, StageCommandKind kind) => Assert.Equal(kind, KeyCommands.For(key)?.Kind);

    [Fact]
    public void BracketsNudgeByFiftyMilliseconds()
    {
        Assert.Equal(-0.05, KeyCommands.For("[")!.Seconds);
        Assert.Equal(0.05, KeyCommands.For("]")!.Seconds);
        Assert.Null(KeyCommands.For("x"));
        Assert.Null(KeyCommands.For(null));
    }

    [Fact]
    public void ThemesCycleThroughAllThree()
    {
        Assert.Equal(StageTheme.Grunge, StageTheme.Kinetic.Next());
        Assert.Equal(StageTheme.Paper, StageTheme.Grunge.Next());
        Assert.Equal(StageTheme.Kinetic, StageTheme.Paper.Next());
        Assert.Equal(StageTheme.Paper, StageOptionNames.ParseTheme("paper"));
        Assert.Null(StageOptionNames.ParseTheme("fisheye"));
        Assert.Null(StageOptionNames.ParseTheme("Paper"));
    }
}
