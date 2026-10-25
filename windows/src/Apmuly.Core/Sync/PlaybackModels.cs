namespace Apmuly.Core.Sync;

public enum PlayerState
{
    Playing,
    Paused,
    Stopped,
}

/// <summary>One reading of the player.</summary>
/// <param name="Duration">0 when unknown.</param>
/// <param name="HostTime">Monotonic seconds when the position was valid.</param>
/// <param name="Latency">How imprecise the reading's timing is (seconds).</param>
public sealed record PlaybackSample(string TrackId, double Position, double Duration, PlayerState State, double HostTime, double Latency);

/// <param name="Duration">0 when unknown.</param>
/// <param name="Since">Host time this deck became current (drives the fade-in).</param>
public sealed record Deck(string TrackId, double Duration, PlaybackClock Clock, double Since)
{
    internal bool HasKnownDuration => Duration > 0;
}

public sealed record OutgoingDeck(Deck Deck, double FadeStart, double FadeEnd);

/// <summary>What the renderer needs for one deck on one frame.</summary>
/// <param name="IsRunning">Whether the position is advancing (lets renderers extrapolate between updates).</param>
/// <param name="Opacity">0…1, linear; renderers apply their own easing.</param>
public sealed record DeckFrame(string TrackId, double Position, bool IsRunning, double Opacity);

public sealed record PlaybackFrame(DeckFrame? Current, DeckFrame? Outgoing);

/// <summary>A backwards jump near a track's end that may be an in-progress transition.</summary>
/// <param name="Since">When the first suspicious sample arrived.</param>
/// <param name="Position">The most recent suspicious sample.</param>
internal sealed record PendingJump(double Since, double Position, double HostTime);
