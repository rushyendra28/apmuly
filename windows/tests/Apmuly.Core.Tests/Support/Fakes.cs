using System.Collections.Concurrent;
using System.Text;
using System.Web;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Tests.Support;

/// <summary>Returns canned responses keyed by URL path and records every request.</summary>
public sealed class FakeHttpGetter(IReadOnlyDictionary<string, (int Status, string Body)>? responses = null, Exception? failure = null)
    : IHttpGetter
{
    private readonly ConcurrentQueue<HttpGetRequest> _requests = new();

    public IReadOnlyList<HttpGetRequest> Requests => _requests.ToArray();

    public Task<HttpGetResponse> GetAsync(HttpGetRequest request, CancellationToken cancellationToken = default)
    {
        _requests.Enqueue(request);
        if (failure is not null)
        {
            throw failure;
        }
        var (status, body) = responses?.GetValueOrDefault(request.Uri.AbsolutePath) ?? (404, "{}");
        if (status == 0 && body is null)
        {
            (status, body) = (404, "{}");
        }
        return Task.FromResult(new HttpGetResponse(status, Encoding.UTF8.GetBytes(body)));
    }

    public static Dictionary<string, string> Query(HttpGetRequest request)
    {
        var parsed = HttpUtility.ParseQueryString(request.Uri.Query);
        return parsed.AllKeys.OfType<string>().ToDictionary(k => k, k => parsed[k] ?? "");
    }
}

/// <summary>A provider with a fixed answer (or a scripted sequence of answers).</summary>
public sealed class FakeProvider : ILyricsProvider
{
    private readonly ConcurrentQueue<Func<LyricsCandidate?>> _script;
    private readonly Func<LyricsCandidate?> _last;
    private int _calls;

    public FakeProvider(params Func<LyricsCandidate?>[] script)
    {
        _script = new ConcurrentQueue<Func<LyricsCandidate?>>(script);
        _last = script[^1];
    }

    public static FakeProvider Returning(LyricsCandidate? candidate) => new(() => candidate);

    public static FakeProvider Failing(LyricsException error) => new(() => throw error);

    /// <summary>Answers only once <paramref name="release"/> is set (so a test can observe "loading").</summary>
    public static FakeProvider Gated(LyricsCandidate? candidate, ManualResetEventSlim release) =>
        new(() => release.Wait(TimeSpan.FromSeconds(10)) ? candidate : throw new TimeoutException("gate never opened"));

    public int Calls => Volatile.Read(ref _calls);

    public Task<LyricsCandidate?> CandidateAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);
        var next = _script.TryDequeue(out var step) ? step : _last;
        return Task.FromResult(next());
    }
}
