namespace Apmuly.Core.Sync;

/// <summary>
/// The player's timeline as Windows reports it (SMTC <c>GetTimelineProperties</c> + playback status):
/// a position that was valid at <see cref="LastUpdated"/>, not "now".
/// </summary>
public sealed record TimelineSnapshot(
    TimeSpan Position,
    DateTimeOffset LastUpdated,
    TimeSpan Start,
    TimeSpan End,
    PlayerState State);

/// <summary>
/// Turns timeline snapshots into <see cref="PlaybackSample"/>s for <see cref="PlaybackTracker"/>.
/// SMTC only refreshes the position on some events (play, pause, seek, track change), so the snapshot is
/// extrapolated to "now" while playing; the tracker then re-anchors on real updates, snaps on seeks and
/// smooths small drift exactly as on the Mac.
/// </summary>
public static class TimelineSampler
{
    /// <summary>Timestamps further in the future than this are clock noise; treat as "just now".</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(2);
    /// <summary>Timestamps before this are unset (SMTC reports year 1601 / 0001 for "never").</summary>
    private static readonly DateTimeOffset EarliestValid = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    /// <summary>A snapshot is exact (no round-trip like an Apple Event), so fine corrections always apply.</summary>
    public const double SnapshotLatency = 0;
    /// <summary>Longest plausible track (positions beyond are rejected as garbage).</summary>
    private const double MaxSeconds = 24 * 60 * 60;

    /// <param name="hostTime">Monotonic seconds now.</param>
    /// <param name="now">Wall-clock time now (SMTC timestamps are wall-clock).</param>
    public static PlaybackSample ToSample(string trackId, TimelineSnapshot snapshot, double hostTime, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        ArgumentNullException.ThrowIfNull(snapshot);
        var duration = Seconds(snapshot.End - snapshot.Start);
        var position = Seconds(snapshot.Position - snapshot.Start);
        if (snapshot.State == PlayerState.Playing)
        {
            position += ElapsedSince(snapshot.LastUpdated, now);
        }
        if (duration > 0)
        {
            position = Math.Min(position, duration);
        }
        return new PlaybackSample(trackId, position, duration, snapshot.State, hostTime, SnapshotLatency);
    }

    /// <summary>Seconds since the snapshot was taken, or 0 when its timestamp is unusable.</summary>
    internal static double ElapsedSince(DateTimeOffset lastUpdated, DateTimeOffset now)
    {
        if (lastUpdated < EarliestValid || lastUpdated > now + FutureTolerance)
        {
            return 0;
        }
        return Math.Max(0, Seconds(now - lastUpdated));
    }

    private static double Seconds(TimeSpan span)
    {
        var seconds = span.TotalSeconds;
        return double.IsFinite(seconds) && seconds > 0 ? Math.Min(seconds, MaxSeconds) : 0;
    }
}
