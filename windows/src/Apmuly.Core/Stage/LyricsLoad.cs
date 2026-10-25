using Apmuly.Core.Insights;
using Apmuly.Core.Provider;
using Apmuly.Core.Visuals;

namespace Apmuly.Core.Stage;

/// <summary>Where a track's lyrics are in their life cycle.</summary>
public abstract record LyricsLoad
{
    private LyricsLoad()
    {
    }

    public sealed record Loading : LyricsLoad;

    public sealed record Loaded(LyricsResult Result) : LyricsLoad;

    public sealed record Failed(LyricsErrorKind Kind, int? Status) : LyricsLoad;
}

/// <summary>Everything a view needs to draw one deck (current or outgoing track).</summary>
/// <param name="LyricsPosition">Playback position with the user's lyric nudge applied.</param>
public sealed record DeckRender(
    string TrackId,
    TrackInfo? Track,
    LyricsLoad? Lyrics,
    double LyricsPosition,
    bool IsRunning,
    double Opacity);

/// <summary>One deck and its track's assets.</summary>
public sealed record DeckSnapshot(DeckRender Deck, TrackAssets Assets);

/// <summary>What the stage needs for one frame, read atomically.</summary>
public sealed record StageSnapshot(DeckSnapshot? Current, DeckSnapshot? Outgoing);

/// <summary>A track's renderer assets and their versions (bumped on change, so the feed knows to resend).</summary>
public sealed record TrackAssets(
    int LyricsVersion,
    ThemePalette? Palette,
    IReadOnlyDictionary<int, LineInsight>? Insights,
    int InsightVersion,
    string? ArtworkUrl);
