using Apmuly.Core.Diagnostics;
using Apmuly.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Apmuly.Core.Tests.Stage;

public sealed class FileLoggerTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void WritesDailyFilesAndPrunesOldOnes()
    {
        var now = new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
        Directory.CreateDirectory(_dir.Path);
        File.WriteAllText(_dir.File("apmuly-20261001.log"), "old");
        File.WriteAllText(_dir.File("apmuly-20261024.log"), "recent");
        using var provider = new FileLoggerProvider(_dir.Path, LogLevel.Information, () => now);
        var logger = provider.CreateLogger("Test");
        logger.LogDebug("hidden");
        logger.LogWarning("shown {Value}", 42);

        Assert.False(File.Exists(_dir.File("apmuly-20261001.log")));
        Assert.True(File.Exists(_dir.File("apmuly-20261024.log")));
        var text = File.ReadAllText(_dir.File("apmuly-20261025.log"));
        Assert.Contains("WARNING Test: shown 42", text);
        Assert.DoesNotContain("hidden", text);
    }
}
