using System.Text.Json;

namespace Apmuly.Core.Stage;

public enum PageMessageType
{
    Ready,
    Error,
    Key,
}

/// <summary>A validated message from the page (<c>{"type": ..., "message": ...}</c>).</summary>
public sealed record PageMessage(PageMessageType Type, string? Message)
{
    private const int MaxKeyLength = 32;
    private const int MaxErrorLength = 2000;
    private const int MaxJsonLength = 64 * 1024;

    /// <summary>Parses <c>WebMessageAsJson</c>; anything malformed or unknown returns null.</summary>
    public static PageMessage? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxJsonLength)
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return type.GetString() switch
            {
                "ready" => new PageMessage(PageMessageType.Ready, null),
                "error" => new PageMessage(PageMessageType.Error, Truncate(message ?? "unknown", MaxErrorLength)),
                "key" when message is { Length: > 0 and <= MaxKeyLength } => new PageMessage(PageMessageType.Key, message),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;
}
