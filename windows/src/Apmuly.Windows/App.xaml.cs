using System.Windows;
using System.Windows.Threading;
using Apmuly.Core.Diagnostics;
using Apmuly.Core.Provider;
using Microsoft.Extensions.Logging;

namespace Apmuly.Windows;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private ILogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddDebug()
            .AddProvider(new FileLoggerProvider(ApmulyPaths.Logs)));
        _logger = _loggerFactory.CreateLogger<App>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "?";
        AppLog.Started(_logger, version);

        var window = new MainWindow(_loggerFactory);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.Unhandled(_logger, e.Exception);
        }
        // Keep the visualizer running; the failure is in the log.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (_logger is not null && e.ExceptionObject is Exception error)
        {
            AppLog.Unhandled(_logger, error);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            AppLog.Unhandled(_logger, e.Exception);
        }
        e.SetObserved();
    }
}

internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Apmuly for Windows {Version} starting")]
    public static partial void Started(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    public static partial void Unhandled(ILogger logger, Exception error);
}
