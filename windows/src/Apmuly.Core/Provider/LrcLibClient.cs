using System.Globalization;
using System.Text;
using System.Text.Json;
using Apmuly.Core.Matching;

namespace Apmuly.Core.Provider;

/// <summary>
/// Fetches lyrics from https://lrclib.net (free, no API key).
/// Strategy: exact lookup by full metadata, then a ranked search with cleaned title/artist.
/// </summary>
public sealed class LrcLibClient : ILyricsProvider
{
    public static readonly Uri DefaultBaseUri = new("https://lrclib.net");
    public const string DefaultUserAgent = "Apmuly (https://github.com/rushyendra28/apmuly)";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly IHttpGetter _http;
    private readonly Uri _baseUri;
    private readonly string _userAgent;

    public LrcLibClient(IHttpGetter http, Uri? baseUri = null, string userAgent = DefaultUserAgent)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUri = baseUri ?? DefaultBaseUri;
        _userAgent = userAgent;
    }

    public async Task<LyricsCandidate?> CandidateAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        var query = new List<(string, string)>
        {
            ("track_name", track.Title),
            ("artist_name", track.Artist),
            ("album_name", track.Album),
        };
        if (track.Duration > 0)
        {
            var seconds = (long)Math.Round(track.Duration, MidpointRounding.AwayFromZero);
            query.Add(("duration", seconds.ToString(CultureInfo.InvariantCulture)));
        }
        var exact = await FetchAsync("/api/get", query, LrcLibJson.DecodeRecord, cancellationToken).ConfigureAwait(false);
        if (exact is not null && HasSynced(exact))
        {
            return exact;
        }

        // No exact match, or the exact record is plain-only: a synced copy may exist under other metadata.
        IReadOnlyList<LyricsCandidate> results;
        try
        {
            results = await FetchAsync("/api/search",
                [("track_name", TitleNormalizer.SearchTitle(track.Title)), ("artist_name", TitleNormalizer.PrimaryArtist(track.Artist))],
                LrcLibJson.DecodeSearch, cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (LyricsException) when (exact is not null)
        {
            return exact;
        }
        var best = CandidateRanker.Best(track, results);
        if (best is not null && HasSynced(best))
        {
            return best;
        }
        return exact ?? best;
    }

    private static bool HasSynced(LyricsCandidate candidate) => !string.IsNullOrEmpty(candidate.SyncedLyrics);

    /// <summary>Decodes a 2xx body, returns null on 404, throws <see cref="LyricsException"/> otherwise.</summary>
    private async Task<T?> FetchAsync<T>(string path, IReadOnlyList<(string Name, string Value)> query,
                                         Func<byte[], T> decode, CancellationToken cancellationToken) where T : class
    {
        var request = new HttpGetRequest(BuildUri(path, query), _userAgent, Timeout);
        HttpGetResponse response;
        try
        {
            response = await _http.GetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException
                                          or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            throw new LyricsException(LyricsErrorKind.Network, inner: error);
        }

        if (response.Status == 404)
        {
            return null;
        }
        if (response.Status is < 200 or >= 300)
        {
            throw LyricsException.Server(response.Status);
        }
        try
        {
            return decode(response.Body);
        }
        catch (JsonException error)
        {
            throw new LyricsException(LyricsErrorKind.Decoding, inner: error);
        }
    }

    private Uri BuildUri(string path, IReadOnlyList<(string Name, string Value)> query)
    {
        var builder = new StringBuilder(path);
        for (var i = 0; i < query.Count; i++)
        {
            builder.Append(i == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(query[i].Name))
                .Append('=')
                .Append(Uri.EscapeDataString(query[i].Value));
        }
        return new Uri(_baseUri, builder.ToString());
    }
}
