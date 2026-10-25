using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Navigation;
using System.Windows.Threading;
using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Apmuly.Core.Sync;
using Apmuly.Windows.Audio;
using Apmuly.Windows.Hosting;
using Apmuly.Windows.Interop;
using Apmuly.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Apmuly.Windows;

/// <summary>The single window: the 3D stage filling it, fed ~30 times a second.</summary>
public partial class MainWindow : Window, IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private static readonly Uri WebView2Download = new("https://developer.microsoft.com/microsoft-edge/webview2/");

    private readonly ILogger<MainWindow> _logger;
    private readonly HttpClient _http = new();
    private readonly NowPlayingModel _model;
    private readonly StageFeed _feed;
    private readonly StageSession _session;
    private readonly StageHost _host;
    private readonly SmtcNowPlaying _smtc;
    private readonly NowPlayingPoller _poller;
    private readonly LoopbackEnergySource _energy;
    private readonly DispatcherTimer _frameTimer;

    internal MainWindow(ILoggerFactory loggers)
    {
        InitializeComponent();
        _logger = loggers.CreateLogger<MainWindow>();
        var lyrics = new LyricsService(new LrcLibClient(new HttpClientGetter(_http)), new LyricsCache(), loggers.CreateLogger<LyricsService>());
        _model = new NowPlayingModel(lyrics, new OffsetStore(), new TimingStore(), logger: loggers.CreateLogger<NowPlayingModel>());
        _feed = new StageFeed();
        _session = new StageSession(new SettingsStore(), _model, _feed, loggers.CreateLogger<StageSession>());
        _host = new StageHost(Stage, _session, loggers.CreateLogger<StageHost>());
        _smtc = new SmtcNowPlaying(loggers.CreateLogger<SmtcNowPlaying>());
        _poller = new NowPlayingPoller(_smtc, _model, Dispatcher, loggers.CreateLogger<NowPlayingPoller>());
        _energy = new LoopbackEnergySource(loggers.CreateLogger<LoopbackEnergySource>());
        _frameTimer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, (_, _) => Tick(), Dispatcher);
        _frameTimer.Stop();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => Dispose();
    }

    private async Task StartAsync()
    {
        try
        {
            await _host.StartAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowFallback("Apmuly needs the Microsoft Edge WebView2 Runtime",
                         "It draws the 3D stage. Install the Evergreen runtime, then start Apmuly again.",
                         WebView2Download);
            return;
        }
        catch (Exception error) when (error is FileNotFoundException or IOException or UnauthorizedAccessException
                                          or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            MainWindowLog.StageFailed(_logger, error);
            ShowFallback("The stage couldn't start", error.Message, null);
            return;
        }
        await _smtc.StartAsync();
        _poller.Start();
        _frameTimer.Start();
    }

    private void Tick()
    {
        if (WindowState == WindowState.Minimized || !IsVisible || !_session.IsReady)
        {
            return;
        }
        _energy.EnsureRunning();
        _host.Post(_feed.Tick(_model, HostClock.Now, _energy.Snapshot()));
    }

    private void ShowFallback(string title, string detail, Uri? link)
    {
        Stage.Visibility = Visibility.Collapsed;
        Fallback.Visibility = Visibility.Visible;
        FallbackTitle.Text = title;
        FallbackDetail.Text = detail;
        if (link is not null)
        {
            FallbackLink.NavigateUri = link;
            FallbackLinkText.Text = link.AbsoluteUri;
            FallbackLinkBlock.Visibility = Visibility.Visible;
        }
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri == WebView2Download)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        e.Handled = true;
    }

    public void Dispose()
    {
        _frameTimer.Stop();
        _poller.Dispose();
        _smtc.Dispose();
        _energy.Dispose();
        _host.Dispose();
        _model.Dispose();
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal static partial class MainWindowLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "The stage could not start")]
    public static partial void StageFailed(ILogger logger, Exception error);
}
