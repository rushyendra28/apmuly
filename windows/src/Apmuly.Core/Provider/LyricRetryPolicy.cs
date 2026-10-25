namespace Apmuly.Core.Provider;

/// <summary>
/// Backoff for automatic lyric retries after temporary failures (LRCLIB often answers 503,
/// rate limits, offline): 5 s, 20 s, then 60 s; permanent failures are not retried.
/// </summary>
public sealed class LyricRetryPolicy
{
    public static readonly LyricRetryPolicy Default = new([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60)]);

    private readonly IReadOnlyList<TimeSpan> _delays;

    public LyricRetryPolicy(IReadOnlyList<TimeSpan> delays)
    {
        _delays = delays ?? throw new ArgumentNullException(nameof(delays));
    }

    public int MaxRetries => _delays.Count;

    /// <summary>The wait before retry number <paramref name="attemptsSoFar"/> + 1, or null to give up.</summary>
    public TimeSpan? DelayBeforeRetry(int attemptsSoFar, LyricsException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.IsTransient && attemptsSoFar >= 0 && attemptsSoFar < _delays.Count ? _delays[attemptsSoFar] : null;
    }
}
