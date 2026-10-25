using System.IO;
using System.Runtime.InteropServices;
using Apmuly.Core.Provider;
using Apmuly.Core.Stage;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Apmuly.Windows.Hosting;

/// <summary>
/// Hosts the shared renderer (App/Resources/Renderer, copied next to the exe) in WebView2 at
/// https://apmuly.stage/ and carries the {fn, arg} / {type, message} conversation. UI thread only.
/// </summary>
internal sealed partial class StageHost : IDisposable
{
    public const string HostName = "apmuly.stage";
    public static readonly Uri EntryUri = new($"https://{HostName}/index.html");

    private readonly WebView2 _view;
    private readonly StageSession _session;
    private readonly ILogger _logger;
    private CoreWebView2? _core;

    public StageHost(WebView2 view, StageSession session, ILogger<StageHost> logger)
    {
        _view = view;
        _session = session;
        _logger = logger;
    }

    public static string RendererFolder => Path.Combine(AppContext.BaseDirectory, "Renderer");

    /// <summary>Starts WebView2 and loads the page.</summary>
    /// <exception cref="WebView2RuntimeNotFoundException">The Evergreen runtime isn't installed.</exception>
    public async Task StartAsync()
    {
        if (!File.Exists(Path.Combine(RendererFolder, "index.html")))
        {
            throw new FileNotFoundException("Renderer files are missing next to Apmuly.exe", Path.Combine(RendererFolder, "index.html"));
        }
        _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        _view.DefaultBackgroundColor = System.Drawing.Color.Black;
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: ApmulyPaths.WebViewData);
        await _view.EnsureCoreWebView2Async(environment);
        _core = _view.CoreWebView2;
        Configure(_core.Settings);
        _core.SetVirtualHostNameToFolderMapping(HostName, RendererFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        _core.NavigationStarting += OnNavigationStarting;
        _core.NavigationCompleted += OnNavigationCompleted;
        _core.NewWindowRequested += OnNewWindowRequested;
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.ProcessFailed += OnProcessFailed;
        _core.Navigate(EntryUri.AbsoluteUri);
    }

    /// <summary>Posts calls to the page (dropped until the page has said "ready").</summary>
    public void Post(IEnumerable<HostCall> calls)
    {
        if (_core is null || !_session.IsReady)
        {
            return;
        }
        foreach (var call in calls)
        {
            try
            {
                _core.PostWebMessageAsJson(call.ToJson());
            }
            catch (Exception error) when (error is COMException or InvalidOperationException or ObjectDisposedException)
            {
                LogPostFailed(call.Fn, error.GetType().Name);
                _session.PageLost();
                return;
            }
        }
    }

    private static void Configure(CoreWebView2Settings settings)
    {
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false; // no Ctrl+R reload, F5, Ctrl+P …
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;
        settings.IsScriptEnabled = true;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }

    private static bool IsStagePage(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps
        && string.Equals(parsed.Host, HostName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The renderer is self-contained: never navigate anywhere else.</summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsStagePage(e.Uri))
        {
            e.Cancel = true;
            LogBlockedNavigation();
            return;
        }
        _session.PageLost();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            LogNavigationFailed(e.WebErrorStatus.ToString());
            return;
        }
        // The page forwards key presses, so it needs keyboard focus.
        _view.Focus();
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsStagePage(e.Source))
        {
            return;
        }
        string? json;
        try
        {
            json = e.WebMessageAsJson;
        }
        catch (ArgumentException)
        {
            return;
        }
        Post(_session.Receive(json));
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        LogProcessFailed(e.ProcessFailedKind.ToString());
        _session.PageLost();
        if (_core is not null && e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive or CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
        {
            _core.Navigate(EntryUri.AbsoluteUri);
        }
    }

    public void Dispose()
    {
        if (_core is not null)
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.ProcessFailed -= OnProcessFailed;
            _core = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting {Function} failed: {Kind}")]
    private partial void LogPostFailed(string function, string kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation away from the stage")]
    private partial void LogBlockedNavigation();

    [LoggerMessage(Level = LogLevel.Error, Message = "Stage navigation failed: {Status}")]
    private partial void LogNavigationFailed(string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView2 process failed: {Kind}; reloading")]
    private partial void LogProcessFailed(string kind);
}
