using Apmuly.Core.Provider;
using Apmuly.Core.Sync;

namespace Apmuly.Core.Media;

/// <summary>A validated reading: the track (null when nothing is playing) and its playback sample.</summary>
public sealed record NowPlayingSample(TrackInfo? Track, PlaybackSample Sample);

/// <summary>Turns a raw <see cref="MediaReading"/> into what <see cref="Stage.NowPlayingModel"/> ingests.</summary>
public static class MediaSampler
{
    public static NowPlayingSample From(MediaReading? reading, double hostTime, DateTimeOffset now)
    {
        if (reading is null)
        {
            return Stopped(hostTime);
        }
        var metadata = MetadataNormalizer.Normalize(reading.Title, reading.Artist, reading.Album);
        if (metadata.Title.Length == 0 || reading.Timeline.State == PlayerState.Stopped)
        {
            return Stopped(hostTime);
        }
        var probe = TimelineSampler.ToSample(string.Empty, reading.Timeline, hostTime, now);
        var id = TrackIdentity.Compute(metadata.Title, metadata.Artist, metadata.Album, probe.Duration);
        var track = new TrackInfo(id, metadata.Title, metadata.Artist, metadata.Album, probe.Duration);
        return new NowPlayingSample(track, probe with { TrackId = id });
    }

    private static NowPlayingSample Stopped(double hostTime) =>
        new(null, new PlaybackSample(string.Empty, 0, 0, PlayerState.Stopped, hostTime, 0));
}
