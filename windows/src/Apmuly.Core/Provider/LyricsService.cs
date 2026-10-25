using Apmuly.Core.Lyrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apmuly.Core.Provider;

/// <summary>Resolves a track to renderable lyrics: cache → provider → document.</summary>
public sealed partial class LyricsService
{
    private readonly ILyricsProvider _provider;
    private readonly LyricsCache _cache;
    private readonly ILogger _logger;

    public LyricsService(ILyricsProvider provider, LyricsCache cache, ILogger<LyricsService>? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <exception cref="LyricsException">The provider failed (never cached).</exception>
    public async Task<LyricsResult> LyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (_cache.Lookup(track.Id) is CacheLookup.Hit hit)
        {
            return Result(hit.Candidate, track);
        }
        var candidate = await _provider.CandidateAsync(track, cancellationToken).ConfigureAwait(false);
        try
        {
            _cache.Store(candidate, track.Id);
        }
        catch (LyricsException error)
        {
            // Lyrics are still usable; we'll just refetch next time.
            LogCacheFailed(track.Id, error.Kind);
        }
        return Result(candidate, track);
    }

    /// <summary>Stores a user-supplied LRC that takes precedence over the provider.</summary>
    public void SetManualLyrics(string lrc, TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var manual = new LyricsCandidate(track.Title, track.Artist, track.Duration, false, null, lrc);
        _cache.Store(manual, track.Id);
    }

    internal static LyricsResult Result(LyricsCandidate? candidate, TrackInfo track)
    {
        if (candidate is null)
        {
            return new LyricsResult.NotFound();
        }
        if (candidate.Instrumental)
        {
            return new LyricsResult.Instrumental();
        }
        if (!string.IsNullOrEmpty(candidate.SyncedLyrics))
        {
            var doc = LyricsTimeline.Build(LrcParser.Parse(candidate.SyncedLyrics), track.Duration);
            if (doc.Lines.Any(line => !line.IsGap))
            {
                return new LyricsResult.Found(doc);
            }
        }
        if (!string.IsNullOrEmpty(candidate.PlainLyrics))
        {
            var doc = LyricsTimeline.Unsynced(candidate.PlainLyrics, track.Duration);
            if (doc.Lines.Count > 0)
            {
                return new LyricsResult.Found(doc);
            }
        }
        return new LyricsResult.NotFound();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not cache lyrics for {TrackId}: {Kind}")]
    private partial void LogCacheFailed(string trackId, LyricsErrorKind kind);
}
