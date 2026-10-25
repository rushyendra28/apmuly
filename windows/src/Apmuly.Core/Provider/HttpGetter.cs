using System.Net.Http.Headers;

namespace Apmuly.Core.Provider;

/// <summary>A GET request with the only headers Apmuly sends.</summary>
public sealed record HttpGetRequest(Uri Uri, string UserAgent, TimeSpan Timeout);

/// <summary>Status code and body of a response.</summary>
public sealed record HttpGetResponse(int Status, byte[] Body);

/// <summary>Minimal HTTP seam so the LRCLIB client can be tested without a network.</summary>
public interface IHttpGetter
{
    /// <summary>Returns the status and body; throws on transport failures.</summary>
    Task<HttpGetResponse> GetAsync(HttpGetRequest request, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IHttpGetter"/> over <see cref="HttpClient"/>, with a response size cap.</summary>
public sealed class HttpClientGetter : IHttpGetter
{
    /// <summary>LRCLIB responses are a few KB; anything this large is not a lyrics record.</summary>
    public const int MaxResponseBytes = 4 * 1024 * 1024;

    private readonly HttpClient _client;

    public HttpClientGetter(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<HttpGetResponse> GetAsync(HttpGetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Uri);
        message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            throw new HttpRequestException("Response too large");
        }
        var body = await ReadCappedAsync(response.Content, timeout.Token).ConfigureAwait(false);
        return new HttpGetResponse((int)response.StatusCode, body);
    }

    private static async Task<byte[]> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw new HttpRequestException("Response too large");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
