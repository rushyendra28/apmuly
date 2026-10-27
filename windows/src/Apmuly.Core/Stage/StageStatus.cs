using Apmuly.Core.Lyrics;
using Apmuly.Core.Media;
using Apmuly.Core.Provider;

namespace Apmuly.Core.Stage;

/// <summary>The status the page shows when there are no synced lyrics to draw (mirrors the Mac overlays).</summary>
public static class StageStatus
{
    public const string NothingPlaying = "Play something in Apple Music or Spotify";
    public const string NothingPlayingDetail = "Apmuly follows whichever one is playing (S to choose)";
    public const string OpenAppleMusic = "Open Apple Music and play something";
    public const string OpenSpotify = "Open Spotify and play something";
    public const string PinnedDetail = "Press S to follow another player";
    public const string Looking = "Looking for lyrics…";
    public const string Failed = "Couldn't load lyrics";
    public const string FailedDetail = "Press R to retry";
    public const string Unsynced = "No synced lyrics for this song";
    public const string Instrumental = "♪ Instrumental";
    public const string NotFound = "No lyrics found for this song";

    public static StatusPayload For(DeckRender? current, SourcePreference preference = SourcePreference.Auto)
    {
        if (current is null)
        {
            return preference switch
            {
                SourcePreference.AppleMusic => new StatusPayload(OpenAppleMusic, PinnedDetail),
                SourcePreference.Spotify => new StatusPayload(OpenSpotify, PinnedDetail),
                _ => new StatusPayload(NothingPlaying, NothingPlayingDetail),
            };
        }
        if (current.Track is { SkipLyrics: true })
        {
            return StatusPayload.None;
        }
        var track = TrackLine(current.Track);
        return current.Lyrics switch
        {
            null or LyricsLoad.Loading => new StatusPayload(Looking, track),
            LyricsLoad.Failed => new StatusPayload(Failed, FailedDetail),
            LyricsLoad.Loaded { Result: LyricsResult.Found { Document.Timing: LyricsTiming.Unsynced } } => new StatusPayload(Unsynced, track),
            LyricsLoad.Loaded { Result: LyricsResult.Found } => StatusPayload.None,
            LyricsLoad.Loaded { Result: LyricsResult.Instrumental } => new StatusPayload(Instrumental, track),
            _ => new StatusPayload(NotFound, track),
        };
    }

    /// <summary>"synced", "loading" or "none" for <c>setTrack</c>.</summary>
    public static string LyricsState(LyricsLoad? load) => load switch
    {
        LyricsLoad.Loaded { Result: LyricsResult.Found found } when found.Document.Timing != LyricsTiming.Unsynced => "synced",
        null or LyricsLoad.Loading => "loading",
        _ => "none",
    };

    private static string TrackLine(TrackInfo? track) =>
        track is null ? string.Empty
        : track.Artist.Length == 0 ? track.Title
        : $"{track.Title} — {track.Artist}";
}
