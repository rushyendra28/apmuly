namespace Apmuly.Core.Media;

/// <summary>Which player a media session belongs to.</summary>
public enum MediaSource
{
    AppleMusic,
    Spotify,
    Other,
}

/// <summary>Which player to follow (persisted as "auto" | "appleMusic" | "spotify").</summary>
public enum SourcePreference
{
    Auto,
    AppleMusic,
    Spotify,
}

public static class MediaSources
{
    /// <summary>Classifies an SMTC AppUserModelId ("AppleInc.AppleMusicWin_…!App", "Spotify.exe", …).</summary>
    public static MediaSource Classify(string? appId)
    {
        if (string.IsNullOrEmpty(appId))
        {
            return MediaSource.Other;
        }
        if (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
        {
            return MediaSource.AppleMusic;
        }
        return appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ? MediaSource.Spotify : MediaSource.Other;
    }

    /// <summary>Shown in captions and status lines.</summary>
    public static string DisplayName(this MediaSource source) => source switch
    {
        MediaSource.AppleMusic => "Apple Music",
        MediaSource.Spotify => "Spotify",
        _ => "your player",
    };

    /// <summary>Track-ID prefix, so nudges and timings never mix between players.</summary>
    public static string IdPrefix(this MediaSource source) => source switch
    {
        MediaSource.AppleMusic => "applemusic|",
        MediaSource.Spotify => "spotify|",
        _ => "other|",
    };

    public static string WireName(this SourcePreference preference) => preference switch
    {
        SourcePreference.AppleMusic => "appleMusic",
        SourcePreference.Spotify => "spotify",
        _ => "auto",
    };

    public static SourcePreference? ParsePreference(string? name) => name switch
    {
        "auto" => SourcePreference.Auto,
        "appleMusic" => SourcePreference.AppleMusic,
        "spotify" => SourcePreference.Spotify,
        _ => null,
    };

    /// <summary>Auto → Apple Music → Spotify → Auto.</summary>
    public static SourcePreference Next(this SourcePreference preference) => preference switch
    {
        SourcePreference.Auto => SourcePreference.AppleMusic,
        SourcePreference.AppleMusic => SourcePreference.Spotify,
        _ => SourcePreference.Auto,
    };

    /// <summary>The source a manual preference pins, or null for Auto.</summary>
    public static MediaSource? Pinned(this SourcePreference preference) => preference switch
    {
        SourcePreference.AppleMusic => MediaSource.AppleMusic,
        SourcePreference.Spotify => MediaSource.Spotify,
        _ => null,
    };
}
