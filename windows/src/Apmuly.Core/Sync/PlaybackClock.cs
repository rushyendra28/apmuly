namespace Apmuly.Core.Sync;

/// <summary>
/// A local playback clock, anchored to the player's reported position and extrapolated between updates.
/// </summary>
public sealed record PlaybackClock(double AnchorPosition, double AnchorHostTime, bool IsRunning)
{
    /// <summary>Errors larger than this are treated as seeks and applied immediately.</summary>
    public const double SnapThreshold = 0.75;
    /// <summary>Fraction of a small error corrected per sample — hides jitter in the reported position.</summary>
    public const double CorrectionGain = 0.3;
    /// <summary>Samples that took longer than this to read are too imprecise to use for fine corrections.</summary>
    public const double MaxUsableLatency = 0.12;

    public double Position(double hostTime) =>
        IsRunning ? AnchorPosition + (hostTime - AnchorHostTime) : AnchorPosition;

    /// <summary>Returns a clock corrected towards a new observation.</summary>
    public PlaybackClock Correcting(double observed, double hostTime, double latency, bool running)
    {
        var snapped = new PlaybackClock(observed, hostTime, running);
        if (running != IsRunning || !running)
        {
            return snapped;
        }
        var predicted = Position(hostTime);
        var error = observed - predicted;
        // Seeks always apply; only fine corrections need a precise (fast) sample.
        if (Math.Abs(error) > SnapThreshold)
        {
            return snapped;
        }
        if (latency > MaxUsableLatency)
        {
            return this;
        }
        return new PlaybackClock(predicted + error * CorrectionGain, hostTime, true);
    }

    /// <summary>Same position at <paramref name="hostTime"/>, but with the given running state.</summary>
    public PlaybackClock WithRunning(bool running, double hostTime) => new(Position(hostTime), hostTime, running);
}
