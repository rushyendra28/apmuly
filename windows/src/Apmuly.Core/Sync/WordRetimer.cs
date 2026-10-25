using Apmuly.Core.Lyrics;
using Apmuly.Core.Matching;

namespace Apmuly.Core.Sync;

/// <summary>
/// Moves each line's words to the singer's actual pace.
/// Words the singer was heard on are pinned to when they were sung; the others are spread between
/// those anchors by syllable weight, and after the last anchor they continue at the singer's measured
/// pace. Lines sung again (choruses) inherit the pattern learned on another occurrence.
/// </summary>
public static class WordRetimer
{
    /// <summary>Fallback seconds per syllable after the last heard word when the pace is unknown.</summary>
    internal const double DefaultPace = 0.32;
    /// <summary>Keep the last word starting a little before the line ends.</summary>
    private const double LineTail = 0.1;
    private const double MinimumGap = 0.02;

    public static LyricsDocument Retime(LyricsDocument document, IReadOnlyCollection<WordObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0)
        {
            return document;
        }
        var byLine = observations.GroupBy(o => o.Line).ToDictionary(g => g.Key, g => g.ToArray());
        var patterns = LearnPatterns(document, byLine);

        var lines = document.Lines.Select((line, index) =>
        {
            if (line.IsGap || line.Words.Count == 0)
            {
                return line;
            }
            var anchors = ConsistentAnchors(byLine.GetValueOrDefault(index) ?? [], line);
            if (anchors.Count == 0 && patterns.TryGetValue(TitleNormalizer.Key(line.Text), out var pattern))
            {
                anchors = pattern
                    .Select(kv => (kv.Key, Time: line.Start + kv.Value))
                    .Where(a => a.Time < line.End - LineTail)
                    .ToDictionary(a => a.Key, a => a.Time);
            }
            return anchors.Count == 0 ? line : line with { Words = Place(line, anchors) };
        }).ToArray();
        return new LyricsDocument(lines, document.Timing);
    }

    /// <summary>Learned relative patterns (seconds after line start), by line text; best-observed wins.</summary>
    private static Dictionary<string, Dictionary<int, double>> LearnPatterns(
        LyricsDocument document, Dictionary<int, WordObservation[]> byLine)
    {
        var patterns = new Dictionary<string, Dictionary<int, double>>();
        foreach (var (index, observed) in byLine.OrderBy(kv => kv.Key))
        {
            if (index < 0 || index >= document.Lines.Count)
            {
                continue;
            }
            var line = document.Lines[index];
            var anchors = ConsistentAnchors(observed, line);
            var key = TitleNormalizer.Key(line.Text);
            if (anchors.Count > 0 && anchors.Count > (patterns.GetValueOrDefault(key)?.Count ?? 0))
            {
                patterns[key] = anchors.ToDictionary(kv => kv.Key, kv => kv.Value - line.Start);
            }
        }
        return patterns;
    }

    /// <summary>One time per word (median of repeats), clamped into the line, strictly increasing.</summary>
    private static Dictionary<int, double> ConsistentAnchors(IEnumerable<WordObservation> observations, LyricLine line)
    {
        var latest = line.End - LineTail;
        var perWord = observations
            .Where(o => o.Word >= 0 && o.Word < line.Words.Count && double.IsFinite(o.Time))
            .GroupBy(o => o.Word)
            .Select(g => (Word: g.Key, Time: Statistics.Median(g.Select(o => o.Time))))
            .Where(w => w.Time <= latest)
            .OrderBy(w => w.Word);
        var anchors = new Dictionary<int, double>();
        var previous = double.NegativeInfinity;
        foreach (var (word, rawTime) in perWord)
        {
            var time = Math.Max(line.Start, rawTime);
            if (time > previous + MinimumGap)
            {
                anchors[word] = time;
                previous = time;
            }
        }
        return anchors;
    }

    private static LyricWord[] Place(LyricLine line, Dictionary<int, double> anchors)
    {
        var words = line.Words;
        var weights = WordTimingEstimator.Weights(words.Select(w => w.Text).ToArray());
        double Weight(int from, int to) => weights.Skip(from).Take(Math.Max(0, to - from)).Sum();
        var indices = anchors.Keys.Order().ToArray();
        var starts = Enumerable.Repeat(line.Start, words.Count).ToArray();

        // Before the first anchor: from the line start.
        var first = indices[0];
        for (var i = 0; i < first; i++)
        {
            starts[i] = line.Start + Weight(0, i) / Math.Max(Weight(0, first), 1e-9) * (anchors[first] - line.Start);
        }
        // Between anchors: spread by syllable weight.
        foreach (var (a, b) in indices.Zip(indices.Skip(1)))
        {
            for (var i = a; i < b; i++)
            {
                starts[i] = anchors[a] + Weight(a, i) / Math.Max(Weight(a, b), 1e-9) * (anchors[b] - anchors[a]);
            }
        }
        // After the last anchor: continue at the singer's measured pace, compressed to fit the line.
        var last = indices[^1];
        starts[last] = anchors[last];
        if (last + 1 < words.Count)
        {
            var pace = indices.Length >= 2 && Weight(first, last) > 0
                ? (anchors[last] - anchors[first]) / Weight(first, last)
                : DefaultPace;
            var room = line.End - LineTail - anchors[last];
            var needed = Weight(last, words.Count - 1) * pace;
            var scale = needed > room && needed > 0 ? room / needed : 1;
            for (var i = last + 1; i < words.Count; i++)
            {
                starts[i] = anchors[last] + Weight(last, i) * pace * scale;
            }
        }

        return words.Select((word, i) =>
        {
            var end = i + 1 < words.Count
                ? starts[i + 1]
                : Math.Min(line.End, starts[i] + WordTimingEstimator.MaxSungDuration(word.Text));
            return new LyricWord(word.Text, starts[i], Math.Max(end, starts[i] + MinimumGap));
        }).ToArray();
    }
}
