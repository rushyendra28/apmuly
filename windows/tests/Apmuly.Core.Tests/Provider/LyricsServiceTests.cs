using Apmuly.Core.Lyrics;
using Apmuly.Core.Provider;
using Apmuly.Core.Tests.Support;

namespace Apmuly.Core.Tests.Provider;

public sealed class LyricsCacheTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly LyricsCandidate Synced = new("T", "A", 100, false, null, "[00:01.00]x");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void RoundTripsACandidateThroughDisk()
    {
        new LyricsCache(_dir.Path).Store(Synced, "A1B2");
        Assert.Equal(new CacheLookup.Hit(Synced), new LyricsCache(_dir.Path).Lookup("A1B2"));
    }

    [Fact]
    public void NegativeEntriesExpire()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        new LyricsCache(_dir.Path, () => start).Store(null, "A1B2");
        Assert.Equal(new CacheLookup.Hit(null), new LyricsCache(_dir.Path, () => start).Lookup("A1B2"));
        var later = start + LyricsCache.NegativeTtl + TimeSpan.FromSeconds(1);
        Assert.IsType<CacheLookup.Miss>(new LyricsCache(_dir.Path, () => later).Lookup("A1B2"));
    }

    [Fact]
    public void PositiveEntriesNeverExpire()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        new LyricsCache(_dir.Path, () => start).Store(Synced, "A1B2");
        Assert.IsType<CacheLookup.Hit>(new LyricsCache(_dir.Path, () => start.AddYears(1)).Lookup("A1B2"));
    }

    [Fact]
    public void UnsafeIdsCannotEscapeTheCacheDirectory()
    {
        new LyricsCache(_dir.Path).Store(Synced with { SyncedLyrics = null, PlainLyrics = "x" }, "../../evil");
        var files = Directory.GetFiles(_dir.Path);
        var file = Assert.Single(files);
        Assert.Equal(_dir.Path, Path.GetDirectoryName(file));
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(file));
    }

    [Fact]
    public void CorruptCacheFilesAreAMiss()
    {
        var cache = new LyricsCache(_dir.Path);
        cache.Store(Synced, "A1B2");
        File.WriteAllText(Directory.GetFiles(_dir.Path)[0], "garbage");
        Assert.IsType<CacheLookup.Miss>(cache.Lookup("A1B2"));
    }

    [Fact]
    public void UnwritableDirectoryIsAStorageError()
    {
        File.WriteAllText(_dir.Path + ".file", "x");
        try
        {
            var error = Assert.Throws<LyricsException>(() => new LyricsCache(_dir.Path + ".file").Store(Synced, "A"));
            Assert.Equal(LyricsErrorKind.Storage, error.Kind);
        }
        finally
        {
            File.Delete(_dir.Path + ".file");
        }
    }
}

public sealed class LyricsServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private static readonly TrackInfo Track = new("A1B2", "T", "A", "L", 100);

    public void Dispose() => _dir.Dispose();

    private static LyricsCandidate Candidate(string? synced = null, string? plain = null, bool instrumental = false) =>
        new("T", "A", 100, instrumental, plain, synced);

    private LyricsService Service(ILyricsProvider provider) => new(provider, new LyricsCache(_dir.Path));

    [Fact]
    public async Task SyncedLyricsBecomeALineSyncedDocument()
    {
        var result = await Service(FakeProvider.Returning(Candidate(synced: "[00:01.00]a\n[00:03.00]b"))).LyricsAsync(Track);
        var found = Assert.IsType<LyricsResult.Found>(result);
        Assert.Equal(LyricsTiming.LineSynced, found.Document.Timing);
        Assert.Equal(2, found.Document.Lines.Count);
    }

    [Fact]
    public async Task FallsBackToPlainWhenSyncedIsUnparsable()
    {
        var result = await Service(FakeProvider.Returning(Candidate(synced: "garbage", plain: "a\nb"))).LyricsAsync(Track);
        Assert.Equal(LyricsTiming.Unsynced, Assert.IsType<LyricsResult.Found>(result).Document.Timing);
    }

    [Fact]
    public async Task InstrumentalAndNotFoundResults()
    {
        Assert.IsType<LyricsResult.Instrumental>(await Service(FakeProvider.Returning(Candidate(instrumental: true))).LyricsAsync(Track));
        Assert.IsType<LyricsResult.NotFound>(await Service(FakeProvider.Returning(null)).LyricsAsync(Track with { Id = "n1" }));
        Assert.IsType<LyricsResult.NotFound>(await Service(FakeProvider.Returning(Candidate())).LyricsAsync(Track with { Id = "n2" }));
    }

    [Fact]
    public async Task SecondRequestIsServedFromCache()
    {
        var provider = FakeProvider.Returning(Candidate(synced: "[00:01.00]a"));
        var service = Service(provider);
        await service.LyricsAsync(Track);
        await service.LyricsAsync(Track);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task ErrorsPropagateAndAreNotCached()
    {
        var provider = FakeProvider.Failing(new LyricsException(LyricsErrorKind.Network));
        var service = Service(provider);
        await Assert.ThrowsAsync<LyricsException>(() => service.LyricsAsync(Track));
        await Assert.ThrowsAsync<LyricsException>(() => service.LyricsAsync(Track));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task UserSuppliedLrcOverridesTheProvider()
    {
        var provider = FakeProvider.Returning(null);
        var service = Service(provider);
        service.SetManualLyrics("[00:02.00]mine", Track);
        var found = Assert.IsType<LyricsResult.Found>(await service.LyricsAsync(Track));
        Assert.Equal(2, found.Document.Lines[0].Start);
        Assert.Equal(0, provider.Calls);
    }
}

public class LyricRetryPolicyTests
{
    [Fact]
    public void BacksOffFiveTwentySixtyForTransientErrors()
    {
        var policy = LyricRetryPolicy.Default;
        var unavailable = LyricsException.Server(503);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBeforeRetry(0, unavailable));
        Assert.Equal(TimeSpan.FromSeconds(20), policy.DelayBeforeRetry(1, unavailable));
        Assert.Equal(TimeSpan.FromSeconds(60), policy.DelayBeforeRetry(2, unavailable));
        Assert.Null(policy.DelayBeforeRetry(3, unavailable));
    }

    [Fact]
    public void PermanentErrorsAreNotRetried() =>
        Assert.Null(LyricRetryPolicy.Default.DelayBeforeRetry(0, new LyricsException(LyricsErrorKind.Decoding)));
}
