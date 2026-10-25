using System.Globalization;
using System.Text;

namespace Apmuly.Core.Text;

/// <summary>
/// Character-class helpers that mirror Swift's <c>Character</c> properties
/// (<c>isLetter</c>, <c>isNumber</c>, <c>isWhitespace</c>) and Foundation's
/// case/diacritic folding, working on Unicode scalars (runes) rather than UTF-16 units.
/// </summary>
public static class TextFolding
{
    private static readonly char[] NewlineChars = ['\n', '\r', '\u000B', '\u000C', '\u0085', '\u2028', '\u2029'];

    /// <summary>Lowercases and strips diacritics ("Déjà" → "deja").</summary>
    public static string FoldCaseAndDiacritics(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Only the letters of <paramref name="text"/>.</summary>
    public static string Letters(string text) => Filter(text, Rune.IsLetter);

    /// <summary>Keeps the runes matching <paramref name="keep"/>.</summary>
    public static string Filter(string text, Func<Rune, bool> keep)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (keep(rune))
            {
                builder.Append(rune.ToString());
            }
        }
        return builder.ToString();
    }

    /// <summary>Number of Unicode scalars (approximates Swift's character count for common text).</summary>
    public static int RuneCount(string text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }
        return count;
    }

    /// <summary>True when every rune is ASCII.</summary>
    public static bool IsAscii(string text) => text.All(char.IsAscii);

    /// <summary>Splits on any line terminator, dropping empty pieces (like Swift's <c>split(whereSeparator: \.isNewline)</c>).</summary>
    public static string[] SplitLines(string text) =>
        text.Split(NewlineChars, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Splits on whitespace, dropping empty pieces.</summary>
    public static string[] SplitWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
