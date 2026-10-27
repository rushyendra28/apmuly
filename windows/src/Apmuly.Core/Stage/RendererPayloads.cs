using System.Text.Json.Serialization;
using Apmuly.Core.Insights;
using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Stage;

// JSON shapes sent to the renderer (see App/Resources/Renderer/js/main.js). Null members are omitted.

public sealed record LyricsPayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("timing")] string Timing,
    [property: JsonPropertyName("lines")] IReadOnlyList<LyricsPayload.Line> Lines)
{
    public sealed record Word(
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("s")] double S,
        [property: JsonPropertyName("e")] double E);

    public sealed record Line(
        [property: JsonPropertyName("s")] double S,
        [property: JsonPropertyName("e")] double E,
        [property: JsonPropertyName("t")] string T,
        [property: JsonPropertyName("w")] IReadOnlyList<Word> W);

    public static LyricsPayload From(string trackId, LyricsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var lines = document.Lines.Select(line => new Line(line.Start, line.End, line.Text,
            line.Words.Select(w => new Word(w.Text, w.Start, w.End)).ToArray())).ToArray();
        return new LyricsPayload(trackId, document.Timing.WireName(), lines);
    }
}

public sealed record InsightsPayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("lines")] IReadOnlyList<InsightsPayload.Line> Lines)
{
    public sealed record Item(
        [property: JsonPropertyName("word")] string Word,
        [property: JsonPropertyName("emoji")] string? Emoji,
        [property: JsonPropertyName("wordIndex")] int? WordIndex);

    public sealed record Line(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("emoji")] string? Emoji,
        [property: JsonPropertyName("burst")] bool Burst,
        [property: JsonPropertyName("objects")] IReadOnlyList<Item> Objects);

    public static InsightsPayload From(string trackId, IReadOnlyDictionary<int, LineInsight> insights)
    {
        ArgumentNullException.ThrowIfNull(insights);
        var lines = insights.OrderBy(kv => kv.Key).Select(kv => new Line(kv.Key, kv.Value.Emoji, kv.Value.Burst,
            kv.Value.Objects.Select(o => new Item(o.Word, o.Emoji, o.WordIndex)).ToArray())).ToArray();
        return new InsightsPayload(trackId, lines);
    }
}

public sealed record PalettePayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("lyric")] string Lyric,
    [property: JsonPropertyName("highlight")] string Highlight,
    [property: JsonPropertyName("secondary")] string Secondary);

public sealed record ArtworkPayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("url")] string Url);

/// <summary>Per-track info the stage shows when there are no lyrics.</summary>
/// <param name="Lyrics">"synced", "loading" or "none".</param>
public sealed record TrackPayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("lyrics")] string Lyrics,
    [property: JsonPropertyName("source")] string? Source = null);

/// <summary>What the page shows when there is nothing else to display; empty strings clear it.</summary>
public sealed record StatusPayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("detail")] string Detail)
{
    public static readonly StatusPayload None = new(string.Empty, string.Empty);
}

/// <param name="ForwardKeys">The page sends key presses to the host.</param>
/// <param name="Sheet">The page draws Lyrics mode itself (WPF can't overlay WebView2).</param>
/// <param name="Pacing">Live singer pacing: the page moves estimated word times onto the singer.</param>
public sealed record OptionsPayload(
    [property: JsonPropertyName("forwardKeys")] bool ForwardKeys,
    [property: JsonPropertyName("sheet")] bool Sheet,
    [property: JsonPropertyName("pacing")] bool Pacing,
    [property: JsonPropertyName("emoji")] bool Emoji);

public sealed record DeckPayload(
    [property: JsonPropertyName("trackID")] string TrackId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("position")] double Position,
    [property: JsonPropertyName("isRunning")] bool IsRunning,
    [property: JsonPropertyName("opacity")] double Opacity);

public sealed record EnergyPayload(
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("bass")] double Bass,
    [property: JsonPropertyName("level")] double Level,
    [property: JsonPropertyName("bands")] IReadOnlyList<double> Bands);

public sealed record FramePayload(
    [property: JsonPropertyName("decks")] IReadOnlyList<DeckPayload> Decks,
    [property: JsonPropertyName("energy")] EnergyPayload? Energy);
