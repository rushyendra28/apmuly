namespace Apmuly.Core.Media;

/// <summary>Chooses which media session to follow: Apple Music if it has one, else the system's current session.</summary>
public static class SessionPicker
{
    public const string AppleMusicMarker = "AppleMusic";

    /// <returns>Index into <paramref name="appIds"/>, or null when nothing should be followed.</returns>
    public static int? Pick(IReadOnlyList<string?> appIds, string? currentAppId)
    {
        ArgumentNullException.ThrowIfNull(appIds);
        for (var i = 0; i < appIds.Count; i++)
        {
            if (IsAppleMusic(appIds[i]))
            {
                return i;
            }
        }
        if (string.IsNullOrEmpty(currentAppId))
        {
            return null;
        }
        for (var i = 0; i < appIds.Count; i++)
        {
            if (string.Equals(appIds[i], currentAppId, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return null;
    }

    public static bool IsAppleMusic(string? appId) =>
        appId is not null && appId.Contains(AppleMusicMarker, StringComparison.OrdinalIgnoreCase);
}
