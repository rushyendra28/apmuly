using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Apmuly.Core.Diagnostics;

/// <summary>
/// A tiny daily-rolling file logger (<c>apmuly-yyyyMMdd.log</c>): keeps a week of files and caps each
/// file's size so a runaway error loop can't fill the disk. Never pass lyric text to it.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int RetainedDays = 7;

    private readonly string _directory;
    private readonly LogLevel _minimum;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();
    private string? _prunedFor;

    public FileLoggerProvider(string directory, LogLevel minimum = LogLevel.Information, Func<DateTimeOffset>? now = null)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _minimum = minimum;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimum;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = _now();
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ')
            .Append(level.ToString().ToUpperInvariant()).Append(' ')
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }
        line.AppendLine();

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                PruneOnce(day, now);
                var path = Path.Combine(_directory, $"apmuly-{day}.log");
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < MaxFileBytes)
                {
                    File.AppendAllText(path, line.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Apmuly log write failed: {error.Message}");
            }
        }
    }

    private void PruneOnce(string day, DateTimeOffset now)
    {
        if (_prunedFor == day)
        {
            return;
        }
        _prunedFor = day;
        var cutoff = now.AddDays(-RetainedDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var file in Directory.EnumerateFiles(_directory, "apmuly-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["apmuly-".Length..];
            if (string.CompareOrdinal(stamp, cutoff) < 0)
            {
                File.Delete(file);
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
