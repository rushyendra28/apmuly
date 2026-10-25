using System.Net.Http;
using Apmuly.Core.Provider;
using Apmuly.Core.Tests.Support;

namespace Apmuly.Core.Tests.Provider;

public class LrcLibClientTests
{
    private static readonly TrackInfo Track = new("ABC", "Night Drive (feat. Guest)", "Main Artist & Guest", "Album", 199.6);
    private const string Record = """{"trackName":"Night Drive","artistName":"Main Artist","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":"[00:01.00]x"}""";
    private const string PlainOnly = """{"trackName":"Night Drive","artistName":"Main Artist","duration":200,"instrumental":false,"plainLyrics":"x","syncedLyrics":null}""";

    private static FakeHttpGetter Http(params (string Path, int Status, string Body)[] responses) =>
        new(responses.ToDictionary(r => r.Path, r => (r.Status, r.Body)));

    [Fact]
    public async Task ExactLookupSendsFullMetadataRoundedDurationAndUserAgent()
    {
        var http = Http(("/api/get", 200, Record));
        var result = await new LrcLibClient(http, userAgent: "apmuly-test").CandidateAsync(Track);

        Assert.Equal("[00:01.00]x", result?.SyncedLyrics);
        var request = http.Requests[0];
        Assert.Equal(new Dictionary<string, string>
        {
            ["track_name"] = Track.Title, ["artist_name"] = Track.Artist, ["album_name"] = "Album", ["duration"] = "200",
        }, FakeHttpGetter.Query(request));
        Assert.Equal("apmuly-test", request.UserAgent);
        Assert.Equal("lrclib.net", request.Uri.Host);
        Assert.Equal("https", request.Uri.Scheme);
    }

    [Fact]
    public void DefaultUserAgentIdentifiesApmuly() =>
        Assert.Equal("Apmuly (https://github.com/rushyendra28/apmuly)", LrcLibClient.DefaultUserAgent);

    [Fact]
    public async Task FallsBackToSearchWithCleanedTitleAndPrimaryArtist()
    {
        var http = Http(("/api/search", 200, $"[{Record}]"));
        var result = await new LrcLibClient(http).CandidateAsync(Track);

        Assert.Equal("Night Drive", result?.TrackName);
        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(new Dictionary<string, string> { ["track_name"] = "Night Drive", ["artist_name"] = "Main Artist" },
                     FakeHttpGetter.Query(http.Requests[1]));
    }

    [Fact]
    public async Task PlainOnlyExactMatchFallsThroughToSyncedSearch()
    {
        var http = Http(("/api/get", 200, PlainOnly), ("/api/search", 200, $"[{Record}]"));
        Assert.Equal("[00:01.00]x", (await new LrcLibClient(http).CandidateAsync(Track))?.SyncedLyrics);
    }

    [Fact]
    public async Task PlainOnlyExactMatchIsKeptWhenSearchHasNothingBetter()
    {
        var http = Http(("/api/get", 200, PlainOnly), ("/api/search", 200, "[]"));
        Assert.Equal("x", (await new LrcLibClient(http).CandidateAsync(Track))?.PlainLyrics);
    }

    [Fact]
    public async Task PlainOnlyExactMatchIsKeptWhenSearchFails()
    {
        var http = Http(("/api/get", 200, PlainOnly), ("/api/search", 503, ""));
        Assert.Equal("x", (await new LrcLibClient(http).CandidateAsync(Track))?.PlainLyrics);
    }

    [Fact]
    public async Task UnknownDurationIsNotSent()
    {
        var http = Http();
        await new LrcLibClient(http).CandidateAsync(new TrackInfo("X", "T", "A", "L", 0));
        Assert.False(FakeHttpGetter.Query(http.Requests[0]).ContainsKey("duration"));
    }

    [Fact]
    public async Task QueryValuesAreFullyEscaped()
    {
        var http = Http();
        await new LrcLibClient(http).CandidateAsync(new TrackInfo("X", "A+B & C?", "D/E", "L#1", 0));
        var query = FakeHttpGetter.Query(http.Requests[0]);
        Assert.Equal("A+B & C?", query["track_name"]);
        Assert.Equal("D/E", query["artist_name"]);
        Assert.Equal("L#1", query["album_name"]);
    }

    [Theory]
    [InlineData(LyricsErrorKind.Network, null, true)]
    [InlineData(LyricsErrorKind.Server, 503, true)]
    [InlineData(LyricsErrorKind.Server, 500, true)]
    [InlineData(LyricsErrorKind.Server, 429, true)]
    [InlineData(LyricsErrorKind.Server, 400, false)]
    [InlineData(LyricsErrorKind.Decoding, null, false)]
    [InlineData(LyricsErrorKind.Storage, null, false)]
    public void OnlyTemporaryFailuresAreTransient(LyricsErrorKind kind, int? status, bool expected) =>
        Assert.Equal(expected, new LyricsException(kind, status).IsTransient);

    [Fact]
    public async Task ReturnsNullWhenNothingMatches()
    {
        Assert.Null(await new LrcLibClient(Http(("/api/search", 200, "[]"))).CandidateAsync(Track));
    }

    [Fact]
    public async Task ServerErrorsAreReportedNotTreatedAsNotFound()
    {
        var error = await Assert.ThrowsAsync<LyricsException>(() => new LrcLibClient(Http(("/api/get", 503, ""))).CandidateAsync(Track));
        Assert.Equal(LyricsErrorKind.Server, error.Kind);
        Assert.Equal(503, error.Status);
    }

    [Fact]
    public async Task NetworkFailuresMapToNetwork()
    {
        var http = new FakeHttpGetter(failure: new HttpRequestException("offline"));
        var error = await Assert.ThrowsAsync<LyricsException>(() => new LrcLibClient(http).CandidateAsync(Track));
        Assert.Equal(LyricsErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task TimeoutsMapToNetwork()
    {
        var http = new FakeHttpGetter(failure: new TaskCanceledException("timeout"));
        var error = await Assert.ThrowsAsync<LyricsException>(() => new LrcLibClient(http).CandidateAsync(Track));
        Assert.Equal(LyricsErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task CallerCancellationIsNotANetworkError()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new FakeHttpGetter(failure: new OperationCanceledException(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LrcLibClient(http).CandidateAsync(Track, cts.Token));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("""{"duration":"long"}""")]
    public async Task MalformedJsonMapsToDecoding(string body)
    {
        var error = await Assert.ThrowsAsync<LyricsException>(() => new LrcLibClient(Http(("/api/get", 200, body))).CandidateAsync(Track));
        Assert.Equal(LyricsErrorKind.Decoding, error.Kind);
    }

    [Fact]
    public async Task MissingFieldsGetDefaultsAndAbsurdDurationsAreDropped()
    {
        var http = Http(("/api/get", 200, """{"syncedLyrics":"[00:01.00]x","duration":-5}"""));
        var result = await new LrcLibClient(http).CandidateAsync(Track);
        Assert.Equal(new LyricsCandidate("", "", null, false, null, "[00:01.00]x"), result);
    }
}
