namespace Apmuly.Core.Sync;

/// <summary>
/// Follows the player across seeks, pauses and crossfade transitions (immutable; each sample returns a new tracker).
/// </summary>
public sealed record PlaybackTracker
{
    /// <summary>A backwards jump this close to the end of a track may be a transition in progress.</summary>
    public const double TransitionEdge = 20;
    /// <summary>Longest a suspected-transition jump is held before being accepted as a seek.</summary>
    public const double JumpHoldTime = 1.5;
    /// <summary>Longest the outgoing track keeps running after a switch.</summary>
    public const double MaxOverlap = 12;
    /// <summary>An incoming track already this far in means the player is blending; a manual skip starts near 0.</summary>
    public const double BlendEvidence = 1.0;
    /// <summary>Fade for a manual skip (old track has stopped).</summary>
    public const double QuickFade = 0.6;
    public const double FadeIn = 0.8;
    private const double MinimumOverlap = 0.25;
    /// <summary>Two held samples this far apart that advance in step confirm a real seek.</summary>
    private const double ConfirmGap = 0.3;
    private const double LinearTolerance = 0.3;

    public PlaybackTracker()
    {
    }

    private PlaybackTracker(Deck? current, OutgoingDeck? outgoing, PendingJump? pendingJump, double? lastHostTime)
    {
        Current = current;
        Outgoing = outgoing;
        PendingJump = pendingJump;
        LastHostTime = lastHostTime;
    }

    public Deck? Current { get; }

    public OutgoingDeck? Outgoing { get; }

    internal PendingJump? PendingJump { get; }

    /// <summary>Host time of the last sample applied; older samples are stale.</summary>
    internal double? LastHostTime { get; }

    public PlaybackTracker Ingesting(PlaybackSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        // Reads can finish out of order (a new track's metadata fetch takes extra time).
        if (LastHostTime is { } last && sample.HostTime < last)
        {
            return this;
        }
        var t = sample.HostTime;
        var liveOutgoing = Outgoing is { } o && o.FadeEnd > t ? o : null;
        if (sample.State == PlayerState.Stopped || sample.TrackId.Length == 0)
        {
            return new PlaybackTracker(null, liveOutgoing, null, t);
        }
        if (Current is null)
        {
            return new PlaybackTracker(DeckFrom(sample), liveOutgoing, null, t);
        }
        return Current.TrackId == sample.TrackId
            ? Continuing(Current, sample, liveOutgoing)
            : Switching(Current, sample);
    }

    private PlaybackTracker Continuing(Deck deck, PlaybackSample sample, OutgoingDeck? outgoing)
    {
        var t = sample.HostTime;
        var isRunning = sample.State == PlayerState.Playing;
        var predicted = deck.Clock.Position(t);
        var jumpedBack = deck.Clock.IsRunning && isRunning
            && deck.HasKnownDuration
            && deck.Duration - predicted < TransitionEdge
            && sample.Position < predicted - PlaybackClock.SnapThreshold;

        if (jumpedBack && Holding(sample) is { } hold)
        {
            // Probably a transition whose track id hasn't caught up yet: keep the old clock.
            return new PlaybackTracker(deck, outgoing, hold, t);
        }
        var clock = deck.Clock.Correcting(sample.Position, t, sample.Latency, isRunning);
        var updated = new Deck(deck.TrackId, sample.Duration, clock, deck.Since);
        return new PlaybackTracker(updated, outgoing, null, t);
    }

    /// <summary>The updated hold for a suspicious jump, or null once it's confirmed as a real seek.</summary>
    private PendingJump? Holding(PlaybackSample sample)
    {
        var t = sample.HostTime;
        if (PendingJump is not { } pending)
        {
            return new PendingJump(t, sample.Position, t);
        }
        var elapsed = t - pending.HostTime;
        var advancesInStep = elapsed >= ConfirmGap
            && Math.Abs(sample.Position - (pending.Position + elapsed)) < LinearTolerance;
        var expired = t - pending.Since >= JumpHoldTime;
        return advancesInStep || expired ? null : new PendingJump(pending.Since, sample.Position, t);
    }

    private static PlaybackTracker Switching(Deck old, PlaybackSample sample)
    {
        var t = sample.HostTime;
        var incoming = DeckFrom(sample);
        var remaining = old.HasKnownDuration ? old.Duration - old.Clock.Position(t) : double.PositiveInfinity;

        if (remaining <= MinimumOverlap)
        {
            return new PlaybackTracker(incoming, null, null, t);
        }
        var blending = old.Clock.IsRunning && remaining <= MaxOverlap && sample.Position >= BlendEvidence;
        var leaving = blending
            ? new OutgoingDeck(old, t, t + remaining)
            : new OutgoingDeck(old with { Clock = old.Clock.WithRunning(false, t) }, t, t + QuickFade);
        return new PlaybackTracker(incoming, leaving, null, t);
    }

    private static Deck DeckFrom(PlaybackSample sample) =>
        new(sample.TrackId, sample.Duration,
            new PlaybackClock(sample.Position, sample.HostTime, sample.State == PlayerState.Playing),
            sample.HostTime);

    public PlaybackFrame Frame(double hostTime)
    {
        var current = Current is { } deck
            ? new DeckFrame(deck.TrackId, Clamped(deck, hostTime), deck.Clock.IsRunning,
                            Math.Min(1, Math.Max(0, (hostTime - deck.Since) / FadeIn)))
            : null;
        DeckFrame? outgoing = null;
        if (Outgoing is { } o && hostTime < o.FadeEnd)
        {
            var progress = (hostTime - o.FadeStart) / (o.FadeEnd - o.FadeStart);
            outgoing = new DeckFrame(o.Deck.TrackId, Clamped(o.Deck, hostTime), o.Deck.Clock.IsRunning,
                                     Math.Min(1, Math.Max(0, 1 - progress)));
        }
        return new PlaybackFrame(current, outgoing);
    }

    private static double Clamped(Deck deck, double hostTime)
    {
        var position = Math.Max(0, deck.Clock.Position(hostTime));
        return deck.HasKnownDuration ? Math.Min(deck.Duration, position) : position;
    }
}
