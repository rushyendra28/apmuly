using System.Globalization;
using System.Text.RegularExpressions;
using Apmuly.Core.Text;

namespace Apmuly.Core.Lyrics;

/// <summary>A word with its start time (enhanced LRC).</summary>
public sealed record TimedWord(string Text, double Start);

/// <summary>One timestamped LRC line.</summary>
/// <param name="WordStarts">Present only for enhanced LRC (<c>&lt;mm:ss.xx&gt;word</c>).</param>
public sealed record LrcEntry(double Start, string Text, IReadOnlyList<TimedWord>? WordStarts);

/// <summary>The entries of an LRC file, sorted by start time.</summary>
public sealed record ParsedLrc(IReadOnlyList<LrcEntry> Entries);

/// <summary>Parses LRC / enhanced LRC text. Tolerant: malformed lines are skipped, never fatal.</summary>
public static partial class LrcParser
{
    private const string OffsetPrefix = "[offset:";

    [GeneratedRegex(@"<(\d+:\d+(?:\.\d+)?)>\s*([^<]*)", RegexOptions.CultureInvariant)]
    private static partial Regex EnhancedWordPattern();

    public static ParsedLrc Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rawLines = TextFolding.SplitLines(text).Select(line => line.Trim()).ToArray();
        var offset = rawLines.Select(OffsetSeconds).FirstOrDefault(value => value.HasValue) ?? 0;

        // OrderBy is stable: entries with equal start keep their file order.
        var ordered = rawLines
            .SelectMany(line => EntriesFromLine(line, offset))
            .OrderBy(entry => entry.Start)
            .ToArray();
        return new ParsedLrc(ordered);
    }

    private static LrcEntry[] EntriesFromLine(string line, double offset)
    {
        var (stamps, body) = LeadingTimestamps(line);
        if (stamps.Count == 0)
        {
            return [];
        }
        var words = EnhancedWords(body, offset);
        var text = words is null ? body.Trim() : string.Join(' ', words.Select(word => word.Text));
        return stamps.Select(stamp => new LrcEntry(Math.Max(0, stamp - offset), text, words)).ToArray();
    }

    /// <summary>Splits <c>[t1][t2]body</c> into timestamps and the remaining body.</summary>
    private static (List<double> Stamps, string Body) LeadingTimestamps(string line)
    {
        var stamps = new List<double>();
        var rest = line;
        while (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }
            var seconds = Timestamp(rest[1..close]);
            if (seconds is null)
            {
                break;
            }
            stamps.Add(seconds.Value);
            rest = rest[(close + 1)..];
        }
        return (stamps, rest);
    }

    private static double? OffsetSeconds(string line)
    {
        if (!line.StartsWith(OffsetPrefix, StringComparison.OrdinalIgnoreCase) || !line.EndsWith(']'))
        {
            return null;
        }
        var value = line[OffsetPrefix.Length..^1].Trim();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && double.IsFinite(ms)
            ? ms / 1000
            : null;
    }

    private static List<TimedWord>? EnhancedWords(string body, double offset)
    {
        var words = new List<TimedWord>();
        foreach (Match match in EnhancedWordPattern().Matches(body))
        {
            var start = Timestamp(match.Groups[1].Value);
            var text = match.Groups[2].Value.Trim();
            if (start is not null && text.Length > 0)
            {
                words.Add(new TimedWord(text, Math.Max(0, start.Value - offset)));
            }
        }
        return words.Count == 0 ? null : words;
    }

    /// <summary><c>mm:ss</c>, <c>mm:ss.f</c>, <c>mm:ss.ff</c>, <c>mm:ss.fff</c> → seconds.</summary>
    internal static double? Timestamp(string tag)
    {
        var parts = tag.Split(':');
        if (parts.Length != 2 || !TextFolding.IsAscii(parts[0]) || !TextFolding.IsAscii(parts[1]))
        {
            return null;
        }
        if (!int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var minutes) || minutes < 0)
        {
            return null;
        }
        if (!parts[1].All(c => char.IsAsciiDigit(c) || c == '.')
            || !double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0)
        {
            return null;
        }
        return minutes * 60.0 + seconds;
    }
}
