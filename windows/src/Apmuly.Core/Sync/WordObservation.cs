using System.Text.Json.Serialization;

namespace Apmuly.Core.Sync;

/// <summary>When one specific lyric word was actually sung, on the lyric file's timeline.</summary>
/// <remarks>
/// The Mac app learns these by listening to the singer; Windows has no listener yet, but keeps the
/// type, <see cref="WordRetimer"/> and <see cref="TimingStore"/> so learned timings can be added later.
/// </remarks>
public sealed record WordObservation(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("word")] int Word,
    [property: JsonPropertyName("time")] double Time);

internal static class Statistics
{
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }
}
