using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Support;

/// <summary>A NowPlayingModel over temp-dir stores, a fake provider and instant retry delays.</summary>
public sealed class ModelHarness : IDisposable
{
    private readonly TempDirectory _dir = new();

    public ModelHarness(ILyricsProvider provider, LyricRetryPolicy? retry = null)
    {
        Provider = provider;
        Offsets = new OffsetStore(_dir.File("offsets.json"));
        Model = new NowPlayingModel(new LyricsService(provider, new LyricsCache(_dir.File("lyrics"))), Offsets,
            new TimingStore(_dir.File("timing")), retry, (_, _) => Task.CompletedTask);
    }

    public ILyricsProvider Provider { get; }

    public OffsetStore Offsets { get; }

    public NowPlayingModel Model { get; }

    public string SettingsPath => _dir.File("settings.json");

    public static TrackInfo Track(string id = "T1", string title = "Glimmer Falcon", double duration = 200) =>
        new(id, title, "Velvet Harbor", "Copper Meadow", duration);

    public void Play(TrackInfo track, double position, double host, PlayerState state = PlayerState.Playing) =>
        Model.Ingest(track, new PlaybackSample(track.Id, position, track.Duration, state, host, 0));

    public Task SettleAsync() => Model.WhenIdleAsync();

    /// <summary>Lets background lyric loads (and their retries) finish before the temp directory goes away.</summary>
    public void Dispose()
    {
        for (var i = 0; i < 20; i++)
        {
            if (!Model.WhenIdleAsync().Wait(TimeSpan.FromSeconds(5)))
            {
                break;
            }
        }
        Model.Dispose();
        _dir.Dispose();
    }
}
