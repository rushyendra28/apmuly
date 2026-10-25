using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apmuly.Core.Provider;

/// <summary>Tolerant decoding of LRCLIB records: missing fields get defaults, absurd values are dropped.</summary>
internal static class LrcLibJson
{
    /// <summary>Longest lyrics text accepted from the network (a long song is ~10 KB).</summary>
    private const int MaxLyricsLength = 256 * 1024;
    private const int MaxNameLength = 1024;
    /// <summary>Search results beyond this are ignored.</summary>
    internal const int MaxSearchResults = 100;

    private sealed class Record
    {
        [JsonPropertyName("trackName")] public string? TrackName { get; set; }
        [JsonPropertyName("artistName")] public string? ArtistName { get; set; }
        [JsonPropertyName("duration")] public double? Duration { get; set; }
        [JsonPropertyName("instrumental")] public bool? Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 8,
    };

    /// <exception cref="JsonException">The body isn't a record.</exception>
    public static LyricsCandidate DecodeRecord(byte[] body)
    {
        var record = JsonSerializer.Deserialize<Record>(body, Options) ?? throw new JsonException("null record");
        return ToCandidate(record);
    }

    /// <exception cref="JsonException">The body isn't an array of records.</exception>
    public static IReadOnlyList<LyricsCandidate> DecodeSearch(byte[] body)
    {
        var records = JsonSerializer.Deserialize<List<Record?>>(body, Options) ?? throw new JsonException("null results");
        return records.Where(r => r is not null).Take(MaxSearchResults).Select(r => ToCandidate(r!)).ToArray();
    }

    private static LyricsCandidate ToCandidate(Record r) => new(
        Clip(r.TrackName ?? string.Empty, MaxNameLength),
        Clip(r.ArtistName ?? string.Empty, MaxNameLength),
        r.Duration is { } d && double.IsFinite(d) && d >= 0 ? d : null,
        r.Instrumental ?? false,
        Lyrics(r.PlainLyrics),
        Lyrics(r.SyncedLyrics));

    private static string? Lyrics(string? text) => text is { Length: > MaxLyricsLength } ? null : text;

    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;
}
