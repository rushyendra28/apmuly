using System.Text;
using System.Text.RegularExpressions;
using Apmuly.Core.Text;

namespace Apmuly.Core.Matching;

/// <summary>Cleans up track metadata for lyric searches and fuzzy comparison.</summary>
public static partial class TitleNormalizer
{
    [GeneratedRegex(@"\s*[\(\[](?:feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*remaster[^\)\]]*[\)\]]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BracketedRemaster();

    [GeneratedRegex(@"\s+-\s+.*remaster.*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DashRemaster();

    [GeneratedRegex(@"\s+(?:&|feat\.?|ft\.?|x|with)\s+|\s*,\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtistSeparator();

    /// <summary>Drops featured-artist and remaster suffixes; keeps remix/version info (different timing).</summary>
    public static string SearchTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return new[] { Featuring(), BracketedRemaster(), DashRemaster() }
            .Aggregate(title, (text, pattern) => pattern.Replace(text, string.Empty))
            .Trim();
    }

    /// <summary>First credited artist: "A &amp; B", "A, B", "A feat. B", "A x B" → "A".</summary>
    public static string PrimaryArtist(string artist)
    {
        ArgumentNullException.ThrowIfNull(artist);
        var match = ArtistSeparator().Match(artist);
        if (!match.Success)
        {
            return artist;
        }
        var primary = artist[..match.Index].Trim();
        return primary.Length == 0 ? artist : primary;
    }

    /// <summary>Case-, accent- and punctuation-insensitive comparison key.</summary>
    public static string Key(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var folded = TextFolding.FoldCaseAndDiacritics(text);
        var spaced = new StringBuilder(folded.Length);
        foreach (var rune in folded.EnumerateRunes())
        {
            spaced.Append(Rune.IsLetter(rune) || Rune.IsNumber(rune) ? rune.ToString() : " ");
        }
        return string.Join(' ', spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
