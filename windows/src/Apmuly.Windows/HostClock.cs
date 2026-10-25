using System.Diagnostics;

namespace Apmuly.Windows;

/// <summary>Monotonic seconds (the app's clock for playback tracking).</summary>
internal static class HostClock
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
