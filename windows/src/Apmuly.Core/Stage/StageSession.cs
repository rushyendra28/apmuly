using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Apmuly.Core.Stage;

/// <summary>
/// The page conversation outside the frame stream: answers "ready" (options, theme, mode, and a full
/// asset resend), applies forwarded key presses, and persists theme + mode. UI thread only.
/// </summary>
public sealed partial class StageSession
{
    /// <summary>
    /// forwardKeys: the page sends key presses; sheet: the page draws its own lyrics sheet (WPF can't overlay
    /// WebView2); pacing: live singer pacing, as the user last set it.
    /// </summary>
    public static OptionsPayload OptionsFor(StageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new OptionsPayload(ForwardKeys: true, Sheet: true, Pacing: settings.Pacing);
    }

    private readonly SettingsStore _store;
    private readonly NowPlayingModel _model;
    private readonly StageFeed _feed;
    private readonly ILogger _logger;

    public StageSession(SettingsStore store, NowPlayingModel model, StageFeed feed, ILogger<StageSession>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        Settings = store.Load();
    }

    public StageSettings Settings { get; private set; }

    /// <summary>True once the page has said "ready" (calls before that are dropped by the host).</summary>
    public bool IsReady { get; private set; }

    /// <summary>The page is gone (navigation, crash); wait for the next "ready".</summary>
    public void PageLost() => IsReady = false;

    /// <summary>Handles a raw <c>WebMessageAsJson</c> string; returns the calls to post in reply.</summary>
    public IReadOnlyList<HostCall> Receive(string? json)
    {
        var message = PageMessage.Parse(json);
        switch (message?.Type)
        {
            case PageMessageType.Ready:
                IsReady = true;
                _feed.Reset();
                LogReady();
                return [HostCall.SetOptions(OptionsFor(Settings)), HostCall.SetTheme(Settings.Theme), HostCall.SetMode(Settings.Mode)];
            case PageMessageType.Error:
                LogPageError(message.Message ?? "unknown");
                return [];
            case PageMessageType.Key:
                return HandleKey(message.Message);
            default:
                LogIgnored();
                return [];
        }
    }

    public IReadOnlyList<HostCall> HandleKey(string? key)
    {
        switch (KeyCommands.For(key))
        {
            case { Kind: StageCommandKind.ToggleMode }:
                return Apply(Settings with { Mode = Settings.Mode.Toggled() });
            case { Kind: StageCommandKind.SelectTheme, Theme: { } theme }:
                return Apply(Settings with { Theme = theme });
            case { Kind: StageCommandKind.NextTheme }:
                return Apply(Settings with { Theme = Settings.Theme.Next() });
            case { Kind: StageCommandKind.TogglePacing }:
                return Apply(Settings with { Pacing = !Settings.Pacing });
            case { Kind: StageCommandKind.Nudge, Seconds: var seconds }:
                _model.AdjustOffset(seconds);
                return [];
            case { Kind: StageCommandKind.ResetNudge }:
                _model.ResetOffset();
                return [];
            case { Kind: StageCommandKind.RetryLyrics }:
                _model.RetryLyrics();
                return [];
            default:
                return [];
        }
    }

    private List<HostCall> Apply(StageSettings updated)
    {
        var calls = new List<HostCall>();
        if (updated.Theme != Settings.Theme)
        {
            calls.Add(HostCall.SetTheme(updated.Theme));
        }
        if (updated.Mode != Settings.Mode)
        {
            calls.Add(HostCall.SetMode(updated.Mode));
        }
        if (updated.Pacing != Settings.Pacing)
        {
            calls.Add(HostCall.SetOptions(OptionsFor(updated)));
        }
        Settings = updated;
        if (!_store.Save(updated))
        {
            LogSettingsNotSaved();
        }
        return calls;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Renderer ready")]
    private partial void LogReady();

    [LoggerMessage(Level = LogLevel.Error, Message = "Renderer error: {Message}")]
    private partial void LogPageError(string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored an unrecognised page message")]
    private partial void LogIgnored();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save settings")]
    private partial void LogSettingsNotSaved();
}
