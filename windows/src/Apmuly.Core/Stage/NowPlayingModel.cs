using Apmuly.Core.Insights;
using Apmuly.Core.Lyrics;
using Apmuly.Core.Provider;
using Apmuly.Core.Sync;
using Apmuly.Core.Visuals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apmuly.Core.Stage;

/// <summary>
/// Follows playback across transitions and loads lyrics, insights and artwork per track — the Windows
/// port of the Mac app's NowPlayingModel, without singer listening / auto-sync. Thread-safe: samples
/// and renders come from the UI thread, lyric loads finish on the thread pool.
/// </summary>
public sealed partial class NowPlayingModel : IDisposable
{
    public const double OffsetStep = KeyCommands.NudgeStep;
    /// <summary>Artwork reads that found no thumbnail are retried this many times (SMTC often sends it late).</summary>
    public const int MaxArtworkAttempts = 5;
    /// <summary>Tracks remembered (lyrics, artwork) before the oldest is forgotten.</summary>
    internal const int MaxRememberedTracks = 64;

    private readonly LyricsService _lyricsService;
    private readonly OffsetStore _offsetStore;
    private readonly TimingStore? _timingStore;
    private readonly LyricRetryPolicy _retryPolicy;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>Cached so it stays usable after <see cref="Dispose"/>.</summary>
    private readonly CancellationToken _shutdown;
    private readonly Lock _gate = new();

    private PlaybackTracker _tracker = new();
    private readonly List<string> _trackOrder = [];
    private readonly Dictionary<string, TrackInfo> _tracks = [];
    private readonly Dictionary<string, LyricsLoad> _lyrics = [];
    private readonly Dictionary<string, int> _lyricsVersions = [];
    private readonly Dictionary<string, IReadOnlyDictionary<int, LineInsight>> _insights = [];
    private readonly Dictionary<string, int> _insightVersions = [];
    private readonly Dictionary<string, ThemePalette> _palettes = [];
    private readonly Dictionary<string, string> _artworkUrls = [];
    private readonly Dictionary<string, int> _artworkAttempts = [];
    private readonly Dictionary<string, double> _offsets = [];
    private readonly Dictionary<string, int> _lyricAttempts = [];
    private readonly HashSet<Task> _inflight = [];

    public NowPlayingModel(
        LyricsService lyricsService,
        OffsetStore offsetStore,
        TimingStore? timingStore = null,
        LyricRetryPolicy? retryPolicy = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger<NowPlayingModel>? logger = null)
    {
        _lyricsService = lyricsService ?? throw new ArgumentNullException(nameof(lyricsService));
        _offsetStore = offsetStore ?? throw new ArgumentNullException(nameof(offsetStore));
        _timingStore = timingStore;
        _retryPolicy = retryPolicy ?? LyricRetryPolicy.Default;
        _delay = delay ?? Task.Delay;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _shutdown = _lifetime.Token;
    }

    public string? CurrentTrackId
    {
        get
        {
            lock (_gate)
            {
                return _tracker.Current?.TrackId;
            }
        }
    }

    /// <summary>The current track's nudge (seconds, "lyrics later" is positive).</summary>
    public double CurrentOffset
    {
        get
        {
            lock (_gate)
            {
                return _tracker.Current is { } deck ? _offsets.GetValueOrDefault(deck.TrackId) : 0;
            }
        }
    }

    /// <summary>Applies one reading of the player; a new track starts loading its lyrics.</summary>
    public void Ingest(TrackInfo? track, PlaybackSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            if (track is not null && sample.TrackId == track.Id && !_tracks.ContainsKey(track.Id))
            {
                Remember(track);
                StartLyricsLoad(track);
            }
            _tracker = _tracker.Ingesting(sample);
        }
    }

    /// <summary>True while the track still has no artwork and more attempts are allowed.</summary>
    public bool NeedsArtwork(string trackId)
    {
        lock (_gate)
        {
            return _tracks.ContainsKey(trackId) && !_artworkUrls.ContainsKey(trackId)
                && _artworkAttempts.GetValueOrDefault(trackId) < MaxArtworkAttempts;
        }
    }

    /// <summary>Counts a read that found no usable thumbnail.</summary>
    public void NoteArtworkMissing(string trackId)
    {
        lock (_gate)
        {
            _artworkAttempts[trackId] = _artworkAttempts.GetValueOrDefault(trackId) + 1;
        }
    }

    public void SetArtwork(string trackId, string dataUrl, ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(dataUrl);
        ArgumentNullException.ThrowIfNull(palette);
        if (!dataUrl.StartsWith("data:image/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Artwork must be an image data URL", nameof(dataUrl));
        }
        lock (_gate)
        {
            if (_tracks.ContainsKey(trackId))
            {
                _artworkUrls[trackId] = dataUrl;
                _palettes[trackId] = palette;
            }
        }
    }

    public TrackAssets Assets(string trackId)
    {
        lock (_gate)
        {
            return AssetsUnlocked(trackId);
        }
    }

    public (DeckRender? Current, DeckRender? Outgoing) Render(double hostTime)
    {
        var snapshot = Snapshot(hostTime);
        return (snapshot.Current?.Deck, snapshot.Outgoing?.Deck);
    }

    /// <summary>Both decks and their assets, read atomically (a lyric load can finish on another thread).</summary>
    public StageSnapshot Snapshot(double hostTime)
    {
        lock (_gate)
        {
            var frame = _tracker.Frame(hostTime);
            return new StageSnapshot(
                frame.Current is { } c ? new DeckSnapshot(RenderDeck(c), AssetsUnlocked(c.TrackId)) : null,
                frame.Outgoing is { } o ? new DeckSnapshot(RenderDeck(o), AssetsUnlocked(o.TrackId)) : null);
        }
    }

    private TrackAssets AssetsUnlocked(string trackId) => new(
        _lyricsVersions.GetValueOrDefault(trackId),
        _palettes.GetValueOrDefault(trackId),
        _insights.GetValueOrDefault(trackId),
        _insightVersions.GetValueOrDefault(trackId),
        _artworkUrls.GetValueOrDefault(trackId));

    private DeckRender RenderDeck(DeckFrame deck) => new(
        deck.TrackId,
        _tracks.GetValueOrDefault(deck.TrackId),
        _lyrics.GetValueOrDefault(deck.TrackId),
        deck.Position - _offsets.GetValueOrDefault(deck.TrackId),
        deck.IsRunning,
        deck.Opacity);

    // MARK: - Offset ("lyrics later" is positive)

    public void AdjustOffset(double delta)
    {
        lock (_gate)
        {
            if (_tracker.Current is { } deck)
            {
                SetOffset(deck.TrackId, _offsets.GetValueOrDefault(deck.TrackId) + delta);
            }
        }
    }

    public void ResetOffset()
    {
        lock (_gate)
        {
            if (_tracker.Current is { } deck)
            {
                SetOffset(deck.TrackId, 0);
            }
        }
    }

    private void SetOffset(string trackId, double value)
    {
        var (stored, saved) = _offsetStore.Set(trackId, value);
        _offsets[trackId] = stored;
        if (!saved)
        {
            LogOffsetNotSaved(trackId);
        }
    }

    // MARK: - Lyrics

    /// <summary>Reloads the current track's lyrics if they failed (the R key).</summary>
    public void RetryLyrics()
    {
        lock (_gate)
        {
            if (_tracker.Current is { } deck && _tracks.TryGetValue(deck.TrackId, out var track)
                && _lyrics.GetValueOrDefault(deck.TrackId) is LyricsLoad.Failed)
            {
                _lyrics.Remove(deck.TrackId);
                StartLyricsLoad(track);
            }
        }
    }

    /// <summary>Completes when every lyric load and scheduled retry started so far has finished (tests).</summary>
    internal Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return Task.WhenAll(_inflight.ToArray());
        }
    }

    private void StartLyricsLoad(TrackInfo track)
    {
        if (_lyrics.ContainsKey(track.Id) || _shutdown.IsCancellationRequested)
        {
            return;
        }
        _lyrics[track.Id] = new LyricsLoad.Loading();
        Track(Task.Run(() => LoadLyricsAsync(track), _shutdown));
    }

    private void Track(Task task)
    {
        _inflight.Add(task);
        task.ContinueWith(done =>
        {
            lock (_gate)
            {
                _inflight.Remove(done);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task LoadLyricsAsync(TrackInfo track)
    {
        try
        {
            var result = await _lyricsService.LyricsAsync(track, _shutdown).ConfigureAwait(false);
            var learned = result is LyricsResult.Found { Document.Timing: not LyricsTiming.Unsynced } && _timingStore is not null
                ? _timingStore.Load(track.Id)
                : [];
            Apply(track.Id, result, learned);
        }
        catch (LyricsException error)
        {
            LogLyricsFailed(track.Id, error.Kind, error.Status);
            lock (_gate)
            {
                _lyrics[track.Id] = new LyricsLoad.Failed(error.Kind, error.Status);
                ScheduleRetry(track, error);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private void Apply(string trackId, LyricsResult result, IReadOnlyList<WordObservation> learned)
    {
        if (result is LyricsResult.Found { Document: var doc } && doc.Timing != LyricsTiming.Unsynced)
        {
            // Timing learned on earlier plays would put words on the singer's pace (none on Windows yet).
            var timed = WordRetimer.Retime(doc, learned.ToArray());
            var insights = LexiconAnalyzer.Insights(doc);
            lock (_gate)
            {
                _lyrics[trackId] = new LyricsLoad.Loaded(new LyricsResult.Found(timed));
                _lyricsVersions[trackId] = _lyricsVersions.GetValueOrDefault(trackId) + 1;
                _insights[trackId] = insights;
                _insightVersions[trackId] = _insightVersions.GetValueOrDefault(trackId) + 1;
            }
            return;
        }
        lock (_gate)
        {
            _lyrics[trackId] = new LyricsLoad.Loaded(result);
            _lyricsVersions[trackId] = _lyricsVersions.GetValueOrDefault(trackId) + 1;
        }
    }

    /// <summary>Retries temporary failures (LRCLIB 503s, rate limits, offline) with backoff. Caller holds the lock.</summary>
    private void ScheduleRetry(TrackInfo track, LyricsException error)
    {
        var attempt = _lyricAttempts.GetValueOrDefault(track.Id);
        if (_retryPolicy.DelayBeforeRetry(attempt, error) is not { } wait)
        {
            return;
        }
        _lyricAttempts[track.Id] = attempt + 1;
        LogRetryScheduled(track.Id, wait.TotalSeconds);
        Track(RetryAfterAsync(track, wait));
    }

    private async Task RetryAfterAsync(TrackInfo track, TimeSpan wait)
    {
        try
        {
            await _delay(wait, _shutdown).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        lock (_gate)
        {
            if (_lyrics.GetValueOrDefault(track.Id) is not LyricsLoad.Failed)
            {
                return;
            }
            _lyrics.Remove(track.Id);
            StartLyricsLoad(track);
        }
    }

    private void Remember(TrackInfo track)
    {
        _tracks[track.Id] = track;
        _trackOrder.Add(track.Id);
        _offsets[track.Id] = _offsetStore.Get(track.Id);
        while (_trackOrder.Count > MaxRememberedTracks)
        {
            Forget(_trackOrder[0]);
        }
    }

    private void Forget(string trackId)
    {
        _trackOrder.Remove(trackId);
        if (_tracker.Current?.TrackId == trackId || _tracker.Outgoing?.Deck.TrackId == trackId)
        {
            return;
        }
        _tracks.Remove(trackId);
        _lyrics.Remove(trackId);
        _insights.Remove(trackId);
        _palettes.Remove(trackId);
        _artworkUrls.Remove(trackId);
        _artworkAttempts.Remove(trackId);
        _offsets.Remove(trackId);
        _lyricAttempts.Remove(trackId);
        // Versions are kept so a returning track is re-sent to the page.
    }

    public void Dispose()
    {
        if (!_shutdown.IsCancellationRequested)
        {
            _lifetime.Cancel();
        }
        _lifetime.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lyrics failed for {TrackId}: {Kind} {Status}")]
    private partial void LogLyricsFailed(string trackId, LyricsErrorKind kind, int? status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrying lyrics for {TrackId} in {Seconds} s")]
    private partial void LogRetryScheduled(string trackId, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the lyric nudge for {TrackId}")]
    private partial void LogOffsetNotSaved(string trackId);
}
