using Apmuly.Core.Audio;
using Apmuly.Core.Lyrics;
using Apmuly.Core.Provider;
using Apmuly.Core.Visuals;

namespace Apmuly.Core.Stage;

/// <summary>
/// Decides what to send to the page each frame — a port of the Mac RendererBridge: per-track assets
/// (track, lyrics, palette, insights, artwork) once each and again when they change, the status
/// line when it changes, then the frame. Not thread-safe; call from the UI thread.
/// </summary>
public sealed class StageFeed
{
    private readonly Dictionary<string, int> _sentLyricsVersions = [];
    private readonly Dictionary<string, int> _sentInsightVersions = [];
    private readonly Dictionary<string, string> _sentTrackStates = [];
    private readonly Dictionary<string, ThemePalette> _sentPalettes = [];
    private readonly HashSet<string> _sentArtwork = [];
    private StatusPayload? _sentStatus;

    /// <summary>Forgets what was sent (the page reloaded and said "ready").</summary>
    public void Reset()
    {
        _sentLyricsVersions.Clear();
        _sentInsightVersions.Clear();
        _sentTrackStates.Clear();
        _sentPalettes.Clear();
        _sentArtwork.Clear();
        _sentStatus = null;
    }

    public IReadOnlyList<HostCall> Tick(NowPlayingModel model, double hostTime, EnergySnapshot? energy)
    {
        ArgumentNullException.ThrowIfNull(model);
        var snapshot = model.Snapshot(hostTime);
        var present = new List<(string Role, DeckSnapshot Deck)>();
        if (snapshot.Current is { } current)
        {
            present.Add(("current", current));
        }
        if (snapshot.Outgoing is { } outgoing)
        {
            present.Add(("outgoing", outgoing));
        }

        var calls = new List<HostCall>();
        foreach (var (_, deck) in present)
        {
            calls.AddRange(Assets(deck.Deck, deck.Assets));
        }
        var status = StageStatus.For(snapshot.Current?.Deck);
        if (status != _sentStatus)
        {
            _sentStatus = status;
            calls.Add(HostCall.SetStatus(status));
        }
        calls.Add(Frame(present.Select(p => (p.Role, p.Deck.Deck)).ToArray(), energy));
        return calls;
    }

    internal IReadOnlyList<HostCall> Assets(DeckRender deck, TrackAssets assets)
    {
        var id = deck.TrackId;
        var calls = new List<HostCall>();
        var lyricsState = StageStatus.LyricsState(deck.Lyrics);
        if (deck.Track is { } track && _sentTrackStates.GetValueOrDefault(id) != lyricsState)
        {
            _sentTrackStates[id] = lyricsState;
            calls.Add(HostCall.SetTrack(new TrackPayload(id, track.Title, track.Artist, lyricsState)));
        }
        if (_sentLyricsVersions.TryGetValue(id, out var sentVersion) is var known && (!known || sentVersion != assets.LyricsVersion)
            && deck.Lyrics is LyricsLoad.Loaded { Result: LyricsResult.Found found } && found.Document.Timing != LyricsTiming.Unsynced)
        {
            _sentLyricsVersions[id] = assets.LyricsVersion;
            calls.Add(HostCall.SetLyrics(LyricsPayload.From(id, found.Document)));
        }
        if (assets.Palette is { } palette && _sentPalettes.GetValueOrDefault(id) != palette)
        {
            _sentPalettes[id] = palette;
            calls.Add(HostCall.SetPalette(new PalettePayload(id, palette.Lyric, palette.Highlight, palette.Secondary)));
        }
        if (assets.Insights is { } insights && assets.InsightVersion > 0
            && _sentInsightVersions.GetValueOrDefault(id) != assets.InsightVersion)
        {
            _sentInsightVersions[id] = assets.InsightVersion;
            calls.Add(HostCall.SetInsights(InsightsPayload.From(id, insights)));
        }
        if (assets.ArtworkUrl is { } url && _sentArtwork.Add(id))
        {
            calls.Add(HostCall.SetArtwork(new ArtworkPayload(id, url)));
        }
        return calls;
    }

    internal static HostCall Frame(IReadOnlyList<(string Role, DeckRender Deck)> decks, EnergySnapshot? energy) =>
        HostCall.SetFrame(new FramePayload(
            decks.Select(d => new DeckPayload(d.Deck.TrackId, d.Role, d.Deck.LyricsPosition, d.Deck.IsRunning, d.Deck.Opacity)).ToArray(),
            energy is null ? null : new EnergyPayload(energy.Sequence, energy.Bass, energy.Level, energy.Bands)));
}
