using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Sync;

public class TimelineSamplerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static TimelineSnapshot Snapshot(double position, double secondsAgo, PlayerState state = PlayerState.Playing,
                                             double end = 200, double start = 0) =>
        new(TimeSpan.FromSeconds(position), Now.AddSeconds(-secondsAgo), TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), state);

    [Fact]
    public void ExtrapolatesAPlayingSnapshotToNow()
    {
        var sample = TimelineSampler.ToSample("A", Snapshot(30, 4), 1000, Now);
        Assert.Equal(34, sample.Position, 6);
        Assert.Equal(200, sample.Duration);
        Assert.Equal(1000, sample.HostTime);
        Assert.Equal(PlayerState.Playing, sample.State);
        Assert.Equal(0, sample.Latency);
    }

    [Fact]
    public void PausedSnapshotsAreNotExtrapolated() =>
        Assert.Equal(30, TimelineSampler.ToSample("A", Snapshot(30, 4, PlayerState.Paused), 1000, Now).Position);

    [Fact]
    public void ExtrapolationStopsAtTheEnd() =>
        Assert.Equal(200, TimelineSampler.ToSample("A", Snapshot(190, 60), 1000, Now).Position);

    [Fact]
    public void UnsetOrFutureTimestampsAreNotExtrapolated()
    {
        var unset = Snapshot(30, 0) with { LastUpdated = DateTimeOffset.MinValue };
        Assert.Equal(30, TimelineSampler.ToSample("A", unset, 1000, Now).Position);
        Assert.Equal(30, TimelineSampler.ToSample("A", Snapshot(30, -60), 1000, Now).Position);
    }

    [Fact]
    public void PositionsAreRelativeToTheTimelineStart()
    {
        var sample = TimelineSampler.ToSample("A", Snapshot(15, 0, PlayerState.Paused, end: 110, start: 10), 1000, Now);
        Assert.Equal(5, sample.Position);
        Assert.Equal(100, sample.Duration);
    }

    [Fact]
    public void GarbageValuesBecomeZero()
    {
        var sample = TimelineSampler.ToSample("A", Snapshot(-5, 0, PlayerState.Paused, end: -1), 1000, Now);
        Assert.Equal(0, sample.Position);
        Assert.Equal(0, sample.Duration);
    }

    [Fact]
    public void RepeatedSnapshotsKeepTheTrackerSteady()
    {
        // SMTC often re-reports the same snapshot; extrapolating it each poll must not move the clock.
        var snapshot = Snapshot(30, 0);
        var tracker = new PlaybackTracker().Ingesting(TimelineSampler.ToSample("A", snapshot, 1000, Now));
        for (var i = 1; i <= 5; i++)
        {
            tracker = tracker.Ingesting(TimelineSampler.ToSample("A", snapshot, 1000 + i, Now.AddSeconds(i)));
        }
        Assert.Equal(35, tracker.Frame(1005).Current!.Position, 6);
    }

    [Fact]
    public void ASeekSnapshotSnapsTheTracker()
    {
        var tracker = new PlaybackTracker().Ingesting(TimelineSampler.ToSample("A", Snapshot(30, 0), 1000, Now));
        var seek = new TimelineSnapshot(TimeSpan.FromSeconds(120), Now.AddSeconds(2), TimeSpan.Zero, TimeSpan.FromSeconds(200), PlayerState.Playing);
        tracker = tracker.Ingesting(TimelineSampler.ToSample("A", seek, 1002, Now.AddSeconds(2)));
        Assert.Equal(120, tracker.Frame(1002).Current!.Position, 6);
    }
}
