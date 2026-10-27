using System.Windows.Threading;
using Apmuly.Core.Media;
using Apmuly.Core.Stage;
using Microsoft.Extensions.Logging;

namespace Apmuly.Windows.Media;

/// <summary>
/// Feeds SMTC readings into the model: on every change event (debounced, since SMTC fires bursts while a
/// track changes) and once a second as a safety net. Loads artwork for new tracks. UI thread only.
/// </summary>
internal sealed partial class NowPlayingPoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly SmtcNowPlaying _smtc;
    private readonly NowPlayingModel _model;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _debounce;
    private readonly HashSet<string> _artworkLoading = [];
    private bool _polling;
    private bool _pollAgain;

    private readonly Func<SourcePreference> _preference;

    public NowPlayingPoller(SmtcNowPlaying smtc, NowPlayingModel model, Func<SourcePreference> preference,
                            Dispatcher dispatcher, ILogger<NowPlayingPoller> logger)
    {
        _preference = preference;
        _smtc = smtc;
        _model = model;
        _dispatcher = dispatcher;
        _logger = logger;
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => _ = PollAsync(), dispatcher);
        _debounce = new DispatcherTimer(Debounce, DispatcherPriority.Background, OnDebounced, dispatcher);
        // This DispatcherTimer constructor starts the timer; run only once Start() is called.
        _poll.Stop();
        _debounce.Stop();
    }

    /// <summary>Reads now (e.g. after the player preference changed).</summary>
    public void PollSoon() => _ = PollAsync();

    public void Start()
    {
        _smtc.Changed += OnChanged;
        _poll.Start();
        _ = PollAsync();
    }

    private void OnChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    private void OnDebounced(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _ = PollAsync();
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            _pollAgain = true;
            return;
        }
        _polling = true;
        try
        {
            var read = await _smtc.ReadAsync(_preference());
            var sample = MediaSampler.From(read?.Reading, HostClock.Now, DateTimeOffset.Now);
            _model.Ingest(sample.Track, sample.Sample);
            if (sample.Track is { } track && _model.NeedsArtwork(track.Id) && !_artworkLoading.Contains(track.Id))
            {
                await LoadArtworkAsync(track.Id, read);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            LogPollFailed(error);
        }
        finally
        {
            _polling = false;
            if (_pollAgain)
            {
                _pollAgain = false;
                _ = PollAsync();
            }
        }
    }

    private async Task LoadArtworkAsync(string trackId, SmtcRead? read)
    {
        _artworkLoading.Add(trackId);
        try
        {
            var bytes = await ArtworkProcessor.ReadAsync(read?.Thumbnail);
            var artwork = bytes is null ? null : await Task.Run(() => ArtworkProcessor.Process(bytes));
            if (artwork is null)
            {
                _model.NoteArtworkMissing(trackId);
                return;
            }
            _model.SetArtwork(trackId, artwork.DataUrl, artwork.Palette);
        }
        finally
        {
            _artworkLoading.Remove(trackId);
        }
    }

    public void Dispose()
    {
        _smtc.Changed -= OnChanged;
        _poll.Stop();
        _debounce.Stop();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Now-playing poll failed")]
    private partial void LogPollFailed(Exception error);
}
