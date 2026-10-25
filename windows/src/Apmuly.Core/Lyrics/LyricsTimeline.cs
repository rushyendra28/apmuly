using Apmuly.Core.Text;

namespace Apmuly.Core.Lyrics;

/// <summary>Turns parsed LRC entries into a <see cref="LyricsDocument"/> with line ends and word timings.</summary>
public static class LyricsTimeline
{
    /// <summary>How long the final line stays up when nothing follows it.</summary>
    public const double MaxLastLineDuration = 6;
    private const double MinimumLineDuration = 0.5;

    public static LyricsDocument Build(ParsedLrc parsed, double trackDuration)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var entries = parsed.Entries;
        var lines = new LyricLine[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var end = i + 1 < entries.Count
                ? entries[i + 1].Start
                : Math.Max(entry.Start + MinimumLineDuration, Math.Min(entry.Start + MaxLastLineDuration, trackDuration));
            lines[i] = new LyricLine(entry.Start, end, entry.Text, Words(entry, end));
        }
        var hasWordTiming = entries.Any(entry => entry.WordStarts is not null);
        return new LyricsDocument(lines, hasWordTiming ? LyricsTiming.WordSynced : LyricsTiming.LineSynced);
    }

    /// <summary>Plain lyrics: spread non-empty lines evenly over the track.</summary>
    public static LyricsDocument Unsynced(string plainText, double trackDuration)
    {
        var texts = TextFolding.SplitLines(plainText).Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (texts.Length == 0)
        {
            return new LyricsDocument([], LyricsTiming.Unsynced);
        }
        var slot = Math.Max(trackDuration, texts.Length) / texts.Length;
        var lines = texts.Select((text, i) =>
        {
            var start = i * slot;
            return new LyricLine(start, start + slot, text, WordTimingEstimator.Estimate(text, start, start + slot));
        }).ToArray();
        return new LyricsDocument(lines, LyricsTiming.Unsynced);
    }

    private static IReadOnlyList<LyricWord> Words(LrcEntry entry, double lineEnd)
    {
        if (entry.Text.Length == 0)
        {
            return [];
        }
        if (entry.WordStarts is not { } timed)
        {
            return WordTimingEstimator.Estimate(entry.Text, entry.Start, lineEnd);
        }
        var words = new LyricWord[timed.Count];
        for (var i = 0; i < timed.Count; i++)
        {
            var word = timed[i];
            // The last word has no following timestamp: cap it so it isn't "sung" across a long gap.
            var end = i + 1 < timed.Count
                ? timed[i + 1].Start
                : Math.Min(lineEnd, word.Start + WordTimingEstimator.MaxSungDuration(word.Text));
            words[i] = new LyricWord(word.Text, word.Start, Math.Max(end, word.Start));
        }
        return words;
    }
}
