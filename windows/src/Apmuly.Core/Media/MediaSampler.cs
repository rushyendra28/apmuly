using Apmuly.Core.Provider;
using Apmuly.Core.Sync;

namespace Apmuly.Core.Media;

/// <summary>A validated reading: the track (null when nothing is playing) and its playback sample.</summary>
public sealed record NowPlayingSample(TrackInfo? Track, PlaybackSample Sample);

/// <summary>Turns a raw <see cref="MediaReading"/> into what <see cref="Stage.NowPlayingModel"/> ingests.</summary>
public static class MediaSampler
{
    /// <summary>Spotify ads are short and credit no artist.</summary>
    public const double MaxAdSeconds = 31;

    public static NowPlayingSample From(MediaReading? reading, double hostTime, DateTimeOffset now)
    {
        if (reading is null)
        {
            return Stopped(hostTime);
        }
        var source = MediaSources.Classify(reading.SourceAppId);
        var metadata = MetadataNormalizer.Normalize(reading.Title, reading.Artist, reading.Album,
                                                    splitArtistAlbum: source == MediaSource.AppleMusic);
        if (metadata.Title.Length == 0 || reading.Timeline.State == PlayerState.Stopped)
        {
            return Stopped(hostTime);
        }
        var probe = TimelineSampler.ToSample(string.Empty, reading.Timeline, hostTime, now);
        var id = source.IdPrefix() + TrackIdentity.Compute(metadata.Title, metadata.Artist, metadata.Album, probe.Duration);
        var track = new TrackInfo(id, metadata.Title, metadata.Artist, metadata.Album, probe.Duration,
                                  source == MediaSource.Other ? string.Empty : source.DisplayName(),
                                  source == MediaSource.Spotify && IsSpotifyAd(metadata, probe.Duration));
        return new NowPlayingSample(track, probe with { TrackId = id });
    }

    /// <summary>Spotify reports ads as "Advertisement" or "Spotify", or as a short item with no artist.</summary>
    internal static bool IsSpotifyAd(MediaMetadata metadata, double duration) =>
        metadata.Title.Equals("Advertisement", StringComparison.OrdinalIgnoreCase)
        || metadata.Title.Equals("Spotify", StringComparison.OrdinalIgnoreCase)
        || (metadata.Artist.Length == 0 && duration < MaxAdSeconds);

    private static NowPlayingSample Stopped(double hostTime) =>
        new(null, new PlaybackSample(string.Empty, 0, 0, PlayerState.Stopped, hostTime, 0));
}
