using System.Text;

namespace Apmuly.Core.Media;

/// <summary>
/// Validates now-playing metadata from other apps: strips control characters, trims, caps the length,
/// and undoes Apple Music for Windows' habit of reporting "Artist — Album" in the artist field.
/// </summary>
public static class MetadataNormalizer
{
    public const int MaxFieldLength = 512;
    private const string AppleMusicSeparator = " — ";

    public static MediaMetadata Normalize(string? title, string? artist, string? album)
    {
        var cleanTitle = Clean(title);
        var cleanArtist = Clean(artist);
        var cleanAlbum = Clean(album);
        var split = cleanArtist.IndexOf(AppleMusicSeparator, StringComparison.Ordinal);
        if (cleanAlbum.Length == 0 && split > 0)
        {
            cleanAlbum = cleanArtist[(split + AppleMusicSeparator.Length)..].Trim();
            cleanArtist = cleanArtist[..split].Trim();
        }
        return new MediaMetadata(cleanTitle, cleanArtist, cleanAlbum);
    }

    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        var builder = new StringBuilder(Math.Min(text.Length, MaxFieldLength));
        foreach (var rune in text.EnumerateRunes())
        {
            if (builder.Length >= MaxFieldLength)
            {
                break;
            }
            builder.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        }
        return builder.ToString().Trim();
    }
}
