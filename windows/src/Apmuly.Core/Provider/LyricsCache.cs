using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apmuly.Core.Provider;

/// <summary>Result of a cache lookup.</summary>
public abstract record CacheLookup
{
    private CacheLookup()
    {
    }

    /// <summary>A stored result; a null candidate is a remembered "no lyrics found".</summary>
    public sealed record Hit(LyricsCandidate? Candidate) : CacheLookup;

    public sealed record Miss : CacheLookup;
}

/// <summary>On-disk cache of provider results, one JSON file per track (thread-safe).</summary>
public sealed class LyricsCache
{
    /// <summary>"Not found" is retried after this long, in case lyrics were added upstream.</summary>
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(3);

    private sealed record Entry(
        [property: JsonPropertyName("candidate")] LyricsCandidate? Candidate,
        [property: JsonPropertyName("storedAt")] DateTimeOffset StoredAt);

    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 8 };

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _now;
    private readonly Lock _gate = new();

    public LyricsCache(string? directory = null, Func<DateTimeOffset>? now = null)
    {
        _directory = directory ?? ApmulyPaths.Lyrics;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public CacheLookup Lookup(string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        lock (_gate)
        {
            Entry? entry;
            try
            {
                var path = FilePath(trackId);
                if (!File.Exists(path))
                {
                    return new CacheLookup.Miss();
                }
                entry = JsonSerializer.Deserialize<Entry>(File.ReadAllBytes(path), Options);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                return new CacheLookup.Miss();
            }
            if (entry is null || (entry.Candidate is null && _now() - entry.StoredAt > NegativeTtl))
            {
                return new CacheLookup.Miss();
            }
            return new CacheLookup.Hit(entry.Candidate);
        }
    }

    /// <exception cref="LyricsException">With <see cref="LyricsErrorKind.Storage"/> when the file can't be written.</exception>
    public void Store(LyricsCandidate? candidate, string trackId)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        lock (_gate)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Entry(candidate, _now()), Options);
                FileNames.WriteAtomically(FilePath(trackId), bytes);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new LyricsException(LyricsErrorKind.Storage, inner: error);
            }
        }
    }

    /// <summary>Hashed so arbitrary ids can never escape the cache directory.</summary>
    private string FilePath(string trackId) => Path.Combine(_directory, FileNames.Hashed(trackId, ".json"));
}
