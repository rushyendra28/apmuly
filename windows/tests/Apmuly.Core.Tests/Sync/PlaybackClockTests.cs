using Apmuly.Core.Sync;

namespace Apmuly.Core.Tests.Sync;

public class PlaybackClockTests
{
    private static readonly PlaybackClock Running = new(10, 100, true);

    [Fact]
    public void ExtrapolatesWhileRunningHoldsWhilePaused()
    {
        Assert.Equal(12.5, Running.Position(102.5));
        Assert.Equal(10, new PlaybackClock(10, 100, false).Position(150));
    }

    [Fact]
    public void SmallErrorIsSmoothedNotSnapped()
    {
        var now = Running.Correcting(11.2, 101, 0.02, true).Position(101);
        Assert.True(now > 11.0 && now < 11.2);
    }

    [Fact]
    public void PersistentErrorConverges()
    {
        var clock = Running;
        for (var i = 1; i <= 10; i++)
        {
            clock = clock.Correcting(10 + i * 0.5 + 0.2, 100 + i * 0.5, 0.02, true);
        }
        Assert.True(Math.Abs(clock.Position(105) - 15.2) < 0.01);
    }

    [Fact]
    public void LargeErrorSnaps() => Assert.Equal(60, Running.Correcting(60, 101, 0.02, true).Position(101));

    [Fact]
    public void SlowSamplesAreIgnored() => Assert.Equal(Running, Running.Correcting(11.3, 101, 0.5, true));

    [Fact]
    public void SeeksApplyEvenWhenTheSampleWasSlow() => Assert.Equal(90, Running.Correcting(90, 101, 0.5, true).Position(101));

    [Fact]
    public void StateChangeSnapsToTheReportedPosition()
    {
        var paused = Running.Correcting(10.9, 101, 0.02, false);
        Assert.False(paused.IsRunning);
        Assert.Equal(10.9, paused.Position(120));
    }

    [Fact]
    public void PausedClockFollowsSeeksEvenOnSlowSamples() =>
        Assert.Equal(42, new PlaybackClock(10, 100, false).Correcting(42, 101, 0.5, false).Position(102));
}
