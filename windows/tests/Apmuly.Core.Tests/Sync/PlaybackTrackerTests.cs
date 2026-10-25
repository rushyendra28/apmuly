using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Sync;

public class PlaybackTrackerTests
{
    private static PlaybackSample Sample(string id, double position, double host, double duration = 200,
                                         PlayerState state = PlayerState.Playing, double latency = 0.02) =>
        new(id, position, duration, state, host, latency);

    private static PlaybackTracker Feed(params PlaybackSample[] samples) =>
        samples.Aggregate(new PlaybackTracker(), (tracker, sample) => tracker.Ingesting(sample));

    [Fact]
    public void FirstSampleBecomesCurrentAndFadesIn()
    {
        var tracker = Feed(Sample("A", 30, 0));
        var frame = tracker.Frame(0.2);
        Assert.Equal("A", frame.Current?.TrackId);
        Assert.Equal(30.2, frame.Current!.Position, 4);
        Assert.True(frame.Current.Opacity < 1);
        Assert.Equal(1, tracker.Frame(5).Current?.Opacity);
        Assert.Null(frame.Outgoing);
    }

    [Fact]
    public void AutoMixNonAtomicUpdateDoesNotFlashTheOldSongsStart()
    {
        const double a = 178.24;
        var tracker = Feed(Sample("A", 175.0, 0, a), Sample("A", 175.6, 0.6, a), Sample("A", 176.2, 1.2, a),
                           Sample("A", 1.70, 1.37, a)); // stale id, new position
        var held = tracker.Frame(1.37);
        Assert.Equal("A", held.Current?.TrackId);
        Assert.True(held.Current!.Position > 176);

        var switched = tracker.Ingesting(Sample("B", 1.88, 1.54, 253.59));
        var frame = switched.Frame(2.0);
        Assert.Equal("B", frame.Current?.TrackId);
        Assert.Equal(2.34, frame.Current!.Position, 4);
        Assert.Equal("A", frame.Outgoing?.TrackId);
        Assert.True(frame.Outgoing!.Position > 176.9);
        Assert.True(frame.Outgoing.Opacity is > 0 and < 1);
        Assert.Null(switched.Frame(3.5).Outgoing);
    }

    [Fact]
    public void NearEndSeekIsConfirmedByALinearSample()
    {
        var held = Feed(Sample("A", 195, 0), Sample("A", 0.2, 0.5));
        Assert.True(held.Frame(0.5).Current!.Position > 190);
        Assert.Equal(0.7, held.Ingesting(Sample("A", 0.7, 1.0)).Frame(1.0).Current!.Position, 4);
    }

    [Fact]
    public void InconsistentNearEndJumpsAreAcceptedWhenTheHoldExpires()
    {
        var tracker = Feed(Sample("A", 195, 0), Sample("A", 0.2, 0.5), Sample("A", 5.0, 1.0));
        Assert.True(tracker.Frame(1.0).Current!.Position > 190);
        var host = 0.5 + PlaybackTracker.JumpHoldTime + 0.1;
        Assert.Equal(9.0, tracker.Ingesting(Sample("A", 9.0, host)).Frame(host).Current!.Position, 4);
    }

    [Fact]
    public void ManualSkipNearTheEndFadesQuicklyInsteadOfBlending()
    {
        var tracker = Feed(Sample("A", 190, 0), Sample("B", 0.1, 0.5));
        var frame = tracker.Frame(0.6);
        Assert.Equal(frame.Outgoing?.Position, tracker.Frame(0.9).Outgoing?.Position);
        Assert.False(frame.Outgoing!.IsRunning);
        Assert.Null(tracker.Frame(0.5 + PlaybackTracker.QuickFade + 0.01).Outgoing);
    }

    [Fact]
    public void OlderSamplesAreIgnored()
    {
        var tracker = Feed(Sample("A", 50, 0), Sample("", 0, 2, state: PlayerState.Stopped), Sample("X", 10, 1));
        Assert.Null(tracker.Frame(2.5).Current);
    }

    [Fact]
    public void EmptyTrackIdIsStopped() => Assert.Null(Feed(Sample("A", 50, 0), Sample("", 0, 1)).Frame(1).Current);

    [Fact]
    public void UnknownDurationDoesNotClampHoldOrBlend()
    {
        var tracker = Feed(Sample("A", 30, 0, 0), Sample("A", 5, 0.5, 0));
        Assert.Equal(5, tracker.Frame(0.5).Current?.Position);
        Assert.Equal(15, tracker.Frame(10.5).Current?.Position);
        var switched = tracker.Ingesting(Sample("B", 2.0, 11, 0));
        Assert.Null(switched.Frame(11 + PlaybackTracker.QuickFade + 0.01).Outgoing);
    }

    [Fact]
    public void BackwardsSeekMidSongAppliesImmediately() =>
        Assert.Equal(20, Feed(Sample("A", 100, 0), Sample("A", 20, 0.5)).Frame(0.5).Current?.Position);

    [Fact]
    public void ForwardSeekAppliesImmediately() =>
        Assert.Equal(150, Feed(Sample("A", 10, 0), Sample("A", 150, 0.5)).Frame(0.5).Current?.Position);

    [Fact]
    public void ManualSkipMidSongFreezesAndFadesTheOldDeck()
    {
        var tracker = Feed(Sample("A", 60, 0), Sample("B", 0.1, 1));
        var early = tracker.Frame(1.1);
        Assert.Equal("B", early.Current?.TrackId);
        Assert.Equal("A", early.Outgoing?.TrackId);
        Assert.Equal(early.Outgoing?.Position, tracker.Frame(1.3).Outgoing?.Position);
        Assert.Null(tracker.Frame(1 + PlaybackTracker.QuickFade + 0.01).Outgoing);
    }

    [Fact]
    public void NaturalEndLeavesNoOutgoingDeck() =>
        Assert.Null(Feed(Sample("A", 199.9, 0), Sample("B", 0.0, 0.3)).Frame(0.3).Outgoing);

    [Fact]
    public void PauseFreezesResumeContinues()
    {
        var paused = Feed(Sample("A", 50, 0), Sample("A", 51, 1, state: PlayerState.Paused));
        Assert.Equal(51, paused.Frame(10).Current?.Position);
        Assert.False(paused.Frame(10).Current?.IsRunning);
        var resumed = paused.Ingesting(Sample("A", 51, 10));
        Assert.Equal(52, resumed.Frame(11).Current?.Position);
        Assert.True(resumed.Frame(11).Current?.IsRunning);
    }

    [Fact]
    public void StoppedClearsTheCurrentDeck() =>
        Assert.Null(Feed(Sample("A", 50, 0), Sample("", 0, 1, state: PlayerState.Stopped)).Frame(1).Current);

    [Fact]
    public void PositionIsClampedToTheDuration() => Assert.Equal(200, Feed(Sample("A", 199, 0)).Frame(10).Current?.Position);
}
