using Apmuly.Core.Lyrics;

namespace Apmuly.Core.Provider;

/// <summary>A track as reported by the player.</summary>
/// <param name="Id">Stable identifier (see <see cref="Media.TrackIdentity"/>).</param>
/// <param name="Duration">Seconds; 0 when unknown.</param>
public sealed record TrackInfo(string Id, string Title, string Artist, string Album, double Duration);

/// <summary>A lyrics record from a provider (shape matches LRCLIB's JSON).</summary>
public sealed record LyricsCandidate(
    string TrackName,
    string ArtistName,
    double? Duration,
    bool Instrumental,
    string? PlainLyrics,
    string? SyncedLyrics);

public enum LyricsErrorKind
{
    Network,
    Server,
    Decoding,
    Storage,
}

/// <summary>Why lyrics couldn't be loaded.</summary>
public sealed class LyricsException : Exception
{
    public LyricsException(LyricsErrorKind kind, int? status = null, Exception? inner = null)
        : base(Describe(kind, status), inner)
    {
        Kind = kind;
        Status = status;
    }

    public LyricsException()
        : this(LyricsErrorKind.Network)
    {
    }

    public LyricsException(string message)
        : base(message)
    {
        Kind = LyricsErrorKind.Network;
    }

    public LyricsException(string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = LyricsErrorKind.Network;
    }

    public LyricsErrorKind Kind { get; }

    /// <summary>HTTP status for <see cref="LyricsErrorKind.Server"/>.</summary>
    public int? Status { get; }

    /// <summary>Failures that may succeed if tried again later (outages, rate limits, connectivity).</summary>
    public bool IsTransient => Kind switch
    {
        LyricsErrorKind.Network => true,
        LyricsErrorKind.Server => Status is >= 500 or 429,
        _ => false,
    };

    public static LyricsException Server(int status) => new(LyricsErrorKind.Server, status);

    private static string Describe(LyricsErrorKind kind, int? status) =>
        status is null ? $"Lyrics error: {kind}" : $"Lyrics error: {kind} (HTTP {status})";
}

/// <summary>The outcome of a lyrics lookup.</summary>
public abstract record LyricsResult
{
    private LyricsResult()
    {
    }

    public sealed record Found(LyricsDocument Document) : LyricsResult;

    public sealed record Instrumental : LyricsResult;

    public sealed record NotFound : LyricsResult;
}

public interface ILyricsProvider
{
    Task<LyricsCandidate?> CandidateAsync(TrackInfo track, CancellationToken cancellationToken = default);
}
