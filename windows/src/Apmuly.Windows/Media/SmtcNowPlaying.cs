using System.Runtime.InteropServices;
using Apmuly.Core.Media;
using Apmuly.Core.Sync;
using Microsoft.Extensions.Logging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Apmuly.Windows.Media;

/// <summary>A reading of the followed session, plus its thumbnail reference for artwork.</summary>
internal sealed record SmtcRead(MediaReading Reading, IRandomAccessStreamReference? Thumbnail);

/// <summary>
/// Reads "now playing" from Windows' System Media Transport Controls (the media flyout data).
/// Follows Apple Music for Windows when it has a session, else the system's current session.
/// <see cref="Changed"/> fires (on any thread) when the sessions, metadata, playback or timeline change.
/// </summary>
internal sealed partial class SmtcNowPlaying : IDisposable
{
    private readonly ILogger _logger;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _watched;

    public SmtcNowPlaying(ILogger<SmtcNowPlaying> logger)
    {
        _logger = logger;
    }

    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnManagerChanged;
            _manager.CurrentSessionChanged += OnCurrentChanged;
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogUnavailable(error);
        }
    }

    /// <summary>The followed session's state, or null when nothing can be followed.</summary>
    public async Task<SmtcRead?> ReadAsync()
    {
        if (_manager is null)
        {
            return null;
        }
        try
        {
            var session = PickSession(_manager);
            Watch(session);
            if (session is null)
            {
                return null;
            }
            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties is null)
            {
                return null;
            }
            var timeline = session.GetTimelineProperties();
            var state = StateOf(session.GetPlaybackInfo()?.PlaybackStatus);
            var snapshot = new TimelineSnapshot(timeline.Position, timeline.LastUpdatedTime, timeline.StartTime, timeline.EndTime, state);
            var reading = new MediaReading(session.SourceAppUserModelId ?? string.Empty,
                properties.Title ?? string.Empty, properties.Artist ?? string.Empty, properties.AlbumTitle ?? string.Empty, snapshot);
            return new SmtcRead(reading, properties.Thumbnail);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The player went away mid-read; the next poll sees the new state.
            LogReadFailed(error);
            return null;
        }
    }

    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var sessions = manager.GetSessions();
        var ids = sessions.Select(s => (string?)s.SourceAppUserModelId).ToArray();
        var current = manager.GetCurrentSession();
        var index = SessionPicker.Pick(ids, current?.SourceAppUserModelId);
        return index is { } i ? sessions[i] : null;
    }

    private static PlayerState StateOf(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlayerState.Playing,
        // "Changing" happens between tracks: hold the position rather than drop the stage.
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlayerState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlayerState.Paused,
        _ => PlayerState.Stopped,
    };

    /// <summary>Subscribes to the followed session's change events (and unsubscribes from the previous one).</summary>
    private void Watch(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _watched))
        {
            return;
        }
        if (_watched is not null)
        {
            _watched.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _watched.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _watched.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _watched = session;
        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged += OnTimelineChanged;
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private void OnManagerChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Raise();

    private void OnCurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Raise();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Raise();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Raise();

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Raise();

    public void Dispose()
    {
        Watch(null);
        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnManagerChanged;
            _manager.CurrentSessionChanged -= OnCurrentChanged;
            _manager = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Media sessions are unavailable")]
    private partial void LogUnavailable(Exception error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Media read failed")]
    private partial void LogReadFailed(Exception error);
}
