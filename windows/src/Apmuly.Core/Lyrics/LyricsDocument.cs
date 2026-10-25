namespace Apmuly.Core.Lyrics;

public enum LyricsTiming
{
    /// <summary>Word-level timestamps from the source.</summary>
    WordSynced,
    /// <summary>Line-level timestamps; word timings are estimated.</summary>
    LineSynced,
    /// <summary>No timestamps; lines spread evenly across the track.</summary>
    Unsynced,
}

public static class LyricsTimingExtensions
{
    /// <summary>The name the renderer expects ("wordSynced", "lineSynced", "unsynced").</summary>
    public static string WireName(this LyricsTiming timing) => timing switch
    {
        LyricsTiming.WordSynced => "wordSynced",
        LyricsTiming.LineSynced => "lineSynced",
        _ => "unsynced",
    };
}

public sealed record LyricWord(string Text, double Start, double End);

public sealed record LyricLine(double Start, double End, string Text, IReadOnlyList<LyricWord> Words)
{
    /// <summary>An empty timestamped line: an instrumental break.</summary>
    public bool IsGap => Text.Length == 0;

    /// <summary>0…1 progress through the line at <paramref name="position"/>.</summary>
    public double Progress(double position) =>
        End > Start ? Math.Min(1, Math.Max(0, (position - Start) / (End - Start))) : 1;

    /// <summary>Index of the word being sung at <paramref name="position"/>, if any.</summary>
    public int? WordIndex(double position)
    {
        for (var i = Words.Count - 1; i >= 0; i--)
        {
            if (Words[i].Start <= position)
            {
                return i;
            }
        }
        return null;
    }

    public bool Equals(LyricLine? other) =>
        other is not null
        && Start.Equals(other.Start) && End.Equals(other.End) && Text == other.Text
        && Words.SequenceEqual(other.Words);

    public override int GetHashCode() => HashCode.Combine(Start, End, Text, Words.Count);
}

public sealed record LyricsDocument(IReadOnlyList<LyricLine> Lines, LyricsTiming Timing)
{
    /// <summary>The last line that has started at <paramref name="position"/> (binary search), or null before the first line.</summary>
    public int? LineIndex(double position)
    {
        var low = 0;
        var high = Lines.Count;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (Lines[mid].Start <= position)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return low == 0 ? null : low - 1;
    }

    public bool Equals(LyricsDocument? other) =>
        other is not null && Timing == other.Timing && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => HashCode.Combine(Timing, Lines.Count);
}
